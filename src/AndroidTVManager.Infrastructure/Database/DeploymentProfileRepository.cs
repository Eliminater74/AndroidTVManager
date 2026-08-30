using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace AndroidTVManager.Infrastructure.Database;

public sealed class DeploymentProfileRepository : IDeploymentProfileRepository
{
    private readonly SqliteDatabase _database;

    public DeploymentProfileRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<DeploymentProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Description, Manufacturer, Brand, Model, Product, Device,
                   MinimumApiLevel, MaximumApiLevel, Abi, RequiresAndroidTv, RequiresGoogleTv,
                   BuildFingerprintPrefix, FormatVersion, CreatedUtc, UpdatedUtc
            FROM DeploymentProfiles ORDER BY Name;
            """;
        var rows = new List<ProfileRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(ReadRow(reader));
        await reader.DisposeAsync();
        var profiles = new List<DeploymentProfile>(rows.Count);
        foreach (var row in rows)
            profiles.Add(await LoadProfileAsync(connection, row, cancellationToken));
        return profiles;
    }

    public async Task<DeploymentProfile?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Description, Manufacturer, Brand, Model, Product, Device,
                   MinimumApiLevel, MaximumApiLevel, Abi, RequiresAndroidTv, RequiresGoogleTv,
                   BuildFingerprintPrefix, FormatVersion, CreatedUtc, UpdatedUtc
            FROM DeploymentProfiles WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        ProfileRow? row = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
            row = ReadRow(reader);
        await reader.DisposeAsync();
        return row is null ? null : await LoadProfileAsync(connection, row, cancellationToken);
    }

    public async Task<long> UpsertAsync(
        DeploymentProfile profile,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow.ToString("O");
        long id;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = profile.Id == 0
                ? """
                  INSERT INTO DeploymentProfiles
                      (Name, Description, Manufacturer, Brand, Model, Product, Device,
                       MinimumApiLevel, MaximumApiLevel, Abi, RequiresAndroidTv, RequiresGoogleTv,
                       BuildFingerprintPrefix, FormatVersion, CreatedUtc, UpdatedUtc)
                  VALUES ($name, $description, $manufacturer, $brand, $model, $product, $device,
                       $minimumApi, $maximumApi, $abi, $requiresAndroidTv, $requiresGoogleTv,
                       $buildFingerprintPrefix, $formatVersion, $now, $now);
                  SELECT last_insert_rowid();
                  """
                : """
                  UPDATE DeploymentProfiles SET Name = $name, Description = $description,
                      Manufacturer = $manufacturer, Brand = $brand, Model = $model, Product = $product,
                      Device = $device, MinimumApiLevel = $minimumApi, MaximumApiLevel = $maximumApi,
                      Abi = $abi, RequiresAndroidTv = $requiresAndroidTv, RequiresGoogleTv = $requiresGoogleTv,
                      BuildFingerprintPrefix = $buildFingerprintPrefix, FormatVersion = $formatVersion,
                      UpdatedUtc = $now
                  WHERE Id = $id;
                  SELECT $id;
                  """;
            AddProfileParameters(command, profile, now);
            id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM DeploymentProfileSteps WHERE ProfileId = $id;";
            delete.Parameters.AddWithValue("$id", id);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var step in profile.Steps.OrderBy(step => step.SortOrder))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO DeploymentProfileSteps
                    (ProfileId, SortOrder, Kind, DisplayName, RelativePath, PackageName, ScriptJson, AssetIdsJson, IsOptional)
                VALUES ($profileId, $sortOrder, $kind, $displayName, $relativePath, $packageName, $scriptJson, $assetIdsJson, $isOptional);
                """;
            command.Parameters.AddWithValue("$profileId", id);
            command.Parameters.AddWithValue("$sortOrder", step.SortOrder);
            command.Parameters.AddWithValue("$kind", (int)step.Kind);
            command.Parameters.AddWithValue("$displayName", step.DisplayName);
            command.Parameters.AddWithValue("$relativePath", (object?)step.RelativePath ?? DBNull.Value);
            command.Parameters.AddWithValue("$packageName", (object?)step.PackageName ?? DBNull.Value);
            command.Parameters.AddWithValue("$scriptJson", (object?)step.ScriptJson ?? DBNull.Value);
            command.Parameters.AddWithValue("$assetIdsJson", JsonSerializer.Serialize(step.AssetIds ?? []));
            command.Parameters.AddWithValue("$isOptional", step.IsOptional ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var asset in profile.Assets ?? [])
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO DeploymentProfileAssets
                    (ProfileId, Sha256, OriginalFileName, StoredFileName, SizeBytes, ContainerKind,
                     PackageName, VersionName, VersionCode, ImportedUtc)
                VALUES ($profileId, $sha256, $originalFileName, $storedFileName, $sizeBytes, $containerKind,
                     $packageName, $versionName, $versionCode, $importedUtc);
                """;
            command.Parameters.AddWithValue("$profileId", id);
            command.Parameters.AddWithValue("$sha256", asset.Sha256);
            command.Parameters.AddWithValue("$originalFileName", asset.OriginalFileName);
            command.Parameters.AddWithValue("$storedFileName", asset.StoredFileName);
            command.Parameters.AddWithValue("$sizeBytes", asset.SizeBytes);
            command.Parameters.AddWithValue("$containerKind", (int)asset.ContainerKind);
            command.Parameters.AddWithValue("$packageName", (object?)asset.PackageName ?? DBNull.Value);
            command.Parameters.AddWithValue("$versionName", (object?)asset.VersionName ?? DBNull.Value);
            command.Parameters.AddWithValue("$versionCode", (object?)asset.VersionCode ?? DBNull.Value);
            command.Parameters.AddWithValue("$importedUtc", asset.ImportedUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DeploymentProfiles WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> StartExecutionAsync(
        long profileId,
        string profileName,
        string serial,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DeploymentExecutions
                (ProfileId, ProfileName, Serial, StartedUtc, Status)
            VALUES ($profileId, $profileName, $serial, $startedUtc, 'Running');
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$profileId", profileId);
        command.Parameters.AddWithValue("$profileName", profileName);
        command.Parameters.AddWithValue("$serial", serial);
        command.Parameters.AddWithValue("$startedUtc", DateTimeOffset.UtcNow.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task CompleteExecutionAsync(
        long executionId,
        string status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DeploymentExecutions
            SET CompletedUtc = $completedUtc, Status = $status, ErrorMessage = $errorMessage
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$completedUtc", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$errorMessage", (object?)errorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", executionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordExecutionStepAsync(
        long executionId,
        DeploymentExecutionStep step,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DeploymentExecutionSteps
                (ExecutionId, ProfileStepId, SortOrder, Status, Output, Reversible, UndoStatus)
            VALUES ($executionId, $profileStepId, $sortOrder, $status, $output, $reversible, $undoStatus);
            """;
        command.Parameters.AddWithValue("$executionId", executionId);
        command.Parameters.AddWithValue("$profileStepId", step.ProfileStepId);
        command.Parameters.AddWithValue("$sortOrder", step.SortOrder);
        command.Parameters.AddWithValue("$status", step.Status);
        command.Parameters.AddWithValue("$output", (object?)step.Output ?? DBNull.Value);
        command.Parameters.AddWithValue("$reversible", step.Reversible ? 1 : 0);
        command.Parameters.AddWithValue("$undoStatus", (object?)step.UndoStatus ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeploymentProfileExecution>> GetExecutionsAsync(
        long profileId,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProfileId, ProfileName, Serial, StartedUtc, CompletedUtc, Status, ErrorMessage
            FROM DeploymentExecutions
            WHERE ProfileId = $profileId ORDER BY StartedUtc DESC LIMIT 50;
            """;
        command.Parameters.AddWithValue("$profileId", profileId);
        var executions = new List<DeploymentProfileExecution>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            executions.Add(new(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return executions;
    }

    private static ProfileRow ReadRow(SqliteDataReader reader)
        => new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetInt32(8),
            reader.IsDBNull(9) ? null : reader.GetInt32(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetInt64(11) == 1,
            reader.IsDBNull(12) ? null : reader.GetInt64(12) == 1,
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.GetInt32(14),
            DateTimeOffset.Parse(reader.GetString(15)),
            DateTimeOffset.Parse(reader.GetString(16)));

    private static async Task<DeploymentProfile> LoadProfileAsync(
        SqliteConnection connection,
        ProfileRow row,
        CancellationToken cancellationToken)
    {
        var steps = new List<DeploymentProfileStep>();
        await using (var stepsCommand = connection.CreateCommand())
        {
            stepsCommand.CommandText = """
                SELECT Id, SortOrder, Kind, DisplayName, RelativePath, PackageName, ScriptJson, AssetIdsJson, IsOptional
                FROM DeploymentProfileSteps WHERE ProfileId = $profileId ORDER BY SortOrder, Id;
                """;
            stepsCommand.Parameters.AddWithValue("$profileId", row.Id);
            await using var stepsReader = await stepsCommand.ExecuteReaderAsync(cancellationToken);
            while (await stepsReader.ReadAsync(cancellationToken))
            {
                IReadOnlyList<long>? assetIds = null;
                if (!stepsReader.IsDBNull(7))
                {
                    try
                    {
                        assetIds = JsonSerializer.Deserialize<IReadOnlyList<long>>(stepsReader.GetString(7));
                    }
                    catch (JsonException)
                    {
                    }
                }
                steps.Add(new(
                    stepsReader.GetInt64(0),
                    stepsReader.GetInt32(1),
                    (DeploymentStepKind)stepsReader.GetInt32(2),
                    stepsReader.GetString(3),
                    stepsReader.IsDBNull(4) ? null : stepsReader.GetString(4),
                    stepsReader.IsDBNull(5) ? null : stepsReader.GetString(5),
                    stepsReader.IsDBNull(6) ? null : stepsReader.GetString(6),
                    stepsReader.GetInt64(8) == 1,
                    assetIds));
            }
        }

        var assets = new List<DeploymentProfileAsset>();
        await using (var assetsCommand = connection.CreateCommand())
        {
            assetsCommand.CommandText = """
                SELECT Id, ProfileId, Sha256, OriginalFileName, StoredFileName, SizeBytes,
                       ContainerKind, PackageName, VersionName, VersionCode, ImportedUtc
                FROM DeploymentProfileAssets WHERE ProfileId = $profileId ORDER BY Id;
                """;
            assetsCommand.Parameters.AddWithValue("$profileId", row.Id);
            await using var assetsReader = await assetsCommand.ExecuteReaderAsync(cancellationToken);
            while (await assetsReader.ReadAsync(cancellationToken))
            {
                assets.Add(new(
                    assetsReader.GetInt64(0),
                    assetsReader.GetInt64(1),
                    assetsReader.GetString(2),
                    assetsReader.GetString(3),
                    assetsReader.GetString(4),
                    assetsReader.GetInt64(5),
                    (ApkContainerKind)assetsReader.GetInt32(6),
                    assetsReader.IsDBNull(7) ? null : assetsReader.GetString(7),
                    assetsReader.IsDBNull(8) ? null : assetsReader.GetString(8),
                    assetsReader.IsDBNull(9) ? null : assetsReader.GetInt64(9),
                    DateTimeOffset.Parse(assetsReader.GetString(10))));
            }
        }
        return new(
            row.Id,
            row.Name,
            row.Description,
            row.Manufacturer,
            row.Brand,
            row.Model,
            row.Product,
            row.Device,
            row.MinimumApiLevel,
            row.MaximumApiLevel,
            row.Abi,
            row.RequiresAndroidTv,
            row.RequiresGoogleTv,
            row.BuildFingerprintPrefix,
            row.FormatVersion,
            row.CreatedUtc,
            row.UpdatedUtc,
            steps,
            assets);
    }

    private sealed record ProfileRow(
        long Id,
        string Name,
        string? Description,
        string? Manufacturer,
        string? Brand,
        string? Model,
        string? Product,
        string? Device,
        int? MinimumApiLevel,
        int? MaximumApiLevel,
        string? Abi,
        bool? RequiresAndroidTv,
        bool? RequiresGoogleTv,
        string? BuildFingerprintPrefix,
        int FormatVersion,
        DateTimeOffset CreatedUtc,
        DateTimeOffset UpdatedUtc);

    private static void AddProfileParameters(SqliteCommand command, DeploymentProfile profile, string now)
    {
        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$description", (object?)profile.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$manufacturer", (object?)profile.Manufacturer ?? DBNull.Value);
        command.Parameters.AddWithValue("$brand", (object?)profile.Brand ?? DBNull.Value);
        command.Parameters.AddWithValue("$model", (object?)profile.Model ?? DBNull.Value);
        command.Parameters.AddWithValue("$product", (object?)profile.Product ?? DBNull.Value);
        command.Parameters.AddWithValue("$device", (object?)profile.Device ?? DBNull.Value);
        command.Parameters.AddWithValue("$minimumApi", (object?)profile.MinimumApiLevel ?? DBNull.Value);
        command.Parameters.AddWithValue("$maximumApi", (object?)profile.MaximumApiLevel ?? DBNull.Value);
        command.Parameters.AddWithValue("$abi", (object?)profile.Abi ?? DBNull.Value);
        command.Parameters.AddWithValue("$requiresAndroidTv",
            profile.RequiresAndroidTv is null ? DBNull.Value : profile.RequiresAndroidTv.Value ? 1 : 0);
        command.Parameters.AddWithValue("$requiresGoogleTv",
            profile.RequiresGoogleTv is null ? DBNull.Value : profile.RequiresGoogleTv.Value ? 1 : 0);
        command.Parameters.AddWithValue("$buildFingerprintPrefix", (object?)profile.BuildFingerprintPrefix ?? DBNull.Value);
        command.Parameters.AddWithValue("$formatVersion", profile.FormatVersion);
        command.Parameters.AddWithValue("$now", now);
    }
}
