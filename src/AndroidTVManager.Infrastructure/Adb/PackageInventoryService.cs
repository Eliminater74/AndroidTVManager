using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class PackageInventoryService : IPackageInventoryService
{
    private static readonly TimeSpan InventoryTimeout = TimeSpan.FromMinutes(2);
    private readonly IAdbProcessRunner _runner;

    public PackageInventoryService(IAdbProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<PackageInventoryResult> GetInventoryAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("A device serial is required.", nameof(serial));
        serial = serial.Trim();

        var commandNames = new[] { "all", "system", "user", "disabled", "enabled", "uninstalled" };
        var tasks = commandNames.Select(async name =>
        {
            var arguments = name switch
            {
                "all" => (IReadOnlyList<string>)["shell", "pm", "list", "packages", "-f"],
                "system" => ["shell", "pm", "list", "packages", "-s"],
                "user" => ["shell", "pm", "list", "packages", "-3"],
                "disabled" => ["shell", "pm", "list", "packages", "-d"],
                "enabled" => ["shell", "pm", "list", "packages", "-e"],
                "uninstalled" => ["shell", "pm", "list", "packages", "-u"],
                _ => []
            };
            return (name, Result: await _runner.RunForDeviceAsync(serial, arguments, InventoryTimeout, cancellationToken));
        });
        var results = (await Task.WhenAll(tasks)).ToDictionary(pair => pair.name, pair => pair.Result);
        var evidence = results.Select(pair => new InspectionCommandEvidence(
            $"pm list packages {pair.Key}",
            pair.Value.IsSuccess ? InspectionSectionState.Completed : InspectionSectionState.Partial,
            pair.Value.StandardOutput,
            pair.Value.StandardError,
            pair.Value.ExitCode,
            pair.Value.Duration,
            pair.Value.IsSuccess ? null : pair.Value.StandardError.Trim())).ToArray();

        var paths = PackageInventoryParser.ParsePackagePaths(results["all"].StandardOutput);
        var all = paths.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var system = PackageInventoryParser.ParsePackageNames(results["system"].StandardOutput);
        var user = PackageInventoryParser.ParsePackageNames(results["user"].StandardOutput);
        var disabled = PackageInventoryParser.ParsePackageNames(results["disabled"].StandardOutput);
        var enabled = PackageInventoryParser.ParsePackageNames(results["enabled"].StandardOutput);
        var uninstalled = PackageInventoryParser.ParsePackageNames(results["uninstalled"].StandardOutput);
        var packageNames = all.Union(uninstalled, StringComparer.OrdinalIgnoreCase);

        var packages = packageNames
            .OrderBy(package => package, StringComparer.OrdinalIgnoreCase)
            .Select(package =>
            {
                var packagePaths = paths.GetValueOrDefault(package) ?? [];
                var isSystem = system.Contains(package)
                    || packagePaths.Any(path => path.Contains("/system/", StringComparison.OrdinalIgnoreCase)
                        || path.Contains("/product/", StringComparison.OrdinalIgnoreCase)
                        || path.Contains("/vendor/", StringComparison.OrdinalIgnoreCase));
                return new PackageInventoryEntry(
                    package,
                    null,
                    null,
                    null,
                    user.Contains(package) ? "0" : null,
                    isSystem,
                    isSystem && packagePaths.Any(path => path.Contains("/data/app", StringComparison.OrdinalIgnoreCase)),
                    !disabled.Contains(package) && (enabled.Count == 0 || enabled.Contains(package)),
                    all.Contains(package),
                    !all.Contains(package) && uninstalled.Contains(package),
                    packagePaths,
                    DateTimeOffset.UtcNow,
                    serial,
                    null,
                    null);
            })
            .ToArray();

        var error = results["all"].IsSuccess
            ? null
            : string.IsNullOrWhiteSpace(results["all"].StandardError)
                ? "Package inventory was unavailable."
                : results["all"].StandardError.Trim();
        return new(serial, DateTimeOffset.UtcNow, packages, evidence, error);
    }

    public async Task<PackageInventoryEntry?> GetDetailsAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial) || string.IsNullOrWhiteSpace(packageName))
            return null;
        var result = await _runner.RunForDeviceAsync(serial.Trim(),
            ["shell", "dumpsys", "package", packageName.Trim()],
            InventoryTimeout,
            cancellationToken);
        if (!result.IsSuccess)
            return null;

        var details = PackageInventoryParser.ParseDetails(packageName.Trim(), result.StandardOutput);
        return new PackageInventoryEntry(
            details.PackageName,
            null,
            details.VersionName,
            details.VersionCode,
            details.UserId,
            details.ApkPaths.Any(path => path.Contains("/system/", StringComparison.OrdinalIgnoreCase)),
            details.ApkPaths.Any(path => path.Contains("/data/app", StringComparison.OrdinalIgnoreCase)
                && path.Contains("updated", StringComparison.OrdinalIgnoreCase)),
            details.IsEnabled,
            details.IsInstalled,
            !details.IsInstalled,
            details.ApkPaths,
            DateTimeOffset.UtcNow,
            serial.Trim(),
            null,
            null);
    }
}
