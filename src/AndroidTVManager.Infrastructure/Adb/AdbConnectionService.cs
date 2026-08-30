using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class AdbConnectionService : IAdbConnectionService
{
    private readonly IAdbProcessRunner _runner;

    public AdbConnectionService(IAdbProcessRunner runner)
    {
        _runner = runner;
    }

    public Task<AdbCommandResult> ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
        => _runner.RunAsync(["connect", endpoint], TimeSpan.FromSeconds(15), cancellationToken);

    public Task<AdbCommandResult> DisconnectAsync(string endpoint, CancellationToken cancellationToken = default)
        => _runner.RunAsync(["disconnect", endpoint], TimeSpan.FromSeconds(15), cancellationToken);

    public Task<AdbCommandResult> PairAsync(
        string endpoint,
        string pairingCode,
        CancellationToken cancellationToken = default)
        => _runner.RunAsync(["pair", endpoint, pairingCode], TimeSpan.FromSeconds(30), cancellationToken);
}

public sealed class ApkInstaller : IApkInstaller
{
    private readonly IAdbProcessRunner _runner;

    public ApkInstaller(IAdbProcessRunner runner)
    {
        _runner = runner;
    }

    public Task<AdbCommandResult> InstallAsync(
        string serial,
        string apkPath,
        bool reinstall = true,
        CancellationToken cancellationToken = default)
    {
        var arguments = reinstall ? new[] { "install", "-r", apkPath } : new[] { "install", apkPath };
        return _runner.RunForDeviceAsync(serial, arguments, TimeSpan.FromMinutes(10), cancellationToken);
    }

    public Task<AdbCommandResult> InstallMultipleAsync(
        string serial,
        IReadOnlyList<string> apkPaths,
        bool reinstall = true,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "install-multiple" };
        if (reinstall)
            arguments.Add("-r");
        arguments.AddRange(apkPaths);
        return _runner.RunForDeviceAsync(
            serial,
            arguments,
            TimeSpan.FromMinutes(10),
            cancellationToken);
    }
}
