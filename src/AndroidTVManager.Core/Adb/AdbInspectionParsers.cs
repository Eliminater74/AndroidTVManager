using System.Globalization;
using System.Text.RegularExpressions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Core.Adb;

public static class AdbInspectionParsers
{
    public static IReadOnlyDictionary<string, string> ParseProperties(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var start = line.IndexOf('[');
            var separator = line.IndexOf("]: [", StringComparison.Ordinal);
            if (start >= 0 && separator > start)
                values[line[(start + 1)..separator]] = line[(separator + 4)..].TrimEnd(']');
        }
        return values;
    }

    public static CpuInfo ParseCpu(string cpuInfo, string? abi, string? abiList, string? hardware, string? board)
    {
        var values = ParseKeyValues(cpuInfo);
        var coreCount = cpuInfo.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.TrimStart().StartsWith("processor", StringComparison.OrdinalIgnoreCase));
        var supported = (abiList ?? abi ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var architecture = supported.FirstOrDefault(value => value.Contains("64", StringComparison.OrdinalIgnoreCase))
            ?? supported.FirstOrDefault();
        return new(
            architecture,
            abi,
            supported,
            coreCount > 0 ? coreCount : null,
            Get(values, "CPU implementer"),
            Get(values, "CPU part"),
            hardware,
            board,
            hardware,
            null,
            null,
            null);
    }

    public static MemoryInfo ParseMemory(string output)
    {
        var values = ParseMemoryValues(output);
        return new(
            Bytes(values, "MemTotal"),
            Bytes(values, "MemAvailable"),
            Bytes(values, "MemFree"),
            Bytes(values, "Cached"),
            Bytes(values, "SwapTotal"),
            values.TryGetValue("SwapTotal", out var swapTotal) && values.TryGetValue("SwapFree", out var swapFree)
                ? Math.Max(0, ParseBytes(swapTotal) - ParseBytes(swapFree))
                : Bytes(values, "SwapFree"),
            Bytes(values, "Zram"));
    }

    public static DisplayInfo ParseDisplay(string wmSize, string wmDensity, string displayDump)
    {
        var physical = MatchValue(wmSize, @"Physical size:\s*(?<value>[^\r\n]+)");
        var current = MatchValue(wmSize, @"Override size:\s*(?<value>[^\r\n]+)") ?? physical;
        var density = int.TryParse(MatchValue(wmDensity, @"(?:Override density|Physical density):\s*(?<value>\d+)")
            ?? string.Empty, out var parsedDensity) ? (int?)parsedDensity : null;
        var refreshRates = Regex.Matches(displayDump, @"(?<rate>\d+(?:\.\d+)?)\s*Hz", RegexOptions.IgnoreCase)
            .Select(match => $"{match.Groups["rate"].Value} Hz").Distinct().ToArray();
        var hdr = Regex.Matches(displayDump, @"HDR10\+?|Dolby Vision|HLG", RegexOptions.IgnoreCase)
            .Select(match => match.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new(current, physical, density, refreshRates.FirstOrDefault(), refreshRates, hdr,
            MatchValue(displayDump, @"colorMode[=:]\s*(?<value>[^\r\n,]+)"),
            MatchValue(displayDump, @"orientation[=:]\s*(?<value>\d+)"));
    }

    public static StorageInfo ParseStorage(string output)
    {
        var volumes = new List<StorageVolume>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var parts = Regex.Split(line.Trim(), @"\s+");
            if (parts.Length < 5 || !long.TryParse(parts[1], out var total))
                continue;
            volumes.Add(new(parts[^1], total * 1024, ParseBytes(parts[2], 1024),
                ParseBytes(parts[3], 1024), parts[0]));
        }
        return new(volumes);
    }

    public static IReadOnlyList<string> ParseFeatures(string output)
        => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("feature:", StringComparison.OrdinalIgnoreCase))
            .Select(line => line["feature:".Length..].Trim())
            .Where(line => line.Length > 0)
            .ToArray();

    public static SecurityInfo ParseSecurity(IReadOnlyDictionary<string, string> properties, string selinux, string rootCheck)
    {
        var root = rootCheck.Contains("uid=0", StringComparison.OrdinalIgnoreCase)
            ? CapabilityState.Supported
            : rootCheck.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? CapabilityState.Unsupported
                : CapabilityState.Unknown;
        var adbRoot = properties.TryGetValue("ro.debuggable", out var debug) && debug == "1"
            ? CapabilityState.Partial
            : CapabilityState.Unsupported;
        return new(
            string.IsNullOrWhiteSpace(selinux) ? null : selinux.Trim(),
            Get(properties, "ro.boot.verifiedbootstate"),
            Get(properties, "ro.boot.vbmeta.device_state"),
            Get(properties, "ro.boot.flash.locked"),
            Get(properties, "ro.build.type"),
            Get(properties, "ro.build.tags"),
            root,
            adbRoot,
            [
                Evidence("getenforce", selinux, "SELinux enforcement state."),
                Evidence("getprop ro.boot.verifiedbootstate", Get(properties, "ro.boot.verifiedbootstate"), "Verified Boot state."),
                Evidence("which su", rootCheck, "Root binary availability.")
            ]);
    }

    public static BootInfo ParseBoot(IReadOnlyDictionary<string, string> properties)
    {
        var ab = BoolProperty(properties, "ro.build.ab_update");
        var virtualAb = BoolProperty(properties, "ro.virtual_ab.enabled");
        var dynamic = !string.IsNullOrWhiteSpace(Get(properties, "ro.boot.super_partition"))
            || BoolProperty(properties, "ro.boot.dynamic_partitions") is true;
        return new(ab, virtualAb, dynamic, Get(properties, "ro.boot.slot_suffix"),
            Get(properties, "ro.boot.super_partition"), Get(properties, "ro.build.system_root_image"),
            [
                Evidence("ro.build.ab_update", Get(properties, "ro.build.ab_update"), "A/B update property."),
                Evidence("ro.virtual_ab.enabled", Get(properties, "ro.virtual_ab.enabled"), "Virtual A/B property."),
                Evidence("ro.boot.super_partition", Get(properties, "ro.boot.super_partition"), "Dynamic partition evidence.")
            ]);
    }

    public static GsiInfo ParseGsi(
        IReadOnlyDictionary<string, string> properties,
        string gsiToolOutput,
        bool dsuServicePresent)
    {
        var treble = BoolCapability(properties, "ro.treble.enabled");
        var dynamic = !string.IsNullOrWhiteSpace(Get(properties, "ro.boot.super_partition"))
            ? CapabilityState.Supported
            : CapabilityState.Unknown;
        var virtualAb = BoolCapability(properties, "ro.virtual_ab.enabled");
        var gsiTool = gsiToolOutput.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? CapabilityState.Unsupported
            : string.IsNullOrWhiteSpace(gsiToolOutput) ? CapabilityState.Unknown : CapabilityState.Supported;
        var dsu = dsuServicePresent ? CapabilityState.Supported : CapabilityState.Unknown;
        var assessment = treble == CapabilityState.Supported && dynamic == CapabilityState.Supported
            && (virtualAb == CapabilityState.Supported || gsiTool == CapabilityState.Supported || dsu == CapabilityState.Supported)
                ? GsiAssessment.LikelySupported
                : treble == CapabilityState.Supported
                    ? GsiAssessment.PossiblySupported
                    : treble == CapabilityState.Unsupported ? GsiAssessment.NotDetected : GsiAssessment.Unknown;
        return new(treble, dynamic, virtualAb, gsiTool, dsu, assessment,
            [
                Evidence("ro.treble.enabled", Get(properties, "ro.treble.enabled"), "Project Treble property."),
                Evidence("gsi_tool", gsiToolOutput, "Optional GSI tool result."),
                Evidence("DSU service/package", dsuServicePresent ? "present" : "not detected", "Dynamic System evidence.")
            ]);
    }

    public static NetworkInfo ParseNetwork(string output, string hostname)
    {
        var addresses = Regex.Matches(output, @"inet6?\s+(?<address>[0-9a-fA-F:.]+)")
            .Select(match => match.Groups["address"].Value).Distinct().ToArray();
        var interfaces = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !char.IsWhiteSpace(line.FirstOrDefault()) && line.Contains(':'))
            .Select(line => line[..line.IndexOf(':')]).Distinct().ToArray();
        return new(addresses, interfaces, hostname.Trim(), null, [], null);
    }

    public static DeveloperVerificationInfo ParseDeveloperVerification(
        string packageList,
        string? packageDetails,
        IReadOnlyDictionary<string, string> properties)
    {
        var present = packageList.Contains("com.google.android.verifier", StringComparison.OrdinalIgnoreCase);
        var version = MatchValue(packageDetails ?? string.Empty, @"versionName[=:]\s*(?<value>[^\r\n ]+)");
        var evidence = new List<CapabilityEvidence>
        {
            Evidence("pm list packages com.google.android.verifier",
                present ? "com.google.android.verifier" : "not detected",
                "Developer Verifier package presence.")
        };
        var legacyInstallProperty = Get(properties, "ro.install.unknown_sources");
        evidence.Add(Evidence("ro.install.unknown_sources", legacyInstallProperty,
            "Legacy unknown-source state does not prove the current Advanced Flow state.",
            EvidenceConfidence.Low));
        return new(present, version, CapabilityState.Supported, AdvancedFlowAvailability.Unknown, AdvancedFlowState.Unknown,
            WaitingPeriodState.NotApplicableToAdb,
            false, evidence, DateTimeOffset.UtcNow);
    }

    private static IReadOnlyDictionary<string, string> ParseKeyValues(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parts in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                     .Select(line => line.Split(':', 2))
                     .Where(parts => parts.Length == 2))
            values[parts[0].Trim()] = parts[1].Trim();
        return values;
    }

    private static Dictionary<string, string> ParseMemoryValues(string output)
        => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Regex.Match(line, @"^(?<key>\w+):\s*(?<value>[\d.]+)\s*(?<unit>\w+)?"))
            .Where(match => match.Success)
            .ToDictionary(match => match.Groups["key"].Value,
                match => $"{match.Groups["value"].Value} {match.Groups["unit"].Value}",
                StringComparer.OrdinalIgnoreCase);

    private static long? Bytes(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? ParseBytes(value) : null;

    private static long ParseBytes(string value, long multiplier = 1)
    {
        var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? (long)(number * multiplier * UnitMultiplier(parts.ElementAtOrDefault(1)))
            : 0;
    }

    private static long UnitMultiplier(string? unit)
        => unit?.ToUpperInvariant() switch { "KB" => 1024, "MB" => 1024 * 1024, "GB" => 1024L * 1024 * 1024, _ => 1 };

    private static string? MatchValue(string value, string pattern)
        => Regex.Match(value, pattern, RegexOptions.IgnoreCase).Groups["value"].Value is { Length: > 0 } result
            ? result.Trim() : null;

    private static string? Get(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static bool? BoolProperty(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
            ? true : values.ContainsKey(key) ? false : null;

    private static CapabilityState BoolCapability(IReadOnlyDictionary<string, string> values, string key)
        => BoolProperty(values, key) is true ? CapabilityState.Supported
            : BoolProperty(values, key) is false ? CapabilityState.Unsupported : CapabilityState.Unknown;

    private static CapabilityEvidence Evidence(
        string source, string? value, string explanation,
        EvidenceConfidence confidence = EvidenceConfidence.High)
        => new(source, value, explanation, confidence);
}
