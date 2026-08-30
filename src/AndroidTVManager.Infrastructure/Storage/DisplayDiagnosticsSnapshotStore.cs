using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Storage;

public sealed class DisplayDiagnosticsSnapshotStore : IDisplayDiagnosticsSnapshotStore
{
    private const int RetainedSnapshots = 50;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILocalAppDataPaths _paths;
    private readonly IAppLogger _logger;

    public DisplayDiagnosticsSnapshotStore(ILocalAppDataPaths paths, IAppLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task SaveAsync(
        DisplayDiagnosticSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        var directory = DirectoryFor(snapshot.Serial);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"{snapshot.CapturedUtc.UtcTicks}-{Guid.NewGuid():N}.json");
        var temporary = $"{file}.tmp";
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

    public async Task<IReadOnlyList<DisplayDiagnosticSnapshot>> GetRecentAsync(
        string serial,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
            return [];
        var directory = DirectoryFor(serial);
        if (!Directory.Exists(directory))
            return [];

        var snapshots = new List<DisplayDiagnosticSnapshot>();
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
                var snapshot = await JsonSerializer.DeserializeAsync<DisplayDiagnosticSnapshot>(
                    stream, JsonOptions, cancellationToken);
                if (snapshot is not null)
                    snapshots.Add(snapshot);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                _logger.Warning("DisplayDiagnostics",
                    $"Could not read snapshot {Path.GetFileName(file)}: {exception.Message}");
            }
        }
        return snapshots;
    }

    private string DirectoryFor(string serial)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serial.Trim())));
        return Path.Combine(_paths.SnapshotsPath, "Display", hash);
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
