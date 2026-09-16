using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class AdbDeviceSessionTests
{
    [Fact]
    public void Prefer_selects_a_matching_live_device()
    {
        var emulator = Usb("emulator-5554", "sdk gphone");
        var shield = Network("192.168.1.65:5555", "SHIELD Android TV");
        var session = CreateSession(devices: [emulator, shield]);

        session.Prefer("192.168.1.65:5555");

        session.SelectedDevice.Should().BeSameAs(shield);
        session.PreferredTarget.Should().Be("192.168.1.65:5555");
        session.SelectedSerial.Should().Be(shield.Serial);
    }

    [Fact]
    public void Prefer_sticks_until_the_endpoint_appears()
    {
        var emulator = Usb("emulator-5554", "sdk gphone");
        var session = CreateSession(devices: [emulator]);
        session.Select(emulator);

        session.Prefer("192.168.1.65:5555");

        session.SelectedDevice.Should().BeSameAs(emulator);
        session.PreferredTarget.Should().Be("192.168.1.65:5555");

        var shield = Network("192.168.1.65:5555", "SHIELD Android TV");
        session.ReplaceLiveDevices([emulator, shield]);

        session.SelectedDevice.Should().BeSameAs(shield);
        session.PreferredTarget.Should().BeNull();
        session.Generation.Should().Be(2);
    }

    [Fact]
    public void Replace_falls_back_to_another_connected_device()
    {
        var emulator = Usb("emulator-5554", "sdk gphone");
        var shield = Network("192.168.1.65:5555", "SHIELD Android TV");
        var session = CreateSession(devices: [emulator, shield]);
        session.Select(shield);

        session.ReplaceLiveDevices([emulator]);

        session.SelectedDevice.Should().BeSameAs(emulator);
        session.Generation.Should().Be(2);
    }

    [Fact]
    public async Task Disconnects_network_devices_and_refreshes_the_tracker()
    {
        var connection = new FakeConnection();
        var tracker = new FakeTracker();
        var logger = new FakeAppLogger();
        var shield = Network("192.168.1.65:5555", "SHIELD Android TV");
        var session = CreateSession(connection, tracker, logger, shield);
        session.Prefer(shield.Serial);

        var result = await session.DisconnectAsync(shield);

        result.Attempted.Should().BeTrue();
        result.Succeeded.Should().BeTrue();
        result.Endpoint.Should().Be("192.168.1.65:5555");
        result.Message.Should().Contain("disconnected");
        result.Message.Should().Contain("Saved devices were left in the list.");
        connection.DisconnectedEndpoint.Should().Be("192.168.1.65:5555");
        tracker.RefreshCount.Should().Be(1);
        session.PreferredTarget.Should().BeNull();
        logger.Entries.Should().Contain(entry => entry.StartsWith("INFO:Devices:Disconnected"));
    }

    [Fact]
    public async Task Disconnects_wireless_debugging_using_the_recorded_endpoint()
    {
        var connection = new FakeConnection();
        var device = new AndroidDevice
        {
            Serial = "serial",
            Endpoint = "192.168.1.65:5555",
            Model = "Google TV Streamer",
            State = DeviceState.Device,
            ConnectionType = ConnectionType.WirelessDebugging
        };
        var session = CreateSession(connection, devices: [device]);

        var result = await session.DisconnectAsync(device);

        result.Succeeded.Should().BeTrue();
        connection.DisconnectedEndpoint.Should().Be("192.168.1.65:5555");
    }

    [Fact]
    public async Task Usb_and_offline_targets_are_not_disconnected()
    {
        var connection = new FakeConnection();
        var emulator = Usb("emulator-5554", "sdk gphone");
        var offline = new AndroidDevice
        {
            Serial = "192.168.1.20:5555",
            Endpoint = "192.168.1.20:5555",
            State = DeviceState.Offline,
            ConnectionType = ConnectionType.Network
        };
        var session = CreateSession(connection, devices: [emulator, offline]);

        session.CanDisconnect(emulator).Should().BeFalse();
        (await session.DisconnectAsync(emulator)).Attempted.Should().BeFalse();
        session.CanDisconnect(offline).Should().BeFalse();
        (await session.DisconnectAsync(offline)).Attempted.Should().BeFalse();
        connection.DisconnectedEndpoint.Should().BeNull();
    }

    [Fact]
    public async Task Failed_disconnect_is_logged_and_does_not_refresh()
    {
        var connection = new FakeConnection { FailDisconnect = true };
        var tracker = new FakeTracker();
        var logger = new FakeAppLogger();
        var shield = Network("192.168.1.65:5555", "SHIELD Android TV");
        var session = CreateSession(connection, tracker, logger, shield);

        var result = await session.DisconnectAsync(shield);

        result.Attempted.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        result.Message.Should().StartWith("Disconnect failed:");
        tracker.RefreshCount.Should().Be(0);
        logger.Entries.Should().Contain(entry => entry.StartsWith("WARN:Devices:Disconnect"));
    }

    private static AdbDeviceSession CreateSession(params AndroidDevice[] devices)
        => CreateSession(new FakeConnection(), new FakeTracker(), new FakeAppLogger(), devices);

    private static AdbDeviceSession CreateSession(
        FakeConnection connection,
        params AndroidDevice[] devices)
        => CreateSession(connection, new FakeTracker(), new FakeAppLogger(), devices);

    private static AdbDeviceSession CreateSession(
        FakeConnection connection,
        FakeTracker tracker,
        FakeAppLogger logger,
        params AndroidDevice[] devices)
    {
        var session = new AdbDeviceSession(connection, tracker, logger);
        if (devices.Length > 0)
            session.ReplaceLiveDevices(devices);
        return session;
    }

    private static AndroidDevice Usb(string serial, string model) => new()
    {
        Serial = serial,
        Model = model,
        State = DeviceState.Device,
        ConnectionType = ConnectionType.Usb
    };

    private static AndroidDevice Network(string serial, string model) => new()
    {
        Serial = serial,
        Endpoint = serial,
        Model = model,
        State = DeviceState.Device,
        ConnectionType = ConnectionType.Network
    };

    private sealed class FakeConnection : IAdbConnectionService
    {
        public string? DisconnectedEndpoint { get; private set; }
        public bool FailDisconnect { get; set; }

        public Task<AdbCommandResult> ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
            => Task.FromResult(new AdbCommandResult("adb.exe", ["connect", endpoint], 0, "", "", TimeSpan.Zero));

        public Task<AdbCommandResult> DisconnectAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            DisconnectedEndpoint = endpoint;
            return Task.FromResult(FailDisconnect
                ? new AdbCommandResult("adb.exe", ["disconnect", endpoint], 1, "", "device not found", TimeSpan.Zero)
                : new AdbCommandResult("adb.exe", ["disconnect", endpoint], 0, "", "", TimeSpan.Zero));
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
}
