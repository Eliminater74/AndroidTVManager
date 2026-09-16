using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class PackageManager : IPackageManager
{
    private readonly IAdbProcessRunner _runner;
    private readonly IPackageSafetyGate? _safetyGate;

    public PackageManager(IAdbProcessRunner runner, IPackageSafetyGate? safetyGate = null)
    {
        _runner = runner;
        _safetyGate = safetyGate;
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
        => MutateAsync(serial, packageName, PackageMutationKind.Enable,
            () => _runner.RunForDeviceAsync(serial, ["shell", "pm", "enable", "--user", "0", packageName],
                TimeSpan.FromSeconds(30), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> DisableAsync(string serial, string packageName, CancellationToken cancellationToken = default)
        => MutateAsync(serial, packageName, PackageMutationKind.Disable,
            () => _runner.RunForDeviceAsync(serial, ["shell", "pm", "disable-user", "--user", "0", packageName],
                TimeSpan.FromSeconds(30), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> UninstallForUserAsync(string serial, string packageName, CancellationToken cancellationToken = default)
        => MutateAsync(serial, packageName, PackageMutationKind.UninstallForUser,
            () => _runner.RunForDeviceAsync(serial, ["shell", "pm", "uninstall", "--user", "0", packageName],
                TimeSpan.FromSeconds(60), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> RestoreAsync(string serial, string packageName, CancellationToken cancellationToken = default)
        => MutateAsync(serial, packageName, PackageMutationKind.Restore,
            () => _runner.RunForDeviceAsync(serial, ["shell", "cmd", "package", "install-existing", "--user", "0", packageName],
                TimeSpan.FromSeconds(60), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> FullUninstallAsync(string serial, string packageName, CancellationToken cancellationToken = default)
        => MutateAsync(serial, packageName, PackageMutationKind.FullUninstall,
            () => _runner.RunForDeviceAsync(serial, ["shell", "pm", "uninstall", packageName],
                TimeSpan.FromSeconds(60), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> ClearDataAsync(string serial, string packageName, CancellationToken cancellationToken = default)
        => MutateAsync(serial, packageName, PackageMutationKind.ClearData,
            () => _runner.RunForDeviceAsync(serial, ["shell", "pm", "clear", packageName],
                TimeSpan.FromMinutes(2), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> ClearCacheAsync(string serial, string packageName, CancellationToken cancellationToken = default)
        => MutateAsync(serial, packageName, PackageMutationKind.ClearCache,
            () => _runner.RunForDeviceAsync(serial, ["shell", "pm", "clear", "--cache-only", packageName],
                TimeSpan.FromMinutes(2), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> GrantPermissionAsync(string serial, string packageName, string permission,
        CancellationToken cancellationToken = default)
        => MutateAsync(serial, packageName, PackageMutationKind.GrantPermission,
            () => _runner.RunForDeviceAsync(serial, ["shell", "pm", "grant", packageName, permission],
                TimeSpan.FromSeconds(30), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> RevokePermissionAsync(string serial, string packageName, string permission,
        CancellationToken cancellationToken = default)
        => MutateAsync(serial, packageName, PackageMutationKind.RevokePermission,
            () => _runner.RunForDeviceAsync(serial, ["shell", "pm", "revoke", packageName, permission],
                TimeSpan.FromSeconds(30), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> OpenAppSettingsAsync(string serial, string packageName, CancellationToken cancellationToken = default)
        => _runner.RunForDeviceAsync(serial, ["shell", "am", "start", "-a", "android.settings.APPLICATION_DETAILS_SETTINGS",
            "-d", $"package:{packageName}"], TimeSpan.FromSeconds(30), cancellationToken);

    public Task<AdbCommandResult> PullApkAsync(string serial, string remotePath, string localPath,
        CancellationToken cancellationToken = default)
        => _runner.RunForDeviceAsync(serial, ["pull", remotePath, localPath],
            TimeSpan.FromMinutes(5), cancellationToken);

    private async Task<AdbCommandResult> MutateAsync(
        string serial,
        string packageName,
        PackageMutationKind kind,
        Func<Task<AdbCommandResult>> operation,
        CancellationToken cancellationToken)
    {
        if (_safetyGate is not null)
            await _safetyGate.EnsureAllowedAsync(new PackageMutationRequest(serial, packageName, kind), cancellationToken);
        return await operation();
    }

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
