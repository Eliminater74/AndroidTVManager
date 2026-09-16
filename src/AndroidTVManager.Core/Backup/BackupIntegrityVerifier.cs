using System.Security.Cryptography;

namespace AndroidTVManager.Core.Backup;

public static class BackupIntegrityVerifier
{
    public static async Task<BackupIntegrityResult> VerifyApkSetAsync(
        string backupRoot,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(backupRoot);
        var checksumPath = Path.Combine(root, "SHA256SUMS.txt");
        if (!File.Exists(checksumPath))
            return Failed(0, 0, 0, ["SHA256SUMS.txt"], [], []);

        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in await File.ReadAllLinesAsync(checksumPath, cancellationToken))
        {
            var fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2)
                continue;
            var relative = Normalize(fields[1].TrimStart('*'));
            if (!relative.StartsWith("apks/", StringComparison.OrdinalIgnoreCase))
                continue;
            expected[relative] = fields[0];
        }

        var apkRoot = Path.Combine(root, "apks");
        var actual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(apkRoot))
        {
            foreach (var file in Directory.EnumerateFiles(apkRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                actual[Normalize(Path.GetRelativePath(root, file))] = file;
            }
        }

        var missing = expected.Keys.Except(actual.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unexpected = actual.Keys.Except(expected.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var mismatched = new List<string>();
        foreach (var relative in expected.Keys.Intersect(actual.Keys, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actualHash = await HashFileAsync(actual[relative], cancellationToken);
            if (!string.Equals(actualHash, expected[relative], StringComparison.OrdinalIgnoreCase))
                mismatched.Add(relative);
        }

        var packageCount = expected.Keys
            .Select(PackageName)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var isValid = missing.Length == 0 && unexpected.Length == 0 && mismatched.Count == 0 && expected.Count > 0;
        return isValid
            ? new(true, packageCount, expected.Count, actual.Count, [], [], [])
            : Failed(packageCount, expected.Count, actual.Count, missing, unexpected, mismatched);
    }

    private static BackupIntegrityResult Failed(
        int packages,
        int expectedFiles,
        int actualFiles,
        IReadOnlyList<string> missing,
        IReadOnlyList<string> unexpected,
        IReadOnlyList<string> mismatched)
        => new(false, packages, expectedFiles, actualFiles, missing, unexpected, mismatched);

    private static string Normalize(string relative)
        => relative.Replace('\\', '/').TrimStart('/');

    private static string PackageName(string relative)
    {
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && parts[0].Equals("apks", StringComparison.OrdinalIgnoreCase)
            ? parts[1]
            : string.Empty;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}
