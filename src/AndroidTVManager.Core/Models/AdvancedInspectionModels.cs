namespace AndroidTVManager.Core.Models;

public enum OemUnlockOptionState
{
    Present,
    Absent,
    Unknown
}

public enum OemUnlockSettingState
{
    Enabled,
    Disabled,
    LockedByDevice,
    Unknown
}

public sealed record OemUnlockInfo(
    OemUnlockOptionState Option,
    OemUnlockSettingState Setting,
    CapabilityState ActualUnlockCapability,
    IReadOnlyList<CapabilityEvidence> Evidence);

public sealed record RootInfo(
    CapabilityState CurrentShellRoot,
    CapabilityState SuAvailability,
    CapabilityState AdbRootFeasibility,
    IReadOnlyList<string> Blockers,
    string Guidance,
    IReadOnlyList<CapabilityEvidence> Evidence);

public sealed record BluetoothInfo(
    CapabilityState Support,
    bool? IsEnabled,
    string? AdapterState,
    IReadOnlyList<string> ConnectedDevices,
    IReadOnlyList<CapabilityEvidence> Evidence);

public sealed record HdmiInfo(
    CapabilityState Support,
    string? CecState,
    string? ActiveInput,
    string? AudioRoute,
    IReadOnlyList<string> Displays,
    IReadOnlyList<CapabilityEvidence> Evidence);

public sealed record DrmInfo(
    CapabilityState Availability,
    string? Schemes,
    string? SecurityLevels,
    string? Hdcp,
    IReadOnlyList<CapabilityEvidence> Evidence);

public sealed record ServiceInfo(
    string Name,
    string? PackageName,
    string? State);
