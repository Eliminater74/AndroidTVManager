using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AndroidTVManager.App.ViewModels;

public sealed partial class ConfigurationPageViewModel : PageViewModel
{
    private readonly IConfigurationExplorerService _explorer;
    private readonly IConfigurationSnapshotStore _snapshots;
    private CancellationTokenSource? _inspectionSource;

    public ConfigurationPageViewModel(
        IConfigurationExplorerService explorer,
        IConfigurationSnapshotStore snapshots,
        ObservableCollection<AndroidDevice> devices,
        Action<AndroidDevice?>? targetChanged = null) : base("Configuration Explorer")
    {
        _explorer = explorer;
        _snapshots = snapshots;
        Devices = devices;
        TargetChanged = targetChanged;
        Devices.CollectionChanged += (_, _) =>
        {
            if (SelectedDevice is null)
                SelectedDevice = Devices.FirstOrDefault(device => device.State == DeviceState.Device);
        };
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public Action<AndroidDevice?>? TargetChanged { get; }

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    private ConfigurationSnapshot? _snapshot;

    [ObservableProperty]
    private ConfigurationSnapshotDiff? _difference;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private string _status = "Select a connected device to explore its configuration.";

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public IReadOnlyList<ConfigurationSection> FilteredSections
        => Snapshot?.Sections
            .Select(section => section with
            {
                Properties = section.Properties
                    .Where(MatchesSearch)
                    .ToArray()
            })
            .Where(section => section.Properties.Count > 0 || string.IsNullOrWhiteSpace(Search))
            .ToArray()
            ?? [];

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
    {
        TargetChanged?.Invoke(value);
        _inspectionSource?.Cancel();
        Snapshot = null;
        Difference = null;
        ProgressText = string.Empty;
        Status = value is null
            ? "Select a connected device to explore its configuration."
            : $"Ready to read configuration from {value.FriendlyName}.";
        if (value?.State == DeviceState.Device)
            _ = RefreshAsync();
    }

    partial void OnSearchChanged(string value)
        => OnPropertyChanged(nameof(FilteredSections));

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (SelectedDevice is null || SelectedDevice.State != DeviceState.Device)
        {
            Status = "Select a connected device before scanning configuration.";
            return;
        }

        _inspectionSource?.Cancel();
        _inspectionSource?.Dispose();
        _inspectionSource = new CancellationTokenSource();
        var cancellationToken = _inspectionSource.Token;
        IsLoading = true;
        Status = $"Reading configuration from {SelectedDevice.FriendlyName}…";
        ProgressText = "Starting read-only configuration scan…";
        try
        {
            var previous = await _snapshots.GetLatestAsync(SelectedDevice.Serial, cancellationToken);
            var progress = new Progress<ConfigurationInspectionProgress>(value =>
            {
                ProgressText = $"{value.Category} · {value.CompletedCategories}/{value.TotalCategories} · {value.State}";
            });
            var snapshot = await _explorer.InspectAsync(
                SelectedDevice.Serial,
                SelectedDevice.FriendlyName,
                progress,
                cancellationToken);
            Snapshot = snapshot;
            Difference = previous is null
                ? null
                : ConfigurationSnapshotComparer.Compare(previous, snapshot);
            Status = snapshot.Sections.Any(section => section.State == InspectionSectionState.Partial)
                ? $"{snapshot.Properties.Count} properties loaded with unavailable sources."
                : $"{snapshot.Properties.Count} properties loaded.";
            ProgressText = $"Captured {snapshot.CapturedUtc.LocalDateTime:g}.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Configuration scan canceled.";
            ProgressText = string.Empty;
        }
        catch (Exception exception)
        {
            Status = $"Configuration scan failed: {exception.Message}";
            ProgressText = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _inspectionSource?.Cancel();
        Status = "Canceling configuration scan…";
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Snapshot is null)
        {
            Status = "Run a configuration scan before exporting.";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON snapshot (*.json)|*.json|Text report (*.txt)|*.txt",
            DefaultExt = ".json",
            FileName = $"android-tv-configuration-{Snapshot.Serial.Replace(':', '-')}.json",
            Title = "Export configuration snapshot"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var content = Path.GetExtension(dialog.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase)
                ? BuildTextReport(Snapshot)
                : JsonSerializer.Serialize(Snapshot, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            await File.WriteAllTextAsync(dialog.FileName, content);
            Status = $"Configuration exported to {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception exception)
        {
            Status = $"Export failed: {exception.Message}";
        }
    }

    private bool MatchesSearch(ConfigurationProperty property)
    {
        if (string.IsNullOrWhiteSpace(Search))
            return true;
        var query = Search.Trim();
        return property.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || property.DisplayValue.Contains(query, StringComparison.OrdinalIgnoreCase)
            || property.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
            || property.SourceSummary.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildTextReport(ConfigurationSnapshot snapshot)
    {
        var report = new StringBuilder();
        report.AppendLine("Android TV Manager - Configuration Report");
        report.AppendLine($"Serial: {snapshot.Serial}");
        report.AppendLine($"Friendly device: {snapshot.FriendlyDeviceName ?? "Unknown"}");
        report.AppendLine($"Manufacturer: {snapshot.Manufacturer ?? "Unknown"}");
        report.AppendLine($"Model: {snapshot.Model ?? "Unknown"}");
        report.AppendLine($"Android: {snapshot.AndroidVersion ?? "Unknown"} (API {snapshot.ApiLevel?.ToString() ?? "Unknown"})");
        report.AppendLine($"Build fingerprint: {snapshot.BuildFingerprint ?? "Unknown"}");
        report.AppendLine($"Security patch: {snapshot.SecurityPatch ?? "Unknown"}");
        report.AppendLine($"Captured: {snapshot.CapturedUtc:O}");
        report.AppendLine();
        foreach (var section in snapshot.Sections)
        {
            report.AppendLine($"[{section.Name}]");
            foreach (var property in section.Properties)
            {
                report.AppendLine($"{property.Name} = {property.DisplayValue}");
                report.AppendLine($"  Status: {property.StatusLabel}; Sources: {property.SourceSummary}");
                foreach (var source in property.StaticValues.Where(source => source.Value is not null || !source.IsAvailable))
                    report.AppendLine($"  {source.SourceName}: {source.Value ?? source.Error ?? "Unavailable"}");
            }
            report.AppendLine();
        }
        return report.ToString();
    }
}
