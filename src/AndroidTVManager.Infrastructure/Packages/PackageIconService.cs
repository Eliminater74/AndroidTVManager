using System.IO.Compression;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Packages;

public sealed class PackageIconService : IPackageIconService
{
    private static readonly TimeSpan PullTimeout = TimeSpan.FromSeconds(45);
    private readonly IAdbProcessRunner _runner;
    private readonly ILocalAppDataPaths _paths;

    public PackageIconService(IAdbProcessRunner runner, ILocalAppDataPaths paths)
    {
        _runner = runner;
        _paths = paths;
    }

    public async Task<string?> GetIconPathAsync(
        string serial,
        PackageInventoryEntry package,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial) || package.ApkPaths.Count == 0 || !package.IsInstalled)
            return null;

        var cacheDirectory = Path.Combine(_paths.Root, "IconCache");
        Directory.CreateDirectory(cacheDirectory);
        var cachePath = Path.Combine(cacheDirectory,
            $"{Sanitize(package.PackageName)}-{package.VersionCode?.ToString() ?? "unknown"}.png");
        if (File.Exists(cachePath))
            return cachePath;

        var remoteApk = package.ApkPaths.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(remoteApk))
            return null;

        var temporaryApk = Path.Combine(_paths.TempPath, $"icon-{Guid.NewGuid():N}.apk");
        try
        {
            Directory.CreateDirectory(_paths.TempPath);
            var pull = await _runner.RunForDeviceAsync(
                serial.Trim(),
                ["pull", remoteApk, temporaryApk],
                PullTimeout,
                cancellationToken);
            if (!pull.IsSuccess || !File.Exists(temporaryApk))
                return null;

            using var archive = ZipFile.OpenRead(temporaryApk);
            var icon = archive.Entries
                .Where(entry => entry.FullName.StartsWith("res/", StringComparison.OrdinalIgnoreCase)
                    && entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ScoreIcon)
                .ThenByDescending(entry => entry.Length)
                .FirstOrDefault();
            if (icon is null)
                return null;

            await using var input = icon.Open();
            await using var output = new FileStream(cachePath, FileMode.CreateNew, FileAccess.Write,
                FileShare.Read, 8192, useAsync: true);
            await input.CopyToAsync(output, cancellationToken);
            return cachePath;
        }
        catch (IOException)
        {
            return File.Exists(cachePath) ? cachePath : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryApk))
                    File.Delete(temporaryApk);
            }
            catch (IOException)
            {
            }
        }
    }

    private static int ScoreIcon(ZipArchiveEntry entry)
    {
        var path = entry.FullName.ToLowerInvariant();
        var score = path.Contains("mipmap") ? 30 : 0;
        score += path.Contains("launcher") || path.Contains("icon") ? 20 : 0;
        score += path.Contains("foreground") || path.Contains("background") ? -10 : 0;
        score += path.Contains("xxxhdpi") ? 8 : path.Contains("xxhdpi") ? 6 : path.Contains("xhdpi") ? 4 : 0;
        return score;
    }

    private static string Sanitize(string packageName)
        => string.Concat(packageName.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_'));
}
