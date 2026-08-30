using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class ConfigurationExplorerService : IConfigurationExplorerService
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly (ConfigurationSource Source, string Path)[] PropertyFiles =
    [
        (ConfigurationSource.System, "/system/build.prop"),
        (ConfigurationSource.Vendor, "/vendor/build.prop"),
        (ConfigurationSource.Product, "/product/build.prop"),
        (ConfigurationSource.SystemExt, "/system_ext/build.prop"),
        (ConfigurationSource.Odm, "/odm/build.prop")
    ];

    private readonly IAdbProcessRunner _runner;
    private readonly IConfigurationSnapshotStore _snapshots;
    private readonly IAppLogger _logger;

    public ConfigurationExplorerService(
        IAdbProcessRunner runner,
        IConfigurationSnapshotStore snapshots,
        IAppLogger logger)
    {
        _runner = runner;
        _snapshots = snapshots;
        _logger = logger;
    }

    public async Task<ConfigurationSnapshot> InspectAsync(
        string serial,
        string? friendlyDeviceName = null,
        IProgress<ConfigurationInspectionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("A device serial is required.", nameof(serial));
        serial = serial.Trim();

        var captures = await CollectAsync(serial, progress, cancellationToken);
        var runtime = captures.First(capture => capture.Source == ConfigurationSource.Runtime).Values;
        var files = captures
            .Where(capture => capture.Source != ConfigurationSource.Runtime)
            .ToDictionary(capture => capture.Source, capture => capture.Values);
        var availability = captures
            .Where(capture => capture.Source != ConfigurationSource.Runtime)
            .ToDictionary(capture => capture.Source, capture => capture.IsAvailable);
        var errors = captures
            .Where(capture => capture.Source != ConfigurationSource.Runtime)
            .ToDictionary(capture => capture.Source, capture => capture.Error);
        var names = runtime.Keys
            .Union(files.Values.SelectMany(values => values.Keys), StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var properties = names
            .Select(name => ConfigurationPropertyParser.CreateProperty(
                name, runtime, files, availability, errors))
            .ToArray();
        var evidence = captures.Select(capture => capture.Evidence).ToArray();
        var state = evidence.All(item => item.State == InspectionSectionState.Completed)
            && captures.Skip(1).All(capture => capture.IsAvailable)
            ? InspectionSectionState.Completed
            : InspectionSectionState.Partial;
        var sections = properties
            .GroupBy(property => property.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ConfigurationSection(
                group.Key,
                group.OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
                state,
                evidence,
                state == InspectionSectionState.Completed
                    ? null
                    : "One or more property sources were unavailable or returned an error."))
            .ToList();
        if (sections.Count == 0)
        {
            sections.Add(new ConfigurationSection(
                "Runtime Properties",
                [],
                state,
                evidence,
                "No readable properties were returned by the connected device."));
        }

        var snapshot = new ConfigurationSnapshot(
            serial,
            DateTimeOffset.UtcNow,
            friendlyDeviceName ?? Get(runtime, "ro.product.model") ?? Get(runtime, "ro.product.device"),
            Get(runtime, "ro.product.manufacturer"),
            Get(runtime, "ro.product.model"),
            Get(runtime, "ro.build.fingerprint"),
            Get(runtime, "ro.build.version.release"),
            ParseInt(Get(runtime, "ro.build.version.sdk")),
            Get(runtime, "ro.build.version.security_patch"),
            sections,
            evidence);

        try
        {
            await _snapshots.SaveAsync(snapshot, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.Warning("Configuration", $"Could not cache configuration for {serial}: {exception.Message}");
        }
        return snapshot;
    }

    private async Task<IReadOnlyList<Capture>> CollectAsync(
        string serial,
        IProgress<ConfigurationInspectionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var total = PropertyFiles.Length + 1;
        var completed = 0;
        using var gate = new SemaphoreSlim(3);
        var tasks = new List<Task<Capture>>
        {
            CollectAsync(
                serial,
                ConfigurationSource.Runtime,
                ["shell", "getprop"],
                "Runtime properties",
                true)
        };
        tasks.AddRange(PropertyFiles.Select(file => CollectAsync(
            serial,
            file.Source,
            ["shell", "sh", "-c",
                $"if [ -r '{file.Path}' ]; then cat '{file.Path}'; else echo '{ConfigurationPropertyParser.UnavailableMarker}'; fi"],
            file.Path,
            false)));

        async Task<Capture> CollectAsync(
            string target,
            ConfigurationSource source,
            IReadOnlyList<string> arguments,
            string category,
            bool runtime)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var result = await _runner.RunForDeviceAsync(target, arguments, ReadTimeout, cancellationToken);
                var output = result.StandardOutput ?? string.Empty;
                var unavailable = !runtime && ConfigurationPropertyParser.IsUnavailableFile(output);
                var state = result.IsSuccess && !unavailable
                    ? InspectionSectionState.Completed
                    : InspectionSectionState.Partial;
                var error = unavailable
                    ? "File is missing or not readable."
                    : result.IsSuccess ? null : result.StandardError.Trim();
                var item = new Capture(
                    source,
                    runtime ? ConfigurationPropertyParser.ParseRuntime(output)
                        : ConfigurationPropertyParser.ParseFile(output),
                    new(
                        category,
                        state,
                        result.StandardOutput,
                        result.StandardError,
                        result.ExitCode,
                        result.Duration,
                        error),
                    !unavailable,
                    error);
                var finished = Interlocked.Increment(ref completed);
                progress?.Report(new(category, finished, total, state));
                return item;
            }
            finally
            {
                gate.Release();
            }
        }

        return await Task.WhenAll(tasks);
    }

    private static string? Get(IReadOnlyDictionary<string, string> properties, string key)
        => properties.TryGetValue(key, out var value) ? value : null;

    private static int? ParseInt(string? value)
        => int.TryParse(value, out var parsed) ? parsed : null;

    private sealed record Capture(
        ConfigurationSource Source,
        IReadOnlyDictionary<string, string> Values,
        InspectionCommandEvidence Evidence,
        bool IsAvailable,
        string? Error);
}
