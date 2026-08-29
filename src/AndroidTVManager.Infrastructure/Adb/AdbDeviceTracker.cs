using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Diagnostics;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class AdbDeviceTracker : IAdbDeviceTracker
{
    private readonly IAdbToolsManager _toolsManager;
    private readonly IAdbProcessRunner _runner;
    private readonly IAppLogger _logger;
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, byte> _enrichmentInFlight = new();
    private readonly ConcurrentDictionary<string, EnrichedMetadata> _metadataCache = new();
    private CancellationTokenSource? _stopSource;
    private Task? _trackingTask;
    private ImmutableArray<AndroidDevice> _currentDevices = [];

    public AdbDeviceTracker(IAdbToolsManager toolsManager, IAdbProcessRunner runner, IAppLogger logger)
    {
        _toolsManager = toolsManager;
        _runner = runner;
        _logger = logger;
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
                _logger.Information("Tracker", "Starting adb track-devices -l.");
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
            catch (Exception exception)
            {
                _logger.Error("Tracker", "ADB device tracker exited unexpectedly; retrying with backoff.", exception);
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
        devices = devices.Select(ApplyCachedMetadata).ToArray();
        if (_currentDevices.Select(device => (device.Serial, device.State, device.Model, device.AndroidVersion,
                device.ApiLevel, device.ReportedName, device.MacAddress))
            .SequenceEqual(devices.Select(device => (device.Serial, device.State, device.Model, device.AndroidVersion,
                device.ApiLevel, device.ReportedName, device.MacAddress))))
            return;

        _currentDevices = devices.ToImmutableArray();
        DevicesChanged?.Invoke(this, _currentDevices);
        foreach (var device in devices.Where(device => device.State == DeviceState.Device
                     && _enrichmentInFlight.TryAdd(device.Serial, 0)))
        {
            _ = EnrichAsync(device);
        }
    }

    private async Task EnrichAsync(AndroidDevice device)
    {
        try
        {
            var metadataTask = _runner.RunForDeviceAsync(device.Serial, ["shell", "getprop"],
                TimeSpan.FromSeconds(30));
            var nameTask = _runner.RunForDeviceAsync(device.Serial, ["shell", "settings", "get", "global", "device_name"],
                TimeSpan.FromSeconds(15));
            var networkTask = _runner.RunForDeviceAsync(device.Serial, ["shell", "ip", "link"],
                TimeSpan.FromSeconds(15));
            await Task.WhenAll(metadataTask, nameTask, networkTask);
            var result = metadataTask.Result;
            if (!result.IsSuccess)
                return;

            var metadata = AdbMetadataParser.Parse(result.StandardOutput);
            var reportedName = nameTask.Result.IsSuccess
                ? AdbMetadataParser.ParseReportedName(nameTask.Result.StandardOutput)
                : null;
            var macAddress = networkTask.Result.IsSuccess
                ? AdbMetadataParser.ParseMacAddress(networkTask.Result.StandardOutput)
                : null;
            _metadataCache[device.Serial] = new EnrichedMetadata(metadata, reportedName, macAddress);
            var enriched = new AndroidDevice
            {
                Serial = device.Serial,
                Endpoint = device.Endpoint,
                State = device.State,
                ConnectionType = device.ConnectionType,
                ReportedName = reportedName,
                MacAddress = macAddress,
                Manufacturer = metadata.Manufacturer,
                Brand = metadata.Brand,
                Model = device.Model ?? metadata.Model,
                Product = metadata.Product,
                DeviceName = metadata.DeviceName,
                Board = metadata.Board,
                AndroidVersion = metadata.AndroidVersion,
                ApiLevel = metadata.ApiLevel,
                SecurityPatch = metadata.SecurityPatch,
                BuildId = metadata.BuildId,
                BuildType = metadata.BuildType,
                BuildFingerprint = metadata.BuildFingerprint,
                SeenAtUtc = device.SeenAtUtc
            };
            var updated = _currentDevices.Select(current =>
                current.Serial == device.Serial ? enriched : current).ToArray();
            Publish(updated);
        }
        finally
        {
            _enrichmentInFlight.TryRemove(device.Serial, out _);
        }
    }

    private AndroidDevice ApplyCachedMetadata(AndroidDevice device)
    {
        if (!_metadataCache.TryGetValue(device.Serial, out var cached))
            return device;

        return new AndroidDevice
        {
            Serial = device.Serial,
            Endpoint = device.Endpoint,
            State = device.State,
            ConnectionType = device.ConnectionType,
            ReportedName = device.ReportedName ?? cached.ReportedName,
            MacAddress = device.MacAddress ?? cached.MacAddress,
            Manufacturer = device.Manufacturer ?? cached.Metadata.Manufacturer,
            Brand = device.Brand ?? cached.Metadata.Brand,
            Model = device.Model ?? cached.Metadata.Model,
            Product = device.Product ?? cached.Metadata.Product,
            DeviceName = device.DeviceName ?? cached.Metadata.DeviceName,
            Board = device.Board ?? cached.Metadata.Board,
            AndroidVersion = device.AndroidVersion ?? cached.Metadata.AndroidVersion,
            ApiLevel = device.ApiLevel ?? cached.Metadata.ApiLevel,
            SecurityPatch = device.SecurityPatch ?? cached.Metadata.SecurityPatch,
            BuildId = device.BuildId ?? cached.Metadata.BuildId,
            BuildType = device.BuildType ?? cached.Metadata.BuildType,
            BuildFingerprint = device.BuildFingerprint ?? cached.Metadata.BuildFingerprint,
            SeenAtUtc = device.SeenAtUtc
        };
    }

    private sealed record EnrichedMetadata(
        AdbDeviceMetadata Metadata,
        string? ReportedName,
        string? MacAddress);

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
