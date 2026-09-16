using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Packages;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DebloatCleanupServiceTests
{
    [Fact]
    public async Task Shield_darcy_gets_shield_aware_recommended_counts()
    {
        var overview = await CreateService([
            Package("com.nvidia.stats", isSystem: true),
            Package("com.nvidia.ota", isSystem: true),
            Package("com.nvidia.osc", isSystem: true),
            Package("com.vendor.unknown", isSystem: true)
        ]).CreateOverviewAsync("tv-1", ShieldDarcy());

        overview.DetectedProfileLabel.Should().Be("NVIDIA Shield TV — darcy");
        overview.HasModelSpecificProfile.Should().BeTrue();
        overview.ActiveProfiles.Should().Contain(profile => profile.BaselineId == "nvidia-shield-tv-reviewed");
        var recommended = overview.Profile(DebloatCleanupKind.Recommended)!;
        recommended.HasActions.Should().BeTrue();
        recommended.ActionCount.Should().BeGreaterThan(0);
        Selected(overview, DebloatCleanupKind.Recommended).Should().Equal("com.nvidia.stats");
        Selected(overview, DebloatCleanupKind.Safe).Should().BeEmpty();
        overview.UnknownLeftUntouched.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Streamer_kirkwood_uses_kirkwood_profile_without_chromecast_diagnostics()
    {
        var overview = await CreateService([
            Package("com.google.android.apps.tv.launcherx", isSystem: true) with { IsActiveLauncher = true },
            Package("com.google.android.gms", isSystem: true),
            Package("com.google.android.apps.tv.netoscope", isSystem: true),
            Package("com.google.android.chromecast.chromecastservice", isSystem: true)
        ]).CreateOverviewAsync("tv-1", Streamer());

        overview.DetectedProfileLabel.Should().Be("Google TV Streamer 4K — kirkwood");
        overview.HasModelSpecificProfile.Should().BeTrue();
        overview.ActiveProfiles.Should().Contain(profile => profile.BaselineId == "google-tv-streamer-kirkwood-4k");
        overview.ActiveProfiles.Should().NotContain(profile => profile.BaselineId == "google-tv-chromecast-sabrina-4k");
        Selected(overview, DebloatCleanupKind.Recommended).Should().NotContain("com.google.android.apps.tv.netoscope");
        Selected(overview, DebloatCleanupKind.Recommended).Should().NotContain("com.google.android.apps.tv.launcherx");
        Selected(overview, DebloatCleanupKind.Deep).Should().NotContain("com.google.android.chromecast.chromecastservice");
    }

    [Fact]
    public async Task Chromecast_sabrina_selects_reviewed_diagnostics_and_keeps_casting()
    {
        var overview = await CreateService([
            Package("com.google.android.apps.tv.netoscope", isSystem: true),
            Package("com.google.android.chromecast.chromecastservice", isSystem: true),
            Package("com.google.android.apps.tv.launcherx", isSystem: true) with { IsActiveLauncher = true },
            Package("com.vendor.unknown", isSystem: true)
        ]).CreateOverviewAsync("tv-1", Chromecast());

        overview.DetectedProfileLabel.Should().Be("Chromecast with Google TV 4K — sabrina");
        overview.ActiveProfiles.Should().Contain(profile => profile.BaselineId == "google-tv-chromecast-sabrina-4k");
        Selected(overview, DebloatCleanupKind.Recommended).Should().Equal("com.google.android.apps.tv.netoscope");
        Selected(overview, DebloatCleanupKind.Safe).Should().BeEmpty();
        Selected(overview, DebloatCleanupKind.Deep).Should().Contain("com.google.android.apps.tv.netoscope");
        Selected(overview, DebloatCleanupKind.Recommended).Should().NotContain("com.google.android.chromecast.chromecastservice");
        Selected(overview, DebloatCleanupKind.Recommended).Should().NotContain("com.vendor.unknown");
    }

    [Fact]
    public async Task Onn_yoc_uses_yoc_profile_and_leaves_unknown_vendor_manual()
    {
        var overview = await CreateService([
            Package("com.google.android.apps.tv.launcherx", isSystem: true) with { IsActiveLauncher = true },
            Package("com.walmart.onn.unknown", isSystem: true),
            Package("com.google.android.tvrecommendations", isSystem: true)
        ]).CreateOverviewAsync("tv-1", OnnYoc());

        overview.DetectedProfileLabel.Should().Be("onn. Google TV 4K Box — YOC");
        overview.ActiveProfiles.Should().Contain(profile => profile.BaselineId == "onn-google-tv-4k-box-yoc");
        Selected(overview, DebloatCleanupKind.Recommended).Should().Equal("com.google.android.tvrecommendations");
        Selected(overview, DebloatCleanupKind.Recommended).Should().NotContain("com.walmart.onn.unknown");
        overview.UnknownLeftUntouched.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Generic_emulator_does_not_activate_hardware_overlays()
    {
        var overview = await CreateService([
            Package("com.nvidia.stats", isSystem: true),
            Package("com.google.android.apps.tv.netoscope", isSystem: true),
            Package("com.walmart.onn.unknown", isSystem: true)
        ]).CreateOverviewAsync("emulator-5554", new AndroidDevice
        {
            Serial = "emulator-5554",
            Manufacturer = "Google",
            Brand = "google",
            Model = "AOSP TV on x86",
            Product = "sdk_google_atv_x86",
            DeviceName = "generic_x86",
            AndroidVersion = "14",
            State = DeviceState.Device,
            ConnectionType = ConnectionType.Usb
        });

        overview.DetectedProfileLabel.Should().Be("Generic Android TV profile");
        overview.HasModelSpecificProfile.Should().BeFalse();
        overview.ProfileDetail.Should().Contain("No model-specific profile applies");
        overview.ActiveProfiles.Should().NotContain(profile => profile.BaselineId == "nvidia-shield-tv-reviewed");
        overview.ActiveProfiles.Should().NotContain(profile => profile.BaselineId == "google-tv-chromecast-sabrina-4k");
        overview.ActiveProfiles.Should().NotContain(profile => profile.BaselineId == "onn-google-tv-4k-box-yoc");
        Selected(overview, DebloatCleanupKind.Recommended).Should().BeEmpty();
        overview.Profile(DebloatCleanupKind.Recommended)!.StatusText.Should().Contain("No model-specific profile");
    }

    [Fact]
    public async Task Unknown_hardware_does_not_activate_a_model_profile()
    {
        var overview = await CreateService([Package("com.nvidia.stats")])
            .CreateOverviewAsync("tv-1", new AndroidDevice
            {
                Serial = "tv-1",
                Manufacturer = "Contoso",
                Brand = "contoso",
                Model = "Mystery Box",
                FriendlyName = "NVIDIA SHIELD Android TV darcy",
                AndroidVersion = "14",
                State = DeviceState.Device,
                ConnectionType = ConnectionType.Network
            });

        overview.DetectedProfileLabel.Should().Be("Generic Android TV / Google TV");
        overview.HasModelSpecificProfile.Should().BeFalse();
        overview.ActiveProfiles.Should().NotContain(profile => profile.BaselineId == "nvidia-shield-tv-reviewed");
        Selected(overview, DebloatCleanupKind.Recommended).Should().BeEmpty();
    }

    [Fact]
    public async Task Safe_recommended_and_deep_never_auto_select_unknown_keep_or_runtime_roles()
    {
        var overview = await CreateService([
            Package("com.nvidia.stats", isSystem: true),
            Package("com.nvidia.feedback", isSystem: true),
            Package("com.nvidia.ota", isSystem: true),
            Package("com.google.android.apps.tv.launcherx", isSystem: true) with { IsActiveLauncher = true },
            Package("com.vendor.unknown", isSystem: true)
        ]).CreateOverviewAsync("tv-1", ShieldDarcy());

        foreach (var kind in new[] { DebloatCleanupKind.Safe, DebloatCleanupKind.Recommended, DebloatCleanupKind.Deep })
        {
            var selected = Selected(overview, kind);
            selected.Should().NotContain("com.nvidia.ota");
            selected.Should().NotContain("com.google.android.apps.tv.launcherx");
            selected.Should().NotContain("com.vendor.unknown");
            overview.SourcePlan.Items.Single(item => item.Package.PackageName == "com.vendor.unknown")
                .Assessment.Risk.Should().Be(PackageRiskLevel.Unknown);
        }

        Selected(overview, DebloatCleanupKind.Safe).Should().BeEmpty();
        Selected(overview, DebloatCleanupKind.Recommended).Should().Contain("com.nvidia.stats");
        Selected(overview, DebloatCleanupKind.Deep).Should().Contain("com.nvidia.stats");
        overview.Profile(DebloatCleanupKind.Deep)!.AdditionalActionCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Already_cleaned_recommended_packages_are_not_selected_again()
    {
        var overview = await CreateService([
            Package("com.nvidia.stats", isSystem: true, isEnabled: false)
        ]).CreateOverviewAsync("tv-1", ShieldDarcy());

        var recommended = overview.Profile(DebloatCleanupKind.Recommended)!;
        recommended.HasActions.Should().BeFalse();
        recommended.IsAlreadyClean.Should().BeTrue();
        recommended.ActionLabel.Should().Be("Clean");
        recommended.StatusText.Should().Contain("already matches");
        Selected(overview, DebloatCleanupKind.Recommended).Should().BeEmpty();
    }

    [Fact]
    public async Task Zero_safe_candidates_is_valid_and_explained()
    {
        var overview = await CreateService([
            Package("com.nvidia.ota", isSystem: true)
        ]).CreateOverviewAsync("tv-1", ShieldDarcy());

        var safe = overview.Profile(DebloatCleanupKind.Safe)!;
        safe.ActionCount.Should().Be(0);
        safe.HasActions.Should().BeFalse();
        safe.Description.Should().Be("No Safe Cleanup actions are available for this device.");
        safe.StatusText.Should().Be("No Safe Cleanup actions are available for this device.");
    }

    private static string[] Selected(DebloatCleanupOverview overview, DebloatCleanupKind kind)
        => new DebloatCleanupService(null!).CreateProfilePlan(overview, kind).Items
            .Where(item => item.Selected)
            .Select(item => item.Package.PackageName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static DebloatCleanupService CreateService(IReadOnlyList<PackageInventoryEntry> packages)
        => new(new DebloatPlanner(
            new FixedInventory(packages),
            new PackageClassifier(),
            new PackageReferenceCatalog(),
            new FakeDeviceSnapshotRepository(),
            new EmptyPreferences()));

    private static AndroidDevice ShieldDarcy()
        => Hardware("NVIDIA", "NVIDIA", "SHIELD Android TV", "darcy", "darcy", "11");

    private static AndroidDevice Streamer()
        => Hardware("Google", "google", "Google TV Streamer", "kirkwood", "kirkwood", "14", "kirkwood",
            "google/kirkwood/kirkwood:14/UTT3.240625.001.K5/12147201:user/release-keys");

    private static AndroidDevice Chromecast()
        => Hardware("Google", "google", "Chromecast", "sabrina_prod_stable", "sabrina", "12",
            fingerprint: "google/sabrina_prod_stable/sabrina:12/STTE.240615.007/12033466:user/release-keys");

    private static AndroidDevice OnnYoc()
        => Hardware("onn", "onn", "onn. 4K Streaming Box", "onn_4k_gtv", "YOC", "12",
            fingerprint: "onn/onn_4k_gtv/YOC:12/SGZ2.230609.049.A1/11261715:user/release-keys");

    private static AndroidDevice Hardware(
        string manufacturer,
        string brand,
        string model,
        string product,
        string deviceName,
        string androidVersion,
        string? board = null,
        string? fingerprint = null)
        => new()
        {
            Serial = "tv-1",
            Manufacturer = manufacturer,
            Brand = brand,
            Model = model,
            Product = product,
            DeviceName = deviceName,
            Board = board,
            BuildFingerprint = fingerprint,
            AndroidVersion = androidVersion,
            ApiLevel = int.Parse(androidVersion) + 19,
            State = DeviceState.Device,
            ConnectionType = ConnectionType.Network
        };

    private static PackageInventoryEntry Package(string name, bool isSystem = false, bool isEnabled = true)
        => new(name, null, null, null, "0", isSystem, false, isEnabled, true, false, [],
            DateTimeOffset.UtcNow, "tv-1", "14", "fingerprint");

    private sealed class FixedInventory(IReadOnlyList<PackageInventoryEntry> packages) : IPackageInventoryService
    {
        public Task<PackageInventoryResult> GetInventoryAsync(string serial, CancellationToken cancellationToken = default)
            => Task.FromResult(new PackageInventoryResult(serial, DateTimeOffset.UtcNow, packages, [], null));

        public Task<PackageInventoryEntry?> GetDetailsAsync(
            string serial, string packageName, CancellationToken cancellationToken = default)
            => Task.FromResult(packages.FirstOrDefault(package =>
                package.PackageName.Equals(packageName, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class EmptyPreferences : IPackagePreferenceRepository
    {
        public Task<IReadOnlyDictionary<string, PackageOverride>> GetOverridesAsync(
            string serial, CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyDictionary<string, PackageOverride>)
                new Dictionary<string, PackageOverride>(StringComparer.OrdinalIgnoreCase));

        public Task SetOverrideAsync(
            string serial, string packageName, PackageOverride value, string? note = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string?> GetNoteAsync(string serial, string packageName, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task SetNoteAsync(string serial, string packageName, string note, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
