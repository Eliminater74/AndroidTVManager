using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class PackageManager : IPackageManager
{
    private readonly IAdbProcessRunner _runner;

    public PackageManager(IAdbProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<IReadOnlyList<PackageInfo>> ListAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunForDeviceAsync(serial, ["shell", "pm", "list", "packages", "-f"],
            TimeSpan.FromMinutes(2), cancellationToken);
        if (!result.IsSuccess)
            return [];

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParsePackage)
            .Where(package => package is not null)
            .Select(package => package!)
            .OrderBy(package => package.PackageName)
            .ToArray();
    }

    public Task<AdbCommandResult> LaunchAsync(string serial, string packageName, CancellationToken cancellationToken = default)
        => _runner.RunForDeviceAsync(serial, ["shell", "monkey", "-p", packageName, "1"],
            TimeSpan.FromSeconds(30), cancellationToken);

    public Task<AdbCommandResult> ForceStopAsync(string serial, string packageName, CancellationToken cancellationToken = default)
        => _runner.RunForDeviceAsync(serial, ["shell", "am", "force-stop", packageName],
            TimeSpan.FromSeconds(30), cancellationToken);

    public Task<AdbCommandResult> EnableAsync(string serial, string packageName, CancellationToken cancellationToken = default)
        => _runner.RunForDeviceAsync(serial, ["shell", "pm", "enable", packageName],
            TimeSpan.FromSeconds(30), cancellationToken);

    public Task<AdbCommandResult> DisableAsync(string serial, string packageName, CancellationToken cancellationToken = default)
        => _runner.RunForDeviceAsync(serial, ["shell", "pm", "disable-user", "--user", "0", packageName],
            TimeSpan.FromSeconds(30), cancellationToken);

    public Task<AdbCommandResult> UninstallForUserAsync(string serial, string packageName, CancellationToken cancellationToken = default)
        => _runner.RunForDeviceAsync(serial, ["shell", "pm", "uninstall", "--user", "0", packageName],
            TimeSpan.FromSeconds(60), cancellationToken);

    public Task<AdbCommandResult> ClearDataAsync(string serial, string packageName, CancellationToken cancellationToken = default)
        => _runner.RunForDeviceAsync(serial, ["shell", "pm", "clear", packageName],
            TimeSpan.FromMinutes(2), cancellationToken);

    private static PackageInfo? ParsePackage(string line)
    {
        var value = line.Trim();
        var separator = value.LastIndexOf('=');
        var packageName = separator >= 0 ? value[(separator + 1)..] : value.Replace("package:", string.Empty);
        return string.IsNullOrWhiteSpace(packageName)
            ? null
            : new PackageInfo(packageName, true, separator >= 0 && value.Contains("/system/", StringComparison.OrdinalIgnoreCase), false);
    }
}
