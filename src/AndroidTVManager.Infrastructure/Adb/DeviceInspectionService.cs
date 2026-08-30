using System.Text.RegularExpressions;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Packages;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class DeviceInspectionService : IDeviceInspectionService
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(20);
    private readonly IAdbProcessRunner _runner;
    private readonly IDeviceSnapshotRepository _snapshots;
    private readonly IAppLogger _logger;
    private readonly IRootGuidanceProvider _rootGuidance;

    public DeviceInspectionService(
        IAdbProcessRunner runner,
        IDeviceSnapshotRepository snapshots,
        IAppLogger logger,
        IRootGuidanceProvider? rootGuidance = null)
    {
        _runner = runner;
        _snapshots = snapshots;
        _logger = logger;
        _rootGuidance = rootGuidance ?? new RootGuidanceProvider();
    }

    public async Task<DeviceInspectionResult> InspectAsync(
        string serial,
        IProgress<DeviceInspectionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("A device serial is required.", nameof(serial));
        serial = serial.Trim();

        var commands = new Dictionary<string, (IReadOnlyList<string> Arguments, TimeSpan Timeout)>
        {
            ["getprop"] = (["shell", "getprop"], ReadTimeout),
            ["cpuinfo"] = (["shell", "cat", "/proc/cpuinfo"], ReadTimeout),
            ["meminfo"] = (["shell", "cat", "/proc/meminfo"], ReadTimeout),
            ["wm-size"] = (["shell", "wm", "size"], ReadTimeout),
            ["wm-density"] = (["shell", "wm", "density"], ReadTimeout),
            ["display"] = (["shell", "dumpsys", "display"], ReadTimeout),
            ["storage"] = (["shell", "df", "-k"], ReadTimeout),
            ["features"] = (["shell", "pm", "list", "features"], ReadTimeout),
            ["selinux"] = (["shell", "getenforce"], ReadTimeout),
            ["root"] = (["shell", "sh", "-c", "id; which su"], ReadTimeout),
            ["network"] = (["shell", "ip", "addr"], ReadTimeout),
            ["routes"] = (["shell", "ip", "route"], ReadTimeout),
            ["device-name"] = (["shell", "settings", "get", "global", "device_name"], ReadTimeout),
            ["mac-address"] = (["shell", "ip", "link"], ReadTimeout),
            ["hostname"] = (["shell", "hostname"], ReadTimeout),
            ["oem-unlock-setting"] = (["shell", "settings", "get", "global", "oem_unlock_allowed"], ReadTimeout),
            ["bluetooth-on"] = (["shell", "settings", "get", "global", "bluetooth_on"], ReadTimeout),
            ["bluetooth"] = (["shell", "dumpsys", "bluetooth_manager"], ReadTimeout),
            ["hdmi"] = (["shell", "dumpsys", "hdmi_control"], ReadTimeout),
            ["audio"] = (["shell", "dumpsys", "audio"], ReadTimeout),
            ["drm"] = (["shell", "dumpsys", "media.drm"], ReadTimeout),
            ["verifier"] = (["shell", "pm", "list", "packages", "com.google.android.verifier"], ReadTimeout),
            ["verifier-details"] = (["shell", "dumpsys", "package", "com.google.android.verifier"], ReadTimeout),
            ["gsi-tool"] = (["shell", "sh", "-c", "which gsi_tool && gsi_tool status"], ReadTimeout),
            ["packages"] = (["shell", "pm", "list", "packages", "-f"], ReadTimeout),
            ["packages-system"] = (["shell", "pm", "list", "packages", "-s"], ReadTimeout),
            ["packages-user"] = (["shell", "pm", "list", "packages", "-3"], ReadTimeout),
            ["packages-disabled"] = (["shell", "pm", "list", "packages", "-d"], ReadTimeout),
            ["packages-enabled"] = (["shell", "pm", "list", "packages", "-e"], ReadTimeout),
            ["packages-uninstalled"] = (["shell", "pm", "list", "packages", "-u"], ReadTimeout),
            ["packages-launcher"] = (["shell", "cmd", "package", "resolve-activity", "--brief", "-a",
                "android.intent.action.MAIN", "-c", "android.intent.category.HOME"], ReadTimeout),
            ["packages-input"] = (["shell", "settings", "get", "secure", "default_input_method"], ReadTimeout),
            ["packages-accessibility"] = (["shell", "settings", "get", "secure", "enabled_accessibility_services"], ReadTimeout),
            ["packages-owner"] = (["shell", "dumpsys", "device_policy"], ReadTimeout),
            ["battery"] = (["shell", "dumpsys", "battery"], ReadTimeout),
            ["uptime"] = (["shell", "cat", "/proc/uptime"], ReadTimeout),
            ["thermal"] = (["shell", "dumpsys", "thermalservice"], ReadTimeout),
            ["processes"] = (["shell", "ps", "-A"], ReadTimeout),
            ["surfaceflinger"] = (["shell", "dumpsys", "SurfaceFlinger"], ReadTimeout),
            ["services"] = (["shell", "dumpsys", "activity", "services"], ReadTimeout)
        };

        var results = await RunCommandsAsync(serial, commands, progress, cancellationToken);
        var props = Properties(results, "getprop");
        var metadata = AdbMetadataParser.Parse(Output(results, "getprop"));
        var reportedName = AdbMetadataParser.ParseReportedName(Output(results, "device-name"));
        var macAddress = AdbMetadataParser.ParseMacAddress(Output(results, "mac-address"));
        var device = new AndroidDevice
        {
            Serial = serial,
            Endpoint = serial.Contains(':') ? serial : null,
            State = DeviceState.Device,
            ConnectionType = serial.Contains(':') ? ConnectionType.Network : ConnectionType.Usb,
            Manufacturer = metadata.Manufacturer,
            Brand = metadata.Brand,
            Model = metadata.Model,
            Product = metadata.Product,
            DeviceName = metadata.DeviceName,
            ReportedName = reportedName,
            MacAddress = macAddress,
            Board = metadata.Board,
            AndroidVersion = metadata.AndroidVersion,
            ApiLevel = metadata.ApiLevel,
            SecurityPatch = metadata.SecurityPatch,
            BuildId = metadata.BuildId,
            BuildType = metadata.BuildType,
            BuildFingerprint = metadata.BuildFingerprint
        };

        var cpu = AdbInspectionParsers.ParseCpu(Output(results, "cpuinfo"),
            Get(props, "ro.product.cpu.abi"),
            Get(props, "ro.product.cpu.abilist"),
            Get(props, "ro.hardware"),
            Get(props, "ro.board.platform"));
        var security = AdbInspectionParsers.ParseSecurity(props, Output(results, "selinux"), Output(results, "root"));
        var oemUnlock = AdbInspectionParsers.ParseOemUnlock(props, Output(results, "oem-unlock-setting"));
        var root = AdbInspectionParsers.ParseRoot(props, Output(results, "root"));
        root = root with { Guidance = _rootGuidance.GetGuidance(device, oemUnlock, security, root) };
        var boot = AdbInspectionParsers.ParseBoot(props);
        var bluetooth = AdbInspectionParsers.ParseBluetooth(
            Output(results, "features"),
            Output(results, "bluetooth-on"),
            Output(results, "bluetooth"));
        var hdmi = AdbInspectionParsers.ParseHdmi(Output(results, "hdmi"), Output(results, "audio"));
        var drm = AdbInspectionParsers.ParseDrm(Output(results, "drm"));
        var verifier = AdbInspectionParsers.ParseDeveloperVerification(
            Output(results, "verifier"), Output(results, "verifier-details"), props,
            results["verifier"].FirstOrDefault()?.State == InspectionSectionState.Completed);
        var gsi = AdbInspectionParsers.ParseGsi(props, Output(results, "gsi-tool"),
            Output(results, "packages").Contains("dynamic.system", StringComparison.OrdinalIgnoreCase));

        var inspection = new DeviceInspectionResult(
            serial,
            DateTimeOffset.UtcNow,
            Section("Overview", results, ["getprop", "device-name", "mac-address"], device),
            Section("CPU", results, ["cpuinfo"], cpu),
            Section("Memory", results, ["meminfo"], AdbInspectionParsers.ParseMemory(Output(results, "meminfo"))),
            Section("Graphics", results, ["surfaceflinger"],
                new GraphicsInfo(
                    FindLine(Output(results, "surfaceflinger"), "GLES"),
                    FindLine(Output(results, "surfaceflinger"), "vendor"),
                    FindLine(Output(results, "surfaceflinger"), "OpenGL ES"),
                    FindLine(Output(results, "surfaceflinger"), "Vulkan"),
                    FindLine(Output(results, "surfaceflinger"), "composer"),
                    null)),
            Section("Display", results, ["wm-size", "wm-density", "display"],
                AdbInspectionParsers.ParseDisplay(Output(results, "wm-size"), Output(results, "wm-density"),
                    Output(results, "display"))),
            Section("Storage", results, ["storage"], AdbInspectionParsers.ParseStorage(Output(results, "storage"))),
            Section("Security", results, ["getprop", "selinux", "root"], security),
            Section("Boot", results, ["getprop"], boot),
            Section("Treble / GSI", results, ["getprop", "gsi-tool", "packages"], gsi),
            Section("Network", results, ["network", "routes", "hostname", "mac-address"],
                AdbInspectionParsers.ParseNetwork(Output(results, "network"), Output(results, "hostname"),
                    Output(results, "routes"), props)),
            Section("Runtime", results, ["uptime", "battery", "thermal", "processes", "services"],
                new RuntimeInfo(Output(results, "uptime"), Summarize(Output(results, "battery")),
                    Summarize(Output(results, "thermal")), Summarize(Output(results, "processes")),
                    Summarize(Output(results, "services")))),
            Section("Features", results, ["features"], AdbInspectionParsers.ParseFeatures(Output(results, "features"))),
            Section("Packages", results, ["packages", "packages-system", "packages-user",
                "packages-disabled", "packages-enabled", "packages-uninstalled", "packages-launcher",
                "packages-input", "packages-accessibility", "packages-owner"],
                ParsePackageSummary(results)),
            Section("Services", results, ["services"],
                new ServiceSummaryInfo(
                    CountLines(Output(results, "services"), "ServiceRecord{"),
                    Summarize(Output(results, "services")),
                    AdbInspectionParsers.ParseServices(Output(results, "services")))),
            Section("Developer Verification", results, ["verifier", "verifier-details"], verifier),
            props,
            BuildCapabilities(device, cpu, security, boot, gsi, verifier, oemUnlock, root,
                bluetooth, hdmi, drm, Output(results, "features")),
            results.Values.SelectMany(value => value).ToArray(),
            Section("OEM Unlock", results, ["getprop", "oem-unlock-setting"], oemUnlock),
            Section("Root Feasibility", results, ["getprop", "root"], root),
            Section("Bluetooth", results, ["features", "bluetooth-on", "bluetooth"], bluetooth),
            Section("HDMI / CEC", results, ["hdmi", "audio"], hdmi),
            Section("DRM", results, ["drm"], drm));

        try
        {
            await _snapshots.SaveAsync(inspection, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.Warning("Inspection", $"Could not cache inspection for {serial}: {exception.Message}");
        }
        return inspection;
    }

    private async Task<Dictionary<string, IReadOnlyList<InspectionCommandEvidence>>> RunCommandsAsync(
        string serial,
        IReadOnlyDictionary<string, (IReadOnlyList<string> Arguments, TimeSpan Timeout)> commands,
        IProgress<DeviceInspectionProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(4);
        var completed = 0;
        var tasks = commands.Select(async pair =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var result = await _runner.RunForDeviceAsync(serial, pair.Value.Arguments, pair.Value.Timeout, cancellationToken);
                var evidence = new InspectionCommandEvidence(pair.Key,
                    result.IsSuccess ? InspectionSectionState.Completed : InspectionSectionState.Partial,
                    result.StandardOutput, result.StandardError, result.ExitCode, result.Duration,
                    result.IsSuccess ? null : result.StandardError.Trim());
                var count = Interlocked.Increment(ref completed);
                progress?.Report(new(pair.Key, count, commands.Count, evidence.State));
                return (pair.Key, Evidence: (IReadOnlyList<InspectionCommandEvidence>)[evidence]);
            }
            finally
            {
                gate.Release();
            }
        });
        return (await Task.WhenAll(tasks)).ToDictionary(pair => pair.Key, pair => pair.Evidence);
    }

    private static InspectionSection<T> Section<T>(
        string name,
        IReadOnlyDictionary<string, IReadOnlyList<InspectionCommandEvidence>> results,
        IReadOnlyList<string> keys,
        T value)
    {
        var evidence = keys.SelectMany(key => results.GetValueOrDefault(key) ?? []).ToArray();
        var failed = evidence.Any(item => item.State != InspectionSectionState.Completed);
        return new(name, failed ? InspectionSectionState.Partial : InspectionSectionState.Completed,
            value, evidence, failed ? "One or more diagnostic commands were unavailable." : null);
    }

    private static IReadOnlyList<DeviceCapability> BuildCapabilities(
        AndroidDevice device,
        CpuInfo cpu,
        SecurityInfo security,
        BootInfo boot,
        GsiInfo gsi,
        DeveloperVerificationInfo verifier,
        OemUnlockInfo oemUnlock,
        RootInfo root,
        BluetoothInfo bluetooth,
        HdmiInfo hdmi,
        DrmInfo drm,
        string features)
        =>
        [
            new("ADB APK Installation", CapabilityState.Supported,
                "ADB installation is available for this connected target.",
                [new("adb install", "connected", "The device is reachable through ADB.", EvidenceConfidence.Verified)]),
            new("Android TV", features.Contains("leanback", StringComparison.OrdinalIgnoreCase)
                ? CapabilityState.Supported : CapabilityState.Unknown,
                "Detected from exposed system features.",
                [new("pm list features", features, "Android TV feature evidence.")]),
            new("64-bit", cpu.Architecture?.Contains("64", StringComparison.OrdinalIgnoreCase) == true
                ? CapabilityState.Supported : CapabilityState.Unknown,
                cpu.Architecture ?? "Architecture not exposed.",
                [new("ro.product.cpu.abilist", string.Join(", ", cpu.SupportedAbis), "Reported ABI list.")]),
            new("Project Treble", gsi.Treble,
                gsi.Treble == CapabilityState.Supported ? "Treble property is enabled." : "Treble support was not verified.",
                gsi.Evidence.Where(item => item.Source.Contains("treble", StringComparison.OrdinalIgnoreCase)).ToArray()),
            new("Dynamic Partitions", gsi.DynamicPartitions,
                "Determined from super-partition evidence.",
                gsi.Evidence.Where(item => item.Source.Contains("super", StringComparison.OrdinalIgnoreCase)).ToArray()),
            new("A/B Updates", boot.IsAbDevice is true ? CapabilityState.Supported : boot.IsAbDevice is false ? CapabilityState.Unsupported : CapabilityState.Unknown,
                boot.IsAbDevice is true ? "A/B update properties detected." : "A/B update state is unknown.",
                boot.Evidence),
            new("GSI / DSU", gsi.Assessment == GsiAssessment.LikelySupported ? CapabilityState.Supported
                : gsi.Assessment == GsiAssessment.PossiblySupported ? CapabilityState.Partial
                : gsi.Assessment == GsiAssessment.NotDetected ? CapabilityState.Unsupported : CapabilityState.Unknown,
                gsi.Assessment.ToString(),
                gsi.Evidence),
            new("ADB Root", security.AdbRoot, "ADB root is separate from root binary availability.", security.Evidence),
            new("OEM Unlock Option", oemUnlock.Option == OemUnlockOptionState.Present
                ? CapabilityState.Supported
                : oemUnlock.Option == OemUnlockOptionState.Absent
                    ? CapabilityState.Unsupported : CapabilityState.Unknown,
                oemUnlock.Option.ToString(), oemUnlock.Evidence),
            new("Actual Bootloader Unlock Capability", oemUnlock.ActualUnlockCapability,
                "ADB properties cannot prove an unlock operation is supported.", oemUnlock.Evidence),
            new("Root Feasibility", root.AdbRootFeasibility,
                root.Guidance, root.Evidence),
            new("Bluetooth", bluetooth.Support,
                bluetooth.IsEnabled is true ? "Bluetooth is enabled." : "Bluetooth state is device-dependent.",
                bluetooth.Evidence),
            new("HDMI / CEC", hdmi.Support,
                "HDMI and CEC support is vendor/API dependent.", hdmi.Evidence),
            new("DRM", drm.Availability,
                "DRM service evidence is available where the device exposes it.", drm.Evidence),
            new("Android Developer Verifier", verifier.VerifierPresent is true ? CapabilityState.Supported
                : CapabilityState.Unknown,
                verifier.VerifierPresent is true ? "Verifier evidence detected" : "Verifier state is not proven",
                verifier.Evidence),
            new("Advanced Installation Flow", verifier.AdvancedFlowAvailability == AdvancedFlowAvailability.Available
                ? CapabilityState.Supported : CapabilityState.Unknown,
                "Availability is device and Android-version dependent.",
                verifier.Evidence),
            new("Manual Unverified Install Flow", CapabilityState.Unknown,
                "Check the device settings; this state is not reliably exposed through standard ADB.",
                [new("Device settings", null, "Manual Developer Verification state is vendor/version dependent.", EvidenceConfidence.Low)]),
            new("On-device setup required", CapabilityState.Unknown,
                "ADB installation does not require manual setup; manual-install setup must be checked on device.",
                [new("Device settings", null, "No standard ADB property proves this policy state.", EvidenceConfidence.Low)])
        ];

    private static IReadOnlyDictionary<string, string> Properties(
        IReadOnlyDictionary<string, IReadOnlyList<InspectionCommandEvidence>> results,
        string key)
        => AdbInspectionParsers.ParseProperties(Output(results, key));

    private static PackageSummaryInfo ParsePackageSummary(
        IReadOnlyDictionary<string, IReadOnlyList<InspectionCommandEvidence>> results)
    {
        var paths = PackageInventoryParser.ParsePackagePaths(Output(results, "packages"));
        var installed = paths.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var known = PackageInventoryParser.ParsePackageNames(Output(results, "packages-uninstalled"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        known.UnionWith(installed);
        var system = PackageInventoryParser.ParsePackageNames(Output(results, "packages-system"));
        var user = PackageInventoryParser.ParsePackageNames(Output(results, "packages-user"));
        var disabled = PackageInventoryParser.ParsePackageNames(Output(results, "packages-disabled"));
        var enabled = PackageInventoryParser.ParsePackageNames(Output(results, "packages-enabled"));
        var uninstalled = known.Except(installed, StringComparer.OrdinalIgnoreCase).Count();
        return new(
            known.Count,
            "Package views are merged from installed, system, user, enabled, disabled, and uninstalled-for-user queries.",
            known.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            CountIfSucceeded(results, "packages", installed.Count),
            CountIfSucceeded(results, "packages-disabled", disabled.Count),
            CountIfSucceeded(results, "packages-enabled", enabled.Count),
            CountIfSucceeded(results, "packages-user", user.Count),
            CountIfSucceeded(results, "packages-system", system.Count),
            CountIfSucceeded(results, "packages-uninstalled", uninstalled),
            CountIfSucceeded(results, "packages-launcher", CountPackageTokens(Output(results, "packages-launcher"))),
            CountIfSucceeded(results, "packages-accessibility", CountPackageTokens(Output(results, "packages-accessibility"))),
            CountIfSucceeded(results, "packages-owner", CountPackageTokens(Output(results, "packages-owner"))));
    }

    private static int? CountIfSucceeded(
        IReadOnlyDictionary<string, IReadOnlyList<InspectionCommandEvidence>> results,
        string key,
        int count)
        => results.GetValueOrDefault(key)?.All(item => item.State == InspectionSectionState.Completed) == true
            ? count
            : null;

    private static int CountPackageTokens(string output)
        => Regex.Matches(output, @"[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+")
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static string Output(
        IReadOnlyDictionary<string, IReadOnlyList<InspectionCommandEvidence>> results,
        string key)
        => string.Join(Environment.NewLine, results.GetValueOrDefault(key)?.Select(item => item.StandardOutput) ?? []);

    private static string? Get(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value : null;

    private static string? FindLine(string output, string token)
        => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.Contains(token, StringComparison.OrdinalIgnoreCase))?.Trim();

    private static string? Summarize(string output)
        => string.IsNullOrWhiteSpace(output) ? null : output.Trim().Length > 1000 ? output.Trim()[..1000] : output.Trim();

    private static int CountLines(string output, string token)
        => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.Contains(token, StringComparison.OrdinalIgnoreCase));
}
