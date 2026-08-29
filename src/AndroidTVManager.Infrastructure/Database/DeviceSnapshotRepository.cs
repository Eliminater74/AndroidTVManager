using System.Text.Json;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Database;

public sealed class DeviceSnapshotRepository : IDeviceSnapshotRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SqliteDatabase _database;

    public DeviceSnapshotRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<long> SaveAsync(DeviceInspectionResult inspection, CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        var deviceId = await EnsureDeviceAsync(connection, inspection.Overview.Value, cancellationToken);
        var payload = JsonSerializer.Serialize(inspection, JsonOptions);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DeviceSnapshots
                (DeviceId, CapturedUtc, AndroidVersion, BuildFingerprint, PayloadJson)
            VALUES ($deviceId, $captured, $android, $fingerprint, $payload);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$deviceId", deviceId);
        command.Parameters.AddWithValue("$captured", inspection.CapturedUtc.ToString("O"));
        command.Parameters.AddWithValue("$android", (object?)inspection.Overview.Value?.AndroidVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$fingerprint", (object?)inspection.Overview.Value?.BuildFingerprint ?? DBNull.Value);
        command.Parameters.AddWithValue("$payload", payload);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<DeviceInspectionResult?> GetLatestAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.PayloadJson
            FROM DeviceSnapshots s
            INNER JOIN Devices d ON d.Id = s.DeviceId
            WHERE d.LastKnownSerial = $serial
            ORDER BY s.CapturedUtc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$serial", serial);
        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return payload is null ? null : JsonSerializer.Deserialize<DeviceInspectionResult>(payload, JsonOptions);
    }

    private static async Task<long> EnsureDeviceAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        AndroidDevice? device,
        CancellationToken cancellationToken)
    {
        if (device is null)
            throw new InvalidOperationException("An inspection must include an overview device.");
        await using var find = connection.CreateCommand();
        find.CommandText = "SELECT Id FROM Devices WHERE LastKnownSerial = $serial;";
        find.Parameters.AddWithValue("$serial", device.Serial);
        var existing = await find.ExecuteScalarAsync(cancellationToken);
        if (existing is not null)
            return Convert.ToInt64(existing);

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO Devices
                (FriendlyName, Manufacturer, Brand, Model, Product, DeviceName, Board,
                 AndroidVersion, ApiLevel, BuildFingerprint, LastKnownSerial,
                 LastKnownEndpoint, FirstSeenUtc, LastSeenUtc, CreatedUtc, UpdatedUtc)
            VALUES ($name, $manufacturer, $brand, $model, $product, $deviceName, $board,
                    $android, $api, $fingerprint, $serial, $endpoint, $now, $now, $now, $now);
            SELECT last_insert_rowid();
            """;
        var now = DateTimeOffset.UtcNow.ToString("O");
        insert.Parameters.AddWithValue("$name", device.Model ?? device.Serial);
        insert.Parameters.AddWithValue("$manufacturer", (object?)device.Manufacturer ?? DBNull.Value);
        insert.Parameters.AddWithValue("$brand", (object?)device.Brand ?? DBNull.Value);
        insert.Parameters.AddWithValue("$model", (object?)device.Model ?? DBNull.Value);
        insert.Parameters.AddWithValue("$product", (object?)device.Product ?? DBNull.Value);
        insert.Parameters.AddWithValue("$deviceName", (object?)device.DeviceName ?? DBNull.Value);
        insert.Parameters.AddWithValue("$board", (object?)device.Board ?? DBNull.Value);
        insert.Parameters.AddWithValue("$android", (object?)device.AndroidVersion ?? DBNull.Value);
        insert.Parameters.AddWithValue("$api", (object?)device.ApiLevel ?? DBNull.Value);
        insert.Parameters.AddWithValue("$fingerprint", (object?)device.BuildFingerprint ?? DBNull.Value);
        insert.Parameters.AddWithValue("$serial", device.Serial);
        insert.Parameters.AddWithValue("$endpoint", (object?)device.Endpoint ?? DBNull.Value);
        insert.Parameters.AddWithValue("$now", now);
        return Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
    }
}
