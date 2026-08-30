using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class AdbParserTests
{
    [Fact]
    public void Parses_track_devices_long_output()
    {
        var devices = AdbParsers.ParseTrackedDevices(
            "List of devices attached\n" +
            "192.168.1.20:5555\tdevice product:onn_4k_pro model:ONN_4K_Pro device:onn\n" +
            "quest-serial\tunauthorized\n");

        devices.Should().HaveCount(2);
        devices[0].State.Should().Be(DeviceState.Device);
        devices[0].ConnectionType.Should().Be(ConnectionType.Network);
        devices[0].Model.Should().Be("ONN 4K Pro");
        devices[1].State.Should().Be(DeviceState.Unauthorized);
    }

    [Fact]
    public void Parses_usb_emulator_from_adb_device_list()
    {
        var devices = AdbParsers.ParseTrackedDevices(
            "List of devices attached\n" +
            "emulator-5554\tdevice product:sdk_gphone64_x86_64 model:sdk_gphone64_x86_64 device:emu64xa transport_id:1\n");

        devices.Should().ContainSingle();
        devices[0].Serial.Should().Be("emulator-5554");
        devices[0].State.Should().Be(DeviceState.Device);
        devices[0].ConnectionType.Should().Be(ConnectionType.Usb);
        devices[0].Endpoint.Should().BeNull();
    }

    [Theory]
    [InlineData("192.168.1.20", "5555", "192.168.1.20:5555")]
    [InlineData("2001:db8::20", "37099", "[2001:db8::20]:37099")]
    public void Validates_network_endpoints(string host, string port, string expected)
    {
        AdbParsers.TryParseEndpoint(host, port, out var endpoint, out var error).Should().BeTrue(error);
        endpoint.Should().Be(expected);
    }

    [Fact]
    public void Parses_adb_version()
    {
        AdbParsers.ParseAdbVersion("Android Debug Bridge version 35.0.2-12147458").Should().Be("35.0.2");
    }
}
