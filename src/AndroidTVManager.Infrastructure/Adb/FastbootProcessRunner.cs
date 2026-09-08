using System.Diagnostics;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class FastbootProcessRunner(IAdbToolsManager tools) : IFastbootProcessRunner
{
    public async Task<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executable = tools.FastbootPath;
        if (string.IsNullOrWhiteSpace(executable))
            return new("fastboot.exe", [], -1, "", "Managed Fastboot is unavailable. Install Platform-Tools in Settings.", TimeSpan.Zero);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start()) throw new InvalidOperationException("Fastboot could not be started.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var timedOut = false;
        var canceled = false;
        try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            canceled = cancellationToken.IsCancellationRequested;
            timedOut = !canceled;
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        // Do not retain the local image path in command diagnostics.
        var diagnosticArguments = arguments.Select((arg, index) =>
            index > 0 && arguments[index - 1] == "recovery" ? "<recovery-image>" : arg).ToArray();
        return new("fastboot.exe", diagnosticArguments, process.ExitCode, await stdout, await stderr,
            stopwatch.Elapsed, WasCanceled: canceled, WasTimedOut: timedOut);
    }
}
