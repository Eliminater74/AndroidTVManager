using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class NetworkDiagnosticsService : INetworkDiagnosticsService
{
    private readonly IAdbProcessRunner _runner;

    public NetworkDiagnosticsService(IAdbProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<NetworkDiagnosticResult> InspectAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        var commands = new[]
        {
            new[] { "shell", "ip", "addr", "show" },
            new[] { "shell", "ip", "route", "show" },
            new[] { "shell", "getprop", "net.dns1" },
            new[] { "shell", "ping", "-c", "1", "-W", "2", "8.8.8.8" }
        };
        var results = await Task.WhenAll(commands.Select(command =>
            _runner.RunForDeviceAsync(serial.Trim(), command, TimeSpan.FromSeconds(15), cancellationToken)));
        return new(
            Format(results[0]),
            Format(results[1]),
            Format(results[2]),
            Format(results[3]),
            DateTimeOffset.UtcNow);
    }

    private static string Format(AdbCommandResult result)
        => result.IsSuccess
            ? result.StandardOutput.Trim()
            : $"Exit {result.ExitCode}: {result.StandardError.Trim()}";
}
