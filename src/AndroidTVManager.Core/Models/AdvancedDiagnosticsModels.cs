namespace AndroidTVManager.Core.Models;

public sealed record NetworkDiagnosticResult(
    string InterfaceOutput,
    string RouteOutput,
    string DnsOutput,
    string PingOutput,
    DateTimeOffset CapturedUtc);

public sealed record CodecCapability(
    string Name,
    string Type,
    string RawLine);

public sealed record CodecInspectionResult(
    IReadOnlyList<CodecCapability> Codecs,
    string RawOutput,
    DateTimeOffset CapturedUtc);

public enum BootTransportState
{
    Unknown,
    AdbDevice,
    AdbUnauthorized,
    AdbOffline,
    Fastboot,
    NoDevice
}

public sealed record BootInspectionResult(
    BootTransportState State,
    string? Serial,
    string? Product,
    string? Slot,
    string? UnlockedState,
    IReadOnlyDictionary<string, string> Variables,
    string Evidence,
    DateTimeOffset CapturedUtc);

public sealed record DeviceFileEntry(
    string Path,
    string Name,
    bool IsDirectory,
    long? SizeBytes,
    string? ModifiedText);

public sealed record DeviceComparisonSection(
    string Name,
    string LeftSummary,
    string RightSummary,
    bool Changed);

public sealed record DeviceComparisonResult(
    string LeftSerial,
    string RightSerial,
    IReadOnlyList<DeviceComparisonSection> Sections,
    DateTimeOffset ComparedUtc);
