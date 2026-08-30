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
    private readonly IDeviceSnapshotRepository _snapshots;
    private readonly IPackagePreferenceRepository _preferences;

    public DebloatPlanner(
        IPackageInventoryService inventory,
        IPackageClassifier classifier,
        IDeviceSnapshotRepository snapshots,
        IPackagePreferenceRepository preferences)
    {
        _inventory = inventory;
        _classifier = classifier;
        _snapshots = snapshots;
        _preferences = preferences;
    }

    public async Task<DebloatPlan> CreatePlanAsync(
        string serial,
        DebloatPreset preset,
        CancellationToken cancellationToken = default)
    {
        var inventory = await _inventory.GetInventoryAsync(serial, cancellationToken);
        var overrides = await _preferences.GetOverridesAsync(serial, cancellationToken);
        var inspection = await _snapshots.GetLatestAsync(serial, cancellationToken);
        var device = inspection?.Overview.Value ?? new AndroidDevice
        {
            Serial = serial,
            State = DeviceState.Device,
            ConnectionType = ConnectionType.Unknown
        };
        var context = new PackageClassificationContext(
            device,
            inventory.Packages.FirstOrDefault(package => package.IsActiveLauncher)?.PackageName,
            inventory.Packages.Where(package => package.IsDefaultInputMethod).Select(package => package.PackageName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            inventory.Packages.Where(package => package.IsEnabledAccessibilityService).Select(package => package.PackageName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            inventory.Packages.Where(package => package.IsDeviceOwner).Select(package => package.PackageName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));

        var items = inventory.Packages
            .Select(package => BuildItem(package, ApplyOverride(
                _classifier.Classify(package, context),
                overrides.GetValueOrDefault(package.PackageName)), preset))
            .ToArray();
        var selected = items.Count(item => item.Selected);
        var warnings = new List<string>
        {
            $"Target locked to {serial}. Review the target before executing.",
            "Recommended operation is Disable before Uninstall for User 0.",
            "Unknown and Critical packages are never automatically selected."
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
            warnings);
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
        DebloatPreset preset)
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
        return new(package, assessment, DebloatAction.Disable, selected, reason);
    }
}
