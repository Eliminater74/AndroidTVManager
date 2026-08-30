namespace AndroidTVManager.Core.Models;

public sealed record UpdateRelease(
    string TagName,
    string Version,
    string Name,
    string ReleaseNotes,
    string ReleaseUrl,
    string InstallerUrl,
    string? InstallerSha256,
    string? ChecksumsUrl,
    DateTimeOffset PublishedUtc);

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    string CurrentVersion,
    UpdateRelease? Release,
    DateTimeOffset CheckedUtc,
    string? ErrorMessage = null);

public sealed record UpdateInstallResult(
    bool Started,
    string Message,
    string? InstallerPath = null);
