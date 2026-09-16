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
    public async Task Deep_scan_is_opt_in_and_retains_vendor_evidence_and_blocked_probes()
    {
        var runner = new FakeAdbProcessRunner();
        runner.Responses["shell getprop"] = Result("[ro.product.manufacturer]: [ATOTO]\n[vendor.mcu.version]: [example-build]");
        runner.Responses["shell dumpsys usb"] = Result("USB devices: keyboard");
        runner.Responses["shell dumpsys sensorservice"] = Result("Permission Denial: not allowed");
        runner.Responses["shell dumpsys media.codec"] = Result("", "Can't find service: media.codec");
        var service = new DeviceInspectionService(runner, new FakeDeviceSnapshotRepository(), new FakeAppLogger());
        var standard = await service.InspectAsync("unit-1");
        standard.Commands.Should().NotContain(c => c.Command == "usb");
        runner.Calls.Clear();

        var deep = await service.InspectAsync("unit-1", deepScan: true);

        standard.IsDeepScan.Should().BeFalse();
        deep.IsDeepScan.Should().BeTrue();
        deep.Commands.Should().HaveCount(standard.Commands.Count + DeepInspectionCatalog.Probes.Count);
        deep.RawProperties["vendor.mcu.version"].Should().Be("example-build");
        deep.Commands.Single(c => c.Command == "usb").StandardOutput.Should().Contain("keyboard");
        deep.Commands.Single(c => c.Command == "sensors").State.Should().Be(InspectionSectionState.PermissionDenied);
        deep.Commands.Single(c => c.Command == "media-codecs").State.Should().Be(InspectionSectionState.Unavailable);
        runner.Calls.Should().OnlyContain(call => call.Serial == "unit-1");
        runner.Calls.Should().NotContain(call => call.Arguments.Contains("reboot") || call.Arguments.Contains("setprop")
            || call.Arguments.Contains("put") || call.Arguments.Contains("install")
            || IsEscalatingSu(call.Arguments));
        runner.Calls.Select(call => string.Join(" ", call.Arguments))
            .Should().NotContain(command => command.Contains("sh -c", StringComparison.Ordinal));
        DeepInspectionCatalog.Describe(deep).Should().Contain("need review");
        var json = System.Text.Json.JsonSerializer.Serialize(deep);
        var restored = System.Text.Json.JsonSerializer.Deserialize<DeviceInspectionResult>(json)!;
        restored.Commands.Single(c => c.Command == "sensors").State.Should().Be(InspectionSectionState.PermissionDenied);
        restored.RawProperties["vendor.mcu.version"].Should().Be("example-build");
    }

    [Theory]
    [InlineData("Permission denied", 0, InspectionSectionState.PermissionDenied)]
    [InlineData("Can't find service: usb", 0, InspectionSectionState.Unavailable)]
    [InlineData("Unknown command: status", 0, InspectionSectionState.Unavailable)]
    [InlineData("", 0, InspectionSectionState.Partial)]
    [InlineData("device offline", 1, InspectionSectionState.Failed)]
    [InlineData("USB device: keyboard", 0, InspectionSectionState.Completed)]
    [InlineData("Optional vendor description not found in database", 0, InspectionSectionState.Completed)]
    public void Command_classification_does_not_equate_exit_zero_with_available_data(string output, int exit, InspectionSectionState expected)
        => DeepInspectionCatalog.Classify(Result(output, exitCode: exit)).Should().Be(expected);

    [Fact]
    public void Timeout_and_cancellation_are_distinct_from_missing_services()
    {
        DeepInspectionCatalog.Classify(Result("partial") with { WasTimedOut = true }).Should().Be(InspectionSectionState.TimedOut);
        DeepInspectionCatalog.Classify(Result("partial") with { WasCanceled = true }).Should().Be(InspectionSectionState.Canceled);
    }

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
        var commands = runner.Calls.Select(call => string.Join(" ", call.Arguments)).ToArray();
        commands.Should().Contain("shell id");
        commands.Should().Contain("shell which su");
        commands.Should().Contain("shell gsi_tool status");
        commands.Should().NotContain(command => command.Contains("sh -c", StringComparison.Ordinal));
        commands.Should().NotContain(command => Regex.IsMatch(command, @"(^| )root( |$)", RegexOptions.IgnoreCase)
                || command.Contains("fastboot", StringComparison.OrdinalIgnoreCase)
                || command.Contains("oem unlock", StringComparison.OrdinalIgnoreCase)
                || command.Contains("reboot", StringComparison.OrdinalIgnoreCase)
                || command.Contains("su -c", StringComparison.OrdinalIgnoreCase));
        commands.Should().Contain("shell pm list packages -u");
        runner.Calls.Should().NotContain(call => IsEscalatingSu(call.Arguments));
    }

    [Fact]
    public void Optional_unavailable_commands_do_not_partial_a_completed_section()
    {
        var evidence = new InspectionCommandEvidence[]
        {
            new("id", InspectionSectionState.Completed, "uid=2000(shell)", "", 0, TimeSpan.Zero),
            new("which-su", InspectionSectionState.Unavailable, "", "which: su: not found", 1, TimeSpan.Zero)
        };

        DeepInspectionCatalog.CombineSection(evidence, ["which-su"]).Should().Be(InspectionSectionState.Completed);
        DeepInspectionCatalog.CombineSection(
            [new("drm", InspectionSectionState.Unavailable, "", "Can't find service: media.drm", 0, TimeSpan.Zero)])
            .Should().Be(InspectionSectionState.Unavailable);
    }

    [Fact]
    public async Task Stock_shield_does_not_treat_missing_optional_tools_as_partial_security()
    {
        var runner = new FakeAdbProcessRunner();
        runner.Responses["shell getprop"] = Result("""
            [ro.product.manufacturer]: [NVIDIA]
            [ro.product.model]: [SHIELD Android TV]
            [ro.product.device]: [darcy]
            [ro.hardware]: [darcy]
            [ro.board.platform]: [tegra]
            [ro.build.type]: [user]
            [ro.debuggable]: [0]
            [ro.treble.enabled]: [true]
            """);
        runner.Responses["shell id"] = Result("uid=2000(shell) gid=2000(shell)");
        runner.Responses["shell which su"] = Result("", "which: su: not found", 1);
        runner.Responses["shell gsi_tool status"] = Result("", "gsi_tool: not found", 1);
        runner.Responses["shell getenforce"] = Result("Enforcing");
        runner.Responses["shell pm list features"] = Result("""
            feature:android.software.leanback
            feature:android.hardware.hdmi.cec
            feature:android.hardware.vulkan.version=4198400
            """);
        runner.Responses["shell dumpsys hdmi_control"] = Result("mHdmiCecEnabled: true\nactive input: HDMI1");
        runner.Responses["shell dumpsys media.drm"] = Result("", "Can't find service: media.drm");
        runner.Responses["shell pm list packages -f"] = Result("package:/system/app/Settings.apk=com.android.settings");
        var service = new DeviceInspectionService(runner, new FakeDeviceSnapshotRepository(), new FakeAppLogger());

        var inspection = await service.InspectAsync("192.168.1.64:5555");

        inspection.Security.State.Should().Be(InspectionSectionState.Completed);
        inspection.Root!.State.Should().Be(InspectionSectionState.Completed);
        inspection.Root.Value!.CurrentShellRoot.Should().Be(CapabilityState.Unsupported);
        inspection.Root.Value.SuAvailability.Should().Be(CapabilityState.Unsupported);
        inspection.Gsi.State.Should().Be(InspectionSectionState.Completed);
        inspection.Gsi.Value!.GsiTool.Should().Be(CapabilityState.Unsupported);
        inspection.Hdmi!.Value!.Support.Should().Be(CapabilityState.Supported);
        inspection.Drm!.State.Should().Be(InspectionSectionState.Unavailable);
        inspection.Drm.Value!.Availability.Should().Be(CapabilityState.Unavailable);
        inspection.Cpu.Value!.DetectedSoC.Should().BeNull();
        inspection.Cpu.Value.Hardware.Should().Be("darcy");
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

    [Fact]
    public void Deep_inspection_probes_do_not_use_compound_sh_c_scripts()
        => DeepInspectionCatalog.Probes.Should().OnlyContain(probe =>
            !probe.Arguments.Contains("sh") && !probe.Arguments.Contains("-c"));

    private static bool IsEscalatingSu(IReadOnlyList<string> arguments)
        => arguments.Count >= 2
            && arguments[0].Equals("shell", StringComparison.OrdinalIgnoreCase)
            && arguments[1].Equals("su", StringComparison.OrdinalIgnoreCase);

    private static AndroidTVManager.Core.Models.AdbCommandResult Result(
        string output,
        string error = "",
        int exitCode = 0)
        => new("adb.exe", [], exitCode, output, error, TimeSpan.Zero);
}
