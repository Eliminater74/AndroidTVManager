namespace AndroidTVManager.Core.Models;

public enum PackageMutationKind
{
    Enable,
    Restore,
    Disable,
    UninstallForUser,
    FullUninstall,
    ClearData,
    ClearCache,
    GrantPermission,
    RevokePermission
}

public sealed record PackageMutationRequest(
    string Serial,
    string PackageName,
    PackageMutationKind Kind,
    string? ExpectedBuildFingerprint = null);

public sealed record PackageMutationDecision(
    bool Allowed,
    string? BlockReason,
    PackageAssessment? Assessment = null);
