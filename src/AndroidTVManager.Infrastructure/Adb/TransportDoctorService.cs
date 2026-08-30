using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class TransportDoctorService : ITransportDoctorService
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private readonly IAdbProcessRunner _runner;

    public TransportDoctorService(IAdbProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<TransportDoctorResult> RunAsync(
        AndroidDevice device,
        int probeCount = 10,
        CancellationToken cancellationToken = default)
    {
        if (device.State != DeviceState.Device)
            throw new InvalidOperationException("The selected device is not currently ready for ADB probes.");
        probeCount = Math.Clamp(probeCount, 1, 50);

        var probes = new List<TransportProbe>(probeCount);
        for (var index = 1; index <= probeCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _runner.RunForDeviceAsync(
                device.Serial,
                ["shell", "echo", "android-tv-manager-transport-check"],
                ProbeTimeout,
                cancellationToken);
            probes.Add(new TransportProbe(
                index,
                result.IsSuccess && result.StandardOutput.Contains(
                    "android-tv-manager-transport-check", StringComparison.Ordinal),
                result.Duration,
                result.StandardOutput.Trim(),
                result.IsSuccess ? null : result.StandardError.Trim()));
        }

        return new TransportDoctorResult(
            device.Serial,
            device.Endpoint,
            device.ConnectionType,
            device.State,
            DateTimeOffset.UtcNow,
            probes);
    }
}
