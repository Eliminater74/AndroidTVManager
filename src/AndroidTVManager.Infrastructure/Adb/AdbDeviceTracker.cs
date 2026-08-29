using System.Collections.Immutable;
using System.Diagnostics;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class AdbDeviceTracker : IAdbDeviceTracker
{
    private readonly IAdbToolsManager _toolsManager;
    private readonly object _sync = new();
    private CancellationTokenSource? _stopSource;
    private Task? _trackingTask;
    private ImmutableArray<AndroidDevice> _currentDevices = [];

    public AdbDeviceTracker(IAdbToolsManager toolsManager)
    {
        _toolsManager = toolsManager;
    }

    public event EventHandler<IReadOnlyList<AndroidDevice>>? DevicesChanged;
    public IReadOnlyList<AndroidDevice> CurrentDevices => _currentDevices;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_trackingTask is not null)
                return Task.CompletedTask;

            _stopSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _trackingTask = TrackLoopAsync(_stopSource.Token);
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? trackingTask;
        lock (_sync)
        {
            _stopSource?.Cancel();
            trackingTask = _trackingTask;
        }

        if (trackingTask is not null)
            await trackingTask.WaitAsync(cancellationToken);

        lock (_sync)
        {
            _trackingTask = null;
            _stopSource?.Dispose();
            _stopSource = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task TrackLoopAsync(CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromMilliseconds(500);
        while (!cancellationToken.IsCancellationRequested)
        {
            var adbPath = _toolsManager.AdbPath;
            if (adbPath is null)
            {
                Publish([]);
                await DelayAsync(retryDelay, cancellationToken);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
                continue;
            }

            try
            {
                using var process = StartTracker(adbPath);
                retryDelay = TimeSpan.FromMilliseconds(500);
                var snapshot = new List<string>();
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                    if (line is null)
                        break;
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        Publish(AdbParsers.ParseTrackedDevices(string.Join(Environment.NewLine, snapshot)));
                        snapshot.Clear();
                    }
                    else
                    {
                        snapshot.Add(line);
                    }
                }

                TryKill(process);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await DelayAsync(retryDelay, cancellationToken);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
            }
        }
    }

    private static Process StartTracker(string adbPath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = adbPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(adbPath) ?? Environment.CurrentDirectory
            }
        };
        process.StartInfo.ArgumentList.Add("track-devices");
        process.StartInfo.ArgumentList.Add("-l");
        if (!process.Start())
            throw new InvalidOperationException("Unable to start the ADB device tracker.");
        return process;
    }

    private void Publish(IReadOnlyList<AndroidDevice> devices)
    {
        if (_currentDevices.Select(device => (device.Serial, device.State, device.Model))
            .SequenceEqual(devices.Select(device => (device.Serial, device.State, device.Model))))
            return;

        _currentDevices = devices.ToImmutableArray();
        DevicesChanged?.Invoke(this, _currentDevices);
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
