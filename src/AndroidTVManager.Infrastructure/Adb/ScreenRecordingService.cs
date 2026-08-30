using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class ScreenRecordingService : IScreenRecordingService
{
    private readonly IAdbStreamingProcessRunner _streaming;
    private readonly IAdbProcessRunner _runner;
    private readonly ILocalAppDataPaths _paths;
    private IAdbProcessSession? _session;

    public ScreenRecordingService(
        IAdbStreamingProcessRunner streaming,
        IAdbProcessRunner runner,
        ILocalAppDataPaths paths)
    {
        _streaming = streaming;
        _runner = runner;
        _paths = paths;
    }

    public bool IsRecording => _session is not null;
    public ScreenRecordingInfo? Current { get; private set; }

    public async Task<ScreenRecordingInfo> StartAsync(
        string serial,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (_session is not null)
            throw new InvalidOperationException("A screen recording is already running.");
        _paths.EnsureCreated();
        var remotePath = $"/sdcard/atm-recording-{Guid.NewGuid():N}.mp4";
        _session = await _streaming.StartForDeviceAsync(
            serial.Trim(),
            ["shell", "screenrecord", "--time-limit", Math.Clamp((int)duration.TotalSeconds, 1, 1800).ToString(), remotePath],
            cancellationToken);
        Current = new(serial.Trim(), remotePath, DateTimeOffset.UtcNow);
        return Current;
    }

    public async Task<string?> StopAsync(CancellationToken cancellationToken = default)
    {
        if (_session is null || Current is null)
            return null;
        var session = _session;
        var recording = Current;
        _session = null;
        Current = null;
        await session.DisposeAsync();
        var localPath = Path.Combine(_paths.RecordingsPath, $"recording-{DateTime.Now:yyyyMMdd-HHmmss}.mp4");
        var pull = await _runner.RunForDeviceAsync(
            recording.Serial,
            ["pull", recording.RemotePath, localPath],
            TimeSpan.FromMinutes(10),
            cancellationToken);
        _ = _runner.RunForDeviceAsync(
            recording.Serial,
            ["shell", "rm", recording.RemotePath],
            cancellationToken: CancellationToken.None);
        return pull.IsSuccess ? localPath : null;
    }
}
