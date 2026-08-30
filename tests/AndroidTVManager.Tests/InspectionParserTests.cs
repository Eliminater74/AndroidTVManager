using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class InspectionParserTests
{
    [Fact]
    public void Parses_cpu_abis_and_core_count_without_inventing_soc_name()
    {
        var cpu = AdbInspectionParsers.ParseCpu("""
            processor : 0
            CPU implementer : 0x41
            CPU part : 0xd08
            processor : 1
            """, "arm64-v8a", "arm64-v8a,armeabi-v7a", "s905x4", "g12a");

        cpu.LogicalCoreCount.Should().Be(2);
        cpu.Architecture.Should().Be("arm64-v8a");
        cpu.SupportedAbis.Should().Contain("armeabi-v7a");
        cpu.DetectedSoC.Should().Be("s905x4");
        cpu.InferredSoC.Should().BeNull();
    }

    [Fact]
    public void Parses_memory_units_as_bytes()
    {
        var memory = AdbInspectionParsers.ParseMemory("""
            MemTotal:        2048000 kB
            MemFree:          256000 kB
            MemAvailable:     768000 kB
            Cached:           512000 kB
            SwapTotal:        128000 kB
            SwapFree:          64000 kB
            """);

        memory.TotalBytes.Should().Be(2048000 * 1024L);
        memory.AvailableBytes.Should().Be(768000 * 1024L);
        memory.SwapFreeBytes.Should().Be(64000 * 1024L);
    }

    [Fact]
    public void Parses_display_size_density_hdr_and_refresh_modes()
    {
        var display = AdbInspectionParsers.ParseDisplay(
            "Physical size: 3840x2160\nOverride size: 1920x1080",
            "Physical density: 320",
            "mode 1920x1080 @ 60Hz; HDR10, Dolby Vision");

        display.CurrentResolution.Should().Be("1920x1080");
        display.PhysicalResolution.Should().Be("3840x2160");
        display.Density.Should().Be(320);
        display.SupportedModes.Should().Contain("60 Hz");
        display.HdrCapabilities.Should().Contain("Dolby Vision");
    }

    [Fact]
    public void Parses_features_and_keeps_unknown_features_available()
    {
        var features = AdbInspectionParsers.ParseFeatures("""
            feature:android.software.leanback
            feature:android.hardware.wifi
            feature:vendor.hardware.unknown
            """);

        features.Should().ContainInOrder(
            "android.software.leanback",
            "android.hardware.wifi",
            "vendor.hardware.unknown");
    }

    [Fact]
    public void Parses_security_without_confusing_root_binary_with_adb_root()
    {
        var security = AdbInspectionParsers.ParseSecurity(
            new Dictionary<string, string>
            {
                ["ro.debuggable"] = "0",
                ["ro.boot.verifiedbootstate"] = "green",
                ["sys.oem_unlock_allowed"] = "0"
            },
            "Enforcing",
            "sh: su: not found");

        security.SelinuxState.Should().Be("Enforcing");
        security.RootAvailability.Should().Be(CapabilityState.Unsupported);
        security.AdbRoot.Should().Be(CapabilityState.Unsupported);
        security.VerifiedBootState.Should().Be("green");
        security.OemUnlockAllowed.Should().Be("0");
    }

    [Fact]
    public void Gsi_assessment_is_possible_not_absolute_when_treble_is_present()
    {
        var result = AdbInspectionParsers.ParseGsi(
            new Dictionary<string, string>
            {
                ["ro.treble.enabled"] = "true",
                ["ro.boot.super_partition"] = "super"
            },
            "gsi_tool: not found",
            false);

        result.Assessment.Should().Be(GsiAssessment.PossiblySupported);
        result.Treble.Should().Be(CapabilityState.Supported);
        result.GsiTool.Should().Be(CapabilityState.Unsupported);
    }

    [Fact]
    public void Missing_treble_evidence_remains_unknown()
    {
        var result = AdbInspectionParsers.ParseGsi(
            new Dictionary<string, string>(),
            string.Empty,
            false);

        result.Assessment.Should().Be(GsiAssessment.Unknown);
        result.Treble.Should().Be(CapabilityState.Unknown);
    }

    [Fact]
    public void Developer_verifier_does_not_claim_manual_flow_state()
    {
        var result = AdbInspectionParsers.ParseDeveloperVerification(
            "package:com.google.android.verifier\n",
            "versionName=1.0",
            new Dictionary<string, string>());

        result.VerifierPresent.Should().BeTrue();
        result.AdbInstallAvailability.Should().Be(CapabilityState.Supported);
        result.AdvancedFlowState.Should().Be(AdvancedFlowState.Unknown);
        result.StateDetectable.Should().BeFalse();
    }

    [Fact]
    public void Missing_developer_verifier_is_not_an_inspection_error()
    {
        var result = AdbInspectionParsers.ParseDeveloperVerification(
            string.Empty,
            null,
            new Dictionary<string, string>());

        result.VerifierPresent.Should().BeFalse();
        result.AdbInstallAvailability.Should().Be(CapabilityState.Supported);
        result.AdvancedFlowAvailability.Should().Be(AdvancedFlowAvailability.Unknown);
    }

    [Fact]
    public void Unavailable_verifier_query_remains_unknown()
    {
        var result = AdbInspectionParsers.ParseDeveloperVerification(
            string.Empty,
            null,
            new Dictionary<string, string>(),
            packageQuerySucceeded: false);

        result.VerifierPresent.Should().BeNull();
        result.Evidence.Should().Contain(item => item.ObservedValue == null);
    }

    [Fact]
    public void Parses_storage_rows_and_skips_headers()
    {
        var storage = AdbInspectionParsers.ParseStorage("""
            Filesystem 1K-blocks Used Available Use% Mounted on
            /dev/block/dm-0 1000000 400000 600000 40% /data
            """);

        storage.Volumes.Should().ContainSingle();
        storage.Volumes[0].MountPoint.Should().Be("/data");
        storage.Volumes[0].TotalBytes.Should().Be(1000000 * 1024L);
    }
}
