using AndroidTVManager.Core.Abstractions;

namespace AndroidTVManager.App.Services;

public sealed class ApplicationShutdownCoordinator(
    IAdbDeviceTracker tracker,
    IConnectionHistoryRepository history,
    ILogViewerService logs)
{
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await tracker.StopAsync();
        cancellationToken.ThrowIfCancellationRequested();
        await history.RecoverOpenSessionsAsync(cancellationToken);
        await logs.FlushAsync(cancellationToken);
    }
}
