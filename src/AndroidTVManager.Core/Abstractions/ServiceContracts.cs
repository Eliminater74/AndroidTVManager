using AndroidTVManager.Core.Models;
using AndroidTVManager.Core.Scripts;

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

public interface IAdbConnectionService
{
    Task<AdbCommandResult> ConnectAsync(
        string endpoint,
        CancellationToken cancellationToken = default);

    Task<AdbCommandResult> DisconnectAsync(
        string endpoint,
        CancellationToken cancellationToken = default);

    Task<AdbCommandResult> PairAsync(
        string endpoint,
        string pairingCode,
        CancellationToken cancellationToken = default);
}

public interface IApkInstaller
{
    Task<AdbCommandResult> InstallAsync(
        string serial,
        string apkPath,
        bool reinstall = true,
        CancellationToken cancellationToken = default);
}

public interface IPackageManager
{
    Task<IReadOnlyList<PackageInfo>> ListAsync(
        string serial,
        CancellationToken cancellationToken = default);

    Task<AdbCommandResult> LaunchAsync(string serial, string packageName, CancellationToken cancellationToken = default);
    Task<AdbCommandResult> ForceStopAsync(string serial, string packageName, CancellationToken cancellationToken = default);
    Task<AdbCommandResult> EnableAsync(string serial, string packageName, CancellationToken cancellationToken = default);
    Task<AdbCommandResult> DisableAsync(string serial, string packageName, CancellationToken cancellationToken = default);
    Task<AdbCommandResult> UninstallForUserAsync(string serial, string packageName, CancellationToken cancellationToken = default);
    Task<AdbCommandResult> RestoreAsync(string serial, string packageName, CancellationToken cancellationToken = default);
    Task<AdbCommandResult> FullUninstallAsync(string serial, string packageName, CancellationToken cancellationToken = default);
    Task<AdbCommandResult> ClearDataAsync(string serial, string packageName, CancellationToken cancellationToken = default);
    Task<AdbCommandResult> ClearCacheAsync(string serial, string packageName, CancellationToken cancellationToken = default);
    Task<AdbCommandResult> GrantPermissionAsync(string serial, string packageName, string permission, CancellationToken cancellationToken = default);
    Task<AdbCommandResult> RevokePermissionAsync(string serial, string packageName, string permission, CancellationToken cancellationToken = default);
    Task<AdbCommandResult> OpenAppSettingsAsync(string serial, string packageName, CancellationToken cancellationToken = default);
    Task<AdbCommandResult> PullApkAsync(string serial, string remotePath, string localPath, CancellationToken cancellationToken = default);
}

public interface IDeviceToolsService
{
    Task<AdbCommandResult> RebootAsync(string serial, string mode = "", CancellationToken cancellationToken = default);
    Task<AdbCommandResult> ShellAsync(string serial, string command, CancellationToken cancellationToken = default);
    Task<string> CaptureScreenshotAsync(string serial, string friendlyName, CancellationToken cancellationToken = default);
}

public sealed record DeviceInspectionProgress(
    string Category,
    int CompletedCategories,
    int TotalCategories,
    InspectionSectionState State);

public interface IDeviceInspectionService
{
    Task<DeviceInspectionResult> InspectAsync(
        string serial,
        IProgress<DeviceInspectionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IDeviceSnapshotRepository
{
    Task<long> SaveAsync(DeviceInspectionResult inspection, CancellationToken cancellationToken = default);
    Task<DeviceInspectionResult?> GetLatestAsync(string serial, CancellationToken cancellationToken = default);
}

public sealed record PackageInventoryResult(
    string Serial,
    DateTimeOffset CapturedUtc,
    IReadOnlyList<PackageInventoryEntry> Packages,
    IReadOnlyList<InspectionCommandEvidence> Evidence,
    string? ErrorMessage = null);

public interface IPackageInventoryService
{
    Task<PackageInventoryResult> GetInventoryAsync(
        string serial,
        CancellationToken cancellationToken = default);

    Task<PackageInventoryEntry?> GetDetailsAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default);
}

public interface IPackageInventoryRepository
{
    Task<long> SaveAsync(PackageInventoryResult inventory, CancellationToken cancellationToken = default);
    Task<PackageInventoryResult?> GetLatestAsync(string serial, CancellationToken cancellationToken = default);
}

public interface IPackagePreferenceRepository
{
    Task<IReadOnlyDictionary<string, PackageOverride>> GetOverridesAsync(
        string serial,
        CancellationToken cancellationToken = default);
    Task SetOverrideAsync(
        string serial,
        string packageName,
        PackageOverride value,
        string? note = null,
        CancellationToken cancellationToken = default);
    Task<string?> GetNoteAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default);
    Task SetNoteAsync(
        string serial,
        string packageName,
        string note,
        CancellationToken cancellationToken = default);
}

public sealed record PackageClassificationContext(
    AndroidDevice Device,
    string? ActiveLauncherPackage,
    IReadOnlySet<string> DefaultInputMethodPackages,
    IReadOnlySet<string> EnabledAccessibilityPackages,
    IReadOnlySet<string> DeviceOwnerPackages);

public interface IPackageClassifier
{
    PackageAssessment Classify(
        PackageInventoryEntry package,
        PackageClassificationContext context);
}

public interface IDebloatPlanner
{
    Task<DebloatPlan> CreatePlanAsync(
        string serial,
        DebloatPreset preset,
        CancellationToken cancellationToken = default);
}

public interface IDebloatExecutionService
{
    Task<ScriptExecutionResult> ExecuteAsync(
        DebloatPlan plan,
        CancellationToken cancellationToken = default);

    Task<ScriptUndoResult> RestoreAsync(
        long executionId,
        string serial,
        CancellationToken cancellationToken = default);
}

public sealed record AdbCommandHistoryItem(
    string Serial,
    string Command,
    AdbCommandResult Result,
    DateTimeOffset ExecutedUtc);

public interface IAdbCommandService
{
    Task<AdbCommandResult> ExecuteAsync(
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

public interface IDeveloperVerificationPolicyProvider
{
    DeveloperVerificationPolicy GetPolicy(AndroidDevice? device);
}

public interface IScriptExecutionService
{
    Task<ScriptExecutionResult> ExecuteAsync(
        ScriptDefinition script,
        AndroidDevice target,
        CancellationToken cancellationToken = default);

    Task<ScriptUndoResult> UndoAsync(
        long executionId,
        string serial,
        CancellationToken cancellationToken = default);
}

public sealed record ScriptExecutionResult(
    long ExecutionId,
    string Status,
    int SuccessfulActions,
    int FailedActions,
    bool CanUndo);

public sealed record ScriptUndoResult(
    long ExecutionId,
    string Status,
    int RestoredActions,
    int FailedActions);

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
    Task SyncSessionsAsync(
        IReadOnlyList<AndroidDevice> devices,
        string? adbVersion,
        CancellationToken cancellationToken = default);
    Task RecoverOpenSessionsAsync(CancellationToken cancellationToken = default);
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
    DateTimeOffset? LastDisconnectedUtc,
    DateTimeOffset StartedUtc,
    DateTimeOffset? EndedUtc)
{
    public TimeSpan? Duration => EndedUtc is { } ended ? ended - StartedUtc : DateTimeOffset.UtcNow - StartedUtc;
}

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

public interface IAppLogger
{
    void Information(string source, string message);
    void Warning(string source, string message);
    void Error(string source, string message, Exception? exception = null);
}
