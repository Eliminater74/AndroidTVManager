using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Storage;

public sealed class ConfigurationSnapshotStore : IConfigurationSnapshotStore
{
    private const int RetainedSnapshots = 10;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILocalAppDataPaths _paths;
    private readonly IAppLogger _logger;

    public ConfigurationSnapshotStore(ILocalAppDataPaths paths, IAppLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task SaveAsync(
        ConfigurationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        var directory = DirectoryFor(snapshot.Serial);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"{snapshot.CapturedUtc.UtcTicks}.json");
        var temporary = $"{file}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, file, overwrite: true);
            Prune(directory);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public async Task<ConfigurationSnapshot?> GetLatestAsync(
        string serial,
        CancellationToken cancellationToken = default)
        => (await GetRecentAsync(serial, 1, cancellationToken)).FirstOrDefault();

    public async Task<IReadOnlyList<ConfigurationSnapshot>> GetRecentAsync(
        string serial,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
            return [];

        var directory = DirectoryFor(serial);
        if (!Directory.Exists(directory))
            return [];

        var snapshots = new List<ConfigurationSnapshot>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json")
                     .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(Math.Clamp(limit, 1, RetainedSnapshots)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(
                    file,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    16 * 1024,
                    useAsync: true);
                var snapshot = await JsonSerializer.DeserializeAsync<ConfigurationSnapshot>(
                    stream, JsonOptions, cancellationToken);
                if (snapshot is not null)
                    snapshots.Add(snapshot);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                _logger.Warning("Configuration", $"Could not read snapshot {Path.GetFileName(file)}: {exception.Message}");
            }
        }
        return snapshots;
    }

    private string DirectoryFor(string serial)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serial.Trim())));
        return Path.Combine(_paths.SnapshotsPath, "Configuration", hash);
    }

    private static void Prune(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.json")
                     .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                     .Skip(RetainedSnapshots))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }
}
