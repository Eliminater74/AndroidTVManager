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
            Get(values, "cpu MHz") ?? Get(values, "CPU MHz"),
            Get(values, "scaling governor") ?? Get(values, "governor"));
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

    public static OemUnlockInfo ParseOemUnlock(
        IReadOnlyDictionary<string, string> properties,
        string settingOutput)
    {
        var supported = BoolProperty(properties, "ro.oem_unlock_supported");
        var setting = BoolProperty(properties, "sys.oem_unlock_allowed")
            ?? ParseBoolean(settingOutput);
        var option = supported is true || setting is not null
            ? OemUnlockOptionState.Present
            : supported is false ? OemUnlockOptionState.Absent : OemUnlockOptionState.Unknown;
        var locked = setting is false
            && string.Equals(Get(properties, "ro.boot.flash.locked"), "1", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Get(properties, "ro.boot.vbmeta.device_state"), "locked", StringComparison.OrdinalIgnoreCase);
        var settingState = setting is true
            ? OemUnlockSettingState.Enabled
            : locked ? OemUnlockSettingState.LockedByDevice
            : setting is false ? OemUnlockSettingState.Disabled
            : OemUnlockSettingState.Unknown;
        var actualCapability = supported is false
            ? CapabilityState.Unsupported
            : CapabilityState.Unknown;
        return new(
            option,
            settingState,
            actualCapability,
            [
                Evidence("ro.oem_unlock_supported", Get(properties, "ro.oem_unlock_supported"),
                    "Android-reported OEM unlock option support."),
                Evidence("sys.oem_unlock_allowed / settings", setting is null ? null : setting.ToString(),
                    locked
                        ? "Unlock is reported not allowed while the bootloader is locked; the policy cause is not independently proven."
                        : "Current Android-reported OEM unlock setting."),
                Evidence("bootloader properties",
                    $"{Get(properties, "ro.boot.flash.locked") ?? "unknown"} / {Get(properties, "ro.boot.vbmeta.device_state") ?? "unknown"}",
                    "Bootloader state does not prove that an unlock operation is supported.",
                    EvidenceConfidence.Medium)
            ]);
    }

    public static RootInfo ParseRoot(
        IReadOnlyDictionary<string, string> properties,
        string rootCheck)
    {
        var shellRoot = rootCheck.Contains("uid=0", StringComparison.OrdinalIgnoreCase)
            ? CapabilityState.Supported
            : rootCheck.Contains("uid=", StringComparison.OrdinalIgnoreCase)
                ? CapabilityState.Unsupported
                : CapabilityState.Unknown;
        var su = rootCheck.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
            ? CapabilityState.PermissionDenied
            : rootCheck.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? CapabilityState.Unsupported
                : rootCheck.Contains("/su", StringComparison.OrdinalIgnoreCase)
                    ? CapabilityState.Partial
                    : CapabilityState.Unknown;
        var isDebuggable = string.Equals(Get(properties, "ro.debuggable"), "1", StringComparison.OrdinalIgnoreCase);
        var buildType = Get(properties, "ro.build.type");
        var adbRoot = isDebuggable
            ? CapabilityState.Partial
            : string.Equals(buildType, "user", StringComparison.OrdinalIgnoreCase)
                ? CapabilityState.Unsupported
                : CapabilityState.Unknown;
        var blockers = new List<string>();
        if (string.Equals(buildType, "user", StringComparison.OrdinalIgnoreCase))
            blockers.Add("Production user build normally rejects adb root.");
        if (string.Equals(Get(properties, "ro.boot.verifiedbootstate"), "green", StringComparison.OrdinalIgnoreCase))
            blockers.Add("Verified Boot reports a trusted state; this does not prove unlock support.");
        if (shellRoot != CapabilityState.Supported)
            blockers.Add("Current ADB shell is not root.");
        return new(
            shellRoot,
            su,
            adbRoot,
            blockers,
            "ADB cannot prove a safe root path. Verify the exact model, firmware, bootloader policy, and vendor documentation before considering any modification.",
            [
                Evidence("shell id", rootCheck, "Current ADB shell identity and visible su binary."),
                Evidence("ro.debuggable", Get(properties, "ro.debuggable"), "Debug build signal; it does not prove adb root will succeed."),
                Evidence("ro.build.type", buildType, "Build type used as a conservative root feasibility signal.")
            ]);
    }

    public static BluetoothInfo ParseBluetooth(
        string features,
        string enabledOutput,
        string bluetoothDump)
    {
        var supported = features.Contains("bluetooth", StringComparison.OrdinalIgnoreCase);
        bool? enabled = ParseBoolean(enabledOutput);
        var state = MatchValue(bluetoothDump, @"(?im)\bstate\s*[:=]\s*(?<value>[A-Z_]+)");
        var connected = bluetoothDump.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => (line.StartsWith("Device ", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Remote device", StringComparison.OrdinalIgnoreCase))
                && line.Contains("connected", StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .ToArray();
        return new(
            supported ? CapabilityState.Supported : CapabilityState.Unknown,
            enabled,
            state,
            connected,
            [
                Evidence("pm list features", supported ? "bluetooth feature detected" : "bluetooth feature not detected",
                    "Bluetooth hardware feature evidence."),
                Evidence("settings get global bluetooth_on", enabledOutput.Trim(),
                    "Current Bluetooth enabled setting."),
                Evidence("dumpsys bluetooth_manager", bluetoothDump, "Optional adapter and connection state.")
            ]);
    }

    public static HdmiInfo ParseHdmi(string hdmiDump, string audioDump)
    {
        var unavailable = string.IsNullOrWhiteSpace(hdmiDump)
            || hdmiDump.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || hdmiDump.Contains("unknown service", StringComparison.OrdinalIgnoreCase);
        var cec = MatchValue(hdmiDump, @"(?im)(?:cec|hdmi)[^\r\n]*(?:state|enabled)\s*[:=]\s*(?<value>[^\r\n, ]+)");
        var activeInput = MatchValue(hdmiDump, @"(?im)active\s+input\s*[:=]\s*(?<value>[^\r\n]+)");
        var displays = hdmiDump.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Contains("display", StringComparison.OrdinalIgnoreCase)
                || line.Contains("port", StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToArray();
        return new(
            unavailable ? CapabilityState.Unknown : CapabilityState.Partial,
            cec,
            activeInput,
            MatchValue(audioDump, @"(?im)(?:current|active|device)\s+(?:audio\s+)?(?:route|output)\s*[:=]\s*(?<value>[^\r\n]+)"),
            displays,
            [
                Evidence("dumpsys hdmi_control", hdmiDump, "Vendor/API-dependent HDMI and CEC evidence."),
                Evidence("dumpsys audio", audioDump, "Current audio route evidence.", EvidenceConfidence.Medium)
            ]);
    }

    public static DrmInfo ParseDrm(string output)
    {
        var unavailable = string.IsNullOrWhiteSpace(output)
            || output.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || output.Contains("unknown service", StringComparison.OrdinalIgnoreCase);
        var schemes = Regex.Matches(output, @"(?i)\b(?:Widevine|ClearKey|PlayReady)\b")
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var levels = Regex.Matches(output, @"(?i)(?:security level|securityLevel)\s*[:=]\s*(?<value>[A-Z0-9._-]+)")
            .Select(match => match.Groups["value"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new(
            unavailable ? CapabilityState.Unknown : CapabilityState.Partial,
            schemes.Length == 0 ? null : string.Join(", ", schemes),
            levels.Length == 0 ? null : string.Join(", ", levels),
            MatchValue(output, @"(?im)HDCP[^\r\n]*[:=]\s*(?<value>[^\r\n]+)"),
            [Evidence("dumpsys media.drm", output, "DRM service output is device/API dependent; identifiers are not extracted.")]);
    }

    public static IReadOnlyList<ServiceInfo> ParseServices(string output)
        => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Contains("ServiceRecord{", StringComparison.OrdinalIgnoreCase))
            .Select(line =>
            {
                var name = line[(line.IndexOf("ServiceRecord{", StringComparison.OrdinalIgnoreCase)
                    + "ServiceRecord{".Length)..].Split([' ', '}'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                    ?? "Unknown service";
                var package = Regex.Match(line, @"(?<package>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)/")
                    .Groups["package"].Value;
                return new ServiceInfo(name, package.Length == 0 ? null : package, null);
            })
            .DistinctBy(service => $"{service.Name}|{service.PackageName}", StringComparer.OrdinalIgnoreCase)
            .Take(250)
            .ToArray();

    private static bool? ParseBoolean(string output)
    {
        var value = output.Trim();
        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("enabled", StringComparison.OrdinalIgnoreCase)
            ? true
            : value.Equals("0", StringComparison.OrdinalIgnoreCase)
                || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                || value.Equals("disabled", StringComparison.OrdinalIgnoreCase)
                ? false
                : null;
    }

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
                Evidence("which su", rootCheck, "Root binary availability."),
                Evidence("getprop OEM unlock", Get(properties, "sys.oem_unlock_allowed")
                    ?? Get(properties, "ro.oem_unlock_supported"), "OEM unlock evidence.")
            ],
            Get(properties, "sys.oem_unlock_allowed") ?? Get(properties, "ro.oem_unlock_supported"));
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

    public static NetworkInfo ParseNetwork(
        string output,
        string hostname,
        string? routeOutput = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        var addresses = Regex.Matches(output, @"inet6?\s+(?<address>[0-9a-fA-F:.]+)")
            .Select(match => match.Groups["address"].Value).Distinct().ToArray();
        var interfaces = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Regex.Match(line, @"^\d+:\s*(?<name>[^:]+):").Groups["name"].Value)
            .Where(name => name.Length > 0).Distinct().ToArray();
        var macAddresses = Regex.Matches(output,
                @"(?<![0-9A-Fa-f])(?<mac>[0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2}){5})(?![0-9A-Fa-f])")
            .Select(match => match.Groups["mac"].Value.ToUpperInvariant())
            .Distinct().ToArray();
        var gateway = MatchValue(routeOutput ?? string.Empty, @"(?im)^default\s+via\s+(?<value>[0-9a-fA-F:.]+)");
        IReadOnlyList<string> dns = properties is null
            ? []
            : Enumerable.Range(1, 4)
                .Select(index => Get(properties, $"net.dns{index}"))
                .Where(value => value is not null)
                .Select(value => value!)
                .Distinct()
                .ToArray();
        var links = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("state ", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Trim())
            .Take(30)
            .ToArray();
        return new(addresses, interfaces, hostname.Trim(), gateway, dns, string.Join("; ", links), macAddresses);
    }

    public static DeveloperVerificationInfo ParseDeveloperVerification(
        string packageList,
        string? packageDetails,
        IReadOnlyDictionary<string, string> properties,
        bool packageQuerySucceeded = true)
    {
        bool? present = packageQuerySucceeded
            ? packageList.Contains("com.google.android.verifier", StringComparison.OrdinalIgnoreCase)
            : null;
        var version = MatchValue(packageDetails ?? string.Empty, @"versionName[=:]\s*(?<value>[^\r\n ]+)");
        var evidence = new List<CapabilityEvidence>
        {
            Evidence("pm list packages com.google.android.verifier",
                present is true ? "com.google.android.verifier" : present is false ? "not detected" : null,
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
        var numberText = parts[0];
        var suffix = parts.Length > 1
            ? parts[1]
            : numberText.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.');
        if (parts.Length == 1)
            numberText = numberText[..(numberText.Length - suffix.Length)];
        return double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? (long)(number * multiplier * UnitMultiplier(suffix))
            : 0;
    }

    private static long UnitMultiplier(string? unit)
        => unit?.ToUpperInvariant() switch
        {
            "K" or "KB" or "KIB" => 1024,
            "M" or "MB" or "MIB" => 1024 * 1024,
            "G" or "GB" or "GIB" => 1024L * 1024 * 1024,
            _ => 1
        };

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
