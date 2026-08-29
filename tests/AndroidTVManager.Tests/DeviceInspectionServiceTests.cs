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
            """);
        runner.Responses["shell cat /proc/cpuinfo"] = Result("processor : 0\nprocessor : 1");
        runner.Responses["shell cat /proc/meminfo"] = Result("MemTotal: 2048 kB");
        runner.Responses["shell pm list features"] = Result("feature:android.software.leanback");
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
        inspection.Display.State.Should().Be(InspectionSectionState.Partial);
        inspection.Capabilities.Should().Contain(capability => capability.Name == "ADB APK Installation"
            && capability.State == CapabilityState.Supported);
        snapshots.Latest.Should().BeSameAs(inspection);
        runner.Calls.Should().NotBeEmpty();
        runner.Calls.Should().OnlyContain(call => call.Serial == "192.168.1.10:5555");
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
