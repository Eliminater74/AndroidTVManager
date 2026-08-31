using System.Net;
using System.Text.RegularExpressions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Core.Adb;

public static partial class AdbParsers
{
    public static IReadOnlyList<AndroidDevice> ParseTrackedDevices(string text)
    {
        var devices = new List<AndroidDevice>();

        foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase))
                continue;

            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 2)
                continue;

            var serial = columns[0];
            var stateText = columns.Length > 2 && columns[1].Equals("no", StringComparison.OrdinalIgnoreCase)
                ? $"{columns[1]} {columns[2]}"
                : columns[1];
            var attributeStart = stateText.Equals("no permissions", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
            var state = ParseState(stateText);
            var attributes = columns.Skip(attributeStart)
                .Select(ParseAttribute)
                .Where(pair => pair.HasValue)
                .Select(pair => pair!.Value)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            devices.Add(new AndroidDevice
            {
                Serial = serial,
                Endpoint = ParseEndpoint(serial),
                State = state,
                ConnectionType = ParseConnectionType(serial),
                Model = attributes.GetValueOrDefault("model")?.Replace('_', ' '),
                Product = attributes.GetValueOrDefault("product"),
                SeenAtUtc = DateTimeOffset.UtcNow
            });
        }

        return devices;
    }

    public static DeviceState ParseState(string value) => value.Trim().ToLowerInvariant() switch
    {
        "device" => DeviceState.Device,
        "offline" => DeviceState.Offline,
        "unauthorized" => DeviceState.Unauthorized,
        "no permissions" => DeviceState.NoPermissions,
        _ => DeviceState.Unknown
    };

    public static ConnectionType ParseConnectionType(string serial)
        => serial.Contains(':') ? ConnectionType.Network : ConnectionType.Usb;

    public static string? ParseEndpoint(string serial)
    {
        if (!serial.Contains(':'))
            return null;

        if (serial.StartsWith("[", StringComparison.Ordinal))
        {
            var end = serial.IndexOf(']');
            return end > 0 && end + 1 < serial.Length && serial[end + 1] == ':'
                ? serial
                : null;
        }

        return serial;
    }

    public static bool TryParseEndpoint(string host, string portText, out string endpoint, out string error)
    {
        endpoint = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(host))
        {
            error = "Enter an IP address or hostname.";
            return false;
        }

        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
        {
            error = "Port must be a number from 1 to 65535.";
            return false;
        }

        var normalizedHost = host.Trim();
        if (IPAddress.TryParse(normalizedHost, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            endpoint = $"[{normalizedHost}]:{port}";
        else
            endpoint = $"{normalizedHost}:{port}";

        return true;
    }

    public static string? ParseAdbVersion(string output)
    {
        var match = VersionRegex().Match(output);
        return match.Success ? match.Groups["version"].Value : null;
    }

    private static KeyValuePair<string, string>? ParseAttribute(string column)
    {
        var separator = column.IndexOf(':');
        return separator <= 0 ? null : new(column[..separator], column[(separator + 1)..]);
    }

    [GeneratedRegex(@"Android Debug Bridge version (?<version>[0-9]+(?:\.[0-9]+){1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();
}
