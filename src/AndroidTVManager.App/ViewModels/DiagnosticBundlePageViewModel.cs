using System.Collections.ObjectModel;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVManager.App.ViewModels;

public sealed partial class DiagnosticBundlePageViewModel : PageViewModel
{
    private readonly IDiagnosticBundleService _bundles;
    private readonly Action<AndroidDevice?> _selectTarget;

    public DiagnosticBundlePageViewModel(
        IDiagnosticBundleService bundles,
        ObservableCollection<AndroidDevice> devices,
        Action<AndroidDevice?> selectTarget) : base("Diagnostic Bundles")
    {
        _bundles = bundles;
        Devices = devices;
        _selectTarget = selectTarget;
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public IReadOnlyList<DiagnosticBundlePrivacyMode> PrivacyModes { get; } =
        Enum.GetValues<DiagnosticBundlePrivacyMode>();

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    private DiagnosticBundlePrivacyMode _privacyMode = DiagnosticBundlePrivacyMode.SupportRedacted;

    [ObservableProperty]
    private int _logcatLineLimit = 500;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "Create a support bundle containing evidence from the selected device.";

    [ObservableProperty]
    private string _lastArchivePath = string.Empty;

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
        => _selectTarget(value);

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (SelectedDevice is null || SelectedDevice.State != DeviceState.Device)
        {
            Status = "Select a connected and authorized device.";
            return;
        }
        IsBusy = true;
        try
        {
            Status = "Collecting device, display, transport, configuration, and logcat evidence…";
            var result = await _bundles.CreateAsync(new(
                SelectedDevice,
                AppInfo.Version,
                PrivacyMode,
                Math.Clamp(LogcatLineLimit, 50, 5000)));
            LastArchivePath = result.ArchivePath;
            Status = $"Bundle created with {result.IncludedFiles.Count} file(s): {result.ArchivePath}"
                + (result.Warnings.Count == 0 ? string.Empty : $" {result.Warnings.Count} warning(s).");
        }
        catch (Exception exception)
        {
            Status = $"Bundle creation failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (string.IsNullOrWhiteSpace(LastArchivePath))
            return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = System.IO.Path.GetDirectoryName(LastArchivePath)!,
            UseShellExecute = true
        });
    }
}
