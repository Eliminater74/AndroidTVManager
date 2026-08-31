using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Packages;

public sealed class DebloatPlanner : IDebloatPlanner
{
    private readonly IPackageInventoryService _inventory;
    private readonly IPackageClassifier _classifier;
    private readonly IPackageReferenceCatalog _referenceCatalog;
    private readonly IDeviceSnapshotRepository _snapshots;
    private readonly IPackagePreferenceRepository _preferences;

    public DebloatPlanner(
        IPackageInventoryService inventory,
        IPackageClassifier classifier,
        IPackageReferenceCatalog referenceCatalog,
        IDeviceSnapshotRepository snapshots,
        IPackagePreferenceRepository preferences)
    {
        _inventory = inventory;
        _classifier = classifier;
        _referenceCatalog = referenceCatalog;
        _snapshots = snapshots;
        _preferences = preferences;
    }

    public async Task<DebloatPlan> CreatePlanAsync(
        string serial,
        DebloatPreset preset,
        AndroidDevice? targetDevice = null,
        CancellationToken cancellationToken = default)
    {
        var inventory = await _inventory.GetInventoryAsync(serial, cancellationToken);
        var overrides = await _preferences.GetOverridesAsync(serial, cancellationToken);
        var inspection = await _snapshots.GetLatestAsync(serial, cancellationToken);
        var device = MergeDevice(targetDevice, inspection?.Overview.Value, serial);
        var context = PackageClassificationContexts.FromInventory(device, inventory.Packages);
        var referenceAnalysis = await _referenceCatalog.AnalyzeAsync(device, inventory.Packages, cancellationToken);
        var references = referenceAnalysis.Packages
            .ToDictionary(reference => reference.PackageName, StringComparer.OrdinalIgnoreCase);

        var items = inventory.Packages
            .Select(package =>
            {
                var reference = references.GetValueOrDefault(package.PackageName);
                var assessment = _classifier.Classify(package, context);
                assessment = ApplyReferenceProtection(assessment, reference);
                assessment = ApplyOverride(assessment, overrides.GetValueOrDefault(package.PackageName));
                return BuildItem(package, assessment, preset, reference);
            })
            .ToArray();
        var selected = items.Count(item => item.Selected);
        var warnings = new List<string>
        {
            $"Target locked to {serial}. Review the target before executing.",
            "Recommended operation is Disable before Uninstall for User 0.",
            "Unknown and Critical packages are never automatically selected.",
            BuildReferenceWarning(referenceAnalysis.Summary)
        };
        if (preset == DebloatPreset.Aggressive)
            warnings.Add("Aggressive mode can affect voice, casting, recommendations, or vendor features.");
        if (inventory.ErrorMessage is not null)
            warnings.Add($"Inventory is incomplete: {inventory.ErrorMessage}");
        if (selected == 0)
            warnings.Add("No trusted packages matched this preset on the current device.");

        var baseline = JsonSerializer.Serialize(inventory.Packages.Select(package =>
            new { package.PackageName, package.IsEnabled, package.IsInstalled }));
        return new(
            serial,
            device.BuildFingerprint,
            DateTimeOffset.UtcNow,
            preset,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(baseline))),
            items,
            warnings,
            referenceAnalysis.Summary);
    }

    private static PackageAssessment ApplyOverride(PackageAssessment assessment, PackageOverride value)
        => value switch
        {
            PackageOverride.AlwaysKeep or PackageOverride.NeverSuggest
                => assessment with { Override = value, IsProtected = true },
            PackageOverride.UserApproved => assessment with { Override = value },
            _ => assessment
        };

    private static DebloatPlanItem BuildItem(
        PackageInventoryEntry package,
        PackageAssessment assessment,
        DebloatPreset preset,
        PackageReferenceAnalysisItem? reference)
    {
        var allowed = assessment.Override == PackageOverride.UserApproved
            || (assessment.Risk switch
            {
                PackageRiskLevel.Safe => true,
                PackageRiskLevel.Caution => preset is DebloatPreset.Medium or DebloatPreset.Aggressive,
                PackageRiskLevel.HighRisk => preset == DebloatPreset.Aggressive,
                _ => false
            });
        var protectedPackage = assessment.IsProtected || assessment.Risk == PackageRiskLevel.Critical;
        var selected = allowed && !protectedPackage && package.IsInstalled;
        var reason = selected
            ? null
            : protectedPackage
                ? "Locked: critical package or active device role."
                : assessment.Risk == PackageRiskLevel.Unknown
                    ? "Not auto-selected: unknown package. You may select it manually after review."
                : !package.IsInstalled
                    ? "Package is not currently installed for the user."
                    : $"Not included in {preset} preset.";
        return new(package, assessment, DebloatAction.Disable, selected, reason, reference);
    }

    private static PackageAssessment ApplyReferenceProtection(
        PackageAssessment assessment,
        PackageReferenceAnalysisItem? reference)
    {
        if (reference is not { Matches.Count: > 0 }
            || reference.Matches.All(match => !match.ActiveRoleProtection)
            || assessment.IsProtected
            || assessment.Risk == PackageRiskLevel.Critical)
            return assessment;

        var protectiveMatches = reference.Matches
            .Where(match => match.ActiveRoleProtection)
            .ToArray();
        var role = protectiveMatches
            .Select(match => match.Role)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var impacts = assessment.Impacts
            .Concat(protectiveMatches.SelectMany(match => match.FeatureImpacts))
            .DistinctBy(impact => (impact.Area, impact.Description, impact.IsKnownDependency))
            .ToArray();
        var reasons = assessment.Reasons
            .Concat([
                $"Reference profile protects this package as {role ?? "a core TV component"}; origin is {reference.Origin}."
            ])
            .ToArray();

        return assessment with
        {
            Risk = PackageRiskLevel.Critical,
            Confidence = Max(assessment.Confidence, protectiveMatches.Max(match => match.Confidence)),
            Category = role ?? assessment.Category,
            RecommendedAction = "Keep",
            Reasons = reasons,
            Impacts = impacts,
            IsProtected = true
        };
    }

    private static string BuildReferenceWarning(PackageReferenceSummary summary)
        => $"Device profile matched {summary.BaselineMatches} reference package record(s); "
           + $"{summary.UnknownPackages} of {summary.TotalPackages} package(s) remain Unknown.";

    private static PackageConfidence Max(PackageConfidence left, PackageConfidence right)
        => left > right ? left : right;

    private static AndroidDevice MergeDevice(
        AndroidDevice? selected,
        AndroidDevice? inspected,
        string serial)
        => new()
        {
            Serial = serial,
            FriendlyName = selected?.FriendlyName ?? inspected?.FriendlyName,
            Endpoint = selected?.Endpoint ?? inspected?.Endpoint,
            ReportedName = selected?.ReportedName ?? inspected?.ReportedName,
            MacAddress = selected?.MacAddress ?? inspected?.MacAddress,
            State = selected?.State ?? inspected?.State ?? DeviceState.Device,
            ConnectionType = selected?.ConnectionType ?? inspected?.ConnectionType ?? ConnectionType.Unknown,
            Manufacturer = selected?.Manufacturer ?? inspected?.Manufacturer,
            Brand = selected?.Brand ?? inspected?.Brand,
            Model = selected?.Model ?? inspected?.Model,
            Product = selected?.Product ?? inspected?.Product,
            DeviceName = selected?.DeviceName ?? inspected?.DeviceName,
            Board = selected?.Board ?? inspected?.Board,
            AndroidVersion = selected?.AndroidVersion ?? inspected?.AndroidVersion,
            ApiLevel = selected?.ApiLevel ?? inspected?.ApiLevel,
            SecurityPatch = selected?.SecurityPatch ?? inspected?.SecurityPatch,
            BuildId = selected?.BuildId ?? inspected?.BuildId,
            BuildType = selected?.BuildType ?? inspected?.BuildType,
            BuildFingerprint = selected?.BuildFingerprint ?? inspected?.BuildFingerprint,
            SeenAtUtc = selected?.SeenAtUtc ?? inspected?.SeenAtUtc ?? DateTimeOffset.UtcNow
        };
}
