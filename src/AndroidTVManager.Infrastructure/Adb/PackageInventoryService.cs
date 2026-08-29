using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class PackageInventoryService : IPackageInventoryService
{
    private static readonly TimeSpan InventoryTimeout = TimeSpan.FromMinutes(2);
    private readonly IAdbProcessRunner _runner;
    private readonly IPackageInventoryRepository _repository;
    private readonly IAppLogger _logger;

    public PackageInventoryService(
        IAdbProcessRunner runner,
        IPackageInventoryRepository repository,
        IAppLogger logger)
    {
        _runner = runner;
        _repository = repository;
        _logger = logger;
    }

    public async Task<PackageInventoryResult> GetInventoryAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("A device serial is required.", nameof(serial));
        serial = serial.Trim();

        var commandNames = new[]
        {
            "all", "system", "user", "disabled", "enabled", "uninstalled",
            "launcher", "input", "accessibility", "device-owner"
        };
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
                "launcher" => ["shell", "cmd", "package", "resolve-activity", "--brief", "-a",
                    "android.intent.action.MAIN", "-c", "android.intent.category.HOME"],
                "input" => ["shell", "settings", "get", "secure", "default_input_method"],
                "accessibility" => ["shell", "settings", "get", "secure", "enabled_accessibility_services"],
                "device-owner" => ["shell", "dumpsys", "device_policy"],
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
        var launcher = ExtractPackage(results["launcher"].StandardOutput);
        var input = ExtractPackages(results["input"].StandardOutput);
        var accessibility = ExtractPackages(results["accessibility"].StandardOutput);
        var owners = ExtractPackages(results["device-owner"].StandardOutput);

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
                    null,
                    string.Equals(package, launcher, StringComparison.OrdinalIgnoreCase),
                    input.Contains(package),
                    accessibility.Contains(package),
                    owners.Contains(package));
            })
            .ToArray();

        var error = results["all"].IsSuccess
            ? null
            : string.IsNullOrWhiteSpace(results["all"].StandardError)
                ? "Package inventory was unavailable."
                : results["all"].StandardError.Trim();
        var inventory = new PackageInventoryResult(serial, DateTimeOffset.UtcNow, packages, evidence, error);
        try
        {
            await _repository.SaveAsync(inventory, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.Warning("PackageInventory", $"Could not cache inventory for {serial}: {exception.Message}");
        }
        return inventory;
    }

    private static string? ExtractPackage(string output)
    {
        var value = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .LastOrDefault(line => line.Contains('/'));
        if (value is null)
            return null;
        var separator = value.IndexOf('/');
        return separator > 0 ? value[..separator] : null;
    }

    private static IReadOnlySet<string> ExtractPackages(string output)
        => output.Split(['\r', '\n', ':', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Split('/')[0])
            .Where(value => value.Contains('.') && !value.Contains('='))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
