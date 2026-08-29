using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace AndroidTVManager.Infrastructure.Database;

public sealed class PackagePreferenceRepository : IPackagePreferenceRepository
{
    private readonly SqliteDatabase _database;

    public PackagePreferenceRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyDictionary<string, PackageOverride>> GetOverridesAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.PackageName, o.Override
            FROM PackageKnowledgeOverrides o
            INNER JOIN Devices d ON d.Id = o.DeviceId
            WHERE d.LastKnownSerial = $serial;
            """;
        command.Parameters.AddWithValue("$serial", serial);
        var values = new Dictionary<string, PackageOverride>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (Enum.TryParse<PackageOverride>(reader.GetString(1), true, out var value))
                values[reader.GetString(0)] = value;
        return values;
    }

    public async Task SetOverrideAsync(
        string serial,
        string packageName,
        PackageOverride value,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var deviceId = await EnsureDeviceAsync(connection, transaction, serial, cancellationToken);
        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM PackageKnowledgeOverrides WHERE DeviceId = $device AND PackageName = $package;";
        delete.Parameters.AddWithValue("$device", deviceId);
        delete.Parameters.AddWithValue("$package", packageName);
        await delete.ExecuteNonQueryAsync(cancellationToken);
        if (value != PackageOverride.None)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO PackageKnowledgeOverrides
                    (DeviceId, PackageName, Override, Note, UpdatedUtc)
                VALUES ($device, $package, $override, $note, $updated);
                """;
            insert.Parameters.AddWithValue("$device", deviceId);
            insert.Parameters.AddWithValue("$package", packageName);
            insert.Parameters.AddWithValue("$override", value.ToString());
            insert.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
            insert.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<string?> GetNoteAsync(string serial, string packageName, CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT n.Note
            FROM PackageNotes n
            INNER JOIN Devices d ON d.Id = n.DeviceId
            WHERE d.LastKnownSerial = $serial AND n.PackageName = $package
            ORDER BY n.UpdatedUtc DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$serial", serial);
        command.Parameters.AddWithValue("$package", packageName);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task SetNoteAsync(string serial, string packageName, string note, CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        var deviceId = await EnsureDeviceAsync(connection, null, serial, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PackageNotes (DeviceId, PackageName, Note, UpdatedUtc)
            VALUES ($device, $package, $note, $updated);
            """;
        command.Parameters.AddWithValue("$device", deviceId);
        command.Parameters.AddWithValue("$package", packageName);
        command.Parameters.AddWithValue("$note", note);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> EnsureDeviceAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string serial,
        CancellationToken cancellationToken)
    {
        await using var find = connection.CreateCommand();
        find.Transaction = transaction;
        find.CommandText = "SELECT Id FROM Devices WHERE LastKnownSerial = $serial;";
        find.Parameters.AddWithValue("$serial", serial);
        var existing = await find.ExecuteScalarAsync(cancellationToken);
        if (existing is not null)
            return Convert.ToInt64(existing);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO Devices
                (FriendlyName, LastKnownSerial, FirstSeenUtc, LastSeenUtc, CreatedUtc, UpdatedUtc)
            VALUES ($name, $serial, $now, $now, $now, $now);
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$name", serial);
        insert.Parameters.AddWithValue("$serial", serial);
        insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        return Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
    }
}
