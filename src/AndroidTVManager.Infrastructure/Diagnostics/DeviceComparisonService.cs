using System.Text.Json;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Diagnostics;

public sealed class DeviceComparisonService : IDeviceComparisonService
{
    private readonly IDeviceInspectionService _inspection;
    private readonly IPackageInventoryService _inventory;

    public DeviceComparisonService(
        IDeviceInspectionService inspection,
        IPackageInventoryService inventory)
    {
        _inspection = inspection;
        _inventory = inventory;
    }

    public async Task<DeviceComparisonResult> CompareAsync(
        AndroidDevice left,
        AndroidDevice right,
        CancellationToken cancellationToken = default)
    {
        if (left.Serial.Equals(right.Serial, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose two different devices for comparison.");
        var leftInspectionTask = _inspection.InspectAsync(left.Serial, cancellationToken: cancellationToken);
        var rightInspectionTask = _inspection.InspectAsync(right.Serial, cancellationToken: cancellationToken);
        var leftPackagesTask = _inventory.GetInventoryAsync(left.Serial, cancellationToken);
        var rightPackagesTask = _inventory.GetInventoryAsync(right.Serial, cancellationToken);
        await Task.WhenAll(leftInspectionTask, rightInspectionTask, leftPackagesTask, rightPackagesTask);
        var leftInspection = await leftInspectionTask;
        var rightInspection = await rightInspectionTask;
        var leftPackages = await leftPackagesTask;
        var rightPackages = await rightPackagesTask;
        var sections = new List<DeviceComparisonSection>
        {
            Compare("Build and identity", leftInspection.Overview.Value, rightInspection.Overview.Value),
            Compare("CPU", leftInspection.Cpu.Value, rightInspection.Cpu.Value),
            Compare("Memory", leftInspection.Memory.Value, rightInspection.Memory.Value),
            Compare("Display", leftInspection.Display.Value, rightInspection.Display.Value),
            Compare("Storage", leftInspection.Storage.Value, rightInspection.Storage.Value),
            Compare("Security", leftInspection.Security.Value, rightInspection.Security.Value),
            Compare("Network", leftInspection.Network.Value, rightInspection.Network.Value),
            Compare("Bluetooth", leftInspection.Bluetooth?.Value, rightInspection.Bluetooth?.Value),
            Compare("HDMI", leftInspection.Hdmi?.Value, rightInspection.Hdmi?.Value),
            Compare("DRM", leftInspection.Drm?.Value, rightInspection.Drm?.Value),
            Compare("Features", leftInspection.Features.Value, rightInspection.Features.Value),
            Compare("Services", leftInspection.Services.Value, rightInspection.Services.Value),
            ComparePackages(leftPackages, rightPackages)
        };
        return new(left.Serial, right.Serial, sections, DateTimeOffset.UtcNow);
    }

    private static DeviceComparisonSection Compare<T>(string name, T? left, T? right)
    {
        var leftSummary = Summarize(left);
        var rightSummary = Summarize(right);
        return new(name, leftSummary, rightSummary, !string.Equals(leftSummary, rightSummary, StringComparison.Ordinal));
    }

    private static DeviceComparisonSection ComparePackages(
        PackageInventoryResult left,
        PackageInventoryResult right)
    {
        var leftSet = left.Packages.Select(package => package.PackageName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rightSet = right.Packages.Select(package => package.PackageName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var onlyLeft = leftSet.Except(rightSet, StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToArray();
        var onlyRight = rightSet.Except(leftSet, StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToArray();
        return new(
            "Packages",
            $"{leftSet.Count} total; only this device: {string.Join(", ", onlyLeft)}",
            $"{rightSet.Count} total; only this device: {string.Join(", ", onlyRight)}",
            onlyLeft.Length > 0 || onlyRight.Length > 0);
    }

    private static string Summarize<T>(T? value)
        => value is null
            ? "Unavailable"
            : JsonSerializer.Serialize(value);
}
