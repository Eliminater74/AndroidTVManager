using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Core.Scripts;

namespace AndroidTVManager.Infrastructure.Packages;

public sealed class DebloatExecutionService : IDebloatExecutionService
{
    private readonly IPackageInventoryService _inventory;
    private readonly IPackageClassifier _classifier;
    private readonly IPackageReferenceCatalog _referenceCatalog;
    private readonly IScriptExecutionService _scripts;
    private readonly IDeviceInspectionService _inspection;

    public DebloatExecutionService(
        IPackageInventoryService inventory,
        IPackageClassifier classifier,
        IPackageReferenceCatalog referenceCatalog,
        IScriptExecutionService scripts,
        IDeviceInspectionService inspection)
    {
        _inventory = inventory;
        _classifier = classifier;
        _referenceCatalog = referenceCatalog;
        _scripts = scripts;
        _inspection = inspection;
    }

    public async Task<ScriptExecutionResult> ExecuteAsync(
        DebloatPlan plan,
        CancellationToken cancellationToken = default)
    {
        var current = await _inventory.GetInventoryAsync(plan.Serial, cancellationToken);
        var live = await _inspection.InspectAsync(plan.Serial, cancellationToken: cancellationToken);
        var liveFingerprint = live.Overview.Value?.BuildFingerprint;
        if (plan.BuildFingerprint is not null
            && liveFingerprint is not null
            && !string.Equals(plan.BuildFingerprint, liveFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The device build changed since the debloat plan was created. Refresh and review the plan.");
        var expected = plan.Items.Where(item => item.Selected)
            .Select(item => new { item.Package.PackageName, item.Package.IsEnabled, item.Package.IsInstalled })
            .OrderBy(item => item.PackageName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var actual = current.Packages.Where(package => expected.Any(item => item.PackageName.Equals(
                package.PackageName, StringComparison.OrdinalIgnoreCase)))
            .Select(package => new { package.PackageName, package.IsEnabled, package.IsInstalled })
            .OrderBy(item => item.PackageName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("The device package state changed since the debloat plan was created. Refresh and review the plan.");

        var device = live.Overview.Value ?? new AndroidDevice
        {
            Serial = plan.Serial,
            State = DeviceState.Device,
            ConnectionType = ConnectionType.Unknown,
            BuildFingerprint = liveFingerprint
        };
        var context = PackageClassificationContexts.FromInventory(device, current.Packages);
        var referenceAnalysis = await _referenceCatalog.AnalyzeAsync(device, current.Packages, cancellationToken);
        var references = referenceAnalysis.Packages
            .ToDictionary(reference => reference.PackageName, StringComparer.OrdinalIgnoreCase);
        var blocked = current.Packages
            .Where(package => expected.Any(item => item.PackageName.Equals(
                package.PackageName,
                StringComparison.OrdinalIgnoreCase)))
            .Select(package => PackageAssessmentReferenceEnricher.ApplyReferenceEvidence(
                _classifier.Classify(package, context),
                references.GetValueOrDefault(package.PackageName)))
            .Where(PackageAssessmentReferenceEnricher.IsSafetyLocked)
            .Select(assessment => assessment.PackageName)
            .OrderBy(package => package, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (blocked.Length > 0)
            throw new InvalidOperationException(
                $"Selected package(s) became protected after preview: {string.Join(", ", blocked)}. Refresh and review the plan.");

        var actions = plan.Items.Where(item => item.Selected).Select(item => new ScriptAction
        {
            Type = item.Action == DebloatAction.UninstallForUser ? "uninstallUser" : "disablePackage",
            Package = item.Package.PackageName,
            Reversible = true
        }).ToList();
        if (actions.Count == 0)
            throw new InvalidOperationException("The debloat plan has no selected packages.");

        return await _scripts.ExecuteAsync(new ScriptDefinition
        {
            SchemaVersion = 1,
            Name = $"{plan.Preset} debloat",
            Description = "Generated from an approved, device-specific debloat preview.",
            Actions = actions
        }, new AndroidDevice
        {
            Serial = plan.Serial,
            State = DeviceState.Device,
            ConnectionType = ConnectionType.Unknown,
            BuildFingerprint = plan.BuildFingerprint
        }, cancellationToken);
    }

    public Task<ScriptUndoResult> RestoreAsync(
        long executionId,
        string serial,
        CancellationToken cancellationToken = default)
        => _scripts.UndoAsync(executionId, serial, cancellationToken);
}
