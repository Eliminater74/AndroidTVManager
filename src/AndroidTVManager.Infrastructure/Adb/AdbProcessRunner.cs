using System.Diagnostics;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class AdbProcessRunner : IAdbProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly IAdbToolsManager _toolsManager;
    private readonly IAppLogger _logger;

    public AdbProcessRunner(IAdbToolsManager toolsManager, IAppLogger logger)
    {
        _toolsManager = toolsManager;
        _logger = logger;
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
        {
            _logger.Warning("ADB", "Managed Platform-Tools are not installed.");
            return new("adb.exe", arguments, -1, string.Empty, "Managed Platform-Tools are not installed.", TimeSpan.Zero);
        }

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
            var result = new AdbCommandResult(Path.GetFileName(adbPath), RedactArguments(arguments), -1,
                    await stdoutTask, await stderrTask, stopwatch.Elapsed, WasTimedOut: true);
                _logger.Warning("ADB", $"Command timed out: {result.CommandText}");
                return result;
            }

            var completed = new AdbCommandResult(Path.GetFileName(adbPath), RedactArguments(arguments), process.ExitCode,
                await stdoutTask, await stderrTask, stopwatch.Elapsed);
            if (!completed.IsSuccess)
                _logger.Warning("ADB", $"Command failed ({completed.ExitCode}): {completed.CommandText}");
            else
                _logger.Information("ADB", $"Command completed ({completed.ExitCode}): {completed.CommandText}");
            return completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            _logger.Information("ADB", $"Command canceled: {string.Join(' ', RedactArguments(arguments))}");
            return new(Path.GetFileName(adbPath), RedactArguments(arguments), -1,
                string.Empty, string.Empty, stopwatch.Elapsed, WasCanceled: true);
        }
        catch (Exception exception)
        {
            TryKill(process);
            _logger.Error("ADB", "Could not start ADB command.", exception);
            return new(Path.GetFileName(adbPath), RedactArguments(arguments), -1,
                string.Empty, exception.Message, stopwatch.Elapsed);
        }
    }

    private static IReadOnlyList<string> RedactArguments(IReadOnlyList<string> arguments)
    {
        var redacted = arguments.ToArray();
        var pairIndex = Array.FindIndex(redacted, value =>
            value.Equals("pair", StringComparison.OrdinalIgnoreCase));
        if (pairIndex >= 0 && pairIndex + 2 < redacted.Length)
            redacted[pairIndex + 2] = "<pairing-code-redacted>";
        var pullIndex = Array.FindIndex(redacted, value =>
            value.Equals("pull", StringComparison.OrdinalIgnoreCase));
        if (pullIndex >= 0 && pullIndex + 2 < redacted.Length)
            redacted[pullIndex + 2] = "<local-path-redacted>";
        return redacted;
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
