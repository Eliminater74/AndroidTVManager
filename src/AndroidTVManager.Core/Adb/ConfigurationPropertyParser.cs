using System.Text;
using System.Text.RegularExpressions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Core.Adb;

public static partial class ConfigurationPropertyParser
{
    public const string UnavailableMarker = "__ANDROID_TV_MANAGER_FILE_UNAVAILABLE__";

    public static IReadOnlyDictionary<string, string> ParseRuntime(string output)
        => ParseLines(output, line =>
        {
            var match = RuntimePropertyRegex().Match(line);
            return match.Success
                ? (match.Groups["name"].Value, match.Groups["value"].Value)
                : null;
        });

    public static IReadOnlyDictionary<string, string> ParseFile(string output)
        => ParseLines(output, line =>
        {
            if (line.StartsWith(UnavailableMarker, StringComparison.Ordinal))
                return null;
            if (line.StartsWith('#') || line.StartsWith(';'))
                return null;

            var separator = line.IndexOf('=');
            return separator > 0
                ? (line[..separator].Trim(), line[(separator + 1)..].Trim())
                : null;
        });

    public static bool IsUnavailableFile(string output)
        => output.Contains(UnavailableMarker, StringComparison.Ordinal);

    public static string Redact(string name, string value, out bool redacted)
    {
        redacted = IsSensitive(name);
        return redacted ? "[redacted]" : value;
    }

    public static ConfigurationProperty CreateProperty(
        string name,
        IReadOnlyDictionary<string, string> runtime,
        IReadOnlyDictionary<ConfigurationSource, IReadOnlyDictionary<string, string>> files,
        IReadOnlyDictionary<ConfigurationSource, bool>? fileAvailability = null,
        IReadOnlyDictionary<ConfigurationSource, string?>? fileErrors = null,
        string? forcedCategory = null)
    {
        runtime.TryGetValue(name, out var runtimeValue);
        var staticValues = files
            .Select(pair =>
            {
                var found = pair.Value.TryGetValue(name, out var value);
                var available = fileAvailability?.GetValueOrDefault(pair.Key) ?? true;
                return new ConfigurationValueSource(
                    pair.Key,
                    value,
                    available,
                    available ? null : fileErrors?.GetValueOrDefault(pair.Key));
            })
            .ToArray();
        var redacted = false;
        var displayRuntime = runtimeValue is null ? null : Redact(name, runtimeValue, out redacted);
        var displayStatic = staticValues
            .Select(value => value with
            {
                Value = value.Value is null ? null : Redact(name, value.Value, out _)
            })
            .ToArray();
        redacted |= staticValues.Any(value => value.Value is not null && IsSensitive(name));

        var availableValues = displayStatic
            .Where(value => value.IsAvailable && value.Value is not null)
            .Select(value => value.Value!)
            .Append(displayRuntime)
            .Where(value => value is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var hasStaticValue = displayStatic.Any(value => value.IsAvailable && value.Value is not null);
        var status = displayRuntime is not null && hasStaticValue && availableValues.Length == 1
            ? ConfigurationValueStatus.Match
            : displayRuntime is not null && hasStaticValue
                ? ConfigurationValueStatus.Conflict
                : displayRuntime is not null
                    ? ConfigurationValueStatus.RuntimeOnly
                    : hasStaticValue
                        ? ConfigurationValueStatus.FileOnly
                        : ConfigurationValueStatus.Unavailable;

        return new(
            name,
            forcedCategory ?? CategoryFor(name),
            Humanize(name),
            displayRuntime,
            displayStatic,
            status,
            redacted);
    }

    public static string CategoryFor(string name)
    {
        var normalized = name.ToLowerInvariant();
        if (normalized.StartsWith("ro.build", StringComparison.Ordinal)
            || normalized.Contains("security_patch", StringComparison.Ordinal))
            return "Build";
        if (normalized.Contains("display", StringComparison.Ordinal)
            || normalized.Contains("screen", StringComparison.Ordinal)
            || normalized.Contains("hdr", StringComparison.Ordinal)
            || normalized.Contains("dolby", StringComparison.Ordinal))
            return "Display";
        if (normalized.Contains("audio", StringComparison.Ordinal)
            || normalized.Contains("media", StringComparison.Ordinal)
            || normalized.Contains("codec", StringComparison.Ordinal))
            return "Audio";
        if (normalized.StartsWith("net.", StringComparison.Ordinal)
            || normalized.Contains("wifi", StringComparison.Ordinal)
            || normalized.Contains("ethernet", StringComparison.Ordinal)
            || normalized.Contains("dns", StringComparison.Ordinal))
            return "Network";
        if (normalized.Contains("security", StringComparison.Ordinal)
            || normalized.Contains("verifiedboot", StringComparison.Ordinal)
            || normalized.Contains("unlock", StringComparison.Ordinal)
            || normalized.Contains("selinux", StringComparison.Ordinal)
            || normalized.Contains("adb", StringComparison.Ordinal))
            return "Security";
        if (normalized.Contains("treble", StringComparison.Ordinal)
            || normalized.Contains("partition", StringComparison.Ordinal)
            || normalized.Contains("dynamic", StringComparison.Ordinal)
            || normalized.Contains("slot", StringComparison.Ordinal)
            || normalized.StartsWith("ro.boot", StringComparison.Ordinal))
            return "Treble / Partitions";
        if (normalized.Contains("hdmi", StringComparison.Ordinal)
            || normalized.Contains("cec", StringComparison.Ordinal)
            || normalized.Contains("leanback", StringComparison.Ordinal)
            || normalized.Contains("tv", StringComparison.Ordinal))
            return "TV / HDMI / CEC";
        if (normalized.Contains("google", StringComparison.Ordinal)
            || normalized.Contains("gms", StringComparison.Ordinal)
            || normalized.Contains("gsf", StringComparison.Ordinal))
            return "Google / Vendor";
        if (normalized.Contains("cpu", StringComparison.Ordinal)
            || normalized.Contains("hardware", StringComparison.Ordinal)
            || normalized.Contains("board", StringComparison.Ordinal)
            || normalized.Contains("product", StringComparison.Ordinal))
            return "Hardware";
        return "System";
    }

    public static string Humanize(string name)
    {
        var value = name.Replace('_', ' ').Replace('.', ' ');
        if (value.StartsWith("ro ", StringComparison.OrdinalIgnoreCase))
            value = value[3..];
        return value.Length == 0
            ? name
            : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static IReadOnlyDictionary<string, string> ParseLines(
        string output,
        Func<string, (string Name, string Value)?> parser)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(output ?? string.Empty);
        while (reader.ReadLine() is { } line)
        {
            var parsed = parser(line.Trim());
            if (parsed is { } item && item.Name.Length > 0)
                values[item.Name] = item.Value;
        }
        return values;
    }

    private static bool IsSensitive(string name)
    {
        var normalized = name.ToLowerInvariant();
        return normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("passwd", StringComparison.Ordinal)
            || normalized.Contains("credential", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("private_key", StringComparison.Ordinal)
            || normalized.EndsWith(".psk", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^\[(?<name>[^\]]+)\]:\s*\[(?<value>.*)\]$", RegexOptions.CultureInvariant)]
    private static partial Regex RuntimePropertyRegex();
}
