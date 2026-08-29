using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Core.Abstractions;

public interface IAdbProcessRunner
{
    Task<AdbCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    Task<AdbCommandResult> RunForDeviceAsync(
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

public interface IAdbToolsManager
{
    string? AdbPath { get; }
    string? InstalledVersion { get; }
    DateTimeOffset? LastUpdateCheckUtc { get; }
    bool IsReady { get; }
    Task<AdbToolStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<AdbToolStatus> InstallOrRepairAsync(
        IProgress<AdbDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record AdbToolStatus(
    bool IsReady,
    string? Version,
    string? ExecutablePath,
    DateTimeOffset? LastUpdateCheckUtc,
    string? ErrorMessage = null);

public interface IAdbDeviceTracker : IAsyncDisposable
{
    event EventHandler<IReadOnlyList<AndroidDevice>>? DevicesChanged;
    IReadOnlyList<AndroidDevice> CurrentDevices { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed record AdbDownloadProgress(long BytesReceived, long? TotalBytes, string Status);

public interface IDeviceRepository
{
    Task<IReadOnlyList<SavedDevice>> GetSavedDevicesAsync(CancellationToken cancellationToken = default);
    Task<long> UpsertAsync(SavedDevice device, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
    Task ClearConnectionHistoryAsync(CancellationToken cancellationToken = default);
}

public interface IConnectionHistoryRepository
{
    Task RecordDeviceSeenAsync(AndroidDevice device, CancellationToken cancellationToken = default);
    Task<long> StartSessionAsync(AndroidDevice device, CancellationToken cancellationToken = default);
    Task EndSessionAsync(long sessionId, DeviceState finalState, string? reason, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConnectionHistoryItem>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default);
}

public sealed record ConnectionHistoryItem(
    string FriendlyName,
    string? Manufacturer,
    string? Model,
    string Serial,
    string? Endpoint,
    ConnectionType ConnectionType,
    DeviceState CurrentState,
    DateTimeOffset? LastSeenUtc,
    DateTimeOffset? LastConnectedUtc,
    DateTimeOffset? LastDisconnectedUtc);

public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
}

public interface ILocalAppDataPaths
{
    string Root { get; }
    string DatabasePath { get; }
    string ToolsPath { get; }
    string LogsPath { get; }
    string ScriptsPath { get; }
    string SnapshotsPath { get; }
    string ScreenshotsPath { get; }
    string RecordingsPath { get; }
    string TempPath { get; }
    void EnsureCreated();
}
