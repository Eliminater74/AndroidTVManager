using System.Threading.Channels;
using AndroidTVManager.Core.Abstractions;

namespace AndroidTVManager.Infrastructure.Logging;

public sealed class FileLogger : IAppLogger, ILogViewerService, IDisposable
{
    private readonly ILocalAppDataPaths _paths;
    private readonly Channel<string> _messages = Channel.CreateUnbounded<string>();
    private readonly CancellationTokenSource _stopSource = new();
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private readonly Task _writerTask;
    private long _fileGeneration;
    private bool _disposed;

    public FileLogger(ILocalAppDataPaths paths)
    {
        _paths = paths;
        _paths.EnsureCreated();
        RemoveOldLogs();
        _writerTask = Task.Run(() => WriteLoopAsync(_stopSource.Token));
    }

    public event EventHandler<string>? EntryWritten;
    public string LogDirectory => _paths.LogsPath;
    public string CurrentLogPath => Path.Combine(_paths.LogsPath, $"androidtvmanager-{DateTime.Now:yyyyMMdd}.log");

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
        _fileGate.Dispose();
    }

    private void Write(string level, string source, string message)
    {
        if (_disposed)
            return;
        var entry = $"{DateTimeOffset.Now:O} [{level}] [{source}] {message}";
        _messages.Writer.TryWrite(entry);
        try
        {
            EntryWritten?.Invoke(this, entry);
        }
        catch
        {
            // Logging must never fail because a viewer subscriber failed.
        }
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Now);
        StreamWriter? writer = null;
        var writerGeneration = -1L;
        try
        {
            await foreach (var message in _messages.Reader.ReadAllAsync(cancellationToken))
            {
                var date = DateOnly.FromDateTime(DateTime.Now);
                await _fileGate.WaitAsync(cancellationToken);
                try
                {
                    if (writer is null || date != currentDate
                        || writerGeneration != Volatile.Read(ref _fileGeneration))
                    {
                        if (writer is not null)
                            await writer.DisposeAsync();
                        currentDate = date;
                        writerGeneration = Volatile.Read(ref _fileGeneration);
                        writer = CreateWriter(date);
                    }
                    await writer.WriteLineAsync(message);
                    await writer.FlushAsync(cancellationToken);
                }
                finally
                {
                    _fileGate.Release();
                }
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
            FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete));

    public async Task<IReadOnlyList<string>> ReadCurrentAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        if (!File.Exists(CurrentLogPath))
            return [];
        await using var stream = new FileStream(CurrentLogPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
            lines.Add(line);
        return lines;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _fileGate.WaitAsync(cancellationToken);
        try
        {
            Interlocked.Increment(ref _fileGeneration);
            foreach (var file in Directory.EnumerateFiles(_paths.LogsPath, "androidtvmanager-*.log"))
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
        finally
        {
            _fileGate.Release();
        }
    }

    private void RemoveOldLogs()
    {
        foreach (var file in Directory.EnumerateFiles(_paths.LogsPath, "androidtvmanager-*.log")
                     .Where(file => File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14)))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }
}
