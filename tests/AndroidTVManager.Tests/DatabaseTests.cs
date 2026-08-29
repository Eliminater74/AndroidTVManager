using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public async Task Initializes_schema_and_persists_saved_device()
    {
        var paths = new TestPaths();
        try
        {
            var database = new SqliteDatabase(paths);
            await database.InitializeAsync();

            var repository = new DeviceRepository(database);
            var id = await repository.UpsertAsync(new SavedDevice
            {
                FriendlyName = "Living Room TV",
                LastKnownSerial = "192.168.1.50:5555",
                LastKnownEndpoint = "192.168.1.50:5555",
                IsFavorite = true
            });

            var saved = await repository.GetSavedDevicesAsync();
            database.SchemaVersion.Should().Be(2);
            id.Should().BeGreaterThan(0);
            saved.Should().ContainSingle(device => device.FriendlyName == "Living Room TV");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(paths.Root, recursive: true);
        }
    }

    [Fact]
    public async Task Records_connection_session_and_history()
    {
        var paths = new TestPaths();
        try
        {
            var database = new SqliteDatabase(paths);
            var history = new ConnectionHistoryRepository(database);
            var device = new AndroidDevice
            {
                Serial = "usb-123",
                Model = "Shield",
                State = DeviceState.Device,
                ConnectionType = ConnectionType.Usb
            };

            await history.RecordDeviceSeenAsync(device);
            var sessionId = await history.StartSessionAsync(device);
            await history.EndSessionAsync(sessionId, DeviceState.Disconnected, "test");

            (await history.GetRecentAsync()).Should().ContainSingle(item => item.Serial == "usb-123");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(paths.Root, recursive: true);
        }
    }

    [Fact]
    public async Task Synchronizes_sessions_without_duplicates_and_closes_missing_devices()
    {
        var paths = new TestPaths();
        try
        {
            var database = new SqliteDatabase(paths);
            var history = new ConnectionHistoryRepository(database);
            var device = new AndroidDevice
            {
                Serial = "tv-456",
                Model = "Google TV",
                State = DeviceState.Device,
                ConnectionType = ConnectionType.Network
            };

            await history.RecordDeviceSeenAsync(device);
            await history.SyncSessionsAsync([device], "35.0.2");
            await history.SyncSessionsAsync([device], "35.0.2");
            await history.SyncSessionsAsync([], "35.0.2");

            var sessions = await history.GetRecentAsync();
            sessions.Should().ContainSingle();
            sessions[0].EndedUtc.Should().NotBeNull();
            sessions[0].Duration.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(paths.Root, recursive: true);
        }
    }

    private sealed class TestPaths : ILocalAppDataPaths
    {
        public TestPaths()
        {
            Root = Path.Combine(Path.GetTempPath(), "AndroidTVManagerTests", Guid.NewGuid().ToString("N"));
        }

        public string Root { get; }
        public string DatabasePath => Path.Combine(Root, "Data", "test.db");
        public string ToolsPath => Path.Combine(Root, "Tools");
        public string LogsPath => Path.Combine(Root, "Logs");
        public string ScriptsPath => Path.Combine(Root, "Scripts");
        public string SnapshotsPath => Path.Combine(Root, "Snapshots");
        public string ScreenshotsPath => Path.Combine(Root, "Screenshots");
        public string RecordingsPath => Path.Combine(Root, "Recordings");
        public string TempPath => Path.Combine(Root, "Temp");

        public void EnsureCreated() => Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
    }
}
