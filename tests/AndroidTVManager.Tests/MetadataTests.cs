using AndroidTVManager.Core.Adb;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class MetadataTests
{
    [Fact]
    public void Parses_getprop_metadata()
    {
        var metadata = AdbMetadataParser.Parse("""
            [ro.product.manufacturer]: [Philips]
            [ro.product.brand]: [Philips]
            [ro.product.model]: [55PUS]
            [ro.product.name]: [tpm191e]
            [ro.product.device]: [tpm191e]
            [ro.product.board]: [mt5896]
            [ro.build.version.release]: [12]
            [ro.build.version.sdk]: [31]
            [ro.build.version.security_patch]: [2025-01-05]
            [ro.build.id]: [SP1A.210812.016]
            [ro.build.type]: [user]
            [ro.build.fingerprint]: [philips/tpm191e/tpm191e:12/SP1A.210812.016:user/release-keys]
            """);

        metadata.Manufacturer.Should().Be("Philips");
        metadata.Model.Should().Be("55PUS");
        metadata.ApiLevel.Should().Be(31);
        metadata.SecurityPatch.Should().Be("2025-01-05");
        metadata.BuildFingerprint.Should().Contain("philips/tpm191e");
    }

    [Fact]
    public void Ignores_malformed_getprop_lines()
    {
        var metadata = AdbMetadataParser.Parse("not getprop output\n[ro.build.version.sdk]: [not-a-number]");

        metadata.ApiLevel.Should().BeNull();
        metadata.Model.Should().BeNull();
    }

    [Fact]
    public void Parses_reported_device_name_and_mac_address_without_inventing_values()
    {
        AdbMetadataParser.ParseReportedName("  Living Room TV \r\n").Should().Be("Living Room TV");
        AdbMetadataParser.ParseReportedName("null").Should().BeNull();
        AdbMetadataParser.ParseMacAddress("""
            2: wlan0: <BROADCAST,MULTICAST,UP> mtu 1500
                link/ether aa:bb:cc:dd:ee:ff brd ff:ff:ff:ff:ff:ff
            """).Should().Be("AA:BB:CC:DD:EE:FF");
        AdbMetadataParser.ParseMacAddress("no hardware address").Should().BeNull();
    }
}
