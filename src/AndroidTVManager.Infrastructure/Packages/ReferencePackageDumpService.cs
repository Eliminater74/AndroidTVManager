using System.Text.Json;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Packages;

public sealed class ReferencePackageDumpService : IReferencePackageDumpService
{
    public async Task ExportAsync(
        AndroidDevice device,
        PackageInventoryResult inventory,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(inventory);
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("An output path is required.", nameof(outputPath));

        var dump = new ReferencePackageDump(
            1,
            "Android TV Manager read-only package reference",
            inventory.CapturedUtc,
            new ReferenceDeviceIdentity(
                device.Manufacturer,
                device.Brand,
                device.Model,
                device.DeviceName,
                device.Product,
                device.AndroidVersion,
                device.ApiLevel,
                device.SecurityPatch,
                device.BuildId,
                device.BuildType,
                device.BuildFingerprint),
            inventory.Packages.Select(package => new ReferencePackageDumpPackage(
                package.PackageName,
                package.Label,
                package.VersionName,
                package.VersionCode,
                package.UserId,
                package.IsSystem,
                package.IsUpdatedSystem,
                package.IsEnabled,
                package.IsInstalled,
                package.IsUninstalledForUser,
                package.ApkPaths,
                package.IsActiveLauncher,
                package.IsDefaultInputMethod,
                package.IsEnabledAccessibilityService,
                package.IsDeviceOwner)).ToArray());

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        await using var stream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(
            stream,
            dump,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            },
            cancellationToken);
    }
}
