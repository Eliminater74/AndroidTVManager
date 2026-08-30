using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class DeviceLogcatService : IDeviceLogcatService
{
    private readonly IAdbStreamingProcessRunner _streaming;
    private readonly IAdbProcessRunner _runner;

    public DeviceLogcatService(
        IAdbStreamingProcessRunner streaming,
        IAdbProcessRunner runner)
    {
        _streaming = streaming;
        _runner = runner;
    }

    public Task<IAdbProcessSession> StartAsync(
        string serial,
        LogcatOptions options,
        CancellationToken cancellationToken = default)
        => _streaming.StartForDeviceAsync(
            serial,
            ["logcat", "-v", "threadtime"],
            cancellationToken);

    public Task<AdbCommandResult> ClearAsync(
        string serial,
        CancellationToken cancellationToken = default)
        => _runner.RunForDeviceAsync(
            serial,
            ["logcat", "-c"],
            TimeSpan.FromSeconds(15),
            cancellationToken);
}
