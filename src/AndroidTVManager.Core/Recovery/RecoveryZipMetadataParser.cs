using System.Text.RegularExpressions;

namespace AndroidTVManager.Core.Recovery;

public static class RecoveryZipMetadataParser
{
    private static readonly Regex MetadataDevice = new(
        @"^pre-device=(?<value>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex MetadataBuild = new(
        @"^pre-build(?:-incremental)?=(?<value>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex ScriptDevice = new(
        @"getprop\s*\(\s*""ro\.product\.device""\s*\)\s*==\s*""(?<value>[^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static RecoveryZipDeclaration Parse(string? metadata, string? updaterScript)
    {
        var devices = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        string? preBuild = null;
        var evidence = new List<string>();

        if (!string.IsNullOrWhiteSpace(metadata))
        {
            var deviceMatch = MetadataDevice.Match(metadata);
            if (deviceMatch.Success)
            {
                foreach (var device in deviceMatch.Groups["value"].Value.Split(
                             [',', '|'],
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    devices.Add(device);
                evidence.Add($"metadata pre-device={string.Join(',', devices)}");
            }
            var buildMatch = MetadataBuild.Match(metadata);
            if (buildMatch.Success)
            {
                preBuild = buildMatch.Groups["value"].Value.Trim();
                evidence.Add($"metadata pre-build={preBuild}");
            }
        }

        if (!string.IsNullOrWhiteSpace(updaterScript))
        {
            foreach (Match match in ScriptDevice.Matches(updaterScript))
            {
                var device = match.Groups["value"].Value.Trim();
                if (device.Length == 0)
                    continue;
                devices.Add(device);
                evidence.Add($"updater-script ro.product.device={device}");
            }
        }

        return new(
            devices.ToArray(),
            preBuild,
            evidence.Count == 0 ? "The ZIP contains no pre-device or updater-script device identity." : string.Join("; ", evidence));
    }

    public static RecoveryZipCompatibility Evaluate(RecoveryZipDeclaration declaration, string? liveDevice)
    {
        var declared = declaration.DeclaredDevices.Count == 0
            ? null
            : string.Join(", ", declaration.DeclaredDevices);
        if (declaration.DeclaredDevices.Count == 0)
            return new(
                RecoveryCompatibilityState.Unknown,
                declared,
                Normalize(liveDevice),
                ["The ZIP does not declare a target device; compatibility was not verified."]);

        var observed = Normalize(liveDevice);
        if (string.IsNullOrWhiteSpace(observed))
            return new(
                RecoveryCompatibilityState.UnknownRequiredEvidence,
                declared,
                null,
                ["The ZIP declares a target device, but the live product identity could not be read."]);

        if (declaration.DeclaredDevices.Any(device => device.Equals(observed, StringComparison.OrdinalIgnoreCase)))
            return new(
                RecoveryCompatibilityState.Compatible,
                declared,
                observed,
                [$"ZIP target {declared} matches live product {observed}."]);

        return new(
            RecoveryCompatibilityState.Incompatible,
            declared,
            observed,
            [$"ZIP target {declared} does not match live product {observed}."]);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
