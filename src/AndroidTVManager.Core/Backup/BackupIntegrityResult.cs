namespace AndroidTVManager.Core.Backup;

public sealed record BackupIntegrityResult(
    bool IsValid,
    int ExpectedPackageCount,
    int ExpectedFileCount,
    int ActualFileCount,
    IReadOnlyList<string> MissingFiles,
    IReadOnlyList<string> UnexpectedFiles,
    IReadOnlyList<string> MismatchedFiles)
{
    public IReadOnlyList<string> Messages
    {
        get
        {
            if (IsValid)
                return [];
            var messages = new List<string>
            {
                $"Backup expected {ExpectedPackageCount} package(s) / {ExpectedFileCount} APK file(s); nothing was installed."
            };
            messages.AddRange(MissingFiles.Select(file => $"{file}: missing from the backup folder."));
            messages.AddRange(UnexpectedFiles.Select(file => $"{file}: no checksum entry."));
            messages.AddRange(MismatchedFiles.Select(file => $"{file}: checksum mismatch."));
            return messages;
        }
    }
}
