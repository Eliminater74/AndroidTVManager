using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class PackageManager : IPackageManager
{
    private readonly IAdbProcessRunner _runner;
    private readonly IPackageSafetyGate _safetyGate;

    public PackageManager(IAdbProcessRunner runner, IPackageSafetyGate safetyGate)
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

    public Task<AdbCommandResult> EnableAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default,
        string? expectedBuildFingerprint = null)
        => MutateAsync(serial, packageName, PackageMutationKind.Enable, expectedBuildFingerprint,
            () => _runner.RunForDeviceAsync(serial, ["shell", "pm", "enable", "--user", "0", packageName],
                TimeSpan.FromSeconds(30), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> DisableAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default,
        string? expectedBuildFingerprint = null)
        => MutateAsync(serial, packageName, PackageMutationKind.Disable, expectedBuildFingerprint,
            () => _runner.RunForDeviceAsync(serial, ["shell", "pm", "disable-user", "--user", "0", packageName],
                TimeSpan.FromSeconds(30), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> UninstallForUserAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default,
        string? expectedBuildFingerprint = null)
        => MutateAsync(serial, packageName, PackageMutationKind.UninstallForUser, expectedBuildFingerprint,
            () => _runner.RunForDeviceAsync(serial, ["shell", "pm", "uninstall", "--user", "0", packageName],
                TimeSpan.FromSeconds(60), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> RestoreAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default,
        string? expectedBuildFingerprint = null)
        => MutateAsync(serial, packageName, PackageMutationKind.Restore, expectedBuildFingerprint,
            () => _runner.RunForDeviceAsync(serial, ["shell", "cmd", "package", "install-existing", "--user", "0", packageName],
                TimeSpan.FromSeconds(60), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> FullUninstallAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default,
        string? expectedBuildFingerprint = null)
        => MutateAsync(serial, packageName, PackageMutationKind.FullUninstall, expectedBuildFingerprint,
            () => _runner.RunForDeviceAsync(serial, ["shell", "pm", "uninstall", packageName],
                TimeSpan.FromSeconds(60), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> ClearDataAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default,
        string? expectedBuildFingerprint = null)
        => MutateAsync(serial, packageName, PackageMutationKind.ClearData, expectedBuildFingerprint,
            () => _runner.RunForDeviceAsync(serial, ["shell", "pm", "clear", packageName],
                TimeSpan.FromMinutes(2), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> ClearCacheAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default,
        string? expectedBuildFingerprint = null)
        => MutateAsync(serial, packageName, PackageMutationKind.ClearCache, expectedBuildFingerprint,
            () => _runner.RunForDeviceAsync(serial, ["shell", "pm", "clear", "--cache-only", packageName],
                TimeSpan.FromMinutes(2), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> GrantPermissionAsync(
        string serial,
        string packageName,
        string permission,
        CancellationToken cancellationToken = default,
        string? expectedBuildFingerprint = null)
        => MutateAsync(serial, packageName, PackageMutationKind.GrantPermission, expectedBuildFingerprint,
            () => _runner.RunForDeviceAsync(serial, ["shell", "pm", "grant", packageName, permission],
                TimeSpan.FromSeconds(30), cancellationToken), cancellationToken);

    public Task<AdbCommandResult> RevokePermissionAsync(
        string serial,
        string packageName,
        string permission,
        CancellationToken cancellationToken = default,
        string? expectedBuildFingerprint = null)
        => MutateAsync(serial, packageName, PackageMutationKind.RevokePermission, expectedBuildFingerprint,
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
        string? expectedBuildFingerprint,
        Func<Task<AdbCommandResult>> operation,
        CancellationToken cancellationToken)
    {
        await _safetyGate.EnsureAllowedAsync(
            new PackageMutationRequest(serial, packageName, kind, expectedBuildFingerprint),
            cancellationToken);
        var result = await operation();
        if (!result.IsSuccess)
            return result;
        return await VerifyMutationAsync(serial, packageName, kind, result, cancellationToken);
    }

    private async Task<AdbCommandResult> VerifyMutationAsync(
        string serial,
        string packageName,
        PackageMutationKind kind,
        AdbCommandResult result,
        CancellationToken cancellationToken)
    {
        var expected = ExpectedState(kind);
        if (expected is null)
            return result;

        var query = expected == PackageReadbackState.Disabled
            || expected == PackageReadbackState.Enabled
            ? await _runner.RunForDeviceAsync(serial, ["shell", "pm", "list", "packages", "-d", "--user", "0"],
                TimeSpan.FromSeconds(30), cancellationToken)
            : await _runner.RunForDeviceAsync(serial, ["shell", "pm", "list", "packages", "--user", "0"],
                TimeSpan.FromSeconds(30), cancellationToken);
        if (!query.IsSuccess)
            return Failed(result, "Package state could not be verified after the mutation.");

        var names = PackageInventoryParser.ParsePackageNames(query.StandardOutput);
        var disabled = expected is PackageReadbackState.Disabled or PackageReadbackState.Enabled
            && names.Contains(packageName);
        var installed = expected is PackageReadbackState.Installed or PackageReadbackState.Missing
            && names.Contains(packageName);
        var matched = expected switch
        {
            PackageReadbackState.Disabled => disabled,
            PackageReadbackState.Enabled => !disabled,
            PackageReadbackState.Installed => installed,
            PackageReadbackState.Missing => !installed,
            _ => true
        };
        return matched
            ? result
            : Failed(result, expected == PackageReadbackState.Disabled
                ? "Package remained enabled after disable reported success."
                : expected == PackageReadbackState.Missing
                    ? "Package remained installed for the user after uninstall reported success."
                    : "Package state did not match the requested mutation.");
    }

    private static PackageReadbackState? ExpectedState(PackageMutationKind kind)
        => kind switch
        {
            PackageMutationKind.Disable => PackageReadbackState.Disabled,
            PackageMutationKind.Enable => PackageReadbackState.Enabled,
            PackageMutationKind.UninstallForUser => PackageReadbackState.Missing,
            PackageMutationKind.Restore => PackageReadbackState.Installed,
            _ => null
        };

    private static AdbCommandResult Failed(AdbCommandResult result, string message)
        => result with
        {
            ExitCode = result.ExitCode == 0 ? 1 : result.ExitCode,
            StandardError = string.IsNullOrWhiteSpace(result.StandardError)
                ? message
                : $"{result.StandardError}{Environment.NewLine}{message}"
        };

    private enum PackageReadbackState
    {
        Disabled,
        Enabled,
        Installed,
        Missing
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
