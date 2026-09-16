using AndroidTVManager.App.Services;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class ApplicationShutdownCoordinatorTests
{
    [Fact]
    public async Task Stops_the_tracker_recovers_sessions_then_flushes_logs()
    {
        var order = new List<string>();
        var coordinator = new ApplicationShutdownCoordinator(
            new FakeTracker(order),
            new FakeHistory(order),
            new FakeLogs(order));

        await coordinator.ShutdownAsync();

        order.Should().Equal("stop", "recover", "flush");
    }

    private sealed class FakeTracker(List<string> order) : IAdbDeviceTracker
    {
        public event EventHandler<IReadOnlyList<AndroidDevice>>? DevicesChanged
        {
            add { }
            remove { }
        }
        public IReadOnlyList<AndroidDevice> CurrentDevices { get; } = [];
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            order.Add("stop");
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeHistory(List<string> order) : IConnectionHistoryRepository
    {
        public Task RecordDeviceSeenAsync(AndroidDevice device, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task SyncSessionsAsync(IReadOnlyList<AndroidDevice> devices, string? adbVersion, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task RecoverOpenSessionsAsync(CancellationToken cancellationToken = default)
        {
            order.Add("recover");
            return Task.CompletedTask;
        }
        public Task<long> StartSessionAsync(AndroidDevice device, CancellationToken cancellationToken = default) => Task.FromResult(1L);
        public Task EndSessionAsync(long sessionId, DeviceState finalState, string? reason, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<ConnectionHistoryItem>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ConnectionHistoryItem>>([]);
    }

    private sealed class FakeLogs(List<string> order) : ILogViewerService
    {
        public event EventHandler<string>? EntryWritten
        {
            add { }
            remove { }
        }
        public string LogDirectory => "";
        public string CurrentLogPath => "";
        public Task<IReadOnlyList<string>> ReadCurrentAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            order.Add("flush");
            return Task.CompletedTask;
        }
    }
}
