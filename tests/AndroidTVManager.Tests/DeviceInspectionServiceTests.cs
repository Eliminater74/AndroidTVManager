using System.Text.RegularExpressions;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Adb;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DeviceInspectionServiceTests
{
    [Fact]
    public async Task Inspects_categories_with_one_target_and_keeps_partial_failures()
    {
        var runner = new FakeAdbProcessRunner();
        runner.Responses["shell getprop"] = Result("""
            [ro.product.manufacturer]: [Philips]
            [ro.product.model]: [OLED TV]
            [ro.build.version.release]: [14]
            [ro.build.version.sdk]: [34]
            [ro.treble.enabled]: [true]
            [ro.boot.super_partition]: [super]
            [ro.oem_unlock_supported]: [1]
            [sys.oem_unlock_allowed]: [0]
            [ro.boot.flash.locked]: [1]
            [ro.boot.vbmeta.device_state]: [locked]
            [ro.build.type]: [user]
            [ro.boot.verifiedbootstate]: [green]
            [net.dns1]: [192.168.1.1]
            """);
        runner.Responses["shell cat /proc/cpuinfo"] = Result("processor : 0\nprocessor : 1");
        runner.Responses["shell cat /proc/meminfo"] = Result("MemTotal: 2048 kB");
        runner.Responses["shell pm list features"] = Result("feature:android.software.leanback");
        runner.Responses["shell pm list packages -f"] =
            Result("package:/system/app/Settings/Settings.apk=com.android.settings\npackage:/data/app/tv.apk=com.example.tv");
        runner.Responses["shell pm list packages -s"] = Result("package:com.android.settings");
        runner.Responses["shell pm list packages -3"] = Result("package:com.example.tv");
        runner.Responses["shell pm list packages -d"] = Result("package:com.example.disabled");
        runner.Responses["shell pm list packages -e"] = Result("package:com.android.settings\npackage:com.example.tv");
        runner.Responses["shell pm list packages -u"] =
            Result("package:com.android.settings\npackage:com.example.tv\npackage:com.example.removed");
        runner.Responses["shell ip route"] = Result("default via 192.168.1.1 dev wlan0");
        runner.Responses["shell settings get global oem_unlock_allowed"] = Result("0");
        runner.Responses["shell pm list packages com.google.android.verifier"] =
            Result("package:com.google.android.verifier");
        runner.Responses["shell dumpsys display"] = Result(string.Empty, "dumpsys unavailable", 1);
        var snapshots = new FakeDeviceSnapshotRepository();
        var service = new DeviceInspectionService(runner, snapshots, new FakeAppLogger());
        var progress = new List<string>();

        var inspection = await service.InspectAsync("192.168.1.10:5555",
            new Progress<DeviceInspectionProgress>(value => progress.Add(value.Category)));

        inspection.Overview.Value!.Manufacturer.Should().Be("Philips");
        inspection.Cpu.Value!.LogicalCoreCount.Should().Be(2);
        inspection.DeveloperVerification.Value!.VerifierPresent.Should().BeTrue();
        inspection.OemUnlock!.Value!.Option.Should().Be(OemUnlockOptionState.Present);
        inspection.OemUnlock.Value.Setting.Should().Be(OemUnlockSettingState.LockedByDevice);
        inspection.Root!.Value!.AdbRootFeasibility.Should().Be(CapabilityState.Unsupported);
        inspection.Network.Value!.Gateway.Should().Be("192.168.1.1");
        inspection.Packages.Value!.UninstalledForUserCount.Should().Be(1);
        inspection.Packages.Value.PackageNames.Should().Contain("com.example.removed");
        inspection.Display.State.Should().Be(InspectionSectionState.Partial);
        inspection.Capabilities.Should().Contain(capability => capability.Name == "ADB APK Installation"
            && capability.State == CapabilityState.Supported);
        snapshots.Latest.Should().BeSameAs(inspection);
        runner.Calls.Should().NotBeEmpty();
        runner.Calls.Should().OnlyContain(call => call.Serial == "192.168.1.10:5555");
        runner.Calls.Select(call => string.Join(" ", call.Arguments))
            .Should().NotContain(command => Regex.IsMatch(command, @"(^| )root( |$)", RegexOptions.IgnoreCase)
                || command.Contains("fastboot", StringComparison.OrdinalIgnoreCase)
                || command.Contains("oem unlock", StringComparison.OrdinalIgnoreCase)
                || command.Contains("reboot", StringComparison.OrdinalIgnoreCase)
                || command.Contains("su -c", StringComparison.OrdinalIgnoreCase));
        runner.Calls.Select(call => string.Join(" ", call.Arguments))
            .Should().Contain("shell pm list packages -u");
    }

    [Fact]
    public async Task Inspection_honors_cancellation_before_running_commands()
    {
        var runner = new FakeAdbProcessRunner();
        var service = new DeviceInspectionService(runner, new FakeDeviceSnapshotRepository(), new FakeAppLogger());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.InspectAsync("tv-1", cancellationToken: cancellation.Token));
        runner.Calls.Should().BeEmpty();
    }

    private static AndroidTVManager.Core.Models.AdbCommandResult Result(
        string output,
        string error = "",
        int exitCode = 0)
        => new("adb.exe", [], exitCode, output, error, TimeSpan.Zero);
}
