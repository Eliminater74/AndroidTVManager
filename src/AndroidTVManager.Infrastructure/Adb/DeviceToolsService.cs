using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Core.Utilities;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class DeviceToolsService : IDeviceToolsService
{
    private readonly IAdbProcessRunner _runner;
    private readonly ILocalAppDataPaths _paths;

    public DeviceToolsService(IAdbProcessRunner runner, ILocalAppDataPaths paths)
    {
        _runner = runner;
        _paths = paths;
    }

    public Task<AdbCommandResult> RebootAsync(
        string serial,
        string mode = "",
        CancellationToken cancellationToken = default)
    {
        var arguments = string.IsNullOrWhiteSpace(mode)
            ? new[] { "reboot" }
            : new[] { "reboot", mode };
        return _runner.RunForDeviceAsync(serial, arguments, TimeSpan.FromSeconds(30), cancellationToken);
    }

    public Task<AdbCommandResult> ShellAsync(
        string serial,
        string command,
        CancellationToken cancellationToken = default)
        => _runner.RunForDeviceAsync(serial, ["shell", command], TimeSpan.FromMinutes(5), cancellationToken);

    public async Task<string> CaptureScreenshotAsync(
        string serial,
        string friendlyName,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        var remotePath = $"/sdcard/atm-screenshot-{Guid.NewGuid():N}.png";
        var fileName = $"{FilenameSanitizer.Sanitize(friendlyName)}-{DateTime.Now:yyyyMMdd-HHmmss}.png";
        var localPath = Path.Combine(_paths.ScreenshotsPath, fileName);

        var capture = await _runner.RunForDeviceAsync(serial, ["shell", "screencap", "-p", remotePath],
            TimeSpan.FromMinutes(2), cancellationToken);
        if (!capture.IsSuccess)
            throw new InvalidOperationException(capture.StandardError);

        var pull = await _runner.RunForDeviceAsync(serial, ["pull", remotePath, localPath],
            TimeSpan.FromMinutes(2), cancellationToken);
        _ = _runner.RunForDeviceAsync(serial, ["shell", "rm", remotePath], cancellationToken: CancellationToken.None);
        if (!pull.IsSuccess)
            throw new InvalidOperationException(pull.StandardError);
        return localPath;
    }
}
