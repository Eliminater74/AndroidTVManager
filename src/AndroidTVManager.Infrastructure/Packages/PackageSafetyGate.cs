using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Packages;

public sealed class PackageSafetyGate : IPackageSafetyGate
{
    private readonly IPackageInventoryService _inventory;
    private readonly IDeviceInspectionService _inspection;
    private readonly IPackageClassifier _classifier;
    private readonly IPackageReferenceCatalog _referenceCatalog;

    public PackageSafetyGate(
        IPackageInventoryService inventory,
        IDeviceInspectionService inspection,
        IPackageClassifier classifier,
        IPackageReferenceCatalog referenceCatalog)
    {
        _inventory = inventory;
        _inspection = inspection;
        _classifier = classifier;
        _referenceCatalog = referenceCatalog;
    }

    public async Task<PackageMutationDecision> EvaluateAsync(
        PackageMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Serial))
            return Denied("A live target serial is required.");
        if (string.IsNullOrWhiteSpace(request.PackageName))
            return Denied("A package name is required.");

        var serial = request.Serial.Trim();
        var packageName = request.PackageName.Trim();
        var inventory = await _inventory.GetInventoryAsync(serial, cancellationToken);
        if (IsDestructive(request.Kind) && inventory.ErrorMessage is not null)
            return Denied(inventory.ErrorMessage);

        DeviceInspectionResult? live = null;
        try
        {
            live = await _inspection.InspectAsync(serial, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && IsDestructive(request.Kind))
        {
            return Denied($"Live device inspection failed: {exception.Message}");
        }

        var liveFingerprint = live?.Overview.Value?.BuildFingerprint
            ?? inventory.Packages.FirstOrDefault()?.BuildFingerprint;
        if (IsDestructive(request.Kind)
            && request.ExpectedBuildFingerprint is not null
            && liveFingerprint is not null
            && !string.Equals(request.ExpectedBuildFingerprint, liveFingerprint, StringComparison.Ordinal))
        {
            return Denied("The device build changed since this operation was prepared. Refresh and review before continuing.");
        }

        var device = live?.Overview.Value ?? new AndroidDevice
        {
            Serial = serial,
            State = DeviceState.Device,
            ConnectionType = ConnectionType.Unknown,
            BuildFingerprint = liveFingerprint
        };
        var package = inventory.Packages.FirstOrDefault(item =>
            item.PackageName.Equals(packageName, StringComparison.OrdinalIgnoreCase));
        if (package is null && IsDestructive(request.Kind))
            return Denied($"Package '{packageName}' was not found in the live User 0 inventory.");

        if (package is null)
            return new PackageMutationDecision(true, null);

        var context = PackageClassificationContexts.FromInventory(device, inventory.Packages);
        var referenceAnalysis = await _referenceCatalog.AnalyzeAsync(device, inventory.Packages, cancellationToken);
        var reference = referenceAnalysis.Packages.FirstOrDefault(item =>
            item.PackageName.Equals(packageName, StringComparison.OrdinalIgnoreCase));
        var assessment = PackageAssessmentReferenceEnricher.ApplyReferenceEvidence(
            _classifier.Classify(package, context),
            reference);

        if (IsDestructive(request.Kind) && PackageAssessmentReferenceEnricher.IsSafetyLocked(assessment))
        {
            return Denied(
                $"'{packageName}' is protected ({assessment.Risk}, {assessment.RecommendedAction}).",
                assessment);
        }

        return new PackageMutationDecision(true, null, assessment);
    }

    public async Task EnsureAllowedAsync(
        PackageMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        var decision = await EvaluateAsync(request, cancellationToken);
        if (!decision.Allowed)
            throw new InvalidOperationException(decision.BlockReason ?? "The package mutation was blocked.");
    }

    private static bool IsDestructive(PackageMutationKind kind)
        => kind is PackageMutationKind.Disable
            or PackageMutationKind.UninstallForUser
            or PackageMutationKind.FullUninstall
            or PackageMutationKind.ClearData
            or PackageMutationKind.ClearCache
            or PackageMutationKind.RevokePermission;

    private static PackageMutationDecision Denied(string reason, PackageAssessment? assessment = null)
        => new(false, reason, assessment);
}
