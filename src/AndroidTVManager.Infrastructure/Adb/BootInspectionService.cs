using System.Diagnostics;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class BootInspectionService : IBootInspectionService
{
    private readonly IAdbProcessRunner _adb;
    private readonly IAdbToolsManager _tools;
    private readonly IDeviceToolsService _deviceTools;

    public BootInspectionService(
        IAdbProcessRunner adb,
        IAdbToolsManager tools,
        IDeviceToolsService deviceTools)
    {
        _adb = adb;
        _tools = tools;
        _deviceTools = deviceTools;
    }

    public async Task<BootInspectionResult> InspectAsync(CancellationToken cancellationToken = default)
    {
        var adb = await _adb.RunAsync(["devices", "-l"], TimeSpan.FromSeconds(15), cancellationToken);
        var adbLine = adb.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);
        if (adbLine is not null)
        {
            var fields = adbLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var state = fields.Length > 1 ? fields[1] : "unknown";
            var transportState = state switch
            {
                "device" => BootTransportState.AdbDevice,
                "unauthorized" => BootTransportState.AdbUnauthorized,
                "offline" => BootTransportState.AdbOffline,
                _ => BootTransportState.Unknown
            };
            if (transportState != BootTransportState.Unknown)
                return new(transportState, fields[0], null, null, null, new Dictionary<string, string>(), adb.StandardOutput, DateTimeOffset.UtcNow);
        }

        if (_tools.FastbootPath is { } fastboot)
        {
            var devices = await RunFastbootAsync(fastboot, [], cancellationToken);
            var serial = devices.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (serial is not null)
            {
                var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var variable in new[] { "product", "current-slot", "unlocked", "secure", "slot-count" })
                {
                    var output = await RunFastbootAsync(fastboot, ["-s", serial, "getvar", variable], cancellationToken);
                    ParseVariable(output.StandardError + Environment.NewLine + output.StandardOutput, variable, variables);
                }
                return new(
                    BootTransportState.Fastboot,
                    serial,
                    variables.GetValueOrDefault("product"),
                    variables.GetValueOrDefault("current-slot"),
                    variables.GetValueOrDefault("unlocked"),
                    variables,
                    devices.StandardOutput + Environment.NewLine + string.Join(Environment.NewLine, variables.Select(item => $"{item.Key}: {item.Value}")),
                    DateTimeOffset.UtcNow);
            }
        }
        return new(BootTransportState.NoDevice, null, null, null, null, new Dictionary<string, string>(), adb.StandardOutput, DateTimeOffset.UtcNow);
    }

    public Task<AdbCommandResult> RebootAsync(
        string serial,
        string mode = "",
        CancellationToken cancellationToken = default)
        => _deviceTools.RebootAsync(serial.Trim(), mode, cancellationToken);

    private static async Task<AdbCommandResult> RunFastbootAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start())
            throw new InvalidOperationException("Fastboot could not be started.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new("fastboot.exe", arguments, process.ExitCode, await stdout, await stderr, TimeSpan.Zero);
    }

    private static void ParseVariable(
        string output,
        string variable,
        IDictionary<string, string> values)
    {
        var marker = $"{variable}:";
        var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(item => item.Contains(marker, StringComparison.OrdinalIgnoreCase));
        if (line is not null)
            values[variable] = line[(line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) + marker.Length)..].Trim();
    }
}
