using System.IO.Compression;
using System.Text.Json;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class BulkApkService : IBulkApkService
{
    private const int MaxArchiveEntries = 2048;
    private const long MaxExtractedBytes = 2L * 1024 * 1024 * 1024;
    private const long MaxXapkExtractedBytes = 8L * 1024 * 1024 * 1024;
    private static readonly string[] AbiTokens =
    [
        "arm64-v8a",
        "armeabi-v7a",
        "armeabi",
        "x86_64",
        "x86"
    ];

    private readonly IApkInstaller _installer;
    private readonly IPackageManager _packageManager;
    private readonly IDeviceFileService _files;
    private readonly ILocalAppDataPaths _paths;

    public BulkApkService(
        IApkInstaller installer,
        IPackageManager packageManager,
        IDeviceFileService files,
        ILocalAppDataPaths paths)
    {
        _installer = installer;
        _packageManager = packageManager;
        _files = files;
        _paths = paths;
    }

    public async Task<BulkInstallPackageSet> PrepareAsync(
        IReadOnlyList<string> paths,
        IReadOnlyList<string>? deviceAbis = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0)
            throw new ArgumentException("At least one APK, archive, or folder is required.", nameof(paths));

        _paths.EnsureCreated();
        var temporaryDirectories = new List<string>();
        var groups = new List<ApkInstallGroup>();
        var directApks = new List<string>();
        try
        {
            foreach (var input in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Preparing package… {Path.GetFileName(input.TrimEnd(Path.DirectorySeparatorChar))}");
                if (Directory.Exists(input))
                {
                    foreach (var file in Directory.EnumerateFiles(input, "*.*", SearchOption.AllDirectories)
                                 .Where(IsSupportedInput))
                    {
                        if (IsArchive(file))
                            groups.Add(await PrepareArchiveAsync(
                                file, temporaryDirectories, deviceAbis, progress, cancellationToken));
                        else
                            directApks.Add(file);
                    }
                }
                else if (File.Exists(input))
                {
                    if (IsArchive(input))
                    {
                        groups.Add(await PrepareArchiveAsync(
                            input, temporaryDirectories, deviceAbis, progress, cancellationToken));
                    }
                    else if (IsApk(input))
                    {
                        directApks.Add(input);
                    }
                    else
                    {
                        throw new InvalidDataException($"'{Path.GetFileName(input)}' is not a supported APK or archive.");
                    }
                }
                else
                {
                    throw new FileNotFoundException("The selected package path does not exist.", input);
                }
            }

            if (directApks.Count > 0)
                groups.AddRange(GroupArtifacts(
                    directApks.Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(file => CreateApkArtifact(file, ApkContainerKind.Apk))
                        .ToArray(),
                    "selected-apks"));

            if (groups.Count == 0)
                throw new InvalidDataException("No APK files were found in the selected input.");
            ValidateGroups(groups);
            RejectIncompatibleAbis(groups, deviceAbis);
            return new BulkInstallPackageSet(groups, temporaryDirectories, paths);
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
                var stage = group.IsSplit
                    ? $"Installing APK splits… {group.DisplayName}"
                    : packageSet.Groups.Count > 1
                        ? $"Installing {index + 1} of {packageSet.Groups.Count} · {group.DisplayName}"
                        : $"Installing {group.DisplayName}…";
                progress?.Report(new(index, packageSet.Groups.Count, group.DisplayName,
                    BulkInstallItemStatus.Installing, stage));

                if (LooksLikeSplitSet(group.Artifacts) && !group.Artifacts.Any(artifact => artifact.IsBase))
                {
                    items.Add(new(
                        group,
                        BulkInstallItemStatus.Failed,
                        Message: "This package appears to contain Android split APKs, but AndroidTVManager could not identify a base APK. Nothing was installed."));
                    continue;
                }

                var result = group.IsSplit
                    ? await _installer.InstallMultipleAsync(
                        serial.Trim(),
                        group.Artifacts.Select(artifact => artifact.Path).ToArray(),
                        cancellationToken: cancellationToken)
                    : await _installer.InstallAsync(
                        serial.Trim(),
                        group.Artifacts[0].Path,
                        cancellationToken: cancellationToken);

                if (result.WasCanceled)
                {
                    items.Add(new(group, BulkInstallItemStatus.Canceled, result,
                        Message: "Installation was canceled after ADB reported the command was canceled."));
                    return await CompleteCanceledAsync(serial, packageSet, items, cancellationToken);
                }

                if (!result.IsSuccess)
                {
                    items.Add(new(group, BulkInstallItemStatus.Failed, result,
                        Message: AdbInstallErrorFormatter.Explain(result)));
                    progress?.Report(new(index + 1, packageSet.Groups.Count, group.DisplayName,
                        BulkInstallItemStatus.Failed, "Install failed."));
                    continue;
                }

                var verification = await VerifyInstalledAsync(serial, group, cancellationToken);
                if (verification.State == BulkInstallVerificationState.Missing)
                {
                    items.Add(new(group, BulkInstallItemStatus.Failed, result, verification.State, verification.Message));
                    continue;
                }

                progress?.Report(new(index, packageSet.Groups.Count, group.DisplayName,
                    BulkInstallItemStatus.Installing, "APK installation successful."));
                if (group.Payloads.Count == 0)
                {
                    items.Add(new(group, BulkInstallItemStatus.Succeeded, result, verification.State, verification.Message));
                    progress?.Report(new(index + 1, packageSet.Groups.Count, group.DisplayName,
                        BulkInstallItemStatus.Succeeded, "Complete."));
                    continue;
                }

                var obb = await CopyObbAsync(serial, group, progress, index, packageSet.Groups.Count, cancellationToken);
                if (obb.WasCanceled)
                {
                    items.Add(new(group, BulkInstallItemStatus.Canceled, result, verification.State,
                        "APK installation may have succeeded before cancellation. Additional XAPK data was not fully copied.",
                        obb));
                    return await CompleteCanceledAsync(serial, packageSet, items, CancellationToken.None);
                }

                if (!obb.IsSuccess)
                {
                    items.Add(new(
                        group,
                        BulkInstallItemStatus.PartialSuccess,
                        result,
                        verification.State,
                        $"Application installed, but additional XAPK data could not be copied.{Environment.NewLine}{Environment.NewLine}APK install: Success{Environment.NewLine}OBB copy: Failed{Environment.NewLine}{Environment.NewLine}{FirstLine(obb.StandardError) ?? obb.StandardOutput}",
                        obb));
                    progress?.Report(new(index + 1, packageSet.Groups.Count, group.DisplayName,
                        BulkInstallItemStatus.PartialSuccess, "APK installed; OBB copy failed."));
                    continue;
                }

                items.Add(new(group, BulkInstallItemStatus.Succeeded, result, verification.State,
                    verification.Message is null
                        ? "Application and additional XAPK data were installed."
                        : $"{verification.Message} Additional XAPK data was copied.",
                    obb));
                progress?.Report(new(index + 1, packageSet.Groups.Count, group.DisplayName,
                    BulkInstallItemStatus.Succeeded, "Complete."));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await CompleteCanceledAsync(serial, packageSet, items, CancellationToken.None);
        }
        finally
        {
            Cleanup(packageSet.TemporaryDirectories);
        }

        return new(items, false);
    }

    public void Cleanup(BulkInstallPackageSet packageSet)
        => Cleanup(packageSet.TemporaryDirectories);

    private async Task<BulkInstallResult> CompleteCanceledAsync(
        string serial,
        BulkInstallPackageSet packageSet,
        List<BulkInstallItem> items,
        CancellationToken cancellationToken)
    {
        items.AddRange(packageSet.Groups
            .Skip(items.Count)
            .Select(group => new BulkInstallItem(group, BulkInstallItemStatus.Canceled)));
        var reconciliation = await ReconcileAsync(serial, packageSet, cancellationToken);
        return new(items, true, reconciliation.State, reconciliation.Message);
    }

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

    private async Task<(BulkInstallVerificationState State, string? Message)> VerifyInstalledAsync(
        string serial,
        ApkInstallGroup group,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(group.PackageName))
            return (BulkInstallVerificationState.IdentityUnavailable,
                "ADB reported success; package identity was unavailable for post-install verification.");
        try
        {
            var packages = await _packageManager.ListAsync(serial.Trim(), cancellationToken);
            var present = packages.Any(package =>
                string.Equals(package.PackageName, group.PackageName, StringComparison.OrdinalIgnoreCase)
                && !package.IsUninstalledForUser);
            return present
                ? (BulkInstallVerificationState.Verified, $"Verified {group.PackageName} is present for User 0.")
                : (BulkInstallVerificationState.Missing,
                    "Install command succeeded, but package verification failed.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (BulkInstallVerificationState.IdentityUnavailable,
                $"ADB reported success; package verification could not be completed ({exception.Message}).");
        }
    }

    private async Task<AdbCommandResult> CopyObbAsync(
        string serial,
        ApkInstallGroup group,
        IProgress<BulkInstallProgress>? progress,
        int index,
        int total,
        CancellationToken cancellationToken)
    {
        AdbCommandResult? last = null;
        foreach (var payload in group.Payloads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(index, total, group.DisplayName, BulkInstallItemStatus.Installing,
                $"Creating OBB directory… {payload.RemoteDirectory}"));
            last = await _files.CreateDirectoryAsync(serial, payload.RemoteDirectory, cancellationToken);
            if (!last.IsSuccess || last.WasCanceled)
                return last;
            progress?.Report(new(index, total, group.DisplayName, BulkInstallItemStatus.Installing,
                $"Copying {payload.FileName}…"));
            last = await _files.PushAsync(serial, payload.LocalPath, payload.RemotePath, cancellationToken);
            if (!last.IsSuccess || last.WasCanceled)
                return last;
        }

        return last ?? new AdbCommandResult("adb.exe", [], 0, string.Empty, string.Empty, TimeSpan.Zero);
    }

    private static async Task<ApkInstallGroup> PrepareArchiveAsync(
        string archivePath,
        ICollection<string> temporaryDirectories,
        IReadOnlyList<string>? deviceAbis,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var kind = ParseContainerKind(Path.GetExtension(archivePath));
        var sourceName = Path.GetFileName(archivePath);
        progress?.Report(kind == ApkContainerKind.Xapk
            ? "Reading XAPK manifest…"
            : $"Extracting {sourceName}…");
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"AndroidTVManager-apk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        temporaryDirectories.Add(temporaryDirectory);
        var limit = kind is ApkContainerKind.Xapk or ApkContainerKind.Apkm
            ? MaxXapkExtractedBytes
            : MaxExtractedBytes;
        await ExtractArchiveAsync(archivePath, temporaryDirectory, limit, cancellationToken);

        var metadata = PackageMetadata.Read(temporaryDirectory);
        var apkFiles = Directory.EnumerateFiles(temporaryDirectory, "*.apk", SearchOption.AllDirectories).ToArray();
        if (apkFiles.Length == 0)
            throw new InvalidDataException($"The archive '{sourceName}' did not contain any APK files.");

        var selected = SelectArchiveApks(kind, apkFiles, deviceAbis, sourceName);
        progress?.Report($"Found {selected.Length} APK component(s)…");
        if (LooksLikeSplitSet(selected.Select(file => CreateApkArtifact(file, kind, metadata.PackageName)))
            && !selected.Any(file => IsBaseApk(Path.GetFileName(file))))
            throw new InvalidDataException(
                "This package appears to contain Android split APKs, but AndroidTVManager could not identify a base APK. Nothing was installed.");

        var artifacts = selected
            .Select(file => CreateApkArtifact(file, kind, metadata.PackageName, AbiFromFileName(Path.GetFileName(file))))
            .ToArray();
        var payloads = kind == ApkContainerKind.Xapk
            ? CollectObbPayloads(temporaryDirectory, metadata.PackageName, metadata.ExpansionFiles)
            : [];
        var nativeAbis = DistinctAbis(artifacts, metadata.NativeAbis);
        return CreateGroup(
            Path.GetFileNameWithoutExtension(archivePath),
            artifacts,
            sourceName,
            payloads,
            nativeAbis);
    }

    private static string[] SelectArchiveApks(
        ApkContainerKind kind,
        string[] apkFiles,
        IReadOnlyList<string>? deviceAbis,
        string sourceName)
    {
        var standalones = apkFiles
            .Where(path => path.Replace('\\', '/').Contains("/standalones/", StringComparison.OrdinalIgnoreCase)
                           || Path.GetFileName(path).Contains("standalone", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (kind == ApkContainerKind.Apks && standalones.Length > 1)
        {
            var matching = standalones
                .Where(path => MatchesDeviceAbi(Path.GetFileName(path), deviceAbis))
                .ToArray();
            if (deviceAbis is { Count: > 0 } && matching.Length == 1)
                return matching;
            throw new InvalidDataException(
                $"The APKS archive '{sourceName}' contains multiple device-targeted variants. AndroidTVManager cannot safely choose one without matching ABI metadata. Nothing was installed.");
        }

        if (kind == ApkContainerKind.Apks && standalones.Length == 1 && apkFiles.Length == standalones.Length)
            return standalones;

        return apkFiles
            .Where(path => !standalones.Contains(path, StringComparer.OrdinalIgnoreCase))
            .DefaultIfEmpty(apkFiles[0])
            .ToArray();
    }

    private static IReadOnlyList<ApkInstallGroup> GroupArtifacts(
        IReadOnlyList<ApkArtifact> artifacts,
        string fallbackName)
    {
        var groups = new List<ApkInstallGroup>();
        var remaining = artifacts.ToList();
        var directories = remaining
            .Where(artifact => artifact.IsBase)
            .Select(artifact => Path.GetDirectoryName(artifact.Path) ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var directory in directories)
        {
            var siblings = remaining
                .Where(artifact => string.Equals(
                    Path.GetDirectoryName(artifact.Path) ?? string.Empty, directory, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            groups.Add(CreateGroup(string.IsNullOrEmpty(directory) ? fallbackName : directory, siblings));
            remaining.RemoveAll(artifact => siblings.Contains(artifact));
        }

        foreach (var artifact in remaining)
            groups.Add(CreateGroup(artifact.FileName, [artifact]));
        return groups;
    }

    private static ApkInstallGroup CreateGroup(
        string key,
        IReadOnlyList<ApkArtifact> artifacts,
        string? sourceName = null,
        IReadOnlyList<ApkAdditionalPayload>? payloads = null,
        IReadOnlyList<string>? nativeAbis = null)
    {
        var ordered = artifacts
            .OrderByDescending(artifact => artifact.IsBase)
            .ThenBy(artifact => artifact.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var kind = ordered.Select(artifact => artifact.ContainerKind).Distinct().ToArray();
        var container = kind.Length == 1 ? kind[0] : ApkContainerKind.Apk;
        var labels = ordered
            .Where(artifact => !artifact.IsBase)
            .Select(artifact => SplitLabel(artifact.FileName))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Cast<string>()
            .ToArray();
        var display = ordered.Length > 1
            ? $"{Path.GetFileName(key)} ({ordered.Length} APK splits)"
            : ordered[0].FileName;
        return new(
            key,
            display,
            ordered,
            ordered.Select(artifact => artifact.PackageName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            ContainerKind: container,
            SourceName: sourceName ?? Path.GetFileName(key),
            SplitLabels: labels,
            AdditionalPayloads: payloads ?? [],
            NativeAbis: nativeAbis ?? DistinctAbis(ordered, null));
    }

    private static void ValidateGroups(IEnumerable<ApkInstallGroup> groups)
    {
        foreach (var group in groups)
        {
            if (LooksLikeSplitSet(group.Artifacts) && !group.Artifacts.Any(artifact => artifact.IsBase))
                throw new InvalidDataException(
                    "This package appears to contain Android split APKs, but AndroidTVManager could not identify a base APK. Nothing was installed.");
        }
    }

    private static void RejectIncompatibleAbis(
        IEnumerable<ApkInstallGroup> groups,
        IReadOnlyList<string>? deviceAbis)
    {
        if (deviceAbis is not { Count: > 0 })
            return;
        foreach (var group in groups)
        {
            var packageAbis = (group.NativeAbis ?? [])
                .Select(NormalizeAbi)
                .Where(abi => AbiTokens.Contains(abi, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (packageAbis.Length == 0)
                continue;
            if (packageAbis.Any(abi => MatchesDeviceAbi(abi, deviceAbis)))
                continue;
            throw new InvalidDataException(
                $"This package only contains {string.Join(", ", packageAbis)} native components, but the selected Android TV reports {string.Join(", ", deviceAbis)}.");
        }
    }

    private static async Task ExtractArchiveAsync(
        string archivePath,
        string destination,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(archivePath);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException)
        {
            throw new InvalidDataException("The package archive is malformed or could not be read.", exception);
        }

        using (archive)
        {
        if (archive.Entries.Count > MaxArchiveEntries)
            throw new InvalidDataException("The package archive contains too many entries.");

        long extractedBytes = 0;
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsZipSymlink(entry))
                throw new InvalidDataException("The package archive contains a symbolic link, which is not allowed.");
            var relative = entry.FullName.Replace('\\', '/');
            if (relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
                throw new InvalidDataException("The package archive contains an unsafe path.");
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The package archive contains an unsafe path.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            if (!written.Add(target))
                throw new InvalidDataException("The package archive contains duplicate paths.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var output = new FileStream(
                target,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                useAsync: true);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                extractedBytes = checked(extractedBytes + read);
                if (extractedBytes > maxBytes)
                    throw new InvalidDataException("The package archive is too large to extract.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        }
    }

    private static IReadOnlyList<ApkAdditionalPayload> CollectObbPayloads(
        string directory,
        string? packageName,
        IReadOnlyList<string> expansionFiles)
    {
        var payloads = new List<ApkAdditionalPayload>();
        if (string.IsNullOrWhiteSpace(packageName))
            return payloads;

        foreach (var file in Directory.EnumerateFiles(directory, "*.obb", SearchOption.AllDirectories)
                     .Concat(expansionFiles.Select(path => Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar)))
                         .Where(File.Exists)))
        {
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            if (!IsSafeObbPath(relative, packageName))
                continue;
            var info = new FileInfo(file);
            payloads.Add(new(
                info.FullName,
                info.Name,
                info.Length,
                packageName,
                $"/sdcard/Android/obb/{packageName}",
                info.Name));
        }

        return payloads
            .DistinctBy(payload => payload.RemotePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSafeObbPath(string relativePath, string packageName)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("Android/data/", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!normalized.EndsWith(".obb", StringComparison.OrdinalIgnoreCase))
            return false;
        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
            return false;
        return normalized.StartsWith($"Android/obb/{packageName}/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetDirectoryName(normalized)?.Replace('\\', '/'),
                $"Android/obb/{packageName}", StringComparison.OrdinalIgnoreCase)
            || normalized.Count(character => character == '/') == 0;
    }

    private static ApkArtifact CreateApkArtifact(
        string path,
        ApkContainerKind kind,
        string? packageName = null,
        string? abi = null)
    {
        var info = new FileInfo(path);
        return new(
            info.FullName,
            info.Name,
            info.Length,
            kind,
            IsBaseApk(info.Name),
            packageName,
            Abi: abi ?? AbiFromFileName(info.Name));
    }

    private static bool LooksLikeSplitSet(IEnumerable<ApkArtifact> artifacts)
        => artifacts.Any(artifact =>
            artifact.FileName.Contains("split", StringComparison.OrdinalIgnoreCase)
            || artifact.FileName.StartsWith("config.", StringComparison.OrdinalIgnoreCase)
            || artifact.FileName.Contains("config.", StringComparison.OrdinalIgnoreCase));

    private static bool IsBaseApk(string fileName)
        => string.Equals(fileName, "base.apk", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "main.apk", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("base-", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("base.", StringComparison.OrdinalIgnoreCase);

    private static string? SplitLabel(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        foreach (var prefix in new[] { "split_config.", "config.", "split_config_", "split." })
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return name[prefix.Length..].Replace('_', '-');
        }
        return name.Contains("config", StringComparison.OrdinalIgnoreCase) ? name : null;
    }

    private static string? AbiFromFileName(string fileName)
    {
        var haystack = fileName.Replace('_', '-');
        foreach (var token in AbiTokens.OrderByDescending(item => item.Length))
        {
            if (haystack.Contains(token.Replace('_', '-'), StringComparison.OrdinalIgnoreCase))
                return NormalizeAbi(token);
        }
        return null;
    }

    private static IReadOnlyList<string> DistinctAbis(
        IEnumerable<ApkArtifact> artifacts,
        IReadOnlyList<string>? metadataAbis)
        => (metadataAbis ?? [])
            .Concat(artifacts.Select(artifact => artifact.Abi))
            .Where(abi => !string.IsNullOrWhiteSpace(abi))
            .Select(abi => NormalizeAbi(abi!))
            .Where(abi => AbiTokens.Contains(abi, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool MatchesDeviceAbi(string value, IReadOnlyList<string>? deviceAbis)
    {
        if (deviceAbis is not { Count: > 0 })
            return false;
        var token = NormalizeAbi(AbiFromFileName(value) ?? value);
        return deviceAbis.Any(abi => string.Equals(NormalizeAbi(abi), token, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeAbi(string abi)
    {
        var value = abi.Replace('_', '-').Trim();
        return value.Equals("x86-64", StringComparison.OrdinalIgnoreCase) ? "x86_64" : value;
    }

    private static bool IsZipSymlink(ZipArchiveEntry entry)
        => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private static bool IsSupportedInput(string path)
        => IsApk(path) || IsArchive(path);

    private static bool IsApk(string path)
        => Path.GetExtension(path).Equals(".apk", StringComparison.OrdinalIgnoreCase);

    private static bool IsArchive(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".apks", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xapk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".apkm", StringComparison.OrdinalIgnoreCase);
    }

    private static ApkContainerKind ParseContainerKind(string extension)
        => extension.ToLowerInvariant() switch
        {
            ".apks" => ApkContainerKind.Apks,
            ".xapk" => ApkContainerKind.Xapk,
            ".apkm" => ApkContainerKind.Apkm,
            _ => ApkContainerKind.Apk
        };

    private static string? FirstLine(string? text)
        => text?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

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

    private sealed record PackageMetadata(
        string? PackageName,
        IReadOnlyList<string> NativeAbis,
        IReadOnlyList<string> ExpansionFiles)
    {
        public static PackageMetadata Read(string directory)
        {
            foreach (var fileName in new[] { "manifest.json", "info.json" })
            {
                var path = Path.Combine(directory, fileName);
                if (!File.Exists(path))
                    continue;
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                    var root = document.RootElement;
                    string? packageName = null;
                    foreach (var property in new[] { "package_name", "packageName", "package" })
                    {
                        if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
                        {
                            packageName = value.GetString();
                            break;
                        }
                    }

                    var abis = new List<string>();
                    if (root.TryGetProperty("native_code", out var native) && native.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in native.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } abi)
                                abis.Add(abi);
                    }

                    var expansions = new List<string>();
                    if (root.TryGetProperty("expansions", out var expansionNode)
                        && expansionNode.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in expansionNode.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.Object
                                && item.TryGetProperty("file", out var file)
                                && file.GetString() is { } expansion)
                                expansions.Add(expansion);
                        }
                    }

                    return new(packageName, abis, expansions);
                }
                catch (JsonException)
                {
                    return new(null, [], []);
                }
            }

            return new(null, [], []);
        }
    }
}
