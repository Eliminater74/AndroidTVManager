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
            database.SchemaVersion.Should().Be(4);
            await database.InitializeAsync();
            await using var connection = await database.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DebloatPlans';";
            Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(1);
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

    [Fact]
    public async Task Persists_package_preferences_and_notes_per_device()
    {
        var paths = new TestPaths();
        try
        {
            var database = new SqliteDatabase(paths);
            var preferences = new PackagePreferenceRepository(database);

            await preferences.SetOverrideAsync("tv-pref", "com.example.app", PackageOverride.NeverSuggest, "Keep this.");
            await preferences.SetNoteAsync("tv-pref", "com.example.app", "Needed for the remote.");

            (await preferences.GetOverridesAsync("tv-pref"))["com.example.app"]
                .Should().Be(PackageOverride.NeverSuggest);
            (await preferences.GetNoteAsync("tv-pref", "com.example.app"))
                .Should().Be("Needed for the remote.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(paths.Root, recursive: true);
        }
    }

    [Fact]
    public async Task Saving_same_serial_updates_the_saved_device_instead_of_duplicating_it()
    {
        var paths = new TestPaths();
        try
        {
            var repository = new DeviceRepository(new SqliteDatabase(paths));
            await repository.UpsertAsync(new SavedDevice
            {
                FriendlyName = "First name",
                LastKnownSerial = "tv-same",
                LastKnownEndpoint = "tv-same"
            });
            await repository.UpsertAsync(new SavedDevice
            {
                FriendlyName = "Renamed TV",
                LastKnownSerial = "tv-same",
                LastKnownEndpoint = "tv-same"
            });

            var saved = await repository.GetSavedDevicesAsync();
            saved.Should().ContainSingle();
            saved[0].FriendlyName.Should().Be("Renamed TV");
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
