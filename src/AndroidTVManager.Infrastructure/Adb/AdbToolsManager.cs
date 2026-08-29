using System.Diagnostics;
using System.IO.Compression;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Adb;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class AdbToolsManager : IAdbToolsManager
{
    private const string DownloadUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";
    private static readonly HttpClient HttpClient = new();
    private readonly ILocalAppDataPaths _paths;
    private readonly SemaphoreSlim _installLock = new(1, 1);
    private string? _installedVersion;

    public AdbToolsManager(ILocalAppDataPaths paths)
    {
        _paths = paths;
        _paths.EnsureCreated();
    }

    public string? AdbPath => File.Exists(GetAdbPath()) ? GetAdbPath() : null;
    public string? InstalledVersion => _installedVersion;
    public DateTimeOffset? LastUpdateCheckUtc { get; private set; }
    public bool IsReady => AdbPath is not null;

    public async Task<AdbToolStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (AdbPath is null)
            return new(false, null, null, LastUpdateCheckUtc, "Platform-Tools are not installed.");

        _installedVersion ??= await ReadVersionAsync(AdbPath, cancellationToken);
        return _installedVersion is null
            ? new(false, null, AdbPath, LastUpdateCheckUtc, "The installed ADB executable did not return a valid version.")
            : new(true, _installedVersion, AdbPath, LastUpdateCheckUtc);
    }

    public async Task<AdbToolStatus> InstallOrRepairAsync(
        IProgress<AdbDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _installLock.WaitAsync(cancellationToken);
        try
        {
            _paths.EnsureCreated();
            var parent = Directory.GetParent(_paths.ToolsPath)?.FullName ?? _paths.Root;
            var staging = Path.Combine(parent, $"PlatformTools.staging-{Guid.NewGuid():N}");
            var archive = Path.Combine(_paths.TempPath, "platform-tools-latest-windows.zip");
            try
            {
                progress?.Report(new(0, null, "Downloading official Google Platform-Tools…"));
                await DownloadAsync(archive, progress, cancellationToken);
                Directory.CreateDirectory(staging);
                ZipFile.ExtractToDirectory(archive, staging);

                var extracted = Directory.GetDirectories(staging).FirstOrDefault(path =>
                    string.Equals(Path.GetFileName(path), "platform-tools", StringComparison.OrdinalIgnoreCase));
                var packageRoot = extracted ?? staging;
                var stagedAdb = Path.Combine(packageRoot, "adb.exe");
                if (!File.Exists(stagedAdb))
                    throw new InvalidDataException("The downloaded archive did not contain adb.exe.");

                var version = await ReadVersionAsync(stagedAdb, cancellationToken)
                    ?? throw new InvalidDataException("The downloaded ADB executable did not return a valid version.");
                var previous = _paths.ToolsPath + ".previous";
                if (Directory.Exists(previous))
                    Directory.Delete(previous, recursive: true);
                if (Directory.Exists(_paths.ToolsPath))
                    Directory.Move(_paths.ToolsPath, previous);
                Directory.Move(packageRoot, _paths.ToolsPath);
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
                if (Directory.Exists(previous))
                    Directory.Delete(previous, recursive: true);

                _installedVersion = version;
                return new(true, version, AdbPath, DateTimeOffset.UtcNow);
            }
            catch
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
                throw;
            }
            finally
            {
                if (File.Exists(archive))
                    File.Delete(archive);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(false, _installedVersion, AdbPath, LastUpdateCheckUtc, exception.Message);
        }
        finally
        {
            _installLock.Release();
        }
    }

    private async Task DownloadAsync(
        string archive,
        IProgress<AdbDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(
            DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(archive);
        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            progress?.Report(new(received, total, "Downloading official Google Platform-Tools…"));
        }
    }

    private static async Task<string?> ReadVersionAsync(string executablePath, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("version");
        if (!process.Start())
            return null;
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return AdbParsers.ParseAdbVersion(output);
    }

    private string GetAdbPath() => Path.Combine(_paths.ToolsPath, "adb.exe");
}
