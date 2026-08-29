using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace AndroidTVManager.Infrastructure.Database;

public sealed class DeviceRepository : IDeviceRepository
{
    private readonly SqliteDatabase _database;

    public DeviceRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<SavedDevice>> GetSavedDevicesAsync(CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, FriendlyName, Manufacturer, Model, LastKnownSerial,
                   LastKnownEndpoint, IsFavorite, Notes, LastSeenUtc,
                   LastConnectedUtc, LastDisconnectedUtc
            FROM Devices WHERE IsSaved = 1 ORDER BY IsFavorite DESC, FriendlyName;
            """;

        var devices = new List<SavedDevice>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            devices.Add(new SavedDevice
            {
                Id = reader.GetInt64(0),
                FriendlyName = reader.GetString(1),
                Manufacturer = reader.IsDBNull(2) ? null : reader.GetString(2),
                Model = reader.IsDBNull(3) ? null : reader.GetString(3),
                LastKnownSerial = reader.IsDBNull(4) ? null : reader.GetString(4),
                LastKnownEndpoint = reader.IsDBNull(5) ? null : reader.GetString(5),
                IsFavorite = reader.GetInt64(6) == 1,
                Notes = reader.IsDBNull(7) ? null : reader.GetString(7),
                LastSeenUtc = ParseDate(reader, 8),
                LastConnectedUtc = ParseDate(reader, 9),
                LastDisconnectedUtc = ParseDate(reader, 10)
            });
        }
        return devices;
    }

    public async Task<long> UpsertAsync(SavedDevice device, CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = device.Id == 0
            ? """
              INSERT INTO Devices (FriendlyName, Manufacturer, Model, LastKnownSerial,
                  LastKnownEndpoint, IsFavorite, IsSaved, Notes, FirstSeenUtc, CreatedUtc, UpdatedUtc)
              VALUES ($name, $manufacturer, $model, $serial, $endpoint, $favorite, 1, $notes,
                  $now, $now, $now);
              SELECT last_insert_rowid();
              """
            : """
              UPDATE Devices SET FriendlyName = $name, Manufacturer = $manufacturer,
                  Model = $model, LastKnownSerial = $serial, LastKnownEndpoint = $endpoint,
                  IsFavorite = $favorite, IsSaved = 1, Notes = $notes, UpdatedUtc = $now
              WHERE Id = $id;
              SELECT $id;
              """;
        AddParameters(command, device);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Devices SET IsSaved = 0, UpdatedUtc = $now WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearConnectionHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var sql in new[] { "DELETE FROM ConnectionEvents;", "DELETE FROM ConnectionSessions;" })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static void AddParameters(SqliteCommand command, SavedDevice device)
    {
        command.Parameters.AddWithValue("$id", device.Id);
        command.Parameters.AddWithValue("$name", device.FriendlyName);
        command.Parameters.AddWithValue("$manufacturer", (object?)device.Manufacturer ?? DBNull.Value);
        command.Parameters.AddWithValue("$model", (object?)device.Model ?? DBNull.Value);
        command.Parameters.AddWithValue("$serial", (object?)device.LastKnownSerial ?? DBNull.Value);
        command.Parameters.AddWithValue("$endpoint", (object?)device.LastKnownEndpoint ?? DBNull.Value);
        command.Parameters.AddWithValue("$favorite", device.IsFavorite ? 1 : 0);
        command.Parameters.AddWithValue("$notes", (object?)device.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
    }

    private static DateTimeOffset? ParseDate(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : DateTimeOffset.Parse(reader.GetString(ordinal));
}
