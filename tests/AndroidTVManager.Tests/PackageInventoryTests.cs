using AndroidTVManager.Core.Adb;
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
}
