using AndroidTVManager.App.ViewModels;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Adb;
using AndroidTVManager.Infrastructure.Packages;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DeviceStatusScanTests
{
    [Fact]
    public async Task Old_scan_cannot_replace_new_target_results_or_clear_busy_state()
    {
        var seed = await new DeviceInspectionService(new FakeAdbProcessRunner(), new FakeDeviceSnapshotRepository(), new FakeAppLogger()).InspectAsync("seed");
        var service = new DeferredInspection();
        var vm = new DeviceStatusPageViewModel(service, new DeveloperVerificationPolicyProvider(), []);
        vm.SelectedDevice = new AndroidDevice { Serial = "old", State = DeviceState.Device };
        vm.SelectedDevice = new AndroidDevice { Serial = "new", State = DeviceState.Device };
        service.Requests[0].Token.IsCancellationRequested.Should().BeTrue();
        service.Requests[0].Completion.SetResult(seed with { Serial = "old" });
        vm.Inspection.Should().BeNull();
        vm.IsBusy.Should().BeTrue();
        service.Requests[1].Completion.SetResult(seed with { Serial = "new" });
        vm.Inspection!.Serial.Should().Be("new");
        vm.IsBusy.Should().BeFalse();

        var deep = vm.DeepInspectCommand.ExecuteAsync(null);
        service.Requests[2].Deep.Should().BeTrue();
        vm.SelectedDevice = null;
        service.Requests[2].Completion.SetResult(seed with { Serial = "new" });
        await deep;
        vm.Inspection.Should().BeNull();
        vm.IsBusy.Should().BeFalse();
    }

    private sealed class DeferredInspection : IDeviceInspectionService
    {
        public List<(TaskCompletionSource<DeviceInspectionResult> Completion, CancellationToken Token, bool Deep)> Requests { get; } = [];
        public Task<DeviceInspectionResult> InspectAsync(string serial, IProgress<DeviceInspectionProgress>? progress = null,
            CancellationToken cancellationToken = default, bool deepScan = false)
        {
            var completion = new TaskCompletionSource<DeviceInspectionResult>();
            Requests.Add((completion, cancellationToken, deepScan));
            return completion.Task;
        }
    }
}
