using AndroidTVManager.App.Services;
using AndroidTVManager.App.ViewModels;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DevicesPageViewModelTests
{
    [Fact]
    public async Task Connect_and_save_refreshes_and_selects_the_network_device()
    {
        var connection = new FakeConnection();
        var tracker = new FakeTracker();
        var context = new AdbDeviceSession(connection, tracker, new FakeAppLogger());
        var vm = CreateViewModel(context, connection, tracker);

        vm.Host = "192.168.1.65";
        vm.Port = "5555";
        vm.FriendlyName = "Living Room Shield";

        await vm.ConnectAndSaveCommand.ExecuteAsync(null);

        connection.ConnectedEndpoint.Should().Be("192.168.1.65:5555");
        tracker.RefreshCount.Should().Be(1);
        context.PreferredTarget.Should().Be("192.168.1.65:5555");
        vm.SaveMessage.Should().Contain("connected and saved");
        context.SelectedDevice.Should().BeNull();
    }

    [Fact]
    public async Task Disconnects_network_devices_and_leaves_saved_devices_in_place()
    {
        var connection = new FakeConnection();
        var tracker = new FakeTracker();
        var saved = new FakeSavedDevices
        {
            Items =
            {
                new SavedDevice
                {
                    Id = 1,
                    FriendlyName = "Living Room Shield",
                    LastKnownSerial = "192.168.1.65:5555",
                    LastKnownEndpoint = "192.168.1.65:5555"
                }
            }
        };
        var shield = new AndroidDevice
        {
            Serial = "192.168.1.65:5555",
            Endpoint = "192.168.1.65:5555",
            Model = "SHIELD Android TV",
            State = DeviceState.Device,
            ConnectionType = ConnectionType.Network
        };
        var context = new AdbDeviceSession(connection, tracker, new FakeAppLogger());
        context.ReplaceLiveDevices([shield]);
        var vm = CreateViewModel(context, connection, tracker, saved);

        await vm.DisconnectCommand.ExecuteAsync(shield);

        connection.DisconnectedEndpoint.Should().Be("192.168.1.65:5555");
        tracker.RefreshCount.Should().Be(1);
        saved.Items.Should().ContainSingle(device => device.FriendlyName == "Living Room Shield");
        vm.SaveMessage.Should().Contain("disconnected");
        vm.SaveMessage.Should().Contain("Saved devices were left in the list.");
    }

    [Fact]
    public async Task Usb_devices_are_not_disconnected()
    {
        var connection = new FakeConnection();
        var emulator = new AndroidDevice
        {
            Serial = "emulator-5554",
            Model = "sdk gphone",
            State = DeviceState.Device,
            ConnectionType = ConnectionType.Usb
        };
        var context = new AdbDeviceSession(connection, new FakeTracker(), new FakeAppLogger());
        context.ReplaceLiveDevices([emulator]);
        var vm = CreateViewModel(context, connection, new FakeTracker());

        vm.DisconnectCommand.CanExecute(emulator).Should().BeFalse();
        await vm.DisconnectCommand.ExecuteAsync(emulator);

        connection.DisconnectedEndpoint.Should().BeNull();
    }

    [Fact]
    public void Selecting_a_live_device_raises_the_global_target()
    {
        var shield = new AndroidDevice
        {
            Serial = "192.168.1.65:5555",
            Endpoint = "192.168.1.65:5555",
            Model = "SHIELD Android TV",
            State = DeviceState.Device,
            ConnectionType = ConnectionType.Network
        };
        var context = new AdbDeviceSession(new FakeConnection(), new FakeTracker(), new FakeAppLogger());
        context.ReplaceLiveDevices([shield]);
        var vm = CreateViewModel(context, new FakeConnection(), new FakeTracker());

        vm.SelectedDevice = shield;

        context.SelectedDevice.Should().BeSameAs(shield);
    }

    private static DevicesPageViewModel CreateViewModel(
        IAdbDeviceSession context,
        FakeConnection connection,
        FakeTracker tracker,
        FakeSavedDevices? saved = null)
        => new(
            context,
            saved ?? new FakeSavedDevices(),
            connection,
            new FakeConfirmation(),
            tracker);

    private sealed class FakeConnection : IAdbConnectionService
    {
        public string? ConnectedEndpoint { get; private set; }
        public string? DisconnectedEndpoint { get; private set; }

        public Task<AdbCommandResult> ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            ConnectedEndpoint = endpoint;
            return Task.FromResult(new AdbCommandResult(
                "adb.exe", ["connect", endpoint], 0, $"connected to {endpoint}", "", TimeSpan.Zero));
        }

        public Task<AdbCommandResult> DisconnectAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            DisconnectedEndpoint = endpoint;
            return Task.FromResult(new AdbCommandResult("adb.exe", ["disconnect", endpoint], 0, "", "", TimeSpan.Zero));
        }

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
        public bool Confirm(string title, string message, string confirmLabel = "Continue") => true;
    }
}
