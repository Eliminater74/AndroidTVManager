using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVManager.App.ViewModels;

public sealed partial class DisplayDiagnosticsPageViewModel : PageViewModel
{
    private readonly IDisplayDiagnosticsService _service;
    private readonly IDisplayDiagnosticsSnapshotStore _snapshots;
    private CancellationTokenSource? _captureSource;
    private CancellationTokenSource? _watcherSource;

    public DisplayDiagnosticsPageViewModel(
        IDisplayDiagnosticsService service,
        IDisplayDiagnosticsSnapshotStore snapshots,
        ObservableCollection<AndroidDevice> devices) : base("Display Diagnostics")
    {
        _service = service;
        _snapshots = snapshots;
        Devices = devices;
        SelectedDevice = devices.FirstOrDefault(device => device.State == DeviceState.Device);
        devices.CollectionChanged += (_, _) =>
        {
            if (SelectedDevice is null)
                SelectedDevice = devices.FirstOrDefault(device => device.State == DeviceState.Device);
        };
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public ObservableCollection<DisplayDiagnosticSnapshot> Snapshots { get; } = [];

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    private DisplayDiagnosticSnapshot? _currentSnapshot;

    [ObservableProperty]
    private DisplayDiagnosticSnapshot? _goodSnapshot;

    [ObservableProperty]
    private DisplayDiagnosticSnapshot? _badSnapshot;

    [ObservableProperty]
    private DisplayDiagnosticComparison? _comparison;

    [ObservableProperty]
    private string _status = "Select a connected device to capture display evidence.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isWatching;

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
    {
        _captureSource?.Cancel();
        StopWatching();
        CurrentSnapshot = null;
        GoodSnapshot = null;
        BadSnapshot = null;
        Comparison = null;
        Snapshots.Clear();
        if (value is null)
        {
            Status = "Select a connected device to capture display evidence.";
            return;
        }
        _ = LoadHistoryAsync(value);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (SelectedDevice is null)
        {
            Status = "Select a connected device first.";
            return;
        }
        await LoadHistoryAsync(SelectedDevice);
    }

    [RelayCommand]
    private Task CaptureGoodAsync() => CaptureAsync(DisplayCaptureLabel.Good);

    [RelayCommand]
    private Task CaptureBadAsync() => CaptureAsync(DisplayCaptureLabel.Bad);

    [RelayCommand]
    private void CompareGoodBad()
    {
        Comparison = GoodSnapshot is not null && BadSnapshot is not null
            ? DisplayDiagnosticsParser.Compare(GoodSnapshot, BadSnapshot)
            : null;
        Status = Comparison is null
            ? "Capture both a Good State and a Bad State before comparing."
            : Comparison.HasChanges
                ? $"{Comparison.Changes.Count} display change(s) found between Good and Bad."
                : "No display changes found between Good and Bad.";
    }

    [RelayCommand]
    private async Task StartWatchingAsync()
    {
        if (SelectedDevice is null || SelectedDevice.State != DeviceState.Device)
        {
            Status = "Select a connected device before starting the watcher.";
            return;
        }
        if (IsWatching)
            return;
        IsWatching = true;
        _watcherSource?.Cancel();
        _watcherSource?.Dispose();
        _watcherSource = new CancellationTokenSource();
        Status = "Display watcher is active; checking every 10 seconds.";
        _ = WatchAsync(SelectedDevice, _watcherSource.Token);
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void StopWatching()
    {
        _watcherSource?.Cancel();
        _watcherSource?.Dispose();
        _watcherSource = null;
        IsWatching = false;
        if (SelectedDevice is not null && !IsBusy)
            Status = "Display watcher stopped.";
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        _captureSource?.Cancel();
        _watcherSource?.Cancel();
        await Task.CompletedTask;
        Status = "Canceling display capture…";
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (CurrentSnapshot is null)
        {
            Status = "Capture a display state before exporting.";
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON diagnostic report (*.json)|*.json|Text report (*.txt)|*.txt",
            DefaultExt = ".json",
            FileName = $"display-diagnostic-{CurrentSnapshot.Serial.Replace(':', '-')}.json",
            Title = "Export display diagnostic"
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            var content = Path.GetExtension(dialog.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase)
                ? BuildTextReport(CurrentSnapshot, Comparison)
                : JsonSerializer.Serialize(new { Snapshot = CurrentSnapshot, Comparison }, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            await File.WriteAllTextAsync(dialog.FileName, content);
            Status = $"Display diagnostic exported to {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception exception)
        {
            Status = $"Export failed: {exception.Message}";
        }
    }

    private async Task CaptureAsync(DisplayCaptureLabel label)
    {
        if (SelectedDevice is null || SelectedDevice.State != DeviceState.Device)
        {
            Status = "Select a connected device before capturing display evidence.";
            return;
        }
        var device = SelectedDevice;
        _captureSource?.Cancel();
        _captureSource?.Dispose();
        _captureSource = new CancellationTokenSource();
        IsBusy = true;
        Status = $"Capturing {label} display state from {device.FriendlyName}…";
        try
        {
            var previous = CurrentSnapshot;
            var progress = new Progress<string>(message => Status = message);
            var snapshot = await _service.CaptureAsync(
                device.Serial,
                device.FriendlyName,
                label,
                progress,
                _captureSource.Token);
            await _snapshots.SaveAsync(snapshot, _captureSource.Token);
            CurrentSnapshot = snapshot;
            Comparison = previous is null ? null : DisplayDiagnosticsParser.Compare(previous, snapshot);
            await LoadHistoryAsync(device, preserveStatus: true);
            Status = label == DisplayCaptureLabel.Unlabeled
                ? Comparison?.HasChanges == true
                    ? $"Watcher detected {Comparison.Changes.Count} display change(s)."
                    : "Watcher capture complete; no display changes detected."
                : $"{label} state captured at {snapshot.CapturedUtc.LocalDateTime:t}.";
        }
        catch (OperationCanceledException)
        {
            Status = "Display capture canceled.";
        }
        catch (Exception exception)
        {
            Status = $"Display capture failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task WatchAsync(AndroidDevice device, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                if (device.State != DeviceState.Device || SelectedDevice?.Serial != device.Serial)
                    break;
                await CaptureAsync(DisplayCaptureLabel.Unlabeled);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (_watcherSource?.Token == cancellationToken)
                IsWatching = false;
        }
    }

    private async Task LoadHistoryAsync(AndroidDevice device, bool preserveStatus = false)
    {
        if (device.State != DeviceState.Device)
        {
            Status = "The selected device is offline.";
            return;
        }
        try
        {
            var history = await _snapshots.GetRecentAsync(device.Serial);
            if (SelectedDevice?.Serial != device.Serial)
                return;
            Snapshots.Clear();
            foreach (var snapshot in history)
                Snapshots.Add(snapshot);
            CurrentSnapshot ??= history.FirstOrDefault();
            GoodSnapshot = history.FirstOrDefault(snapshot => snapshot.Label == DisplayCaptureLabel.Good);
            BadSnapshot = history.FirstOrDefault(snapshot => snapshot.Label == DisplayCaptureLabel.Bad);
            if (GoodSnapshot is not null && BadSnapshot is not null)
                Comparison = DisplayDiagnosticsParser.Compare(GoodSnapshot, BadSnapshot);
            if (!preserveStatus)
                Status = history.Count == 0
                    ? "No saved display captures. Capture a Good State before reproducing a problem."
                    : $"{history.Count} display capture(s) loaded.";
        }
        catch (Exception exception)
        {
            Status = $"Could not load display history: {exception.Message}";
        }
    }

    private static string BuildTextReport(
        DisplayDiagnosticSnapshot snapshot,
        DisplayDiagnosticComparison? comparison)
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("Android TV Manager - Display Diagnostic Report");
        report.AppendLine($"Device: {snapshot.FriendlyDeviceName ?? "Unknown"}");
        report.AppendLine($"Serial: {snapshot.Serial}");
        report.AppendLine($"Captured: {snapshot.CapturedUtc:O}");
        report.AppendLine($"Label: {snapshot.Label}");
        report.AppendLine($"Current resolution: {snapshot.Display.CurrentResolution ?? "Unknown"}");
        report.AppendLine($"Refresh rate: {snapshot.Display.RefreshRate ?? "Unknown"}");
        report.AppendLine($"Display modes: {string.Join(", ", snapshot.Display.SupportedModes)}");
        report.AppendLine($"HDR: {string.Join(", ", snapshot.Display.HdrCapabilities)}");
        report.AppendLine($"HDCP: {snapshot.HdcpState ?? "Unknown"}");
        report.AppendLine($"CEC: {snapshot.Hdmi.CecState ?? "Unknown"}");
        report.AppendLine($"CEC physical address: {snapshot.CecPhysicalAddress ?? "Unknown"}");
        report.AppendLine($"CEC logical address: {snapshot.CecLogicalAddress ?? "Unknown"}");
        report.AppendLine($"Active input: {snapshot.Hdmi.ActiveInput ?? "Unknown"}");
        report.AppendLine($"Audio route: {snapshot.Hdmi.AudioRoute ?? "Unknown"}");
        report.AppendLine($"SurfaceFlinger modes: {string.Join(", ", snapshot.SurfaceFlingerModes)}");
        if (comparison is not null)
        {
            report.AppendLine();
            report.AppendLine("Changes");
            foreach (var change in comparison.Changes)
                report.AppendLine($"{change.Name}: {change.PreviousValue ?? "Unknown"} -> {change.CurrentValue ?? "Unknown"}");
        }
        return report.ToString();
    }
}
