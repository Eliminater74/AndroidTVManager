using System.Collections.ObjectModel;
using System.IO;
using AndroidTVManager.App.Services;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVManager.App.ViewModels;

public sealed partial class AdvancedDiagnosticsPageViewModel : PageViewModel
{
    private readonly INetworkDiagnosticsService _network;
    private readonly ICodecInspectionService _codecs;
    private readonly IBootInspectionService _boot;
    private readonly IDeviceFileService _files;
    private readonly IScreenRecordingService _recording;
    private readonly IConfirmationService _confirmation;
    private readonly Action<AndroidDevice?> _selectTarget;

    public AdvancedDiagnosticsPageViewModel(
        INetworkDiagnosticsService network,
        ICodecInspectionService codecs,
        IBootInspectionService boot,
        IDeviceFileService files,
        IScreenRecordingService recording,
        IConfirmationService confirmation,
        ObservableCollection<AndroidDevice> devices,
        Action<AndroidDevice?> selectTarget) : base("Advanced Diagnostics")
    {
        _network = network;
        _codecs = codecs;
        _boot = boot;
        _files = files;
        _recording = recording;
        _confirmation = confirmation;
        Devices = devices;
        _selectTarget = selectTarget;
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public ObservableCollection<DeviceFileEntry> FileEntries { get; } = [];

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    private string _networkOutput = "Network inspection has not run.";

    [ObservableProperty]
    private string _codecOutput = "Codec inspection has not run.";

    [ObservableProperty]
    private string _bootOutput = "Boot/USB inspection has not run.";

    [ObservableProperty]
    private string _remoteDirectory = "/sdcard";

    [ObservableProperty]
    private DeviceFileEntry? _selectedFile;

    [ObservableProperty]
    private string _newDirectoryName = string.Empty;

    [ObservableProperty]
    private string _status = "Select a connected device and choose a diagnostic action.";

    [ObservableProperty]
    private int _recordingDurationSeconds = 60;

    [ObservableProperty]
    private string _recordingStatus = "No screen recording is active.";

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
        => _selectTarget(value);

    [RelayCommand]
    private async Task InspectNetworkAsync()
    {
        if (!TryGetSerial(out var serial))
            return;
        var result = await _network.InspectAsync(serial);
        NetworkOutput = $"INTERFACES{Environment.NewLine}{result.InterfaceOutput}{Environment.NewLine}{Environment.NewLine}"
            + $"ROUTES{Environment.NewLine}{result.RouteOutput}{Environment.NewLine}{Environment.NewLine}"
            + $"DNS{Environment.NewLine}{result.DnsOutput}{Environment.NewLine}{Environment.NewLine}"
            + $"PING{Environment.NewLine}{result.PingOutput}";
        Status = "Network inspection complete.";
    }

    [RelayCommand]
    private async Task InspectCodecsAsync()
    {
        if (!TryGetSerial(out var serial))
            return;
        var result = await _codecs.InspectAsync(serial);
        CodecOutput = result.Codecs.Count == 0
            ? result.RawOutput
            : string.Join(Environment.NewLine, result.Codecs.Select(codec => $"{codec.Type}: {codec.Name}"));
        Status = $"Codec inspection found {result.Codecs.Count} codec entries.";
    }

    [RelayCommand]
    private async Task InspectBootAsync()
    {
        var result = await _boot.InspectAsync();
        BootOutput = $"{result.State} · Serial: {result.Serial ?? "none"}{Environment.NewLine}"
            + $"Product: {result.Product ?? "unknown"} · Slot: {result.Slot ?? "unknown"} · Unlocked: {result.UnlockedState ?? "unknown"}"
            + $"{Environment.NewLine}{result.Evidence}";
        Status = "Boot/USB inspection complete. No flashing or erase operation was performed.";
    }

    [RelayCommand]
    private async Task RefreshFilesAsync()
    {
        if (!TryGetSerial(out var serial))
            return;
        try
        {
            var entries = await _files.ListAsync(serial, RemoteDirectory);
            FileEntries.Clear();
            foreach (var entry in entries)
                FileEntries.Add(entry);
            Status = $"Listed {FileEntries.Count} item(s) in {RemoteDirectory}.";
        }
        catch (Exception exception)
        {
            Status = $"File listing failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task PullFileAsync()
    {
        if (!TryGetSerial(out var serial) || SelectedFile is null || SelectedFile.IsDirectory)
            return;
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = SelectedFile.Name };
        if (dialog.ShowDialog() != true)
            return;
        var result = await _files.PullAsync(serial, SelectedFile.Path, dialog.FileName);
        Status = result.IsSuccess ? $"Pulled {SelectedFile.Name}." : $"Pull failed: {result.StandardError.Trim()}";
    }

    [RelayCommand]
    private async Task PushFileAsync()
    {
        if (!TryGetSerial(out var serial))
            return;
        var dialog = new Microsoft.Win32.OpenFileDialog();
        if (dialog.ShowDialog() != true)
            return;
        var destination = RemoteDirectory.TrimEnd('/') + "/" + Path.GetFileName(dialog.FileName);
        var result = await _files.PushAsync(serial, dialog.FileName, destination);
        Status = result.IsSuccess ? $"Pushed {Path.GetFileName(dialog.FileName)}." : $"Push failed: {result.StandardError.Trim()}";
    }

    [RelayCommand]
    private async Task CreateDirectoryAsync()
    {
        if (!TryGetSerial(out var serial))
            return;
        var name = NewDirectoryName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;
        var path = RemoteDirectory.TrimEnd('/') + "/" + Path.GetFileName(name);
        var result = await _files.CreateDirectoryAsync(serial, path);
        Status = result.IsSuccess ? $"Created {path}." : $"Create directory failed: {result.StandardError.Trim()}";
        if (result.IsSuccess)
            NewDirectoryName = string.Empty;
        if (result.IsSuccess)
            await RefreshFilesAsync();
    }

    [RelayCommand]
    private async Task DeleteFileAsync()
    {
        if (!TryGetSerial(out var serial) || SelectedFile is null)
            return;
        if (!_confirmation.Confirm("Delete device file", $"Delete {SelectedFile.Path}? This cannot be undone."))
            return;
        var result = await _files.DeleteAsync(serial, SelectedFile.Path);
        Status = result.IsSuccess ? $"Deleted {SelectedFile.Name}." : $"Delete failed: {result.StandardError.Trim()}";
        if (result.IsSuccess)
            await RefreshFilesAsync();
    }

    [RelayCommand]
    private async Task StartRecordingAsync()
    {
        if (!TryGetSerial(out var serial))
            return;
        try
        {
            var recording = await _recording.StartAsync(
                serial,
                TimeSpan.FromSeconds(Math.Clamp(RecordingDurationSeconds, 1, 1800)));
            RecordingStatus = $"Recording for up to {RecordingDurationSeconds} seconds: {recording.RemotePath}";
        }
        catch (Exception exception)
        {
            RecordingStatus = $"Recording failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        try
        {
            var path = await _recording.StopAsync();
            RecordingStatus = path is null ? "No recording was pulled from the device." : $"Recording saved to {path}";
        }
        catch (Exception exception)
        {
            RecordingStatus = $"Stopping recording failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task RebootAsync(string mode)
    {
        if (!TryGetSerial(out var serial))
            return;
        var label = string.IsNullOrWhiteSpace(mode) ? "system" : mode;
        if (!_confirmation.Confirm(
                $"Reboot to {label}",
                $"Reboot {SelectedDevice!.FriendlyName ?? serial} to {label}? This may temporarily disconnect ADB."))
            return;
        var result = await _boot.RebootAsync(serial, mode);
        Status = result.IsSuccess ? $"Reboot to {label} requested." : $"Reboot failed: {result.StandardError.Trim()}";
    }

    private bool TryGetSerial(out string serial)
    {
        serial = SelectedDevice?.Serial.Trim() ?? string.Empty;
        if (SelectedDevice?.State == DeviceState.Device && serial.Length > 0)
            return true;
        Status = "Select a connected and authorized device first.";
        return false;
    }
}
