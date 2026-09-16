using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DeviceSelectionTests
{
    [Fact]
    public void Prefers_a_newly_connected_network_device_over_the_current_emulator()
    {
        var emulator = Usb("emulator-5554", "sdk gphone");
        var shield = Network("192.168.1.65:5555", "SHIELD Android TV");

        var selected = DeviceSelection.Resolve(
            [emulator, shield],
            currentSerial: emulator.Serial,
            preferredSerial: "192.168.1.65:5555");

        selected.Should().BeSameAs(shield);
    }

    [Fact]
    public void Keeps_the_current_target_when_the_list_is_rebuilt_with_new_instances()
    {
        var previous = Usb("emulator-5554", "old");
        var refreshedEmulator = Usb("emulator-5554", "sdk gphone");
        var shield = Network("192.168.1.65:5555", "SHIELD Android TV");

        var selected = DeviceSelection.Resolve(
            [refreshedEmulator, shield],
            currentSerial: previous.Serial);

        selected.Should().BeSameAs(refreshedEmulator);
        selected!.Model.Should().Be("sdk gphone");
    }

    [Fact]
    public void Matches_network_devices_by_endpoint_or_serial()
    {
        var shield = Network("192.168.1.65:5555", "SHIELD Android TV");

        DeviceSelection.Find([shield], "192.168.1.65:5555").Should().BeSameAs(shield);
        DeviceSelection.Matches(shield, shield.Endpoint).Should().BeTrue();
    }

    [Fact]
    public void Falls_back_to_another_connected_device_when_the_current_target_leaves()
    {
        var emulator = Usb("emulator-5554", "sdk gphone");
        var selected = DeviceSelection.Resolve(
            [emulator],
            currentSerial: "192.168.1.65:5555",
            preferredSerial: "192.168.1.65:5555");

        selected.Should().BeSameAs(emulator);
    }

    [Fact]
    public void Only_live_network_and_wireless_devices_can_disconnect()
    {
        Network("192.168.1.65:5555", "SHIELD Android TV").CanDisconnect.Should().BeTrue();
        Usb("emulator-5554", "sdk gphone").CanDisconnect.Should().BeFalse();
        new AndroidDevice
        {
            Serial = "192.168.1.20:5555",
            Endpoint = "192.168.1.20:5555",
            State = DeviceState.Offline,
            ConnectionType = ConnectionType.Network
        }.CanDisconnect.Should().BeFalse();
    }

    [Fact]
    public void Disconnect_endpoint_prefers_the_recorded_endpoint()
    {
        var device = new AndroidDevice
        {
            Serial = "serial",
            Endpoint = "192.168.1.65:5555",
            State = DeviceState.Device,
            ConnectionType = ConnectionType.WirelessDebugging
        };
        device.CanDisconnect.Should().BeTrue();
        device.DisconnectEndpoint.Should().Be("192.168.1.65:5555");
    }

    [Fact]
    public void Display_label_uses_model_when_a_friendly_name_is_missing()
    {
        var shield = Network("192.168.1.65:5555", "SHIELD Android TV");
        shield.DisplayLabel.Should().Be("SHIELD Android TV");
        shield.DisplaySubtitle.Should().Be("Network · 192.168.1.65:5555");

        var emulator = Usb("emulator-5554", "sdk gphone64 x86 64");
        emulator.DisplayLabel.Should().Be("sdk gphone64 x86 64");
        emulator.DisplaySubtitle.Should().Be("USB · emulator-5554");
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
}
