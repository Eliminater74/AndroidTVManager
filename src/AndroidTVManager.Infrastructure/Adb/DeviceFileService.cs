using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class DeviceFileService : IDeviceFileService
{
    private readonly IAdbProcessRunner _runner;

    public DeviceFileService(IAdbProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<IReadOnlyList<DeviceFileEntry>> ListAsync(
        string serial,
        string remoteDirectory,
        CancellationToken cancellationToken = default)
    {
        ValidatePath(remoteDirectory);
        var result = await _runner.RunForDeviceAsync(
            serial.Trim(),
            ["shell", "ls", "-la", remoteDirectory],
            TimeSpan.FromSeconds(30),
            cancellationToken);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.StandardError.Trim());
        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => ParseLine(remoteDirectory, line))
            .Where(entry => entry is not null)
            .Cast<DeviceFileEntry>()
            .ToArray();
    }

    public Task<AdbCommandResult> PushAsync(
        string serial,
        string localPath,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(localPath))
            throw new FileNotFoundException("The local file does not exist.", localPath);
        ValidatePath(remotePath);
        return _runner.RunForDeviceAsync(
            serial.Trim(),
            ["push", localPath, remotePath],
            TimeSpan.FromMinutes(10),
            cancellationToken);
    }

    public Task<AdbCommandResult> PullAsync(
        string serial,
        string remotePath,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        ValidatePath(remotePath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(localPath))!);
        return _runner.RunForDeviceAsync(
            serial.Trim(),
            ["pull", remotePath, localPath],
            TimeSpan.FromMinutes(10),
            cancellationToken);
    }

    public Task<AdbCommandResult> CreateDirectoryAsync(
        string serial,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        ValidatePath(remotePath);
        return _runner.RunForDeviceAsync(
            serial.Trim(),
            ["shell", "mkdir", "-p", remotePath],
            TimeSpan.FromSeconds(30),
            cancellationToken);
    }

    public Task<AdbCommandResult> DeleteAsync(
        string serial,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        ValidatePath(remotePath);
        if (remotePath.TrimEnd('/', '\\').Equals("/sdcard", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Deleting the shared-storage root is not allowed.", nameof(remotePath));
        return _runner.RunForDeviceAsync(
            serial.Trim(),
            ["shell", "rm", "-rf", remotePath],
            TimeSpan.FromSeconds(30),
            cancellationToken);
    }

    private static DeviceFileEntry? ParseLine(string directory, string line)
    {
        if (line.StartsWith("total ", StringComparison.OrdinalIgnoreCase))
            return null;
        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 8 || (fields[0][0] != 'd' && fields[0][0] != '-'))
            return null;
        var name = string.Join(' ', fields.Skip(7));
        if (name is "." or "..")
            return null;
        return new(
            directory.TrimEnd('/') + "/" + name,
            name,
            fields[0][0] == 'd',
            long.TryParse(fields[4], out var size) ? size : null,
            string.Join(' ', fields.Skip(5).Take(2)));
    }

    private static void ValidatePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        if (!normalized.StartsWith("/sdcard", StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("File operations are limited to shared storage paths.", nameof(path));
        if (normalized.Contains('\0') || normalized.Contains("/../", StringComparison.Ordinal)
            || normalized.EndsWith("/..", StringComparison.Ordinal))
            throw new ArgumentException("The remote path contains an invalid or unsafe location.", nameof(path));
    }
}
