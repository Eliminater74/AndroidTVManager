using System.Threading.Channels;
using AndroidTVManager.Core.Abstractions;

namespace AndroidTVManager.Infrastructure.Logging;

public sealed class FileLogger : IAppLogger, IDisposable
{
    private readonly ILocalAppDataPaths _paths;
    private readonly Channel<string> _messages = Channel.CreateUnbounded<string>();
    private readonly CancellationTokenSource _stopSource = new();
    private readonly Task _writerTask;
    private bool _disposed;

    public FileLogger(ILocalAppDataPaths paths)
    {
        _paths = paths;
        _paths.EnsureCreated();
        RemoveOldLogs();
        _writerTask = Task.Run(() => WriteLoopAsync(_stopSource.Token));
    }

    public void Information(string source, string message) => Write("INFO", source, message);
    public void Warning(string source, string message) => Write("WARN", source, message);
    public void Error(string source, string message, Exception? exception = null)
        => Write("ERROR", source, exception is null ? message : $"{message} {exception}");

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _messages.Writer.TryComplete();
        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _stopSource.Cancel();
        _stopSource.Dispose();
    }

    private void Write(string level, string source, string message)
    {
        if (_disposed)
            return;
        _messages.Writer.TryWrite($"{DateTimeOffset.Now:O} [{level}] [{source}] {message}");
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Now);
        StreamWriter? writer = null;
        try
        {
            await foreach (var message in _messages.Reader.ReadAllAsync(cancellationToken))
            {
                var date = DateOnly.FromDateTime(DateTime.Now);
                if (writer is null || date != currentDate)
                {
                    if (writer is not null)
                        await writer.DisposeAsync();
                    currentDate = date;
                    writer = CreateWriter(date);
                }
                await writer.WriteLineAsync(message);
                await writer.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (writer is not null)
                await writer.DisposeAsync();
        }
    }

    private StreamWriter CreateWriter(DateOnly date)
        => new(File.Open(Path.Combine(_paths.LogsPath, $"androidtvmanager-{date:yyyyMMdd}.log"),
            FileMode.Append, FileAccess.Write, FileShare.Read));

    private void RemoveOldLogs()
    {
        foreach (var file in Directory.EnumerateFiles(_paths.LogsPath, "androidtvmanager-*.log")
                     .Where(file => File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14)))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }
}
