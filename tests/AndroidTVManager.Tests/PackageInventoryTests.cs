using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Adb;
using AndroidTVManager.Infrastructure.Packages;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class PackageInventoryTests
{
    [Fact]
    public void Parses_base_and_split_apk_paths()
    {
        var packages = PackageInventoryParser.ParsePackagePaths("""
            package:/data/app/~~abc==/com.example/base.apk=com.example
            package:/data/app/~~abc==/com.example/split_config.arm64_v8a.apk=com.example
            package:/system/priv-app/Settings/Settings.apk=com.android.settings
            """);

        packages["com.example"].Should().HaveCount(2);
        packages["com.android.settings"].Single().Should().Contain("/system/");
    }

    [Fact]
    public void Parses_package_name_sets_and_ignores_non_package_lines()
    {
        var packages = PackageInventoryParser.ParsePackageNames("""
            package:com.example.one
            package:com.example.two uid:10023
            warning: inaccessible
            """);

        packages.Should().BeEquivalentTo(["com.example.one", "com.example.two"]);
    }

    [Fact]
    public void Parses_package_details_and_disabled_state()
    {
        var details = PackageInventoryParser.ParseDetails("com.example.app", """
            versionCode=42 minSdk=29 targetSdk=34
            versionName=2.5.1
            userId=10123
            path:/data/app/com.example/base.apk
            enabled=false
            """);

        details.VersionName.Should().Be("2.5.1");
        details.VersionCode.Should().Be(42);
        details.UserId.Should().Be("10123");
        details.IsEnabled.Should().BeFalse();
        details.ApkPaths.Should().ContainSingle();
    }

    [Fact]
    public void Parses_device_owner_without_counting_policy_package_lists()
    {
        var owners = PackageInventoryParser.ParseDeviceOwnerPackages("""
            Device policy manager state:
              Device Owner: null
              Lock task packages: [com.purefusion.iptv]
              Keep uninstalled packages: [com.purefusion.iptv]
              mUserControlDisabledPackages=[com.purefusion.iptv]
            """);

        owners.Should().BeEmpty();
    }

    [Fact]
    public void Parses_actual_device_owner_component()
    {
        var owners = PackageInventoryParser.ParseDeviceOwnerPackages("""
            Device Owner:
              admin=ComponentInfo{com.enterprise.dpc/.DeviceAdminReceiver}
              package=com.enterprise.dpc
            """);

        owners.Should().BeEquivalentTo(["com.enterprise.dpc"]);
    }

    [Fact]
    public async Task Inventory_keeps_iptv_player_unknown_when_it_is_not_an_active_role()
    {
        var runner = new FakeAdbProcessRunner();
        runner.Responses["shell pm list packages -f"] = Result("""
            package:/system/priv-app/TvLauncher/TvLauncher.apk=com.android.tvlauncher
            package:/data/app/~~iptv/base.apk=com.purefusion.iptv
            """);
        runner.Responses["shell pm list packages -s"] = Result("package:com.android.tvlauncher");
        runner.Responses["shell pm list packages -3"] = Result("package:com.purefusion.iptv");
        runner.Responses["shell pm list packages -e"] = Result("""
            package:com.android.tvlauncher
            package:com.purefusion.iptv
            """);
        runner.Responses["shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.HOME"] =
            Result("com.android.tvlauncher/.TvLauncherActivity");
        runner.Responses["shell settings get secure default_input_method"] =
            Result("com.google.android.inputmethod.latin/.LatinIME");
        runner.Responses["shell settings get secure enabled_input_methods"] =
            Result("com.google.android.inputmethod.latin/.LatinIME:com.google.android.tts/com.google.android.apps.speech.tts.googletts.service.GoogleTTSVoiceIME");
        runner.Responses["shell settings get secure enabled_accessibility_services"] =
            Result("com.google.android.marvin.talkback/.TalkBackService");
        runner.Responses["shell dumpsys device_policy"] = Result("""
            Device policy manager state:
              Device Owner: null
              Lock task packages: [com.purefusion.iptv]
              Keep uninstalled packages: [com.purefusion.iptv]
            """);
        var service = new PackageInventoryService(
            runner,
            new CapturingPackageInventoryRepository(),
            new FakeAppLogger());

        var inventory = await service.GetInventoryAsync("tv-1");
        var iptv = inventory.Packages.Single(package => package.PackageName == "com.purefusion.iptv");
        var context = PackageClassificationContexts.FromInventory(
            new AndroidDevice { Serial = "tv-1", State = DeviceState.Device },
            inventory.Packages);
        var assessment = new PackageClassifier().Classify(iptv, context);

        iptv.IsActiveLauncher.Should().BeFalse();
        iptv.IsDefaultInputMethod.Should().BeFalse();
        iptv.IsEnabledAccessibilityService.Should().BeFalse();
        iptv.IsDeviceOwner.Should().BeFalse();
        assessment.Risk.Should().Be(PackageRiskLevel.Unknown);
        assessment.IsProtected.Should().BeFalse();
    }

    [Fact]
    public async Task Inventory_marks_default_and_enabled_input_method_packages()
    {
        var runner = new FakeAdbProcessRunner();
        runner.Responses["shell pm list packages -f"] = Result("""
            package:/product/app/LatinImeGoogle/LatinImeGoogle.apk=com.google.android.inputmethod.latin
            package:/product/app/GoogleTTS/GoogleTTS.apk=com.google.android.tts
            package:/data/app/~~iptv/base.apk=com.purefusion.iptv
            """);
        runner.Responses["shell pm list packages -s"] = Result("""
            package:com.google.android.inputmethod.latin
            package:com.google.android.tts
            """);
        runner.Responses["shell pm list packages -3"] = Result("package:com.purefusion.iptv");
        runner.Responses["shell pm list packages -e"] = Result("""
            package:com.google.android.inputmethod.latin
            package:com.google.android.tts
            package:com.purefusion.iptv
            """);
        runner.Responses["shell settings get secure default_input_method"] =
            Result("com.google.android.inputmethod.latin/.LatinIME");
        runner.Responses["shell settings get secure enabled_input_methods"] =
            Result("com.google.android.inputmethod.latin/.LatinIME:com.google.android.tts/com.google.android.apps.speech.tts.googletts.service.GoogleTTSVoiceIME");
        var service = new PackageInventoryService(
            runner,
            new CapturingPackageInventoryRepository(),
            new FakeAppLogger());

        var inventory = await service.GetInventoryAsync("tv-1");

        inventory.Packages.Single(package => package.PackageName == "com.google.android.inputmethod.latin")
            .IsDefaultInputMethod.Should().BeTrue();
        inventory.Packages.Single(package => package.PackageName == "com.google.android.tts")
            .IsDefaultInputMethod.Should().BeTrue();
        inventory.Packages.Single(package => package.PackageName == "com.purefusion.iptv")
            .IsDefaultInputMethod.Should().BeFalse();
    }

    private static AdbCommandResult Result(string output)
        => new("adb.exe", [], 0, output, string.Empty, TimeSpan.Zero);

    private sealed class CapturingPackageInventoryRepository : IPackageInventoryRepository
    {
        public PackageInventoryResult? Saved { get; private set; }

        public Task<long> SaveAsync(
            PackageInventoryResult inventory,
            CancellationToken cancellationToken = default)
        {
            Saved = inventory;
            return Task.FromResult(1L);
        }

        public Task<PackageInventoryResult?> GetLatestAsync(
            string serial,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Saved?.Serial == serial ? Saved : null);
    }
}
