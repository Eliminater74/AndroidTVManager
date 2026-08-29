using AndroidTVManager.Core.Scripts;
using Microsoft.Data.Sqlite;

namespace AndroidTVManager.Infrastructure.Database;

public sealed class ScriptExecutionStore : IScriptExecutionStore
{
    private readonly SqliteDatabase _database;

    public ScriptExecutionStore(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<long> CreateAsync(
        string serial,
        string scriptName,
        string? scriptHash,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        var deviceId = await EnsureDeviceAsync(connection, serial, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ScriptExecutions
                (DeviceId, Serial, ScriptName, ScriptHash, StartedUtc, Status)
            VALUES ($deviceId, $serial, $name, $hash, $started, 'Running');
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$deviceId", deviceId);
        command.Parameters.AddWithValue("$serial", serial);
        command.Parameters.AddWithValue("$name", scriptName);
        command.Parameters.AddWithValue("$hash", (object?)scriptHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$started", DateTimeOffset.UtcNow.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<long> AddActionAsync(
        long executionId,
        ScriptActionRecord action,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ScriptExecutionActions
                (ExecutionId, ActionIndex, ActionType, Target, PreviousState,
                 RequestedState, ResultingState, Output, Success, Reversible, UndoStatus)
            VALUES ($executionId, $actionIndex, $actionType, $target, $previous,
                    $requested, $resulting, $output, $success, $reversible, $undoStatus);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$executionId", executionId);
        command.Parameters.AddWithValue("$actionIndex", action.ActionIndex);
        command.Parameters.AddWithValue("$actionType", action.ActionType);
        command.Parameters.AddWithValue("$target", (object?)action.Target ?? DBNull.Value);
        command.Parameters.AddWithValue("$previous", (object?)action.PreviousState ?? DBNull.Value);
        command.Parameters.AddWithValue("$requested", (object?)action.RequestedState ?? DBNull.Value);
        command.Parameters.AddWithValue("$resulting", (object?)action.ResultingState ?? DBNull.Value);
        command.Parameters.AddWithValue("$output", (object?)action.Output ?? DBNull.Value);
        command.Parameters.AddWithValue("$success", action.Success ? 1 : 0);
        command.Parameters.AddWithValue("$reversible", action.Reversible ? 1 : 0);
        command.Parameters.AddWithValue("$undoStatus", (object?)action.UndoStatus ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task CompleteAsync(long executionId, string status, CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ScriptExecutions SET EndedUtc = $ended, Status = $status WHERE Id = $id;";
        command.Parameters.AddWithValue("$ended", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$id", executionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateActionAsync(
        long actionId,
        bool success,
        bool reversible,
        string? resultingState,
        string? output,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ScriptExecutionActions
            SET Success = $success, Reversible = $reversible,
                ResultingState = $resulting, Output = $output
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$success", success ? 1 : 0);
        command.Parameters.AddWithValue("$reversible", reversible ? 1 : 0);
        command.Parameters.AddWithValue("$resulting", (object?)resultingState ?? DBNull.Value);
        command.Parameters.AddWithValue("$output", (object?)output ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", actionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ScriptExecutionRecord?> GetAsync(long executionId, CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, DeviceId, Serial, ScriptName, ScriptHash, StartedUtc, EndedUtc, Status
            FROM ScriptExecutions WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", executionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var execution = new ScriptExecutionRecord(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5)),
            reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
            reader.GetString(7),
            []);
        await reader.CloseAsync();

        await using var actionCommand = connection.CreateCommand();
        actionCommand.CommandText = """
            SELECT Id, ActionIndex, ActionType, Target, PreviousState, RequestedState,
                   ResultingState, Output, Success, Reversible, UndoStatus
            FROM ScriptExecutionActions WHERE ExecutionId = $id ORDER BY ActionIndex;
            """;
        actionCommand.Parameters.AddWithValue("$id", executionId);
        var actions = new List<ScriptActionRecord>();
        await using var actionReader = await actionCommand.ExecuteReaderAsync(cancellationToken);
        while (await actionReader.ReadAsync(cancellationToken))
        {
            actions.Add(new(
                actionReader.GetInt64(0),
                actionReader.GetInt32(1),
                actionReader.GetString(2),
                NullableString(actionReader, 3),
                NullableString(actionReader, 4),
                NullableString(actionReader, 5),
                NullableString(actionReader, 6),
                NullableString(actionReader, 7),
                actionReader.GetInt64(8) == 1,
                actionReader.GetInt64(9) == 1,
                NullableString(actionReader, 10)));
        }

        return execution with { Actions = actions };
    }

    public async Task SetUndoStatusAsync(long actionId, string status, CancellationToken cancellationToken = default)
        => await ExecuteUpdateAsync(
            "UPDATE ScriptExecutionActions SET UndoStatus = $status WHERE Id = $id;",
            actionId, status, cancellationToken);

    public async Task SetExecutionStatusAsync(long executionId, string status, CancellationToken cancellationToken = default)
        => await ExecuteUpdateAsync(
            "UPDATE ScriptExecutions SET Status = $status WHERE Id = $id;",
            executionId, status, cancellationToken);

    private async Task ExecuteUpdateAsync(string sql, long id, string status, CancellationToken cancellationToken)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<long> EnsureDeviceAsync(SqliteConnection connection, string serial, CancellationToken cancellationToken)
    {
        await using var find = connection.CreateCommand();
        find.CommandText = "SELECT Id FROM Devices WHERE LastKnownSerial = $serial;";
        find.Parameters.AddWithValue("$serial", serial);
        var existing = await find.ExecuteScalarAsync(cancellationToken);
        if (existing is not null)
            return Convert.ToInt64(existing);

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO Devices (FriendlyName, LastKnownSerial, FirstSeenUtc, CreatedUtc, UpdatedUtc)
            VALUES ($name, $serial, $now, $now, $now);
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
