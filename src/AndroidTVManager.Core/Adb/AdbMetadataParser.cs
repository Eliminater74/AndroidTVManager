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

    private static string? Get(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    private static int? ParseInt(string? value)
        => int.TryParse(value, out var result) ? result : null;
}
