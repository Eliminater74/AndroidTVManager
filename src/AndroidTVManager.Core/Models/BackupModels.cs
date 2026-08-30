namespace AndroidTVManager.Core.Models;

public enum BackupKind
{
    DeviceReport,
    ConfigurationSnapshot,
    PackageApks,
    SharedStorage,
    LegacyAppData,
    FullDeviceImage
}

public sealed record BackupCapability(
    BackupKind Kind,
    string Name,
    CapabilityState State,
    string Description,
    string Evidence);

public sealed record BackupRequest(
    string Serial,
    string DestinationDirectory,
    IReadOnlySet<BackupKind> Kinds,
    string SharedStoragePath = "/sdcard/");

public sealed record BackupProgress(
    BackupKind Kind,
    int CompletedKinds,
    int TotalKinds,
    string Status);

public sealed record BackupArtifact(
    BackupKind Kind,
    string Name,
    string Path,
    long? SizeBytes,
    CapabilityState State,
    string? Details = null);

public sealed record DeviceBackupManifest(
    string Serial,
    string? FriendlyDeviceName,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<BackupKind> RequestedKinds,
    IReadOnlyList<BackupArtifact> Artifacts,
    IReadOnlyList<string> Warnings);

public sealed record DeviceBackupResult(
    string Serial,
    string DestinationDirectory,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<BackupArtifact> Artifacts,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage = null);

public sealed record BackupRestoreResult(
    string Serial,
    int RestoredPackages,
    int FailedPackages,
    IReadOnlyList<string> Messages);
