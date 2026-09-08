namespace AndroidTVManager.Core.Models;

public sealed record InspectionProbe(string Key, string Category, IReadOnlyList<string> Arguments);

public static class DeepInspectionCatalog
{
    // Fixed read-only commands. Never execute service names or property values returned by a device.
    public static IReadOnlyList<InspectionProbe> Probes { get; } = Array.AsReadOnly<InspectionProbe>([
        new("kernel", "Kernel / hardware", ["shell", "uname", "-a"]),
        new("kernel-version", "Kernel / hardware", ["shell", "cat", "/proc/version"]),
        new("cpu-online", "Kernel / hardware", ["shell", "cat", "/sys/devices/system/cpu/online"]),
        new("cpu-frequency", "Kernel / hardware", ["shell", "sh", "-c", "for p in /sys/devices/system/cpu/cpufreq/policy*; do echo \"$p\"; for f in scaling_cur_freq cpuinfo_max_freq cpuinfo_min_freq scaling_governor; do echo \"$f\"; cat \"$p/$f\"; done; done"]),
        new("partitions", "Storage", ["shell", "cat", "/proc/partitions"]),
        new("mounts", "Storage", ["shell", "cat", "/proc/mounts"]),
        new("filesystems", "Storage", ["shell", "cat", "/proc/filesystems"]),
        new("swap", "Memory", ["shell", "cat", "/proc/swaps"]),
        new("memory-processes", "Memory", ["shell", "dumpsys", "meminfo"]),
        new("volumes", "Storage", ["shell", "sm", "list-volumes", "all"]),
        new("disks", "Storage", ["shell", "sm", "list-disks"]),
        new("input-devices", "Input / peripherals", ["shell", "cat", "/proc/bus/input/devices"]),
        new("input", "Input / peripherals", ["shell", "dumpsys", "input"]),
        new("usb", "Input / peripherals", ["shell", "dumpsys", "usb"]),
        new("sensors", "Input / peripherals", ["shell", "dumpsys", "sensorservice"]),
        new("camera", "Input / peripherals", ["shell", "dumpsys", "media.camera"]),
        new("audio-flinger", "Media", ["shell", "dumpsys", "media.audio_flinger"]),
        new("audio-policy", "Media", ["shell", "dumpsys", "media.audio_policy"]),
        new("media-codecs", "Media", ["shell", "dumpsys", "media.codec"]),
        new("tv-input", "Media", ["shell", "dumpsys", "tv_input"]),
        new("power", "Power", ["shell", "dumpsys", "power"]),
        new("idle", "Power", ["shell", "dumpsys", "deviceidle"]),
        new("connectivity", "Network", ["shell", "dumpsys", "connectivity"]),
        new("ethernet", "Network", ["shell", "dumpsys", "ethernet"]),
        new("wifi-status", "Network", ["shell", "cmd", "wifi", "status"]),
        new("users", "Users / packages", ["shell", "pm", "list", "users"]),
        new("foreground-user", "Users / packages", ["shell", "am", "get-current-user"]),
        new("package-versions", "Users / packages", ["shell", "pm", "list", "packages", "-f", "-U", "--show-versioncode"]),
        new("shared-libraries", "Users / packages", ["shell", "pm", "list", "libraries"]),
        new("service-directory", "Services / vendor", ["shell", "dumpsys", "-l"]),
        new("binder-services", "Services / vendor", ["shell", "service", "list"]),
        new("hardware-services", "Services / vendor", ["shell", "lshal"])
    ]);

    public static string Describe(DeviceInspectionResult result)
    {
        var completed = result.Commands.Count(c => c.State == InspectionSectionState.Completed);
        return $"{completed}/{result.Commands.Count} commands returned usable output; {result.Commands.Count - completed} need review. " +
               "Results describe exposed evidence, not a complete physical inventory. Private app data, DRM keys and unexposed MCU/CAN-bus firmware are not available through this scan.";
    }

    public static InspectionSectionState Classify(AdbCommandResult result)
    {
        if (result.WasCanceled) return InspectionSectionState.Canceled;
        if (result.WasTimedOut) return InspectionSectionState.TimedOut;
        var output = result.StandardOutput + "\n" + result.StandardError;
        if (output.Contains("Permission Denial", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
            || output.Contains("SecurityException", StringComparison.OrdinalIgnoreCase)) return InspectionSectionState.PermissionDenied;
        if (output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Any(line =>
            line.TrimStart().StartsWith("Can't find service", StringComparison.OrdinalIgnoreCase)
            || line.TrimEnd().EndsWith(": not found", StringComparison.OrdinalIgnoreCase)
            || line.Contains(": No such file", StringComparison.OrdinalIgnoreCase)
            || line.TrimStart().StartsWith("Unknown command", StringComparison.OrdinalIgnoreCase)
            || line.TrimStart().StartsWith("Unknown option", StringComparison.OrdinalIgnoreCase))) return InspectionSectionState.Unavailable;
        return !result.IsSuccess ? InspectionSectionState.Failed
            : string.IsNullOrWhiteSpace(result.StandardOutput) ? InspectionSectionState.Partial : InspectionSectionState.Completed;
    }
}
