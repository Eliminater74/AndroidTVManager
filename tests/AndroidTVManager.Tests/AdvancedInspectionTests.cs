using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Packages;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class AdvancedInspectionTests
{
    [Fact]
    public void Separates_oem_option_setting_and_actual_unlock_capability()
    {
        var result = AdbInspectionParsers.ParseOemUnlock(
            new Dictionary<string, string>
            {
                ["ro.oem_unlock_supported"] = "1",
                ["sys.oem_unlock_allowed"] = "0",
                ["ro.boot.flash.locked"] = "1",
                ["ro.boot.vbmeta.device_state"] = "locked"
            },
            "0");

        result.Option.Should().Be(OemUnlockOptionState.Present);
        result.Setting.Should().Be(OemUnlockSettingState.LockedByDevice);
        result.ActualUnlockCapability.Should().Be(CapabilityState.Unknown);
    }

    [Fact]
    public void Reports_root_evidence_without_attempting_escalation()
    {
        var result = AdbInspectionParsers.ParseRoot(
            new Dictionary<string, string>
            {
                ["ro.debuggable"] = "0",
                ["ro.build.type"] = "user",
                ["ro.boot.verifiedbootstate"] = "green"
            },
            "uid=2000(shell) gid=2000(shell)\n/system/xbin/su");

        result.CurrentShellRoot.Should().Be(CapabilityState.Unsupported);
        result.SuAvailability.Should().Be(CapabilityState.Partial);
        result.AdbRootFeasibility.Should().Be(CapabilityState.Unsupported);
        result.Guidance.Should().Contain("cannot prove");
    }

    [Fact]
    public void Parses_network_route_dns_and_link_evidence()
    {
        var result = AdbInspectionParsers.ParseNetwork(
            "2: wlan0: <BROADCAST,UP> state UP\n    inet 192.168.1.12/24",
            "living-room-tv",
            "default via 192.168.1.1 dev wlan0",
            new Dictionary<string, string> { ["net.dns1"] = "192.168.1.1" });

        result.Gateway.Should().Be("192.168.1.1");
        result.DnsServers.Should().Contain("192.168.1.1");
        result.LinkSummary.Should().Contain("state UP");
    }

    [Fact]
    public void Parses_optional_bluetooth_hdmi_drm_and_service_evidence()
    {
        var bluetooth = AdbInspectionParsers.ParseBluetooth(
            "feature:android.hardware.bluetooth",
            "1",
            "state: ON\nDevice AA connected");
        var hdmi = AdbInspectionParsers.ParseHdmi(
            "HDMI control enabled: true\nactive input: HDMI1",
            "current audio route: HDMI");
        var drm = AdbInspectionParsers.ParseDrm(
            "Widevine\nsecurityLevel: L1\nHDCP: 2.2");
        var services = AdbInspectionParsers.ParseServices(
            "ServiceRecord{com.example.tv/.PlaybackService}");

        bluetooth.Support.Should().Be(CapabilityState.Supported);
        bluetooth.IsEnabled.Should().BeTrue();
        bluetooth.ConnectedDevices.Should().ContainSingle();
        hdmi.ActiveInput.Should().Be("HDMI1");
        hdmi.AudioRoute.Should().Be("HDMI");
        drm.Schemes.Should().Contain("Widevine");
        drm.SecurityLevels.Should().Be("L1");
        services.Should().ContainSingle(item => item.PackageName == "com.example.tv");
    }

    [Fact]
    public void Root_guidance_is_device_aware_without_claiming_a_root_method()
    {
        var guidance = new RootGuidanceProvider().GetGuidance(
            new AndroidDevice { Manufacturer = "Google", Model = "Chromecast" },
            new OemUnlockInfo(
                OemUnlockOptionState.Present,
                OemUnlockSettingState.LockedByDevice,
                CapabilityState.Unknown,
                []),
            new SecurityInfo(
                "Enforcing", "green", "locked", "1", "user", "release-keys",
                CapabilityState.Unsupported, CapabilityState.Unsupported, []),
            new RootInfo(
                CapabilityState.Unsupported,
                CapabilityState.Unsupported,
                CapabilityState.Unsupported,
                [],
                string.Empty,
                []));

        guidance.Should().Contain("Google retail streaming devices");
        guidance.Should().Contain("does not attempt");
    }
}
