using System.Collections.ObjectModel;
using System.IO;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVManager.App.ViewModels;

public sealed partial class BackupOptionViewModel : ObservableObject
{
    public BackupOptionViewModel(BackupCapability capability, bool isSelected)
    {
        Capability = capability;
        _isSelected = isSelected;
    }

    public BackupCapability Capability { get; }
    public BackupKind Kind => Capability.Kind;
    public string Name => Capability.Name;
    public string Description => Capability.Description;
    public string Evidence => Capability.Evidence;
    public CapabilityState State => Capability.State;
    public string StateLabel => State switch
    {
        CapabilityState.Supported => "AVAILABLE",
        CapabilityState.Partial => "PARTIAL",
        CapabilityState.Unsupported => "UNSUPPORTED",
        CapabilityState.Unavailable => "UNAVAILABLE",
        CapabilityState.PermissionDenied => "PERMISSION DENIED",
        _ => "UNKNOWN"
    };
    public bool CanSelect => State is CapabilityState.Supported or CapabilityState.Partial;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class BackupPageViewModel : PageViewModel
{
    private readonly IDeviceBackupService _backup;
    private readonly ILocalAppDataPaths _paths;
    private CancellationTokenSource? _operationSource;

    public BackupPageViewModel(
        IDeviceBackupService backup,
        ILocalAppDataPaths paths,
        ObservableCollection<AndroidDevice> devices) : base("Backup / Restore")
    {
        _backup = backup;
        _paths = paths;
        Devices = devices;
        DestinationDirectory = paths.BackupsPath;
        RestoreDirectory = paths.BackupsPath;
        foreach (var device in devices)
        {
            if (device.State == DeviceState.Device)
            {
                SelectedDevice = device;
                break;
            }
        }
        devices.CollectionChanged += (_, _) =>
        {
            if (SelectedDevice is null)
                SelectedDevice = devices.FirstOrDefault(device => device.State == DeviceState.Device);
        };
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public ObservableCollection<BackupOptionViewModel> Options { get; } = [];

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    private string _destinationDirectory;

    [ObservableProperty]
    private string _restoreDirectory;

    [ObservableProperty]
    private string _status = "Select a connected device to check backup capabilities.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private DeviceBackupResult? _lastBackup;

    [ObservableProperty]
    private BackupRestoreResult? _lastRestore;

    public int SelectedOptionCount => Options.Count(option => option.IsSelected);
    public bool HasOptions => Options.Count > 0;

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
    {
        _operationSource?.Cancel();
        Options.Clear();
        LastBackup = null;
        LastRestore = null;
        OnPropertyChanged(nameof(SelectedOptionCount));
        if (value is null)
        {
            Status = "Select a connected device to check backup capabilities.";
            return;
        }
        _ = LoadCapabilitiesAsync(value);
    }

    [RelayCommand]
    private async Task RefreshCapabilitiesAsync()
    {
        if (SelectedDevice is null)
        {
            Status = "Select a connected device first.";
            return;
        }
        await LoadCapabilitiesAsync(SelectedDevice);
    }

    [RelayCommand]
    private void BrowseDestination()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose where this device backup will be stored.",
            SelectedPath = Directory.Exists(DestinationDirectory)
                ? DestinationDirectory
                : _paths.BackupsPath,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            DestinationDirectory = dialog.SelectedPath;
    }

    [RelayCommand]
    private void BrowseRestoreDirectory()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a folder containing a backup-manifest.json and an apks folder.",
            SelectedPath = Directory.Exists(RestoreDirectory)
                ? RestoreDirectory
                : _paths.BackupsPath,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            RestoreDirectory = dialog.SelectedPath;
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        if (SelectedDevice is null || SelectedDevice.State != DeviceState.Device)
        {
            Status = "Select a connected device before creating a backup.";
            return;
        }
        var selected = Options.Where(option => option.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            Status = "Select at least one backup option.";
            return;
        }
        if (selected.Any(option => option.Kind is BackupKind.SharedStorage or BackupKind.LegacyAppData)
            && !Confirm(
                "Create extended backup",
                "This may copy a large amount of data or use deprecated Android backup support. Continue?"))
        {
            Status = "Backup canceled.";
            return;
        }

        var serial = SelectedDevice.Serial;
        _operationSource?.Cancel();
        _operationSource?.Dispose();
        _operationSource = new CancellationTokenSource();
        IsBusy = true;
        Status = $"Backing up {SelectedDevice.FriendlyName}…";
        try
        {
            var progress = new Progress<BackupProgress>(value =>
                Status = $"{value.Status} ({value.CompletedKinds}/{value.TotalKinds})");
            LastBackup = await _backup.CreateAsync(
                new BackupRequest(
                    serial,
                    DestinationDirectory,
                    selected.Select(option => option.Kind).ToHashSet()),
                SelectedDevice,
                progress,
                _operationSource.Token);
            Status = $"Backup completed: {LastBackup.Artifacts.Count} artifact(s) in {LastBackup.DestinationDirectory}.";
        }
        catch (OperationCanceledException)
        {
            Status = "Backup canceled.";
        }
        catch (Exception exception)
        {
            Status = $"Backup failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreApksAsync()
    {
        if (SelectedDevice is null || SelectedDevice.State != DeviceState.Device)
        {
            Status = "Select a connected device before restoring APKs.";
            return;
        }
        if (!Directory.Exists(RestoreDirectory))
        {
            Status = "Choose an existing backup folder first.";
            return;
        }
        if (!Confirm(
                "Restore APKs",
                $"Install the APKs from this backup onto {SelectedDevice.FriendlyName}?\n\n{RestoreDirectory}\n\nExisting app versions may be replaced. App data is not restored by this operation."))
        {
            Status = "Restore canceled.";
            return;
        }

        var serial = SelectedDevice.Serial;
        IsBusy = true;
        Status = "Restoring APKs…";
        try
        {
            LastRestore = await _backup.RestoreApksAsync(serial, RestoreDirectory);
            Status = $"APK restore finished: {LastRestore.RestoredPackages} restored, {LastRestore.FailedPackages} failed.";
        }
        catch (Exception exception)
        {
            Status = $"APK restore failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _operationSource?.Cancel();
        Status = "Canceling backup…";
    }

    private async Task LoadCapabilitiesAsync(AndroidDevice device)
    {
        if (device.State != DeviceState.Device)
        {
            Status = "The selected device is offline.";
            return;
        }
        IsBusy = true;
        Status = $"Checking backup capabilities for {device.FriendlyName}…";
        try
        {
            var capabilities = await _backup.GetCapabilitiesAsync(device);
            if (!ReferenceEquals(SelectedDevice, device))
                return;
            Options.Clear();
            foreach (var capability in capabilities)
            {
                var selected = capability.Kind is BackupKind.DeviceReport
                    or BackupKind.ConfigurationSnapshot
                    or BackupKind.PackageApks;
                var option = new BackupOptionViewModel(capability,
                    selected && capability.State is (CapabilityState.Supported or CapabilityState.Partial));
                option.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(BackupOptionViewModel.IsSelected))
                        OnPropertyChanged(nameof(SelectedOptionCount));
                };
                Options.Add(option);
            }
            OnPropertyChanged(nameof(HasOptions));
            OnPropertyChanged(nameof(SelectedOptionCount));
            Status = "Choose the backup types to create. Full device images require root or recovery tooling.";
        }
        catch (Exception exception)
        {
            Status = $"Capability check failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool Confirm(string title, string message)
        => System.Windows.MessageBox.Show(
            message,
            title,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
}
