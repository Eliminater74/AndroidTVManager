using System.Text.RegularExpressions;

namespace AndroidTVManager.Core.Adb;

public sealed record AdbDeviceMetadata(
    string? Manufacturer,
    string? Brand,
    string? Model,
    string? Product,
    string? DeviceName,
    string? Board,
    string? AndroidVersion,
    int? ApiLevel,
    string? SecurityPatch,
    string? BuildId,
    string? BuildType,
    string? BuildFingerprint)
{
    public static AdbDeviceMetadata Empty { get; } = new(null, null, null, null, null, null, null, null, null, null, null, null);
}

public static class AdbMetadataParser
{
    public static AdbDeviceMetadata Parse(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var start = line.IndexOf('[');
            var separator = line.IndexOf("]: [", StringComparison.Ordinal);
            if (start < 0 || separator <= start)
                continue;
            var key = line[(start + 1)..separator];
            var valueStart = separator + 4;
            var value = line[valueStart..].TrimEnd(']');
            values[key] = value;
        }

        return new(
            Get(values, "ro.product.manufacturer"),
            Get(values, "ro.product.brand"),
            Get(values, "ro.product.model"),
            Get(values, "ro.product.name"),
            Get(values, "ro.product.device"),
            Get(values, "ro.product.board"),
            Get(values, "ro.build.version.release"),
            ParseInt(Get(values, "ro.build.version.sdk")),
            Get(values, "ro.build.version.security_patch"),
            Get(values, "ro.build.id"),
            Get(values, "ro.build.type"),
            Get(values, "ro.build.fingerprint"));
    }

    public static string? ParseReportedName(string output)
    {
        var value = output.Trim();
        return value.Length == 0 || value.Equals("null", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;
    }

    public static string? ParseMacAddress(string output)
    {
        var interfaces = Regex.Matches(output,
                @"(?ms)^\d+:\s*(?<interface>[^\s:]+).*?(?=^\d+:\s|\z)")
            .Select(match =>
            {
                var name = match.Groups["interface"].Value;
                var flags = match.Value.Contains("<", StringComparison.Ordinal)
                    && match.Value.Contains("UP", StringComparison.Ordinal);
                var mac = Regex.Match(match.Value,
                    @"(?<![0-9A-Fa-f])(?<mac>[0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2}){5})(?![0-9A-Fa-f])",
                    RegexOptions.CultureInvariant);
                return (Name: name, IsUp: flags, Mac: mac.Success ? mac.Groups["mac"].Value.ToUpperInvariant() : null);
            })
            .Where(item => item.Mac is not null && item.Mac.Replace(":", string.Empty)
                .Any(character => character != '0'))
            .OrderByDescending(item => IsPhysicalInterface(item.Name) && item.IsUp)
            .ThenByDescending(item => IsPhysicalInterface(item.Name))
            .Select(item => item.Mac)
            .FirstOrDefault();
        return interfaces;
    }

    private static bool IsPhysicalInterface(string name)
        => name.StartsWith("eth", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("wlan", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("wifi", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("usb", StringComparison.OrdinalIgnoreCase);

    private static string? Get(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    private static int? ParseInt(string? value)
        => int.TryParse(value, out var result) ? result : null;
}
