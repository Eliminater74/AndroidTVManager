using Microsoft.Data.Sqlite;
using AndroidTVManager.Core.Abstractions;

namespace AndroidTVManager.Infrastructure.Database;

public sealed class SqliteDatabase
{
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
            await using var connection = await OpenAsync(cancellationToken);
            await DatabaseMigrations.ApplyAsync(connection, cancellationToken);
            _initialized = true;
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
        await connection.OpenAsync(cancellationToken);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }
}
