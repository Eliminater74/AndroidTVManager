using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Core.Recovery;

public enum RecoveryCompatibilityState
{
    Compatible,
    Incompatible,
    Unknown,
    UnknownRequiredEvidence
}

public sealed record RecoveryZipDeclaration(
    IReadOnlyList<string> DeclaredDevices,
    string? PreBuild,
    string Evidence);

public sealed record RecoveryZipCompatibility(
    RecoveryCompatibilityState State,
    string? DeclaredDeviceSummary,
    string? ObservedDevice,
    IReadOnlyList<string> Reasons);
