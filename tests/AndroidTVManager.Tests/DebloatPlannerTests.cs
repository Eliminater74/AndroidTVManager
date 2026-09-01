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

    [Fact]
    public async Task Reference_recommendation_can_select_reviewed_optional_package()
    {
        var planner = CreatePlanner([Package("com.tcl.initsetup", isSystem: true)]);

        var plan = await planner.CreatePlanAsync(
            "tv-1",
            DebloatPreset.Medium,
            Device("TCL", "65C7K", androidVersion: "11"));

        var item = plan.Items.Single();

        item.Reference.Should().NotBeNull();
        item.Reference!.Origin.Should().Be(PackageOrigin.Oem);
        item.Assessment.Risk.Should().Be(PackageRiskLevel.Caution);
        item.Assessment.RecommendedAction.Should().Be("Disable");
        item.Assessment.IsProtected.Should().BeFalse();
        item.Selected.Should().BeTrue();
        item.SelectionBlockReason.Should().BeNull();
    }

    [Fact]
    public async Task Aggressive_preset_does_not_select_high_risk_keep_rules()
    {
        var planner = CreatePlanner([Package("com.tivo.tvlaunchercustomization", isSystem: true)]);

        var plan = await planner.CreatePlanAsync(
            "tv-1",
            DebloatPreset.Aggressive,
            Device("SEI Robotics", "TiVo Stream 4K", androidVersion: "10"));

        var item = plan.Items.Single();

        item.Assessment.Risk.Should().Be(PackageRiskLevel.HighRisk);
        item.Assessment.RecommendedAction.Should().Be("Keep");
        item.Selected.Should().BeFalse();
        item.SelectionBlockReason.Should().Contain("Locked");
    }

    [Fact]
    public async Task Android_16_emulator_profile_selects_optional_candidates_and_locks_core()
    {
        var planner = CreatePlanner([
            Package("com.android.dreams.basic", isSystem: true),
            Package("com.google.android.backdrop", isSystem: true),
            Package("com.google.android.tvrecommendations", isSystem: true),
            Package("com.google.android.tvlauncher", isSystem: true) with { IsActiveLauncher = true },
            Package("dev.eliminater.purefusioniptv", isSystem: false)
        ]);

        var plan = await planner.CreatePlanAsync(
            "tv-1",
            DebloatPreset.Medium,
            Device("Google", "sdk_google_atv64_x86_64", "16", apiLevel: 36));

        var dreams = plan.Items.Single(item => item.Package.PackageName == "com.android.dreams.basic");
        var backdrop = plan.Items.Single(item => item.Package.PackageName == "com.google.android.backdrop");
        var recommendations = plan.Items.Single(item => item.Package.PackageName == "com.google.android.tvrecommendations");
        var launcher = plan.Items.Single(item => item.Package.PackageName == "com.google.android.tvlauncher");
        var iptv = plan.Items.Single(item => item.Package.PackageName == "dev.eliminater.purefusioniptv");

        dreams.Assessment.Risk.Should().Be(PackageRiskLevel.Caution);
        dreams.Assessment.RecommendedAction.Should().Be("Disable");
        dreams.Action.Should().Be(DebloatAction.Disable);
        dreams.Selected.Should().BeTrue();
        backdrop.Selected.Should().BeTrue();
        recommendations.Selected.Should().BeTrue();

        launcher.Assessment.Risk.Should().Be(PackageRiskLevel.Critical);
        launcher.Assessment.IsProtected.Should().BeTrue();
        launcher.Selected.Should().BeFalse();
        iptv.Assessment.Risk.Should().Be(PackageRiskLevel.Unknown);
        iptv.Selected.Should().BeFalse();
        plan.ReferenceSummary!.ProfileMatches.Should().Contain(profile =>
            profile.BaselineId == "google-atv-emulator-api36");
    }

    [Fact]
    public async Task Already_disabled_candidates_are_not_selected_again()
    {
        var planner = CreatePlanner([
            Package("com.android.dreams.basic", isSystem: true, isEnabled: false)
        ]);

        var plan = await planner.CreatePlanAsync(
            "tv-1",
            DebloatPreset.Medium,
            Device("Google", "sdk_google_atv64_x86_64", "16", apiLevel: 36));

        var item = plan.Items.Single();

        item.Assessment.Risk.Should().Be(PackageRiskLevel.Caution);
        item.Selected.Should().BeFalse();
        item.SelectionBlockReason.Should().Be("Package is already disabled.");
    }

    [Fact]
    public async Task Planner_uses_reviewed_uninstall_for_user_actions()
    {
        var planner = CreatePlanner(
            [Package("com.example.optional", isSystem: true)],
            new FixedClassifier("Uninstall for user 0"));

        var plan = await planner.CreatePlanAsync(
            "tv-1",
            DebloatPreset.Medium,
            Device("Example", "Example TV", "14"));

        var item = plan.Items.Single();

        item.Action.Should().Be(DebloatAction.UninstallForUser);
        item.Selected.Should().BeTrue();
    }

    private static DebloatPlanner CreatePlanner(
        IReadOnlyList<PackageInventoryEntry> packages,
        IPackageClassifier? classifier = null)
        => new(
            new FixedInventoryService(packages),
            classifier ?? new PackageClassifier(),
            new PackageReferenceCatalog(),
            new FakeDeviceSnapshotRepository(),
            new EmptyPackagePreferenceRepository());

    private static AndroidDevice Device(
        string manufacturer,
        string model,
        string androidVersion,
        int? apiLevel = null)
        => new()
        {
            Serial = "tv-1",
            Manufacturer = manufacturer,
            Brand = manufacturer,
            Model = model,
            AndroidVersion = androidVersion,
            ApiLevel = apiLevel ?? int.Parse(androidVersion) + 19,
            State = DeviceState.Device,
            ConnectionType = ConnectionType.Network
        };

    private static PackageInventoryEntry Package(
        string name,
        bool isSystem = false,
        bool isEnabled = true)
        => new(
            name,
            null,
            null,
            null,
            "0",
            isSystem,
            false,
            isEnabled,
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

    private sealed class FixedClassifier : IPackageClassifier
    {
        private readonly string _recommendedAction;

        public FixedClassifier(string recommendedAction)
        {
            _recommendedAction = recommendedAction;
        }

        public PackageAssessment Classify(
            PackageInventoryEntry package,
            PackageClassificationContext context)
            => new(
                package.PackageName,
                PackageRiskLevel.Caution,
                PackageConfidence.High,
                "Optional package",
                "Optional test package.",
                _recommendedAction,
                ["Synthetic classifier rule."],
                [],
                false,
                "test");
    }
}
