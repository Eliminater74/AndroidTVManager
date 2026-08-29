using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class DeviceInspectionService : IDeviceInspectionService
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(20);
    private readonly IAdbProcessRunner _runner;
    private readonly IDeviceSnapshotRepository _snapshots;
    private readonly IAppLogger _logger;

    public DeviceInspectionService(
        IAdbProcessRunner runner,
        IDeviceSnapshotRepository snapshots,
        IAppLogger logger)
    {
        _runner = runner;
        _snapshots = snapshots;
        _logger = logger;
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
            ["hostname"] = (["shell", "hostname"], ReadTimeout),
            ["verifier"] = (["shell", "pm", "list", "packages", "com.google.android.verifier"], ReadTimeout),
            ["verifier-details"] = (["shell", "dumpsys", "package", "com.google.android.verifier"], ReadTimeout),
            ["gsi-tool"] = (["shell", "sh", "-c", "which gsi_tool && gsi_tool status"], ReadTimeout),
            ["packages"] = (["shell", "pm", "list", "packages"], ReadTimeout),
            ["battery"] = (["shell", "dumpsys", "battery"], ReadTimeout),
            ["uptime"] = (["shell", "cat", "/proc/uptime"], ReadTimeout),
            ["surfaceflinger"] = (["shell", "dumpsys", "SurfaceFlinger"], ReadTimeout),
            ["services"] = (["shell", "dumpsys", "activity", "services"], ReadTimeout)
        };

        var results = await RunCommandsAsync(serial, commands, progress, cancellationToken);
        var props = Properties(results, "getprop");
        var metadata = AdbMetadataParser.Parse(Output(results, "getprop"));
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
        var boot = AdbInspectionParsers.ParseBoot(props);
        var verifier = AdbInspectionParsers.ParseDeveloperVerification(
            Output(results, "verifier"), Output(results, "verifier-details"), props,
            results["verifier"].FirstOrDefault()?.State == InspectionSectionState.Completed);
        var gsi = AdbInspectionParsers.ParseGsi(props, Output(results, "gsi-tool"),
            Output(results, "packages").Contains("dynamic.system", StringComparison.OrdinalIgnoreCase));

        var inspection = new DeviceInspectionResult(
            serial,
            DateTimeOffset.UtcNow,
            Section("Overview", results, ["getprop"], device),
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
            Section("Network", results, ["network", "hostname"],
                AdbInspectionParsers.ParseNetwork(Output(results, "network"), Output(results, "hostname"))),
            Section("Runtime", results, ["uptime", "battery", "services"],
                new RuntimeInfo(Output(results, "uptime"), Summarize(Output(results, "battery")), null, null,
                    Summarize(Output(results, "services")))),
            Section("Features", results, ["features"], AdbInspectionParsers.ParseFeatures(Output(results, "features"))),
            Section("Developer Verification", results, ["verifier", "verifier-details"], verifier),
            props,
            BuildCapabilities(device, cpu, security, boot, gsi, verifier, Output(results, "features")),
            results.Values.SelectMany(value => value).ToArray());

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
            new("Android Developer Verifier", verifier.VerifierPresent is true ? CapabilityState.Supported
                : verifier.VerifierPresent is false ? CapabilityState.Unsupported : CapabilityState.Unknown,
                verifier.VerifierPresent is true ? "Installed" : "Not detected",
                verifier.Evidence),
            new("Manual Unverified Install Flow", CapabilityState.Unknown,
                "Check the device settings; this state is not reliably exposed through standard ADB.",
                [new("Device settings", null, "Manual Developer Verification state is vendor/version dependent.", EvidenceConfidence.Low)])
        ];

    private static IReadOnlyDictionary<string, string> Properties(
        IReadOnlyDictionary<string, IReadOnlyList<InspectionCommandEvidence>> results,
        string key)
        => AdbInspectionParsers.ParseProperties(Output(results, key));

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
}
