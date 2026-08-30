using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DisplayDiagnosticsTests
{
    [Fact]
    public void Parses_surfaceflinger_modes_vendor_properties_and_hdcp_evidence()
    {
        var surfaceFlinger = """
            Display 0 modes: 3840x2160 @ 59.94 Hz, 1920x1080 @ 60 Hz
            activeMode=3840x2160
            """;
        var properties = """
            [ro.vendor.hdmi.tx.mode]: [2160p]
            [persist.sys.hdr.mode]: [hdr10]
            [ro.product.model]: [Test TV]
            """;

        DisplayDiagnosticsParser.ParseSurfaceFlingerModes(surfaceFlinger)
            .Should().Contain(["3840x2160 @ 59.94 Hz", "1920x1080 @ 60 Hz"]);
        DisplayDiagnosticsParser.ParseVendorProperties(properties)
            .Should().HaveCount(2);
        DisplayDiagnosticsParser.ParseHdcp(
                "hdcpStatus=authenticated",
                string.Empty,
                new Dictionary<string, string>())
            .Should().Be("authenticated");
    }

    [Fact]
    public void Comparison_reports_the_display_changes_that_matter_for_diagnosis()
    {
        var previous = Snapshot(
            "3840x2160",
            "HDR10",
            "authenticated",
            "3000");
        var current = Snapshot(
            "1280x720",
            string.Empty,
            "none",
            "FFFF");

        var comparison = DisplayDiagnosticsParser.Compare(previous, current);

        comparison.HasChanges.Should().BeTrue();
        comparison.Changes.Select(change => change.Name).Should().Contain([
            "Current resolution",
            "HDR capabilities",
            "CEC physical address",
            "HDCP state"
        ]);
    }

    private static DisplayDiagnosticSnapshot Snapshot(
        string resolution,
        string hdr,
        string hdcp,
        string physicalAddress)
        => new(
            "tv-1",
            "Test TV",
            DateTimeOffset.UtcNow,
            DisplayCaptureLabel.Unlabeled,
            new DisplayInfo(
                resolution,
                "3840x2160",
                320,
                "60 Hz",
                ["3840x2160 @ 60 Hz"],
                string.IsNullOrWhiteSpace(hdr) ? [] : [hdr],
                "standard",
                "0"),
            new HdmiInfo(
                CapabilityState.Partial,
                "enabled",
                "HDMI-1",
                "HDMI",
                [],
                []),
            hdcp,
            physicalAddress,
            "1",
            ["3840x2160 @ 60 Hz"],
            [],
            []);
}
