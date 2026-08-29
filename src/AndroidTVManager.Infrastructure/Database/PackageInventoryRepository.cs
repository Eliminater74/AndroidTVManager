using System.Text.Json;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace AndroidTVManager.Infrastructure.Database;

public sealed class PackageInventoryRepository : IPackageInventoryRepository
{
    private readonly SqliteDatabase _database;

    public PackageInventoryRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<long> SaveAsync(
        PackageInventoryResult inventory,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var deviceId = await EnsureDeviceAsync(connection, transaction, inventory.Serial, cancellationToken);
        await using var capture = connection.CreateCommand();
        capture.Transaction = transaction;
        capture.CommandText = """
            INSERT INTO PackageInventoryCaptures
                (DeviceId, Serial, CapturedUtc, ErrorMessage)
            VALUES ($deviceId, $serial, $captured, $error);
            SELECT last_insert_rowid();
            """;
        capture.Parameters.AddWithValue("$deviceId", deviceId);
        capture.Parameters.AddWithValue("$serial", inventory.Serial);
        capture.Parameters.AddWithValue("$captured", inventory.CapturedUtc.ToString("O"));
        capture.Parameters.AddWithValue("$error", (object?)inventory.ErrorMessage ?? DBNull.Value);
        var captureId = Convert.ToInt64(await capture.ExecuteScalarAsync(cancellationToken));

        foreach (var package in inventory.Packages)
        {
            await using var item = connection.CreateCommand();
            item.Transaction = transaction;
            item.CommandText = """
                INSERT INTO PackageInventoryItems
                    (CaptureId, PackageName, Label, VersionName, VersionCode, UserId,
                     IsSystem, IsUpdatedSystem, IsEnabled, IsInstalled, IsUninstalledForUser,
                     ApkPathsJson, IsActiveLauncher, IsDefaultInputMethod,
                     IsEnabledAccessibilityService, IsDeviceOwner)
                VALUES ($captureId, $name, $label, $versionName, $versionCode, $userId,
                        $system, $updated, $enabled, $installed, $uninstalled,
                        $paths, $launcher, $input, $accessibility, $owner);
                """;
            item.Parameters.AddWithValue("$captureId", captureId);
            item.Parameters.AddWithValue("$name", package.PackageName);
            item.Parameters.AddWithValue("$label", (object?)package.Label ?? DBNull.Value);
            item.Parameters.AddWithValue("$versionName", (object?)package.VersionName ?? DBNull.Value);
            item.Parameters.AddWithValue("$versionCode", (object?)package.VersionCode ?? DBNull.Value);
            item.Parameters.AddWithValue("$userId", (object?)package.UserId ?? DBNull.Value);
            item.Parameters.AddWithValue("$system", package.IsSystem ? 1 : 0);
            item.Parameters.AddWithValue("$updated", package.IsUpdatedSystem ? 1 : 0);
            item.Parameters.AddWithValue("$enabled", package.IsEnabled ? 1 : 0);
            item.Parameters.AddWithValue("$installed", package.IsInstalled ? 1 : 0);
            item.Parameters.AddWithValue("$uninstalled", package.IsUninstalledForUser ? 1 : 0);
            item.Parameters.AddWithValue("$paths", JsonSerializer.Serialize(package.ApkPaths));
            item.Parameters.AddWithValue("$launcher", package.IsActiveLauncher ? 1 : 0);
            item.Parameters.AddWithValue("$input", package.IsDefaultInputMethod ? 1 : 0);
            item.Parameters.AddWithValue("$accessibility", package.IsEnabledAccessibilityService ? 1 : 0);
            item.Parameters.AddWithValue("$owner", package.IsDeviceOwner ? 1 : 0);
            await item.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return captureId;
    }

    public async Task<PackageInventoryResult?> GetLatestAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var capture = connection.CreateCommand();
        capture.CommandText = """
            SELECT Id, CapturedUtc, ErrorMessage
            FROM PackageInventoryCaptures
            WHERE Serial = $serial
            ORDER BY CapturedUtc DESC LIMIT 1;
            """;
        capture.Parameters.AddWithValue("$serial", serial);
        await using var reader = await capture.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var captureId = reader.GetInt64(0);
        var captured = DateTimeOffset.Parse(reader.GetString(1));
        var error = reader.IsDBNull(2) ? null : reader.GetString(2);
        await reader.CloseAsync();

        await using var items = connection.CreateCommand();
        items.CommandText = """
            SELECT PackageName, Label, VersionName, VersionCode, UserId,
                   IsSystem, IsUpdatedSystem, IsEnabled, IsInstalled, IsUninstalledForUser,
                   ApkPathsJson, IsActiveLauncher, IsDefaultInputMethod,
                   IsEnabledAccessibilityService, IsDeviceOwner
            FROM PackageInventoryItems WHERE CaptureId = $captureId ORDER BY PackageName;
            """;
        items.Parameters.AddWithValue("$captureId", captureId);
        var packages = new List<PackageInventoryEntry>();
        await using var itemReader = await items.ExecuteReaderAsync(cancellationToken);
        while (await itemReader.ReadAsync(cancellationToken))
        {
            packages.Add(new(
                itemReader.GetString(0),
                NullableString(itemReader, 1),
                NullableString(itemReader, 2),
                itemReader.IsDBNull(3) ? null : itemReader.GetInt64(3),
                NullableString(itemReader, 4),
                itemReader.GetInt64(5) == 1,
                itemReader.GetInt64(6) == 1,
                itemReader.GetInt64(7) == 1,
                itemReader.GetInt64(8) == 1,
                itemReader.GetInt64(9) == 1,
                JsonSerializer.Deserialize<string[]>(itemReader.GetString(10)) ?? [],
                captured,
                serial,
                null,
                null,
                itemReader.GetInt64(11) == 1,
                itemReader.GetInt64(12) == 1,
                itemReader.GetInt64(13) == 1,
                itemReader.GetInt64(14) == 1));
        }
        return new(serial, captured, packages, [], error);
    }

    private static async Task<long> EnsureDeviceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
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

    private static string? NullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
