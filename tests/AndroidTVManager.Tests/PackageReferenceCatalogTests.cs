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
    public async Task Origin_metadata_does_not_create_a_safe_debloat_assessment()
    {
        var device = Device("Google", "Chromecast with Google TV", "12");
        var analysis = await _catalog.AnalyzeAsync(
            device,
            [Package("com.google.android.apps.tv.launcherx")]);

        var match = analysis.Packages[0].Matches[0];
        match.ActiveRoleProtection.Should().BeTrue();
        match.Risk.Should().BeNull();
        match.RecommendedAction.Should().BeNull();
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

    private static AndroidDevice Device(string manufacturer, string model, string androidVersion)
        => new()
        {
            Serial = "reference-device",
            Manufacturer = manufacturer,
            Brand = manufacturer,
            Model = model,
            AndroidVersion = androidVersion,
            ApiLevel = int.Parse(androidVersion) + 19
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
