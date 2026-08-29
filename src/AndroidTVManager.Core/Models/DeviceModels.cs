namespace AndroidTVManager.Core.Models;

public enum DeviceState
{
    Unknown,
    Device,
    Offline,
    Unauthorized,
    NoPermissions,
    Disconnected
}

public enum ConnectionType
{
    Unknown,
    Usb,
    Network,
    WirelessDebugging
}

public sealed class AndroidDevice
{
    public string Serial { get; init; } = string.Empty;
    public string? FriendlyName { get; init; }
    public string? Endpoint { get; init; }
    public DeviceState State { get; init; }
    public ConnectionType ConnectionType { get; init; }
    public string? Manufacturer { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public string? Product { get; init; }
    public string? DeviceName { get; init; }
    public string? Board { get; init; }
    public string? AndroidVersion { get; init; }
    public int? ApiLevel { get; init; }
    public string? SecurityPatch { get; init; }
    public string? BuildId { get; init; }
    public string? BuildType { get; init; }
    public string? BuildFingerprint { get; init; }
    public DateTimeOffset SeenAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class SavedDevice
{
    public long Id { get; set; }
    public string FriendlyName { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? LastKnownSerial { get; set; }
    public string? LastKnownEndpoint { get; set; }
    public ConnectionType PreferredConnectionType { get; set; }
    public bool IsFavorite { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? LastSeenUtc { get; set; }
    public DateTimeOffset? LastConnectedUtc { get; set; }
    public DateTimeOffset? LastDisconnectedUtc { get; set; }
}

public sealed record DeviceConnection(
    string Serial,
    string? Endpoint,
    ConnectionType Type,
    DeviceState State,
    DateTimeOffset OccurredAtUtc);

public sealed record AdbCommandResult(
    string FileName,
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool WasCanceled = false,
    bool WasTimedOut = false)
{
    public bool IsSuccess => !WasCanceled && !WasTimedOut && ExitCode == 0;
    public string CommandText => $"{FileName} {string.Join(" ", Arguments)}";
}

public sealed record ConnectionSession(
    long Id,
    long DeviceId,
    string Serial,
    string? Endpoint,
    ConnectionType ConnectionType,
    DateTimeOffset StartedUtc,
    DateTimeOffset? EndedUtc,
    DeviceState FinalState,
    string? DisconnectReason);

public sealed record PackageInfo(
    string PackageName,
    bool IsEnabled,
    bool IsSystem,
    bool IsUninstalledForUser,
    string? VersionName = null);

public sealed record DeviceSnapshot(
    long Id,
    long DeviceId,
    DateTimeOffset CapturedUtc,
    string? AndroidVersion,
    string? BuildFingerprint,
    IReadOnlyList<PackageInfo> Packages);
