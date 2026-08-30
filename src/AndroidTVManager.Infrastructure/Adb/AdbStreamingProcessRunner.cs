using System.Diagnostics;
using System.Threading.Channels;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class AdbStreamingProcessRunner : IAdbStreamingProcessRunner
{
    private readonly IAdbToolsManager _toolsManager;

    public AdbStreamingProcessRunner(IAdbToolsManager toolsManager)
    {
        _toolsManager = toolsManager;
    }

    public Task<IAdbProcessSession> StartForDeviceAsync(
        string serial,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var adbPath = _toolsManager.AdbPath
            ?? throw new InvalidOperationException("Managed Platform-Tools are not installed.");
        var process = new Process
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
        process.StartInfo.ArgumentList.Add("-s");
        process.StartInfo.ArgumentList.Add(serial.Trim());
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("ADB streaming process could not be started.");
        }
        return Task.FromResult<IAdbProcessSession>(new AdbProcessSession(process, arguments));
    }

    private sealed class AdbProcessSession : IAdbProcessSession
    {
        private readonly Process _process;
        private readonly IReadOnlyList<string> _arguments;
        private readonly Channel<string> _stdout = Channel.CreateUnbounded<string>();
        private readonly Channel<string> _stderr = Channel.CreateUnbounded<string>();
        private int _stopped;

        public AdbProcessSession(Process process, IReadOnlyList<string> arguments)
        {
            _process = process;
            _arguments = arguments;
            Completion = CompleteAsync();
        }

        public Task<AdbCommandResult> Completion { get; }

        public IAsyncEnumerable<string> ReadStandardOutputAsync(CancellationToken cancellationToken = default)
            => _stdout.Reader.ReadAllAsync(cancellationToken);

        public IAsyncEnumerable<string> ReadStandardErrorAsync(CancellationToken cancellationToken = default)
            => _stderr.Reader.ReadAllAsync(cancellationToken);

        public async Task StopAsync()
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 0)
            {
                try
                {
                    if (!_process.HasExited)
                        _process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            }
            await Completion.ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            _process.Dispose();
        }

        private async Task<AdbCommandResult> CompleteAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            var stdoutTask = PumpAsync(_process.StandardOutput, _stdout.Writer);
            var stderrTask = PumpAsync(_process.StandardError, _stderr.Writer);
            try
            {
                await _process.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
                return new(
                    Path.GetFileName(_process.StartInfo.FileName),
                    _arguments,
                    _process.ExitCode,
                    string.Empty,
                    string.Empty,
                    stopwatch.Elapsed,
                    WasCanceled: Volatile.Read(ref _stopped) == 1);
            }
            finally
            {
                _stdout.Writer.TryComplete();
                _stderr.Writer.TryComplete();
            }
        }

        private static async Task PumpAsync(
            StreamReader reader,
            ChannelWriter<string> writer)
        {
            try
            {
                while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                    await writer.WriteAsync(line).ConfigureAwait(false);
            }
            finally
            {
                writer.TryComplete();
            }
        }
    }
}
