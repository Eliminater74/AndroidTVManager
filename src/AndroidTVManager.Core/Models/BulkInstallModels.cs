namespace AndroidTVManager.Core.Models;

public enum ApkContainerKind
{
    Apk,
    Apks,
    Xapk,
    Apkm
}

public enum BulkInstallItemStatus
{
    Pending,
    Installing,
    Succeeded,
    Failed,
    Skipped,
    Canceled
}

public enum BulkInstallReconciliationState
{
    NotNeeded,
    Verified,
    Unknown
}

public sealed record ApkArtifact(
    string Path,
    string FileName,
    long SizeBytes,
    ApkContainerKind ContainerKind,
    bool IsBase,
    string? PackageName = null,
    string? VersionName = null,
    long? VersionCode = null);

public sealed record ApkInstallGroup(
    string Key,
    string DisplayName,
    IReadOnlyList<ApkArtifact> Artifacts,
    string? PackageName = null,
    string? VersionName = null,
    long? VersionCode = null)
{
    public bool IsSplit => Artifacts.Count > 1;
}

public sealed record BulkInstallItem(
    ApkInstallGroup Group,
    BulkInstallItemStatus Status = BulkInstallItemStatus.Pending,
    AdbCommandResult? Result = null);

public sealed record BulkInstallPackageSet(
    IReadOnlyList<ApkInstallGroup> Groups,
    IReadOnlyList<string> TemporaryDirectories);

public sealed record BulkInstallProgress(
    int Completed,
    int Total,
    string CurrentItem,
    BulkInstallItemStatus Status);

public sealed record BulkInstallResult(
    IReadOnlyList<BulkInstallItem> Items,
    bool WasCanceled,
    BulkInstallReconciliationState ReconciliationState = BulkInstallReconciliationState.NotNeeded,
    string? ReconciliationMessage = null)
{
    public int SucceededCount => Items.Count(item => item.Status == BulkInstallItemStatus.Succeeded);
    public int FailedCount => Items.Count(item => item.Status == BulkInstallItemStatus.Failed);
    public int SkippedCount => Items.Count(item => item.Status == BulkInstallItemStatus.Skipped);
}

public enum DeploymentStepKind
{
    InstallApk,
    DisablePackage,
    EnablePackage,
    RunScript
}

public enum DeploymentCompatibilityState
{
    Compatible,
    Warning,
    Incompatible,
    Unknown
}

public sealed record DeploymentCompatibility(
    DeploymentCompatibilityState State,
    IReadOnlyList<string> Reasons);

public sealed record DeploymentProfileStep(
    long Id,
    int SortOrder,
    DeploymentStepKind Kind,
    string DisplayName,
    string? RelativePath = null,
    string? PackageName = null,
    string? ScriptJson = null,
    bool IsOptional = false,
    IReadOnlyList<long>? AssetIds = null);

public sealed record DeploymentProfileAsset(
    long Id,
    long ProfileId,
    string Sha256,
    string OriginalFileName,
    string StoredFileName,
    long SizeBytes,
    ApkContainerKind ContainerKind,
    string? PackageName,
    string? VersionName,
    long? VersionCode,
    DateTimeOffset ImportedUtc);

public sealed record DeploymentProfile(
    long Id,
    string Name,
    string? Description,
    string? Manufacturer,
    string? Brand,
    string? Model,
    string? Product,
    string? Device,
    int? MinimumApiLevel,
    int? MaximumApiLevel,
    string? Abi,
    bool? RequiresAndroidTv,
    bool? RequiresGoogleTv,
    string? BuildFingerprintPrefix,
    int FormatVersion,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    IReadOnlyList<DeploymentProfileStep> Steps,
    IReadOnlyList<DeploymentProfileAsset>? Assets = null);

public sealed record DeploymentProfileExecution(
    long Id,
    long ProfileId,
    string ProfileName,
    string Serial,
    DateTimeOffset StartedUtc,
    DateTimeOffset? CompletedUtc,
    string Status,
    string? ErrorMessage);

public sealed record DeploymentProfileStepResult(
    DeploymentProfileStep Step,
    string Status,
    AdbCommandResult? CommandResult = null,
    string? ErrorMessage = null);

public sealed record DeploymentExecutionStep(
    long Id,
    long ExecutionId,
    long ProfileStepId,
    int SortOrder,
    string Status,
    string? Output,
    bool Reversible,
    string? UndoStatus);

public sealed record DeploymentProfileDeploymentResult(
    DeploymentProfileExecution Execution,
    IReadOnlyList<DeploymentProfileStepResult> Steps,
    bool WasCanceled);
