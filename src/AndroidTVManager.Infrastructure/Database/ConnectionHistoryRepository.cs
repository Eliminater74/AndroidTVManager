using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace AndroidTVManager.Infrastructure.Database;

public sealed class ConnectionHistoryRepository : IConnectionHistoryRepository
{
    private readonly SqliteDatabase _database;

    public ConnectionHistoryRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task RecordDeviceSeenAsync(AndroidDevice device, CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        var deviceId = await EnsureDeviceAsync(connection, device, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT State FROM ConnectionEvents
            WHERE DeviceId = $deviceId ORDER BY OccurredUtc DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$deviceId", deviceId);
        var previous = await command.ExecuteScalarAsync(cancellationToken);
        if (previous is not null && Convert.ToInt32(previous) == (int)device.State)
            return;

        command.CommandText = """
            INSERT INTO ConnectionEvents
                (DeviceId, EventType, State, Message, OccurredUtc)
            VALUES ($deviceId, 'state-change', $state, $message, $utc);
            """;
        command.Parameters.AddWithValue("$state", (int)device.State);
        command.Parameters.AddWithValue("$message", $"{device.Serial} is {device.State}");
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SyncSessionsAsync(
        IReadOnlyList<AndroidDevice> devices,
        string? adbVersion,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow.ToString("O");
        var openSessions = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        await using (var openCommand = connection.CreateCommand())
        {
            openCommand.Transaction = transaction;
            openCommand.CommandText = "SELECT Id, Serial FROM ConnectionSessions WHERE EndedUtc IS NULL;";
            await using var reader = await openCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                openSessions[reader.GetString(1)] = reader.GetInt64(0);
        }

        var connectedSerials = devices
            .Where(device => device.State == DeviceState.Device)
            .Select(device => device.Serial)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var session in openSessions.Where(session => !connectedSerials.Contains(session.Key)))
        {
            await using var close = connection.CreateCommand();
            close.Transaction = transaction;
            close.CommandText = """
                UPDATE ConnectionSessions
                SET EndedUtc = $ended, FinalState = $state,
                    DisconnectReason = 'Device no longer reported by ADB'
                WHERE Id = $id;
                """;
            close.Parameters.AddWithValue("$ended", now);
            close.Parameters.AddWithValue("$state", (int)DeviceState.Disconnected);
            close.Parameters.AddWithValue("$id", session.Value);
            await close.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var device in devices.Where(device => device.State == DeviceState.Device))
        {
            if (openSessions.ContainsKey(device.Serial))
                continue;

            var deviceId = await EnsureDeviceAsync(connection, device, cancellationToken, transaction);
            await using var start = connection.CreateCommand();
            start.Transaction = transaction;
            start.CommandText = """
                INSERT INTO ConnectionSessions
                    (DeviceId, Serial, Endpoint, ConnectionType, StartedUtc, FinalState, AdbVersion)
                VALUES ($deviceId, $serial, $endpoint, $type, $started, $state, $adbVersion);
                """;
            start.Parameters.AddWithValue("$deviceId", deviceId);
            start.Parameters.AddWithValue("$serial", device.Serial);
            start.Parameters.AddWithValue("$endpoint", (object?)device.Endpoint ?? DBNull.Value);
            start.Parameters.AddWithValue("$type", (int)device.ConnectionType);
            start.Parameters.AddWithValue("$started", now);
            start.Parameters.AddWithValue("$state", (int)device.State);
            start.Parameters.AddWithValue("$adbVersion", (object?)adbVersion ?? DBNull.Value);
            await start.ExecuteNonQueryAsync(cancellationToken);

            await using var connected = connection.CreateCommand();
            connected.Transaction = transaction;
            connected.CommandText = "UPDATE Devices SET LastConnectedUtc = $now, UpdatedUtc = $now WHERE Id = $id;";
            connected.Parameters.AddWithValue("$now", now);
            connected.Parameters.AddWithValue("$id", deviceId);
            await connected.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecoverOpenSessionsAsync(CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ConnectionSessions
            SET EndedUtc = $ended, FinalState = $state,
                DisconnectReason = 'Application terminated before session closure'
            WHERE EndedUtc IS NULL;
            """;
        command.Parameters.AddWithValue("$ended", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$state", (int)DeviceState.Disconnected);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> StartSessionAsync(AndroidDevice device, CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        var deviceId = await EnsureDeviceAsync(connection, device, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ConnectionSessions
                (DeviceId, Serial, Endpoint, ConnectionType, StartedUtc, FinalState)
            VALUES ($deviceId, $serial, $endpoint, $type, $started, $state);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$deviceId", deviceId);
        command.Parameters.AddWithValue("$serial", device.Serial);
        command.Parameters.AddWithValue("$endpoint", (object?)device.Endpoint ?? DBNull.Value);
        command.Parameters.AddWithValue("$type", (int)device.ConnectionType);
        command.Parameters.AddWithValue("$started", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$state", (int)device.State);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task EndSessionAsync(long sessionId, DeviceState finalState, string? reason, CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ConnectionSessions
            SET EndedUtc = $ended, FinalState = $state, DisconnectReason = $reason
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$ended", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$state", (int)finalState);
        command.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConnectionHistoryItem>> GetRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.FriendlyName, d.Manufacturer, d.Model, s.Serial, s.Endpoint,
                   s.ConnectionType, s.FinalState, d.LastSeenUtc,
                   d.LastConnectedUtc, d.LastDisconnectedUtc, s.StartedUtc, s.EndedUtc
            FROM ConnectionSessions s
            INNER JOIN Devices d ON d.Id = s.DeviceId
            ORDER BY s.StartedUtc DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));

        var result = new List<ConnectionHistoryItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ConnectionHistoryItem(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                (ConnectionType)reader.GetInt32(5),
                (DeviceState)reader.GetInt32(6),
                ParseDate(reader, 7),
                ParseDate(reader, 8),
                ParseDate(reader, 9),
                DateTimeOffset.Parse(reader.GetString(10)),
                ParseDate(reader, 11)));
        }
        return result;
    }

    private static async Task<long> EnsureDeviceAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        AndroidDevice device,
        CancellationToken cancellationToken,
        Microsoft.Data.Sqlite.SqliteTransaction? transaction = null)
    {
        await using var find = connection.CreateCommand();
        find.Transaction = transaction;
        find.CommandText = "SELECT Id FROM Devices WHERE LastKnownSerial = $serial;";
        find.Parameters.AddWithValue("$serial", device.Serial);
        var existing = await find.ExecuteScalarAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (existing is null)
        {
            command.CommandText = """
                INSERT INTO Devices (FriendlyName, Manufacturer, Brand, Model, Product, DeviceName,
                    Board, AndroidVersion, ApiLevel, SecurityPatch, BuildId, BuildType, BuildFingerprint,
                    LastKnownSerial, LastKnownEndpoint,
                    FirstSeenUtc, LastSeenUtc, CreatedUtc, UpdatedUtc)
                VALUES ($name, $manufacturer, $brand, $model, $product, $deviceName,
                    $board, $android, $api, $securityPatch, $buildId, $buildType, $fingerprint,
                    $serial, $endpoint, $now, $now, $now, $now);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$name", device.Model ?? device.Serial);
        }
        else
        {
            command.CommandText = """
                UPDATE Devices SET Manufacturer = $manufacturer, Brand = $brand, Model = $model,
                    Product = $product, DeviceName = $deviceName, Board = $board,
                    SecurityPatch = $securityPatch, BuildId = $buildId, BuildType = $buildType,
                    LastKnownEndpoint = $endpoint,
                    AndroidVersion = COALESCE($android, AndroidVersion),
                    ApiLevel = COALESCE($api, ApiLevel),
                    BuildFingerprint = COALESCE($fingerprint, BuildFingerprint),
                    LastSeenUtc = $now, UpdatedUtc = $now
                WHERE Id = $id;
                SELECT $id;
                """;
            command.Parameters.AddWithValue("$id", existing);
        }

        command.Parameters.AddWithValue("$manufacturer", (object?)device.Manufacturer ?? DBNull.Value);
        command.Parameters.AddWithValue("$brand", (object?)device.Brand ?? DBNull.Value);
        command.Parameters.AddWithValue("$model", (object?)device.Model ?? DBNull.Value);
        command.Parameters.AddWithValue("$product", (object?)device.Product ?? DBNull.Value);
        command.Parameters.AddWithValue("$deviceName", (object?)device.DeviceName ?? DBNull.Value);
        command.Parameters.AddWithValue("$board", (object?)device.Board ?? DBNull.Value);
        command.Parameters.AddWithValue("$serial", device.Serial);
        command.Parameters.AddWithValue("$endpoint", (object?)device.Endpoint ?? DBNull.Value);
        command.Parameters.AddWithValue("$android", (object?)device.AndroidVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$api", (object?)device.ApiLevel ?? DBNull.Value);
        command.Parameters.AddWithValue("$securityPatch", (object?)device.SecurityPatch ?? DBNull.Value);
        command.Parameters.AddWithValue("$buildId", (object?)device.BuildId ?? DBNull.Value);
        command.Parameters.AddWithValue("$buildType", (object?)device.BuildType ?? DBNull.Value);
        command.Parameters.AddWithValue("$fingerprint", (object?)device.BuildFingerprint ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static DateTimeOffset? ParseDate(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : DateTimeOffset.Parse(reader.GetString(ordinal));
}
