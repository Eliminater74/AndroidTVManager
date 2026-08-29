using System.Diagnostics;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class AdbProcessRunner : IAdbProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly IAdbToolsManager _toolsManager;

    public AdbProcessRunner(IAdbToolsManager toolsManager)
    {
        _toolsManager = toolsManager;
    }

    public Task<AdbCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => RunCoreAsync(arguments, timeout ?? DefaultTimeout, cancellationToken);

    public Task<AdbCommandResult> RunForDeviceAsync(
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var deviceArguments = new[] { "-s", serial }.Concat(arguments).ToArray();
        return RunCoreAsync(deviceArguments, timeout ?? DefaultTimeout, cancellationToken);
    }

    private async Task<AdbCommandResult> RunCoreAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var adbPath = _toolsManager.AdbPath;
        if (string.IsNullOrWhiteSpace(adbPath))
            return new("adb.exe", arguments, -1, string.Empty, "Managed Platform-Tools are not installed.", TimeSpan.Zero);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = adbPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(adbPath) ?? Environment.CurrentDirectory
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("ADB process could not be started.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                await process.WaitForExitAsync(CancellationToken.None);
                return new(Path.GetFileName(adbPath), arguments, -1,
                    await stdoutTask, await stderrTask, stopwatch.Elapsed, WasTimedOut: true);
            }

            return new(Path.GetFileName(adbPath), arguments, process.ExitCode,
                await stdoutTask, await stderrTask, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new(Path.GetFileName(adbPath), arguments, -1,
                string.Empty, string.Empty, stopwatch.Elapsed, WasCanceled: true);
        }
        catch (Exception exception)
        {
            TryKill(process);
            return new(Path.GetFileName(adbPath), arguments, -1,
                string.Empty, exception.Message, stopwatch.Elapsed);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
