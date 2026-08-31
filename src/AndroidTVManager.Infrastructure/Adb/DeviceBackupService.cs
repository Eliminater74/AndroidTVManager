using System.Text.Json;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class DeviceBackupService : IDeviceBackupService
{
    private static readonly TimeSpan PullTimeout = TimeSpan.FromMinutes(30);
    private readonly IAdbProcessRunner _runner;
    private readonly IDeviceInspectionService _inspection;
    private readonly IConfigurationExplorerService _configuration;
    private readonly IPackageInventoryService _inventory;
    private readonly IAppLogger _logger;

    public DeviceBackupService(
        IAdbProcessRunner runner,
        IDeviceInspectionService inspection,
        IConfigurationExplorerService configuration,
        IPackageInventoryService inventory,
        IAppLogger logger)
    {
        _runner = runner;
        _inspection = inspection;
        _configuration = configuration;
        _inventory = inventory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BackupCapability>> GetCapabilitiesAsync(
        AndroidDevice device,
        CancellationToken cancellationToken = default)
    {
        if (device.State != DeviceState.Device)
            return AllCapabilities(CapabilityState.Unavailable, "Connect the device before checking backup capabilities.");

        var storage = await _runner.RunForDeviceAsync(
            device.Serial,
            ["shell", "test", "-d", "/sdcard", "&&", "echo", "available"],
            TimeSpan.FromSeconds(30),
            cancellationToken);
        var storageState = storage.IsSuccess && storage.StandardOutput.Contains("available", StringComparison.OrdinalIgnoreCase)
            ? CapabilityState.Supported
            : CapabilityState.Unknown;

        var legacyState = device.ApiLevel is <= 30
            ? CapabilityState.Partial
            : CapabilityState.Unsupported;
        var legacyEvidence = device.ApiLevel is <= 30
            ? "Legacy adb backup may be exposed on this Android API level and will be tested only when explicitly selected."
            : "adb backup is deprecated or unavailable on modern Android builds.";

        return
        [
            new(BackupKind.DeviceReport, "Device inspection report", CapabilityState.Supported,
                "Read-only hardware, Android, security, network, service, and package evidence.", "Standard ADB inspection"),
            new(BackupKind.ConfigurationSnapshot, "Configuration snapshot", CapabilityState.Supported,
                "Read-only runtime properties and available partition property files.", "getprop and build.prop collection"),
            new(BackupKind.PackageApks, "APK and split APK backup", CapabilityState.Supported,
                "Pull installed APK paths, including split APKs where the device exposes them.", "Package inventory APK paths"),
            new(BackupKind.SharedStorage, "Shared/user storage", storageState,
                "Pull a user-selected device path such as /sdcard/. This can be large.", storage.IsSuccess
                    ? "The device exposes /sdcard/."
                    : storage.StandardError.Trim()),
            new(BackupKind.LegacyAppData, "Legacy app-data backup", legacyState,
                "Attempt the legacy ADB backup format only when explicitly selected. It does not work on many modern builds.",
                legacyEvidence),
            new(BackupKind.FullDeviceImage, "Full device image", CapabilityState.Unsupported,
                "A complete boot/system/data image requires supported root, recovery, or vendor tooling. Standard ADB cannot provide it safely.",
                "No full-image operation is attempted by this application.")
        ];
    }

    public async Task<DeviceBackupResult> CreateAsync(
        BackupRequest request,
        AndroidDevice device,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Serial, device.Serial, StringComparison.Ordinal))
            throw new InvalidOperationException("The backup target changed. Select the current device and try again.");
        if (device.State != DeviceState.Device)
            throw new InvalidOperationException("The selected device is not connected.");
        if (request.Kinds.Count == 0)
            throw new ArgumentException("Select at least one backup option.", nameof(request));

        var parentDirectory = Path.GetFullPath(request.DestinationDirectory);
        Directory.CreateDirectory(parentDirectory);
        var destination = Path.Combine(
            parentDirectory,
            $"backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destination);
        var artifacts = new List<BackupArtifact>();
        var warnings = new List<string>();
        var kinds = request.Kinds.OrderBy(kind => kind).ToArray();

        for (var index = 0; index < kinds.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = kinds[index];
            progress?.Report(new BackupProgress(kind, index, kinds.Length, $"Backing up {DisplayName(kind)}…"));
            try
            {
                var artifact = kind switch
                {
                    BackupKind.DeviceReport => await WriteInspectionAsync(device, destination, cancellationToken),
                    BackupKind.ConfigurationSnapshot => await WriteConfigurationAsync(device, destination, cancellationToken),
                    BackupKind.PackageApks => await PullApksAsync(device, destination, cancellationToken),
                    BackupKind.SharedStorage => await PullSharedStorageAsync(device, request, destination, cancellationToken),
                    BackupKind.LegacyAppData => await CreateLegacyBackupAsync(device, destination, cancellationToken),
                    BackupKind.FullDeviceImage => UnsupportedArtifact(kind, "Requires supported root, recovery, or vendor tooling; no image command was run."),
                    _ => UnsupportedArtifact(kind, "Unsupported backup option.")
                };
                artifact = artifact with
                {
                    Sha256 = await HashFileAsync(artifact.Path, cancellationToken)
                };
                artifacts.Add(artifact);
                if (artifact.State != CapabilityState.Supported)
                    warnings.Add($"{artifact.Name}: {artifact.Details ?? artifact.State.ToString()}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.Warning("Backup", $"{DisplayName(kind)} failed for {device.Serial}: {exception.Message}");
                artifacts.Add(new BackupArtifact(kind, DisplayName(kind), destination, null,
                    CapabilityState.Unavailable, exception.Message));
                warnings.Add($"{DisplayName(kind)} failed: {exception.Message}");
            }
        }

        var checksumPath = Path.Combine(destination, "SHA256SUMS.txt");
        await WriteChecksumsAsync(destination, checksumPath, cancellationToken);
        artifacts.Add(new BackupArtifact(
            BackupKind.DeviceReport,
            "Backup checksums",
            checksumPath,
            new FileInfo(checksumPath).Length,
            CapabilityState.Supported,
            "SHA-256 checksums for files in this backup.",
            await HashFileAsync(checksumPath, cancellationToken)));

        var manifest = new DeviceBackupManifest(
            device.Serial,
            device.FriendlyName,
            DateTimeOffset.UtcNow,
            kinds,
            artifacts,
            warnings);
        var manifestPath = Path.Combine(destination, "backup-manifest.json");
        await WriteJsonAsync(manifestPath, manifest, cancellationToken);
        artifacts.Add(new BackupArtifact(
            BackupKind.DeviceReport,
            "Backup manifest",
            manifestPath,
            new FileInfo(manifestPath).Length,
            CapabilityState.Supported,
            "Lists requested options, artifacts, warnings, and target identity."));
        progress?.Report(new BackupProgress(
            kinds[^1],
            kinds.Length,
            kinds.Length,
            "Backup completed."));
        return new DeviceBackupResult(device.Serial, destination, manifest.CreatedUtc, artifacts, warnings);
    }

    public async Task<BackupRestoreResult> RestoreApksAsync(
        string serial,
        string backupDirectory,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(backupDirectory);
        if (!Directory.Exists(root))
            return new BackupRestoreResult(serial, 0, 0, ["The selected backup folder does not exist."]);
        var manifestPath = Path.Combine(root, "backup-manifest.json");
        if (!File.Exists(manifestPath))
            return new BackupRestoreResult(serial, 0, 0, ["backup-manifest.json was not found; choose a folder created by Android TV Manager."]);

        try
        {
            await using var manifestStream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<DeviceBackupManifest>(
                manifestStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);
            if (manifest is null)
                return new BackupRestoreResult(serial, 0, 0, ["The backup manifest is invalid."]);
            if (string.IsNullOrWhiteSpace(manifest.Serial)
                || !string.Equals(manifest.Serial.Trim(), serial.Trim(), StringComparison.OrdinalIgnoreCase))
                return new BackupRestoreResult(
                    serial,
                    0,
                    0,
                    [string.IsNullOrWhiteSpace(manifest.Serial)
                        ? "The backup manifest does not identify its source device."
                        : $"The backup belongs to device {manifest.Serial}, not the selected device."]);
        }
        catch (JsonException)
        {
            return new BackupRestoreResult(serial, 0, 0, ["The backup manifest is invalid."]);
        }

        var apkRoot = Path.Combine(root, "apks");
        if (!Directory.Exists(apkRoot))
            return new BackupRestoreResult(serial, 0, 0, ["No APK backup folder was found."]);

        var messages = new List<string>();
        var verification = await VerifyBackupAsync(root, apkRoot, cancellationToken);
        if (!verification.IsValid)
        {
            messages.AddRange(verification.Messages);
            return new BackupRestoreResult(
                serial,
                0,
                Directory.EnumerateDirectories(apkRoot).Count(),
                messages);
        }
        var restored = 0;
        var failed = 0;
        foreach (var packageDirectory in Directory.EnumerateDirectories(apkRoot).OrderBy(path => path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var apks = Directory.EnumerateFiles(packageDirectory, "*.apk", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (apks.Length == 0)
                continue;

            var arguments = apks.Length == 1
                ? new[] { "install", "-r", apks[0] }
                : new[] { "install-multiple", "-r" }.Concat(apks).ToArray();
            var result = await _runner.RunForDeviceAsync(serial, arguments, PullTimeout, cancellationToken);
            var packageName = Path.GetFileName(packageDirectory);
            if (result.IsSuccess)
            {
                restored++;
                messages.Add($"{packageName}: restored.");
            }
            else
            {
                failed++;
                messages.Add($"{packageName}: {FirstLine(result.StandardError, "APK restore failed.")}");
            }
        }
        return new BackupRestoreResult(serial, restored, failed, messages);
    }

    private static async Task<(bool IsValid, IReadOnlyList<string> Messages)> VerifyBackupAsync(
        string root,
        string apkRoot,
        CancellationToken cancellationToken)
    {
        var checksumPath = Path.Combine(root, "SHA256SUMS.txt");
        if (!File.Exists(checksumPath))
            return (false, ["Backup verification failed: SHA256SUMS.txt is missing."]);
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in await File.ReadAllLinesAsync(checksumPath, cancellationToken))
        {
            var fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 2)
                expected[fields[1].TrimStart('*').Replace('/', Path.DirectorySeparatorChar)] = fields[0];
        }

        var errors = new List<string>();
        foreach (var file in Directory.EnumerateFiles(apkRoot, "*.apk", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!expected.TryGetValue(relative.Replace('/', Path.DirectorySeparatorChar), out var expectedHash)
                && !expected.TryGetValue(relative, out expectedHash))
            {
                errors.Add($"{relative}: no checksum entry.");
                continue;
            }
            var actualHash = await HashFileAsync(file, cancellationToken);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                errors.Add($"{relative}: checksum mismatch.");
        }
        return errors.Count == 0
            ? (true, [])
            : (false, errors.Prepend("Backup verification failed; no APKs were installed.").ToArray());
    }

    private async Task<BackupArtifact> WriteInspectionAsync(
        AndroidDevice device,
        string destination,
        CancellationToken cancellationToken)
    {
        var inspection = await _inspection.InspectAsync(device.Serial, cancellationToken: cancellationToken);
        var path = Path.Combine(destination, "device-inspection.json");
        await WriteJsonAsync(path, inspection, cancellationToken);
        return SupportedArtifact(BackupKind.DeviceReport, "Device inspection report", path);
    }

    private async Task<BackupArtifact> WriteConfigurationAsync(
        AndroidDevice device,
        string destination,
        CancellationToken cancellationToken)
    {
        var snapshot = await _configuration.InspectAsync(
            device.Serial,
            device.FriendlyName,
            cancellationToken: cancellationToken);
        var path = Path.Combine(destination, "configuration-snapshot.json");
        await WriteJsonAsync(path, snapshot, cancellationToken);
        return SupportedArtifact(BackupKind.ConfigurationSnapshot, "Configuration snapshot", path);
    }

    private async Task<BackupArtifact> PullApksAsync(
        AndroidDevice device,
        string destination,
        CancellationToken cancellationToken)
    {
        var inventory = await _inventory.GetInventoryAsync(device.Serial, cancellationToken);
        var apkRoot = Path.Combine(destination, "apks");
        Directory.CreateDirectory(apkRoot);
        var failures = 0;
        var pulled = 0;
        foreach (var package in inventory.Packages.Where(package => package.IsInstalled))
        {
            var packageDirectory = Path.Combine(apkRoot, SafeFileName(package.PackageName));
            Directory.CreateDirectory(packageDirectory);
            foreach (var remotePath in package.ApkPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = SafeFileName(Path.GetFileName(remotePath));
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;
                var localPath = Path.Combine(packageDirectory, fileName);
                var result = await _runner.RunForDeviceAsync(
                    device.Serial,
                    ["pull", remotePath, localPath],
                    PullTimeout,
                    cancellationToken);
                if (result.IsSuccess)
                    pulled++;
                else
                    failures++;
            }
        }

        var details = failures == 0
            ? $"Pulled {pulled} APK file(s)."
            : $"Pulled {pulled} APK file(s); {failures} could not be read.";
        return new BackupArtifact(
            BackupKind.PackageApks,
            "APK and split APK backup",
            apkRoot,
            DirectorySize(apkRoot),
            failures == 0 ? CapabilityState.Supported : CapabilityState.Partial,
            details);
    }

    private async Task<BackupArtifact> PullSharedStorageAsync(
        AndroidDevice device,
        BackupRequest request,
        string destination,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(destination, "shared-storage");
        Directory.CreateDirectory(path);
        var result = await _runner.RunForDeviceAsync(
            device.Serial,
            ["pull", request.SharedStoragePath, path],
            PullTimeout,
            cancellationToken);
        return result.IsSuccess
            ? SupportedArtifact(BackupKind.SharedStorage, "Shared/user storage", path)
            : new BackupArtifact(BackupKind.SharedStorage, "Shared/user storage", path, DirectorySize(path),
                CapabilityState.Partial, FirstLine(result.StandardError, "ADB could not pull the selected path."));
    }

    private async Task<BackupArtifact> CreateLegacyBackupAsync(
        AndroidDevice device,
        string destination,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(destination, "legacy-app-data.ab");
        var result = await _runner.RunForDeviceAsync(
            device.Serial,
            ["backup", "-apk", "-obb", "-shared", "-all", "-f", path],
            PullTimeout,
            cancellationToken);
        return result.IsSuccess
            ? SupportedArtifact(BackupKind.LegacyAppData, "Legacy app-data backup", path)
            : new BackupArtifact(BackupKind.LegacyAppData, "Legacy app-data backup", path,
                File.Exists(path) ? new FileInfo(path).Length : null,
                CapabilityState.Unsupported,
                FirstLine(result.StandardError, "The connected Android build does not support legacy ADB backup."));
    }

    private static IReadOnlyList<BackupCapability> AllCapabilities(CapabilityState state, string evidence)
        => Enum.GetValues<BackupKind>()
            .Select(kind => new BackupCapability(kind, DisplayName(kind), state, evidence, evidence))
            .ToArray();

    private static BackupArtifact SupportedArtifact(BackupKind kind, string name, string path)
        => new(kind, name, path, File.Exists(path) ? new FileInfo(path).Length : DirectorySize(path),
            CapabilityState.Supported);

    private static BackupArtifact UnsupportedArtifact(BackupKind kind, string details)
        => new(kind, DisplayName(kind), string.Empty, null, CapabilityState.Unsupported, details);

    private static async Task<string?> HashFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            useAsync: true);
        return Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(
            stream,
            cancellationToken)).ToLowerInvariant();
    }

    private static async Task WriteChecksumsAsync(
        string destination,
        string checksumPath,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        foreach (var file in Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories)
                     .Where(file => !string.Equals(file, checksumPath, StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(file, Path.Combine(destination, "backup-manifest.json"),
                             StringComparison.OrdinalIgnoreCase))
                     .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hash = await HashFileAsync(file, cancellationToken);
            if (hash is not null)
                lines.Add($"{hash}  {Path.GetRelativePath(destination, file).Replace('\\', '/')}");
        }
        await File.WriteAllLinesAsync(checksumPath, lines, cancellationToken);
    }

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, new JsonSerializerOptions
        {
            WriteIndented = true
        }, cancellationToken);
    }

    private static long? DirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return null;
        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Select(file =>
            {
                try
                {
                    return new FileInfo(file).Length;
                }
                catch
                {
                    return 0L;
                }
            })
            .Sum();
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character));
    }

    private static string DisplayName(BackupKind kind)
        => kind switch
        {
            BackupKind.DeviceReport => "Device inspection report",
            BackupKind.ConfigurationSnapshot => "Configuration snapshot",
            BackupKind.PackageApks => "APK and split APK backup",
            BackupKind.SharedStorage => "Shared/user storage",
            BackupKind.LegacyAppData => "Legacy app-data backup",
            BackupKind.FullDeviceImage => "Full device image",
            _ => kind.ToString()
        };

    private static string FirstLine(string? text, string fallback)
        => text?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
            ?? fallback;
}
