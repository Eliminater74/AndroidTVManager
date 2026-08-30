namespace AndroidTVManager.Core.Models;

public enum DiagnosticBundlePrivacyMode
{
    LocalFull,
    SupportRedacted
}

public sealed record DiagnosticBundleRequest(
    AndroidDevice Device,
    string ApplicationVersion,
    DiagnosticBundlePrivacyMode PrivacyMode = DiagnosticBundlePrivacyMode.SupportRedacted,
    int LogcatLineLimit = 500);

public sealed record DiagnosticBundleResult(
    string ArchivePath,
    IReadOnlyList<string> IncludedFiles,
    IReadOnlyList<string> Warnings);
