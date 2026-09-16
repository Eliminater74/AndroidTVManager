using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Updates;

public sealed class UpdateService : IUpdateService
{
    public const long MaximumInstallerBytes = 200L * 1024 * 1024;
    private const string Repository = "Eliminater74/AndroidTVManager";
    private readonly HttpClient _http;
    private readonly ILocalAppDataPaths _paths;
    private readonly IAppLogger _logger;
    private readonly long _maximumInstallerBytes;

    public UpdateService(ILocalAppDataPaths paths, IAppLogger logger)
        : this(paths, logger, CreateHttpClient())
    {
    }

    public UpdateService(ILocalAppDataPaths paths, IAppLogger logger, HttpClient httpClient)
        : this(paths, logger, httpClient, MaximumInstallerBytes)
    {
    }

    public UpdateService(
        ILocalAppDataPaths paths,
        IAppLogger logger,
        HttpClient httpClient,
        long maximumInstallerBytes)
    {
        _paths = paths;
        _logger = logger;
        _http = httpClient;
        _maximumInstallerBytes = maximumInstallerBytes;
    }

    public async Task<UpdateCheckResult> CheckAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        var checkedUtc = DateTimeOffset.UtcNow;
        try
        {
            var releases = await _http.GetFromJsonAsync<List<GitHubRelease>>(
                $"https://api.github.com/repos/{Repository}/releases?per_page=30",
                cancellationToken) ?? [];
            var candidate = releases
                .Where(release => !release.Draft && release.PublishedAt is not null)
                .Select(CreateRelease)
                .Where(release => release is not null)
                .Cast<UpdateRelease>()
                .OrderByDescending(release => ParseVersion(release.Version))
                .FirstOrDefault();
            return new(
                candidate is not null && IsNewer(candidate.Version, currentVersion),
                currentVersion,
                candidate,
                checkedUtc);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.Warning("Updates", $"Update check failed: {exception.Message}");
            return new(false, currentVersion, null, checkedUtc, exception.Message);
        }
    }

    public async Task<UpdateInstallResult> DownloadAndInstallAsync(
        UpdateRelease release,
        CancellationToken cancellationToken = default)
    {
        var installerName = Path.GetFileName(new Uri(release.InstallerUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(installerName)
            || !installerName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return new(false, "The release did not provide a valid installer asset.");

        _paths.EnsureCreated();
        Directory.CreateDirectory(_paths.TempPath);
        var installerPath = Path.Combine(_paths.TempPath, $"AndroidTVManager-update-{release.Version}-{Guid.NewGuid():N}.exe");
        var keepInstaller = false;
        try
        {
            await DownloadAsync(release.InstallerUrl, installerPath, cancellationToken);
            var expectedHash = release.InstallerSha256;
            if (string.IsNullOrWhiteSpace(expectedHash) && !string.IsNullOrWhiteSpace(release.ChecksumsUrl))
            {
                var checksums = await _http.GetStringAsync(release.ChecksumsUrl, cancellationToken);
                expectedHash = FindChecksum(checksums, installerName);
            }
            if (string.IsNullOrWhiteSpace(expectedHash))
                return new(false, "The installer has no verifiable SHA-256 checksum.");

            var actualHash = await HashAsync(installerPath, cancellationToken);
            if (!string.Equals(NormalizeHash(expectedHash), actualHash, StringComparison.OrdinalIgnoreCase))
                return new(false, "The downloaded installer checksum did not match the release checksum.");

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? _paths.TempPath
            });
            if (process is null)
                return new(false, "The verified installer could not be started.");
            keepInstaller = true;
            return new(true, "The verified installer was started. Android TV Manager will now close.", installerPath);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or CryptographicException)
        {
            _logger.Warning("Updates", $"Update installation failed: {exception.Message}");
            return new(false, $"Update failed: {exception.Message}");
        }
        finally
        {
            if (!keepInstaller)
                TryDelete(installerPath);
        }
    }

    private static UpdateRelease? CreateRelease(GitHubRelease release)
    {
        var installer = release.Assets.FirstOrDefault(asset =>
            asset.Name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase));
        if (installer is null || release.PublishedAt is null)
            return null;
        var checksum = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
        var version = release.TagName.TrimStart('v', 'V');
        return ParseVersion(version).IsValid
            ? new(
                release.TagName,
                version,
                string.IsNullOrWhiteSpace(release.Name) ? $"Android TV Manager {version}" : release.Name,
                string.IsNullOrWhiteSpace(release.Body) ? "No release notes were provided." : release.Body,
                release.HtmlUrl,
                installer.BrowserDownloadUrl,
                NormalizeHash(installer.Digest),
                checksum?.BrowserDownloadUrl,
                release.PublishedAt.Value)
            : null;
    }

    private async Task DownloadAsync(
        string url,
        string destination,
        CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } announced
            && announced > _maximumInstallerBytes)
            throw new IOException(
                $"The installer is {announced} bytes, which exceeds the {_maximumInstallerBytes} byte limit.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            useAsync: true);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
                break;
            total += read;
            if (total > _maximumInstallerBytes)
                throw new IOException(
                    $"The installer exceeded the {_maximumInstallerBytes} byte limit while downloading.");
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static string? FindChecksum(string content, string fileName)
        => content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? NormalizeHash(string? value)
        => value?.Trim().Replace("sha256:", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static bool IsNewer(string candidate, string current)
        => ParseVersion(candidate).CompareTo(ParseVersion(current)) > 0;

    private static ReleaseVersion ParseVersion(string value)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            value,
            @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-B(?<beta>\d+))?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success
            ? new(
                true,
                int.Parse(match.Groups["major"].Value),
                int.Parse(match.Groups["minor"].Value),
                int.Parse(match.Groups["patch"].Value),
                match.Groups["beta"].Success ? int.Parse(match.Groups["beta"].Value) : int.MaxValue)
            : new(false, 0, 0, 0, 0);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AndroidTVManager", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private readonly record struct ReleaseVersion(
        bool IsValid,
        int Major,
        int Minor,
        int Patch,
        int Beta) : IComparable<ReleaseVersion>
    {
        public int CompareTo(ReleaseVersion other)
        {
            if (IsValid != other.IsValid)
                return IsValid ? 1 : -1;
            return (Major, Minor, Patch, Beta).CompareTo((other.Major, other.Minor, other.Patch, other.Beta));
        }
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset> Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        [property: JsonPropertyName("digest")] string? Digest);
}
