using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Core.Scripts;

namespace AndroidTVManager.Infrastructure.Packages;

public sealed class DebloatExecutionService : IDebloatExecutionService
{
    private readonly IPackageInventoryService _inventory;
    private readonly IScriptExecutionService _scripts;

    public DebloatExecutionService(
        IPackageInventoryService inventory,
        IScriptExecutionService scripts)
    {
        _inventory = inventory;
        _scripts = scripts;
    }

    public async Task<ScriptExecutionResult> ExecuteAsync(
        DebloatPlan plan,
        CancellationToken cancellationToken = default)
    {
        var current = await _inventory.GetInventoryAsync(plan.Serial, cancellationToken);
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
