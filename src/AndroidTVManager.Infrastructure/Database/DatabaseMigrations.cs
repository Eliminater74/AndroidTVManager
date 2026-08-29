using Microsoft.Data.Sqlite;

namespace AndroidTVManager.Infrastructure.Database;

public static class DatabaseMigrations
{
    public const int CurrentVersion = 2;

    public static async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS SchemaVersions (
                Version INTEGER NOT NULL,
                AppliedUtc TEXT NOT NULL
            );
            """, cancellationToken);

        var version = await GetVersionAsync(connection, transaction, cancellationToken);
        if (version < 1)
        {
            await ExecuteAsync(connection, transaction, """
                CREATE TABLE Devices (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FriendlyName TEXT NOT NULL,
                    Manufacturer TEXT,
                    Model TEXT,
                    Product TEXT,
                    DeviceName TEXT,
                    LastKnownSerial TEXT,
                    LastKnownEndpoint TEXT,
                    AndroidVersion TEXT,
                    ApiLevel INTEGER,
                    BuildFingerprint TEXT,
                    FirstSeenUtc TEXT NOT NULL,
                    LastSeenUtc TEXT,
                    LastConnectedUtc TEXT,
                    LastDisconnectedUtc TEXT,
                    PreferredConnectionType INTEGER NOT NULL DEFAULT 0,
                    IsFavorite INTEGER NOT NULL DEFAULT 0,
                    IsSaved INTEGER NOT NULL DEFAULT 0,
                    Notes TEXT,
                    CreatedUtc TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IX_Devices_LastKnownSerial
                    ON Devices(LastKnownSerial) WHERE LastKnownSerial IS NOT NULL;
                CREATE INDEX IX_Devices_LastSeenUtc ON Devices(LastSeenUtc);

                CREATE TABLE ConnectionSessions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DeviceId INTEGER NOT NULL REFERENCES Devices(Id) ON DELETE CASCADE,
                    Serial TEXT NOT NULL,
                    Endpoint TEXT,
                    ConnectionType INTEGER NOT NULL,
                    StartedUtc TEXT NOT NULL,
                    EndedUtc TEXT,
                    FinalState INTEGER NOT NULL DEFAULT 0,
                    DisconnectReason TEXT
                );
                CREATE INDEX IX_ConnectionSessions_DeviceId ON ConnectionSessions(DeviceId);
                CREATE INDEX IX_ConnectionSessions_StartedUtc ON ConnectionSessions(StartedUtc);

                CREATE TABLE ConnectionEvents (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DeviceId INTEGER NOT NULL REFERENCES Devices(Id) ON DELETE CASCADE,
                    ConnectionSessionId INTEGER REFERENCES ConnectionSessions(Id) ON DELETE SET NULL,
                    EventType TEXT NOT NULL,
                    State INTEGER NOT NULL,
                    Message TEXT,
                    OccurredUtc TEXT NOT NULL
                );
                CREATE INDEX IX_ConnectionEvents_DeviceId_OccurredUtc
                    ON ConnectionEvents(DeviceId, OccurredUtc);

                CREATE TABLE PairingHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DeviceId INTEGER REFERENCES Devices(Id) ON DELETE SET NULL,
                    Endpoint TEXT NOT NULL,
                    OccurredUtc TEXT NOT NULL,
                    Result TEXT NOT NULL,
                    ErrorMessage TEXT
                );

                CREATE TABLE AppSettings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL
                );

                CREATE TABLE AdbToolInstallations (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Version TEXT,
                    InstalledUtc TEXT NOT NULL,
                    ActivePath TEXT,
                    Source TEXT,
                    LastUpdateCheckUtc TEXT,
                    Result TEXT
                );

                CREATE TABLE Scripts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    FilePath TEXT NOT NULL,
                    ScriptHash TEXT,
                    SchemaVersion INTEGER NOT NULL,
                    ImportedUtc TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL
                );

                CREATE TABLE ScriptExecutions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DeviceId INTEGER NOT NULL REFERENCES Devices(Id) ON DELETE CASCADE,
                    Serial TEXT NOT NULL,
                    ScriptName TEXT NOT NULL,
                    ScriptHash TEXT,
                    StartedUtc TEXT NOT NULL,
                    EndedUtc TEXT,
                    Status TEXT NOT NULL
                );
                CREATE INDEX IX_ScriptExecutions_StartedUtc ON ScriptExecutions(StartedUtc);

                CREATE TABLE ScriptExecutionActions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ExecutionId INTEGER NOT NULL REFERENCES ScriptExecutions(Id) ON DELETE CASCADE,
                    ActionIndex INTEGER NOT NULL,
                    ActionType TEXT NOT NULL,
                    Target TEXT,
                    PreviousState TEXT,
                    RequestedState TEXT,
                    ResultingState TEXT,
                    Output TEXT,
                    Success INTEGER NOT NULL,
                    Reversible INTEGER NOT NULL,
                    UndoStatus TEXT
                );

                CREATE TABLE DeviceSnapshots (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DeviceId INTEGER NOT NULL REFERENCES Devices(Id) ON DELETE CASCADE,
                    CapturedUtc TEXT NOT NULL,
                    AndroidVersion TEXT,
                    BuildFingerprint TEXT,
                    PayloadJson TEXT NOT NULL
                );
                """, cancellationToken);
            await ExecuteAsync(connection, transaction,
                "INSERT INTO SchemaVersions (Version, AppliedUtc) VALUES (1, $utc);",
                cancellationToken, ("$utc", DateTimeOffset.UtcNow.ToString("O")));
        }
        if (version < 2)
        {
            await ExecuteAsync(connection, transaction, """
                ALTER TABLE Devices ADD COLUMN Brand TEXT;
                ALTER TABLE Devices ADD COLUMN Board TEXT;
                ALTER TABLE Devices ADD COLUMN SecurityPatch TEXT;
                ALTER TABLE Devices ADD COLUMN BuildId TEXT;
                ALTER TABLE Devices ADD COLUMN BuildType TEXT;
                ALTER TABLE ConnectionSessions ADD COLUMN AdbVersion TEXT;
                """, cancellationToken);
            await ExecuteAsync(connection, transaction,
                "INSERT INTO SchemaVersions (Version, AppliedUtc) VALUES (2, $utc);",
                cancellationToken, ("$utc", DateTimeOffset.UtcNow.ToString("O")));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<int> GetVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersions;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
