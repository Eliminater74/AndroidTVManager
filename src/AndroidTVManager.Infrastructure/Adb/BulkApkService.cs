using System.IO.Compression;
using System.Text.Json;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class BulkApkService : IBulkApkService
{
    private const int MaxArchiveEntries = 2048;
    private const long MaxExtractedBytes = 2L * 1024 * 1024 * 1024;
    private readonly IApkInstaller _installer;
    private readonly IPackageManager _packageManager;
    private readonly ILocalAppDataPaths _paths;

    public BulkApkService(
        IApkInstaller installer,
        IPackageManager packageManager,
        ILocalAppDataPaths paths)
    {
        _installer = installer;
        _packageManager = packageManager;
        _paths = paths;
    }

    public async Task<BulkInstallPackageSet> PrepareAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0)
            throw new ArgumentException("At least one APK, archive, or folder is required.", nameof(paths));

        _paths.EnsureCreated();
        var temporaryDirectories = new List<string>();
        var artifacts = new List<ApkArtifact>();
        try
        {
            foreach (var input in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Directory.Exists(input))
                {
                    foreach (var file in Directory.EnumerateFiles(input, "*.*", SearchOption.AllDirectories)
                                 .Where(IsSupportedInput))
                        await AddInputAsync(file, artifacts, temporaryDirectories, cancellationToken);
                }
                else if (File.Exists(input))
                {
                    await AddInputAsync(input, artifacts, temporaryDirectories, cancellationToken);
                }
                else
                {
                    throw new FileNotFoundException("The selected package path does not exist.", input);
                }
            }

            var groups = GroupArtifacts(artifacts);
            if (groups.Count == 0)
                throw new InvalidDataException("No APK files were found in the selected input.");
            return new BulkInstallPackageSet(groups, temporaryDirectories);
        }
        catch
        {
            Cleanup(temporaryDirectories);
            throw;
        }
    }

    public async Task<BulkInstallResult> InstallAsync(
        string serial,
        BulkInstallPackageSet packageSet,
        IProgress<BulkInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("A target serial is required.", nameof(serial));

        var items = new List<BulkInstallItem>();
        try
        {
            for (var index = 0; index < packageSet.Groups.Count; index++)
            {
                var group = packageSet.Groups[index];
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new(index, packageSet.Groups.Count, group.DisplayName, BulkInstallItemStatus.Installing));
                var result = group.IsSplit
                    ? await _installer.InstallMultipleAsync(
                        serial.Trim(),
                        group.Artifacts.Select(artifact => artifact.Path).ToArray(),
                        cancellationToken: cancellationToken)
                    : await _installer.InstallAsync(
                        serial.Trim(),
                        group.Artifacts[0].Path,
                        cancellationToken: cancellationToken);
                var status = result.IsSuccess
                    ? BulkInstallItemStatus.Succeeded
                    : BulkInstallItemStatus.Failed;
                if (result.WasCanceled)
                {
                    items.Add(new(group, BulkInstallItemStatus.Canceled, result));
                    var reconciliation = await ReconcileAsync(serial, packageSet, CancellationToken.None);
                    items.AddRange(packageSet.Groups
                        .Skip(items.Count)
                        .Select(remaining => new BulkInstallItem(remaining, BulkInstallItemStatus.Canceled)));
                    return new(items, true, reconciliation.State, reconciliation.Message);
                }
                items.Add(new(group, status, result));
                progress?.Report(new(index + 1, packageSet.Groups.Count, group.DisplayName, status));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            items.AddRange(packageSet.Groups
                .Skip(items.Count)
                .Select(group => new BulkInstallItem(group, BulkInstallItemStatus.Canceled)));
            var reconciliation = await ReconcileAsync(serial, packageSet, CancellationToken.None);
            return new(items, true, reconciliation.State, reconciliation.Message);
        }
        finally
        {
            Cleanup(packageSet.TemporaryDirectories);
        }
        return new(items, false);
    }

    public void Cleanup(BulkInstallPackageSet packageSet)
        => Cleanup(packageSet.TemporaryDirectories);

    private async Task<(BulkInstallReconciliationState State, string Message)> ReconcileAsync(
        string serial,
        BulkInstallPackageSet packageSet,
        CancellationToken cancellationToken)
    {
        try
        {
            var packages = await _packageManager.ListAsync(serial.Trim(), cancellationToken);
            var expected = packageSet.Groups
                .Select(group => group.PackageName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (expected.Length == 0)
                return (BulkInstallReconciliationState.Unknown,
                    "Cancelled — package identity was not available, so the resulting device state is unknown.");
            var installed = expected.Count(name =>
                packages.Any(package => string.Equals(package.PackageName, name, StringComparison.OrdinalIgnoreCase)));
            return (BulkInstallReconciliationState.Verified,
                $"Cancelled — final device state queried; {installed} of {expected.Length} identified package(s) are present.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (BulkInstallReconciliationState.Unknown,
                $"Cancelled — resulting device state is unknown ({exception.Message}).");
        }
    }

    private static async Task AddInputAsync(
        string path,
        ICollection<ApkArtifact> artifacts,
        ICollection<string> temporaryDirectories,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".apk", StringComparison.OrdinalIgnoreCase))
        {
            var info = new FileInfo(path);
            artifacts.Add(new(
                path,
                info.Name,
                info.Length,
                ApkContainerKind.Apk,
                IsBaseApk(info.Name)));
            return;
        }
        if (!IsArchive(path))
            return;

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"AndroidTVManager-apk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        temporaryDirectories.Add(temporaryDirectory);
        await ExtractArchiveAsync(path, temporaryDirectory, cancellationToken);
        var archiveKind = ParseContainerKind(extension);
        var archiveName = Path.GetFileNameWithoutExtension(path);
        foreach (var apk in Directory.EnumerateFiles(temporaryDirectory, "*.apk", SearchOption.AllDirectories))
        {
            var info = new FileInfo(apk);
            artifacts.Add(new(
                apk,
                info.Name,
                info.Length,
                archiveKind,
                IsBaseApk(info.Name),
                PackageNameFromMetadata(temporaryDirectory),
                null,
                null));
        }
        if (!artifacts.Any(artifact => artifact.Path.StartsWith(temporaryDirectory, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"The archive '{archiveName}' did not contain any APK files.");
    }

    private static async Task ExtractArchiveAsync(
        string archivePath,
        string destination,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaxArchiveEntries)
            throw new InvalidDataException("The package archive contains too many entries.");

        long extractedBytes = 0;
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The package archive contains an unsafe path.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            extractedBytes = checked(extractedBytes + entry.Length);
            if (extractedBytes > MaxExtractedBytes)
                throw new InvalidDataException("The package archive is too large to extract.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var output = new FileStream(
                target,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                useAsync: true);
            await source.CopyToAsync(output, cancellationToken);
        }
    }

    private static IReadOnlyList<ApkInstallGroup> GroupArtifacts(IReadOnlyList<ApkArtifact> artifacts)
    {
        var groups = new List<ApkInstallGroup>();
        foreach (var archiveGroup in artifacts.Where(IsArchiveArtifact).GroupBy(ArchiveGroupKey))
        {
            groups.Add(CreateGroup(archiveGroup.Key, archiveGroup.ToArray()));
        }

        var direct = artifacts.Where(artifact => !IsArchiveArtifact(artifact)).ToArray();
        foreach (var baseGroup in direct.Where(artifact => IsBaseApk(artifact.FileName))
                     .GroupBy(artifact => Path.GetDirectoryName(artifact.Path), StringComparer.OrdinalIgnoreCase))
        {
            var siblings = direct.Where(artifact =>
                string.Equals(Path.GetDirectoryName(artifact.Path), baseGroup.Key, StringComparison.OrdinalIgnoreCase));
            groups.Add(CreateGroup(baseGroup.Key ?? baseGroup.First().FileName, siblings.ToArray()));
        }
        foreach (var artifact in direct.Where(artifact =>
                     !groups.SelectMany(group => group.Artifacts).Any(item => item.Path == artifact.Path)))
            groups.Add(CreateGroup(artifact.FileName, [artifact]));
        return groups;
    }

    private static ApkInstallGroup CreateGroup(string key, IReadOnlyList<ApkArtifact> artifacts)
    {
        var ordered = artifacts.OrderByDescending(artifact => artifact.IsBase).ThenBy(artifact => artifact.FileName).ToArray();
        return new(
            key,
            ordered.Length > 1 ? $"{key} ({ordered.Length} APK splits)" : ordered[0].FileName,
            ordered,
            ordered.Select(artifact => artifact.PackageName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string? PackageNameFromMetadata(string directory)
    {
        foreach (var fileName in new[] { "manifest.json", "info.json" })
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
                continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var property in new[] { "package_name", "packageName", "package" })
                    if (document.RootElement.TryGetProperty(property, out var value))
                        return value.GetString();
            }
            catch (JsonException)
            {
                return null;
            }
        }
        return null;
    }

    private static bool IsSupportedInput(string path)
        => Path.GetExtension(path) is ".apk" or ".apks" or ".xapk" or ".apkm";

    private static bool IsArchive(string path)
        => Path.GetExtension(path) is ".apks" or ".xapk" or ".apkm";

    private static ApkContainerKind ParseContainerKind(string extension)
        => extension.ToLowerInvariant() switch
        {
            ".apks" => ApkContainerKind.Apks,
            ".xapk" => ApkContainerKind.Xapk,
            ".apkm" => ApkContainerKind.Apkm,
            _ => ApkContainerKind.Apk
        };

    private static bool IsBaseApk(string fileName)
        => string.Equals(fileName, "base.apk", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "main.apk", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("base-", StringComparison.OrdinalIgnoreCase);

    private static bool IsArchiveArtifact(ApkArtifact artifact)
        => artifact.ContainerKind is ApkContainerKind.Apks or ApkContainerKind.Xapk or ApkContainerKind.Apkm;

    private static string ArchiveGroupKey(ApkArtifact artifact)
        => Path.GetDirectoryName(artifact.Path) ?? artifact.FileName;

    private static void Cleanup(IEnumerable<string> directories)
    {
        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
