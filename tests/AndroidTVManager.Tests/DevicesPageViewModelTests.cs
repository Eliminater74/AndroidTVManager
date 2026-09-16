using System.Collections.ObjectModel;
using AndroidTVManager.App.Services;
using AndroidTVManager.App.ViewModels;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DevicesPageViewModelTests
{
    [Fact]
    public async Task Connect_and_save_refreshes_and_selects_the_network_device()
    {
        var connection = new FakeConnection();
        var tracker = new FakeTracker();
        string? preferred = null;
        AndroidDevice? selected = null;
        var vm = new DevicesPageViewModel(
            [],
            new FakeSavedDevices(),
            connection,
            new FakeConfirmation(),
            tracker,
            value => preferred = value,
            value => selected = value);

        vm.Host = "192.168.1.65";
        vm.Port = "5555";
        vm.FriendlyName = "Living Room Shield";

        await vm.ConnectAndSaveCommand.ExecuteAsync(null);

        connection.ConnectedEndpoint.Should().Be("192.168.1.65:5555");
        tracker.RefreshCount.Should().Be(1);
        preferred.Should().Be("192.168.1.65:5555");
        vm.SaveMessage.Should().Contain("connected and saved");
        selected.Should().BeNull();
    }

    [Fact]
    public void Selecting_a_live_device_raises_the_global_target()
    {
        AndroidDevice? selected = null;
        var shield = new AndroidDevice
        {
            Serial = "192.168.1.65:5555",
            Endpoint = "192.168.1.65:5555",
            Model = "SHIELD Android TV",
            State = DeviceState.Device,
            ConnectionType = ConnectionType.Network
        };
        var vm = new DevicesPageViewModel(
            new ObservableCollection<AndroidDevice> { shield },
            new FakeSavedDevices(),
            new FakeConnection(),
            new FakeConfirmation(),
            new FakeTracker(),
            _ => { },
            value => selected = value);

        vm.SelectedDevice = shield;

        selected.Should().BeSameAs(shield);
    }

    private sealed class FakeConnection : IAdbConnectionService
    {
        public string? ConnectedEndpoint { get; private set; }

        public Task<AdbCommandResult> ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            ConnectedEndpoint = endpoint;
            return Task.FromResult(new AdbCommandResult(
                "adb.exe", ["connect", endpoint], 0, $"connected to {endpoint}", "", TimeSpan.Zero));
        }

        public Task<AdbCommandResult> DisconnectAsync(string endpoint, CancellationToken cancellationToken = default)
            => Task.FromResult(new AdbCommandResult("adb.exe", ["disconnect", endpoint], 0, "", "", TimeSpan.Zero));

        public Task<AdbCommandResult> PairAsync(string endpoint, string pairingCode, CancellationToken cancellationToken = default)
            => Task.FromResult(new AdbCommandResult("adb.exe", ["pair", endpoint], 0, "", "", TimeSpan.Zero));
    }

    private sealed class FakeTracker : IAdbDeviceTracker
    {
        public int RefreshCount { get; private set; }
        public event EventHandler<IReadOnlyList<AndroidDevice>>? DevicesChanged;
        public IReadOnlyList<AndroidDevice> CurrentDevices { get; } = [];
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            DevicesChanged?.Invoke(this, CurrentDevices);
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSavedDevices : IDeviceRepository
    {
        public List<SavedDevice> Items { get; } = [];
        public Task<IReadOnlyList<SavedDevice>> GetSavedDevicesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SavedDevice>>(Items);
        public Task<long> UpsertAsync(SavedDevice device, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(item => item.LastKnownSerial == device.LastKnownSerial);
            device.Id = Items.Count + 1;
            Items.Add(device);
            return Task.FromResult(device.Id);
        }
        public Task DeleteAsync(long id, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(item => item.Id == id);
            return Task.CompletedTask;
        }
        public Task ClearConnectionHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeConfirmation : IConfirmationService
    {
        public bool Confirm(string title, string message) => true;
    }
}
