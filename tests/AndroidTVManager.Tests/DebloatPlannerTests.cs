using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Packages;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DebloatPlannerTests
{
    [Fact]
    public async Task Planner_uses_selected_device_identity_for_manufacturer_rules()
    {
        var planner = CreatePlanner([Package("com.tcl.guard")]);

        var generic = await planner.CreatePlanAsync("tv-1", DebloatPreset.Medium);
        var tcl = await planner.CreatePlanAsync(
            "tv-1",
            DebloatPreset.Medium,
            Device("TCL", "65C7K", androidVersion: "11"));

        generic.Items.Single().Assessment.Risk.Should().Be(PackageRiskLevel.Unknown);
        generic.Items.Single().Selected.Should().BeFalse();
        tcl.Items.Single().Assessment.Risk.Should().Be(PackageRiskLevel.Caution);
        tcl.Items.Single().Selected.Should().BeTrue();
    }

    [Fact]
    public async Task Reference_profile_protects_tv_core_but_not_unrelated_user_apps()
    {
        var planner = CreatePlanner([
            Package("com.android.tv.settings", isSystem: true),
            Package("com.purefusion.iptv", isSystem: false)
        ]);

        var plan = await planner.CreatePlanAsync(
            "tv-1",
            DebloatPreset.Aggressive,
            Device("Google", "Chromecast with Google TV", androidVersion: "14"));

        var tvSettings = plan.Items.Single(item => item.Package.PackageName == "com.android.tv.settings");
        var iptv = plan.Items.Single(item => item.Package.PackageName == "com.purefusion.iptv");

        tvSettings.Reference.Should().NotBeNull();
        tvSettings.Reference!.Origin.Should().Be(PackageOrigin.AospTvCore);
        tvSettings.Assessment.Risk.Should().Be(PackageRiskLevel.Critical);
        tvSettings.Assessment.IsProtected.Should().BeTrue();
        tvSettings.Selected.Should().BeFalse();
        iptv.Reference!.Origin.Should().Be(PackageOrigin.Unknown);
        iptv.Assessment.Risk.Should().Be(PackageRiskLevel.Unknown);
        iptv.Assessment.IsProtected.Should().BeFalse();
        iptv.Selected.Should().BeFalse();
        plan.ReferenceSummary.Should().NotBeNull();
        plan.Warnings.Should().Contain(warning => warning.Contains("Device profile matched", StringComparison.Ordinal));
    }

    private static DebloatPlanner CreatePlanner(IReadOnlyList<PackageInventoryEntry> packages)
        => new(
            new FixedInventoryService(packages),
            new PackageClassifier(),
            new PackageReferenceCatalog(),
            new FakeDeviceSnapshotRepository(),
            new EmptyPackagePreferenceRepository());

    private static AndroidDevice Device(string manufacturer, string model, string androidVersion)
        => new()
        {
            Serial = "tv-1",
            Manufacturer = manufacturer,
            Brand = manufacturer,
            Model = model,
            AndroidVersion = androidVersion,
            ApiLevel = int.Parse(androidVersion) + 19,
            State = DeviceState.Device,
            ConnectionType = ConnectionType.Network
        };

    private static PackageInventoryEntry Package(string name, bool isSystem = false)
        => new(
            name,
            null,
            null,
            null,
            "0",
            isSystem,
            false,
            true,
            true,
            false,
            [],
            DateTimeOffset.UtcNow,
            "tv-1",
            "14",
            "fingerprint");

    private sealed class FixedInventoryService : IPackageInventoryService
    {
        private readonly IReadOnlyList<PackageInventoryEntry> _packages;

        public FixedInventoryService(IReadOnlyList<PackageInventoryEntry> packages)
        {
            _packages = packages;
        }

        public Task<PackageInventoryResult> GetInventoryAsync(
            string serial,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PackageInventoryResult(serial, DateTimeOffset.UtcNow, _packages, []));

        public Task<PackageInventoryEntry?> GetDetailsAsync(
            string serial,
            string packageName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_packages.FirstOrDefault(package =>
                package.PackageName.Equals(packageName, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class EmptyPackagePreferenceRepository : IPackagePreferenceRepository
    {
        public Task<IReadOnlyDictionary<string, PackageOverride>> GetOverridesAsync(
            string serial,
            CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyDictionary<string, PackageOverride>)
                new Dictionary<string, PackageOverride>(StringComparer.OrdinalIgnoreCase));

        public Task SetOverrideAsync(
            string serial,
            string packageName,
            PackageOverride value,
            string? note = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string?> GetNoteAsync(
            string serial,
            string packageName,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task SetNoteAsync(
            string serial,
            string packageName,
            string note,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
