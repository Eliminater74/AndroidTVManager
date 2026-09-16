using System.Text.Json;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Packages;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class PackageReferenceCatalogTests
{
    private readonly PackageReferenceCatalog _catalog = new();

    [Fact]
    public async Task Matches_the_applicable_AOSP_generation_and_Google_reference()
    {
        var device = Device("Google", "Chromecast with Google TV", "12");
        var packages = new[]
        {
            Package("com.android.tv.settings"),
            Package("com.google.android.apps.tv.launcherx"),
            Package("com.example.unknown")
        };

        var analysis = await _catalog.AnalyzeAsync(device, packages);

        analysis.Summary.TotalPackages.Should().Be(3);
        analysis.Summary.UnknownPackages.Should().Be(1);
        analysis.Packages.Single(package => package.PackageName == "com.android.tv.settings")
            .Origin.Should().Be(PackageOrigin.AospTvCore);
        var launcher = analysis.Packages.Single(package =>
            package.PackageName == "com.google.android.apps.tv.launcherx");
        launcher.Origin.Should().Be(PackageOrigin.GoogleTvGms);
        launcher.Matches.Should().ContainSingle();
        launcher.Matches[0].Dependencies.Should()
            .Contain("com.google.android.tungsten.setupwraith");
    }

    [Fact]
    public async Task Keeps_multiple_reference_matches_without_merging_their_origins()
    {
        var device = Device("TCL", "65C7K", "11");
        var analysis = await _catalog.AnalyzeAsync(
            device,
            [Package("com.tcl.tv"), Package("com.android.tv.frameworkpackagestubs")]);

        var tcl = analysis.Packages.Single(package => package.PackageName == "com.tcl.tv");
        tcl.Origin.Should().Be(PackageOrigin.Oem);
        tcl.Matches.Should().ContainSingle();
        tcl.Matches[0].FeatureImpacts.Should().Contain(impact => impact.Area == "TV and HDMI");
        analysis.Packages.Single(package =>
                package.PackageName == "com.android.tv.frameworkpackagestubs")
            .Origin.Should().Be(PackageOrigin.AospTvCore);
    }

    [Fact]
    public async Task Google_tv_launcher_reference_is_keep_not_safe_candidate()
    {
        var device = Device("Google", "Chromecast with Google TV", "12");
        var analysis = await _catalog.AnalyzeAsync(
            device,
            [Package("com.google.android.apps.tv.launcherx")]);

        var match = analysis.Packages[0].Matches[0];
        match.ActiveRoleProtection.Should().BeTrue();
        match.Risk.Should().Be(PackageRiskLevel.Critical);
        match.RecommendedAction.Should().Be("Keep");
    }

    [Fact]
    public async Task Reference_baseline_can_carry_reviewed_risk_and_action()
    {
        var device = Device("TCL", "65C7K", "11");

        var analysis = await _catalog.AnalyzeAsync(device, [Package("com.tcl.initsetup")]);

        var match = analysis.Packages.Single().Matches.Single();

        match.Origin.Should().Be(PackageOrigin.Oem);
        match.Risk.Should().Be(PackageRiskLevel.Caution);
        match.RecommendedAction.Should().Be("Disable");
        match.SourceConfidence.Should().Be(PackageSourceConfidence.MultiSourceCommunityEvidence);
    }

    [Fact]
    public async Task Google_tv_reference_exact_packages_apply_across_oem_devices()
    {
        var device = Device("TCL", "65C7K", "11");

        var analysis = await _catalog.AnalyzeAsync(
            device,
            [Package("com.google.android.tungsten.setupwraith")]);

        var match = analysis.Packages.Single().Matches.Single();

        match.Origin.Should().Be(PackageOrigin.GoogleTvGms);
        match.Risk.Should().Be(PackageRiskLevel.HighRisk);
        match.RecommendedAction.Should().Be("Keep");
        match.ObservedOn.Should().Contain("Chromecast with Google TV GA01919-US");
    }

    [Fact]
    public async Task Android_16_emulator_uses_current_aosp_and_google_emulator_profiles()
    {
        var device = Device("Google", "sdk_google_atv64_x86_64", "16", apiLevel: 36);

        var analysis = await _catalog.AnalyzeAsync(
            device,
            [
                Package("com.android.dreams.basic"),
                Package("com.android.tv.feedbackconsent"),
                Package("com.google.android.tvlauncher"),
                Package("com.google.android.play.games"),
                Package("dev.eliminater.purefusioniptv")
            ]);

        var dreams = analysis.Packages.Single(package => package.PackageName == "com.android.dreams.basic");
        dreams.Origin.Should().Be(PackageOrigin.AospTvCore);
        dreams.Matches.Should().Contain(match =>
            match.Risk == PackageRiskLevel.Caution
            && match.RecommendedAction == "Disable"
            && match.Role!.Contains("screensaver", StringComparison.OrdinalIgnoreCase));

        var launcher = analysis.Packages.Single(package => package.PackageName == "com.google.android.tvlauncher");
        launcher.Origin.Should().Be(PackageOrigin.GoogleTvGms);
        launcher.Matches.Should().Contain(match =>
            match.Risk == PackageRiskLevel.Critical
            && match.RecommendedAction == "Keep");

        var games = analysis.Packages.Single(package => package.PackageName == "com.google.android.play.games");
        games.Matches.Should().Contain(match =>
            match.Risk == PackageRiskLevel.Caution
            && match.RecommendedAction == "Disable"
            && match.FeatureImpacts.Any(impact => impact.Area == "Games"));

        analysis.Packages.Single(package => package.PackageName == "dev.eliminater.purefusioniptv")
            .Origin.Should().Be(PackageOrigin.Unknown);
        analysis.Summary.ProfileMatches.Should().NotBeNull();
        analysis.Summary.ProfileMatches!.Should().Contain(profile =>
            profile.BaselineId == "aosp-tv-android15-current"
            && profile.MatchedPackages >= 2);
        analysis.Summary.ProfileMatches.Should().Contain(profile =>
            profile.BaselineId == "google-atv-emulator-api36"
            && profile.MatchedPackages >= 2);
    }

    [Fact]
    public async Task Streamer_activates_kirkwood_overlay_and_google_tv_core_without_sabrina()
    {
        var device = HardwareDevice(
            manufacturer: "Google",
            brand: "google",
            model: "Google TV Streamer",
            product: "kirkwood",
            deviceName: "kirkwood",
            board: "kirkwood",
            fingerprint: "google/kirkwood/kirkwood:14/UTT3.240625.001.K5/12147201:user/release-keys",
            androidVersion: "14");
        var analysis = await _catalog.AnalyzeAsync(device, [
            Package("com.google.android.apps.tv.launcherx"),
            Package("com.google.android.gms"),
            Package("com.google.android.apps.tv.netoscope")
        ]);

        analysis.Summary.ProfileMatches.Should().Contain(profile =>
            profile.BaselineId == "google-tv-chromecast-ga01919");
        analysis.Summary.ProfileMatches.Should().Contain(profile =>
            profile.BaselineId == "google-tv-streamer-kirkwood-4k");
        analysis.Summary.ProfileMatches.Should().NotContain(profile =>
            profile.BaselineId == "google-tv-chromecast-sabrina-4k");
        analysis.Summary.ProfileMatches.Should().NotContain(profile =>
            profile.BaselineId == "onn-google-tv-4k-box-yoc");
        analysis.Summary.ProfileMatches.Should().NotContain(profile =>
            profile.BaselineId == "nvidia-shield-tv-reviewed");

        var launcher = analysis.Packages.Single(package =>
            package.PackageName == "com.google.android.apps.tv.launcherx");
        launcher.Matches.Should().Contain(match => match.BaselineId == "google-tv-chromecast-ga01919"
            && match.RecommendedAction == "Keep");
        launcher.Matches.Should().Contain(match => match.BaselineId == "google-tv-streamer-kirkwood-4k"
            && match.RecommendedAction == "Keep");
        launcher.Matches.Should().OnlyContain(match => match.RecommendedAction != "Disable");

        analysis.Packages.Single(package => package.PackageName == "com.google.android.gms")
            .Matches.Should().Contain(match => match.RecommendedAction == "Keep"
                && match.Risk == PackageRiskLevel.Critical);
        analysis.Packages.Single(package => package.PackageName == "com.google.android.apps.tv.netoscope")
            .Matches.Should().NotContain(match => match.BaselineId == "google-tv-chromecast-sabrina-4k");
    }

    [Fact]
    public async Task Chromecast_4k_activates_sabrina_overlay_without_streamer_or_onn()
    {
        var device = HardwareDevice(
            manufacturer: "Google",
            brand: "google",
            model: "Chromecast",
            product: "sabrina_prod_stable",
            deviceName: "sabrina",
            fingerprint: "google/sabrina_prod_stable/sabrina:12/STTE.240615.007/12033466:user/release-keys",
            androidVersion: "12");
        var analysis = await _catalog.AnalyzeAsync(device, [
            Package("com.google.android.chromecast.chromecastservice"),
            Package("com.google.android.apps.tv.netoscope"),
            Package("com.google.android.apps.tv.launcherx")
        ]);

        analysis.Summary.ProfileMatches.Should().Contain(profile =>
            profile.BaselineId == "google-tv-chromecast-ga01919");
        analysis.Summary.ProfileMatches.Should().Contain(profile =>
            profile.BaselineId == "google-tv-chromecast-sabrina-4k");
        analysis.Summary.ProfileMatches.Should().NotContain(profile =>
            profile.BaselineId == "google-tv-streamer-kirkwood-4k");

        analysis.Packages.Single(package =>
                package.PackageName == "com.google.android.chromecast.chromecastservice")
            .Matches.Should().Contain(match => match.RecommendedAction == "Keep"
                && match.Risk == PackageRiskLevel.Critical);
        analysis.Packages.Single(package =>
                package.PackageName == "com.google.android.apps.tv.netoscope")
            .Matches.Should().Contain(match => match.BaselineId == "google-tv-chromecast-sabrina-4k"
                && match.RecommendedAction == "Disable");
    }

    [Fact]
    public async Task Onn_yoc_activates_box_overlay_and_google_tv_core()
    {
        var device = HardwareDevice(
            manufacturer: "onn",
            brand: "onn",
            model: "onn. 4K Streaming Box",
            product: "onn_4k_gtv",
            deviceName: "YOC",
            fingerprint: "onn/onn_4k_gtv/YOC:12/SGZ2.230609.049.A1/11261715:user/release-keys",
            androidVersion: "12");
        var analysis = await _catalog.AnalyzeAsync(device, [
            Package("com.google.android.apps.tv.launcherx"),
            Package("com.android.tv.settings"),
            Package("com.walmart.onn.unknown")
        ]);

        analysis.Summary.ProfileMatches.Should().Contain(profile =>
            profile.BaselineId == "google-tv-chromecast-ga01919");
        analysis.Summary.ProfileMatches.Should().Contain(profile =>
            profile.BaselineId == "onn-google-tv-4k-box-yoc");
        analysis.Summary.ProfileMatches.Should().NotContain(profile =>
            profile.BaselineId == "google-tv-chromecast-sabrina-4k");
        analysis.Summary.ProfileMatches.Should().NotContain(profile =>
            profile.BaselineId == "google-tv-streamer-kirkwood-4k");

        analysis.Packages.Single(package => package.PackageName == "com.android.tv.settings")
            .Matches.Should().Contain(match => match.RecommendedAction == "Keep"
                && match.Risk == PackageRiskLevel.Critical);
        analysis.Packages.Single(package => package.PackageName == "com.walmart.onn.unknown")
            .Origin.Should().Be(PackageOrigin.Unknown);
    }

    [Fact]
    public async Task Friendly_name_alone_does_not_activate_hardware_overlays()
    {
        var device = new AndroidDevice
        {
            Serial = "reference-device",
            FriendlyName = "Google TV Streamer kirkwood sabrina YOC SHIELD",
            ReportedName = "onn. Google TV 4K Box",
            AndroidVersion = "14",
            ApiLevel = 34
        };

        var analysis = await _catalog.AnalyzeAsync(device, [
            Package("com.google.android.apps.tv.launcherx"),
            Package("com.nvidia.stats")
        ]);

        analysis.Summary.ProfileMatches.Should().NotContain(profile =>
            profile.BaselineId == "google-tv-streamer-kirkwood-4k");
        analysis.Summary.ProfileMatches.Should().NotContain(profile =>
            profile.BaselineId == "google-tv-chromecast-sabrina-4k");
        analysis.Summary.ProfileMatches.Should().NotContain(profile =>
            profile.BaselineId == "onn-google-tv-4k-box-yoc");
        analysis.Summary.ProfileMatches.Should().NotContain(profile =>
            profile.BaselineId == "nvidia-shield-tv-reviewed");
    }

    [Fact]
    public void Reference_profiles_include_the_new_hardware_overlays()
    {
        var profiles = _catalog.GetProfiles();

        profiles.Should().Contain(profile =>
            profile.BaselineId == "aosp-tv-android15-current"
            && profile.PackageRecords > 0);
        profiles.Should().Contain(profile =>
            profile.BaselineId == "google-atv-emulator-api36"
            && profile.Generation == "Android TV 16 emulator");
        profiles.Should().Contain(profile => profile.BaselineId == "google-tv-streamer-kirkwood-4k");
        profiles.Should().Contain(profile => profile.BaselineId == "google-tv-chromecast-sabrina-4k");
        profiles.Should().Contain(profile => profile.BaselineId == "onn-google-tv-4k-box-yoc");
        profiles.Should().Contain(profile => profile.BaselineId == "nvidia-shield-tv-reviewed");
    }

    [Fact]
    public async Task Exported_reference_dump_contains_device_and_package_state_without_serial()
    {
        var output = Path.Combine(Path.GetTempPath(), $"atm-reference-{Guid.NewGuid():N}.json");
        try
        {
            var device = new AndroidDevice
            {
                Serial = "reference-device",
                Manufacturer = "Hisense",
                Brand = "Hisense",
                Model = "55U6G",
                AndroidVersion = "12",
                ApiLevel = 31,
                DeviceName = "hisense-tv",
                Product = "hisense-tv"
            };
            var inventory = new PackageInventoryResult(
                "192.0.2.10:5555",
                DateTimeOffset.UtcNow,
                [Package("com.example.app") with { UserId = "0", IsSystem = false }],
                []);
            var service = new ReferencePackageDumpService();

            await service.ExportAsync(device, inventory, output);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output));
            document.RootElement.GetProperty("device").GetProperty("model")
                .GetString().Should().Be("55U6G");
            document.RootElement.GetProperty("packages").GetArrayLength().Should().Be(1);
            document.RootElement.GetProperty("packages")[0].GetProperty("packageName")
                .GetString().Should().Be("com.example.app");
            document.RootElement.TryGetProperty("serial", out _).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    private static AndroidDevice HardwareDevice(
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
            Serial = "reference-device",
            Manufacturer = manufacturer,
            Brand = brand,
            Model = model,
            Product = product,
            DeviceName = deviceName,
            Board = board,
            BuildFingerprint = fingerprint,
            AndroidVersion = androidVersion,
            ApiLevel = int.Parse(androidVersion) + 19
        };

    private static AndroidDevice Device(
        string manufacturer,
        string model,
        string androidVersion,
        int? apiLevel = null)
        => new()
        {
            Serial = "reference-device",
            Manufacturer = manufacturer,
            Brand = manufacturer,
            Model = model,
            AndroidVersion = androidVersion,
            ApiLevel = apiLevel ?? int.Parse(androidVersion) + 19
        };

    private static PackageInventoryEntry Package(string packageName)
        => new(
            packageName,
            null,
            null,
            null,
            null,
            true,
            false,
            true,
            true,
            false,
            [],
            DateTimeOffset.UtcNow,
            "reference-device",
            null,
            null);
}
