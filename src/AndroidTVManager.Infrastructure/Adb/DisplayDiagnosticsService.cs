using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class DisplayDiagnosticsService : IDisplayDiagnosticsService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly IAdbProcessRunner _runner;

    public DisplayDiagnosticsService(IAdbProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<DisplayDiagnosticSnapshot> CaptureAsync(
        string serial,
        string? friendlyDeviceName = null,
        DisplayCaptureLabel label = DisplayCaptureLabel.Unlabeled,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("A device serial is required.", nameof(serial));
        serial = serial.Trim();

        var commands = new Dictionary<string, IReadOnlyList<string>>
        {
            ["getprop"] = ["shell", "getprop"],
            ["wm-size"] = ["shell", "wm", "size"],
            ["wm-density"] = ["shell", "wm", "density"],
            ["display"] = ["shell", "dumpsys", "display"],
            ["surfaceflinger"] = ["shell", "dumpsys", "SurfaceFlinger"],
            ["hdmi"] = ["shell", "dumpsys", "hdmi_control"],
            ["audio"] = ["shell", "dumpsys", "audio"]
        };
        var results = new Dictionary<string, AdbCommandResult>();
        foreach (var pair in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Reading {pair.Key}…");
            results[pair.Key] = await _runner.RunForDeviceAsync(
                serial,
                pair.Value,
                Timeout,
                cancellationToken);
        }

        var displayOutput = Output(results, "display");
        var hdmiOutput = Output(results, "hdmi");
        var audioOutput = Output(results, "audio");
        var properties = AdbInspectionParsers.ParseProperties(Output(results, "getprop"));
        var display = AdbInspectionParsers.ParseDisplay(
            Output(results, "wm-size"),
            Output(results, "wm-density"),
            displayOutput);
        var hdmi = AdbInspectionParsers.ParseHdmi(hdmiOutput, audioOutput);
        var evidence = results.Select(pair => new InspectionCommandEvidence(
            pair.Key,
            pair.Value.IsSuccess ? InspectionSectionState.Completed : InspectionSectionState.Partial,
            pair.Value.StandardOutput,
            pair.Value.StandardError,
            pair.Value.ExitCode,
            pair.Value.Duration,
            pair.Value.IsSuccess ? null : pair.Value.StandardError.Trim())).ToArray();

        progress?.Report("Display diagnostic capture complete.");
        return new DisplayDiagnosticSnapshot(
            serial,
            friendlyDeviceName,
            DateTimeOffset.UtcNow,
            label,
            display,
            hdmi,
            DisplayDiagnosticsParser.ParseHdcp(displayOutput, hdmiOutput, properties),
            DisplayDiagnosticsParser.ParseCecAddress(hdmiOutput, physical: true),
            DisplayDiagnosticsParser.ParseCecAddress(hdmiOutput, physical: false),
            DisplayDiagnosticsParser.ParseSurfaceFlingerModes(Output(results, "surfaceflinger")),
            DisplayDiagnosticsParser.ParseVendorProperties(Output(results, "getprop")),
            evidence);
    }

    private static string Output(
        IReadOnlyDictionary<string, AdbCommandResult> results,
        string key)
        => results.TryGetValue(key, out var result) ? result.StandardOutput : string.Empty;
}
