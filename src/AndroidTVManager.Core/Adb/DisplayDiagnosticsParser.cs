using System.Text.RegularExpressions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Core.Adb;

public static class DisplayDiagnosticsParser
{
    public static string? ParseHdcp(string displayOutput, string hdmiOutput, IReadOnlyDictionary<string, string> properties)
        => MatchValue($"{displayOutput}\n{hdmiOutput}",
               @"(?im)\bhdcp[^\r\n]*?(?:state|status|level)?\s*[:=]\s*(?<value>[A-Za-z0-9._-]+)")
            ?? properties.FirstOrDefault(pair =>
                pair.Key.Contains("hdcp", StringComparison.OrdinalIgnoreCase)).Value;

    public static string? ParseCecAddress(string output, bool physical)
    {
        var name = physical ? "physical" : "logical";
        return MatchValue(output,
            $@"(?im)\b{name}\s+(?:address|addr)\s*[:=]\s*(?<value>[0-9A-Fa-fx-]+)")
            ?? MatchValue(output, $@"(?im)\b{name}\s*[:=]\s*(?<value>[0-9A-Fa-fx-]+)");
    }

    public static IReadOnlyList<string> ParseSurfaceFlingerModes(string output)
    {
        var matches = Regex.Matches(
                output,
                @"(?<resolution>\d{3,5}x\d{3,5})(?:\s*@\s*(?<rate>\d+(?:\.\d+)?)\s*Hz)?",
                RegexOptions.IgnoreCase)
            .Select(match =>
            {
                var resolution = match.Groups["resolution"].Value;
                var rate = match.Groups["rate"].Value;
                return string.IsNullOrWhiteSpace(rate) ? resolution : $"{resolution} @ {rate} Hz";
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return matches;
    }

    public static IReadOnlyList<string> ParseVendorProperties(string getpropOutput)
        => getpropOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Contains("]: [", StringComparison.Ordinal)
                && (line.Contains("display", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("hdmi", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("hdr", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("cec", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("surfaceflinger", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("drm", StringComparison.OrdinalIgnoreCase)))
            .Take(100)
            .ToArray();

    public static DisplayDiagnosticComparison Compare(
        DisplayDiagnosticSnapshot previous,
        DisplayDiagnosticSnapshot current)
    {
        var changes = new List<DisplayDiagnosticChange>();
        Add(changes, "Current resolution", previous.Display.CurrentResolution, current.Display.CurrentResolution);
        Add(changes, "Physical resolution", previous.Display.PhysicalResolution, current.Display.PhysicalResolution);
        Add(changes, "Refresh rate", previous.Display.RefreshRate, current.Display.RefreshRate);
        Add(changes, "HDR capabilities", Join(previous.Display.HdrCapabilities), Join(current.Display.HdrCapabilities));
        Add(changes, "Display modes", Join(previous.Display.SupportedModes), Join(current.Display.SupportedModes));
        Add(changes, "Color mode", previous.Display.ColorMode, current.Display.ColorMode);
        Add(changes, "CEC state", previous.Hdmi.CecState, current.Hdmi.CecState);
        Add(changes, "CEC physical address", previous.CecPhysicalAddress, current.CecPhysicalAddress);
        Add(changes, "CEC logical address", previous.CecLogicalAddress, current.CecLogicalAddress);
        Add(changes, "Active input", previous.Hdmi.ActiveInput, current.Hdmi.ActiveInput);
        Add(changes, "Audio route", previous.Hdmi.AudioRoute, current.Hdmi.AudioRoute);
        Add(changes, "HDCP state", previous.HdcpState, current.HdcpState);
        Add(changes, "SurfaceFlinger modes", Join(previous.SurfaceFlingerModes), Join(current.SurfaceFlingerModes));
        return new(previous.CapturedUtc, current.CapturedUtc, changes);
    }

    private static void Add(
        ICollection<DisplayDiagnosticChange> changes,
        string name,
        string? previous,
        string? current)
    {
        if (!string.Equals(previous, current, StringComparison.OrdinalIgnoreCase))
            changes.Add(new(name, previous, current));
    }

    private static string Join(IEnumerable<string> values)
        => string.Join(", ", values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

    private static string? MatchValue(string output, string pattern)
        => Regex.Match(output, pattern).Groups["value"] is { Success: true } group
            ? group.Value.Trim()
            : null;
}
