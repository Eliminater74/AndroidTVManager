using Microsoft.Data.Sqlite;
using AndroidTVManager.Core.Abstractions;

namespace AndroidTVManager.Infrastructure.Database;

public sealed class SqliteDatabase
{
    public const string PreMigrationBackupPattern = "*.pre-migrate.bak";
    public const int RetainedPreMigrationBackups = 2;
    private readonly ILocalAppDataPaths _paths;
    private readonly SemaphoreSlim _migrationLock = new(1, 1);
    private bool _initialized;

    public SqliteDatabase(ILocalAppDataPaths paths)
    {
        _paths = paths;
    }

    public int SchemaVersion => DatabaseMigrations.CurrentVersion;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        await _migrationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;

            _paths.EnsureCreated();
            if (File.Exists(_paths.DatabasePath))
                await BackupBeforeMigrationAsync(cancellationToken);

            await using var connection = await OpenAsync(cancellationToken);
            await EnsureIntegrityAsync(connection, cancellationToken);
            await DatabaseMigrations.ApplyAsync(connection, cancellationToken);
            _initialized = true;
        }
        catch (SqliteException exception)
        {
            SqliteConnection.ClearAllPools();
            throw new InvalidOperationException(
                "The local database could not be opened or migrated. Restore a .pre-migrate.bak copy if one exists.",
                exception);
        }
        finally
        {
            _migrationLock.Release();
        }
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        var connection = new SqliteConnection($"Data Source={_paths.DatabasePath};Cache=Shared");
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
            await pragma.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task BackupBeforeMigrationAsync(CancellationToken cancellationToken)
    {
        int version;
        await using (var connection = await OpenAsync(cancellationToken))
        {
            version = await PeekVersionAsync(connection, cancellationToken);
            if (version >= DatabaseMigrations.CurrentVersion)
                return;
            await using var checkpoint = connection.CreateCommand();
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await checkpoint.ExecuteNonQueryAsync(cancellationToken);
        }

        SqliteConnection.ClearAllPools();
        var directory = Path.GetDirectoryName(_paths.DatabasePath)
            ?? throw new InvalidOperationException("The database path is invalid.");
        var backupPath = Path.Combine(
            directory,
            $"androidtvmanager-v{version}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.pre-migrate.bak");
        File.Copy(_paths.DatabasePath, backupPath, overwrite: false);
        foreach (var sidecar in new[] { "-wal", "-shm" })
        {
            var source = _paths.DatabasePath + sidecar;
            if (File.Exists(source))
                File.Copy(source, backupPath + sidecar, overwrite: false);
        }

        PrunePreMigrationBackups(directory);
    }

    private static void PrunePreMigrationBackups(string directory)
    {
        var backups = Directory.EnumerateFiles(directory, PreMigrationBackupPattern)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        var retained = backups.Take(RetainedPreMigrationBackups)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in backups.Where(path => !retained.Contains(path)))
        {
            TryDelete(stale);
            TryDelete(stale + "-wal");
            TryDelete(stale + "-shm");
        }

        foreach (var sidecar in Directory.EnumerateFiles(directory, "*.pre-migrate.bak-wal")
                     .Concat(Directory.EnumerateFiles(directory, "*.pre-migrate.bak-shm")))
        {
            if (!retained.Contains(sidecar[..^4]))
                TryDelete(sidecar);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private static async Task<int> PeekVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE
                WHEN EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersions')
                THEN COALESCE((SELECT MAX(Version) FROM SchemaVersions), 0)
                ELSE 0
            END;
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task EnsureIntegrityAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? "unknown";
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"The local database failed an integrity check ({result}). Restore a .pre-migrate.bak copy before retrying.");
    }
}
