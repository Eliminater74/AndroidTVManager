using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using AndroidTVManager.App.Services;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Core.Scripts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVManager.App.ViewModels;

public sealed record NavigationEntry(string Label, string Glyph);

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<string, object> _pages;
    private readonly IAdbToolsManager _toolsManager;
    private readonly IAdbDeviceTracker _deviceTracker;
    private readonly IConnectionHistoryRepository _history;
    private readonly IAdbConnectionService _connectionService;
    private readonly IApkInstaller _apkInstaller;
    private readonly IPackageManager _packageManager;
    private readonly IDeviceToolsService _toolsService;
    private readonly IDeviceRepository _deviceRepository;
    private readonly ILocalAppDataPaths _paths;
    private readonly IConfirmationService _confirmation;
    private readonly ISettingsStore _settingsStore;
    private readonly IScriptExecutionService _scriptExecutionService;
    private readonly IDeviceInspectionService _inspectionService;
    private readonly IDebloatPlanner _debloatPlanner;
    private readonly IDebloatExecutionService _debloatExecutionService;
    private readonly IAdbCommandService _commandService;
    private readonly IPackageInventoryService _packageInventoryService;
    private readonly IPackageIconService _packageIconService;
    private readonly IPackagePreferenceRepository _packagePreferences;
    private readonly IDeveloperVerificationPolicyProvider _verificationPolicy;
    private readonly ILogViewerService _logViewer;
    private object _currentPage;
    private NavigationEntry _selectedNavigation;
    private AndroidDevice? _selectedDevice;
    private string _adbStatus = "ADB · Checking";
    private string _adbVersion = "Checking managed Platform-Tools…";
    private bool _sessionsRecovered;

    public MainWindowViewModel(
        IAdbToolsManager toolsManager,
        IAdbDeviceTracker deviceTracker,
        IConnectionHistoryRepository history,
        IAdbConnectionService connectionService,
        IApkInstaller apkInstaller,
        IPackageManager packageManager,
        IDeviceToolsService toolsService,
        IDeviceRepository deviceRepository,
        ILocalAppDataPaths paths,
        IConfirmationService confirmation,
        ISettingsStore settingsStore,
        IScriptExecutionService scriptExecutionService,
        IDeviceInspectionService inspectionService,
        IDebloatPlanner debloatPlanner,
        IDebloatExecutionService debloatExecutionService,
        IAdbCommandService commandService,
        IPackageInventoryService packageInventoryService,
        IPackageIconService packageIconService,
        IDeveloperVerificationPolicyProvider verificationPolicy,
        IPackagePreferenceRepository packagePreferences,
        ILogViewerService logViewer)
    {
        _toolsManager = toolsManager;
        _deviceTracker = deviceTracker;
        _history = history;
        _connectionService = connectionService;
        _apkInstaller = apkInstaller;
        _packageManager = packageManager;
        _toolsService = toolsService;
        _deviceRepository = deviceRepository;
        _paths = paths;
        _confirmation = confirmation;
        _settingsStore = settingsStore;
        _scriptExecutionService = scriptExecutionService;
        _inspectionService = inspectionService;
        _debloatPlanner = debloatPlanner;
        _debloatExecutionService = debloatExecutionService;
        _commandService = commandService;
        _packageInventoryService = packageInventoryService;
        _packageIconService = packageIconService;
        _packagePreferences = packagePreferences;
        _verificationPolicy = verificationPolicy;
        _logViewer = logViewer;
        Navigation = new ObservableCollection<NavigationEntry>
        {
            new("Dashboard", "⌂"),
            new("Devices", "◉"),
            new("Device Status", "◈"),
            new("Connections", "↔"),
            new("Install APK", "＋"),
            new("Applications", "▦"),
            new("Debloat", "◌"),
            new("Scripts", "◇"),
            new("Tools", "⚙"),
            new("Logs", "≡"),
            new("Settings", "☷"),
            new("About", "?")
        };

        Devices = [];
        _pages = Navigation.ToDictionary(item => item.Label, item => (object)CreatePage(item));
        _selectedNavigation = Navigation[0];
        _currentPage = _pages[_selectedNavigation.Label];
        _deviceTracker.DevicesChanged += OnDevicesChanged;
    }

    public ObservableCollection<NavigationEntry> Navigation { get; }
    public IEnumerable<NavigationEntry> MainNavigation => Navigation.Take(9);
    public IEnumerable<NavigationEntry> SecondaryNavigation => Navigation.Skip(9);
    public ObservableCollection<AndroidDevice> Devices { get; }

    private object CreatePage(NavigationEntry entry) => entry.Label switch
    {
        "Dashboard" => new DashboardPageViewModel(Devices, _deviceRepository),
        "Devices" => new DevicesPageViewModel(Devices, _deviceRepository, _connectionService, _confirmation),
        "Device Status" => new DeviceStatusPageViewModel(_inspectionService, _verificationPolicy, Devices),
        "Connections" => new ConnectionsPageViewModel(_connectionService, _history, _deviceRepository),
        "Install APK" => new InstallApkPageViewModel(_apkInstaller, _verificationPolicy),
        "Applications" => new ApplicationsPageViewModel(_packageManager, _packageInventoryService, _packageIconService,
            _packagePreferences, _confirmation, _settingsStore, Devices),
        "Debloat" => new DebloatPageViewModel(_debloatPlanner, _debloatExecutionService, _confirmation, Devices),
        "Scripts" => new ScriptsPageViewModel(_scriptExecutionService, Devices),
        "Tools" => new ToolsPageViewModel(_toolsService, _commandService, Devices),
        "Logs" => new LogPageViewModel(_logViewer, _confirmation),
        "Settings" => new SettingsPageViewModel(_toolsManager, _paths, _settingsStore),
        "About" => new AboutPageViewModel(),
        _ => new PageViewModel(entry.Label)
    };
    public object CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public NavigationEntry SelectedNavigation
    {
        get => _selectedNavigation;
        set
        {
            if (SetProperty(ref _selectedNavigation, value))
            {
                CurrentPage = _pages[value.Label];
                OnPropertyChanged(nameof(SelectedPageDescription));
            }
        }
    }

    public AndroidDevice? SelectedDevice
    {
        get => _selectedDevice;
        set => SetProperty(ref _selectedDevice, value);
    }

    public int ConnectedDeviceCount => Devices.Count(device => device.State == DeviceState.Device);
    public string AdbStatus => _adbStatus;
    public string AdbVersion => _adbVersion;
    public string Version => AppInfo.Version;
    public string BetaLabel => $"{AppInfo.ReleaseChannel}  ·  {Version}";
    public string FooterText => $"Android TV Manager  ·  {Version}";
    public string SelectedPageDescription => SelectedNavigation.Label switch
    {
        "Dashboard" => "Your connected Android TV and Google TV devices, at a glance.",
        "Devices" => "Connected and saved Android devices.",
        "Device Status" => "Evidence-backed hardware, Android, security, and capability information.",
        "Connections" => "Connect over network or pair Android Wireless Debugging.",
        "Install APK" => "Install applications on the selected device.",
        "Applications" => "Inspect and manage installed packages.",
        "Debloat" => "Preview conservative, device-specific package changes.",
        "Scripts" => "Preview safe, structured ADB automation.",
        "Tools" => "Targeted device utilities and diagnostics.",
        "Settings" => "Configure Android TV Manager and managed ADB.",
        "About" => "About Android TV Manager.",
        _ => string.Empty
    };

    [RelayCommand]
    private void Navigate(string label)
    {
        if (Navigation.FirstOrDefault(item => item.Label == label) is { } item)
            SelectedNavigation = item;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await InitializeRuntimeAsync();
    }

    public async Task InitializeRuntimeAsync(CancellationToken cancellationToken = default)
    {
        _adbStatus = "ADB · Checking";
        OnPropertyChanged(nameof(AdbStatus));
        if (!_sessionsRecovered)
        {
            await _history.RecoverOpenSessionsAsync(cancellationToken);
            _sessionsRecovered = true;
        }
        var status = await _toolsManager.GetStatusAsync(cancellationToken);
        if (!status.IsReady)
        {
            _adbStatus = "ADB · Preparing";
            _adbVersion = "Downloading official Platform-Tools…";
            OnPropertyChanged(nameof(AdbStatus));
            OnPropertyChanged(nameof(AdbVersion));
            status = await _toolsManager.InstallOrRepairAsync(
                new Progress<AdbDownloadProgress>(progress =>
                {
                    _adbStatus = progress.TotalBytes is > 0
                        ? $"ADB · {progress.BytesReceived * 100 / progress.TotalBytes:0}%"
                        : "ADB · Downloading";
                    OnPropertyChanged(nameof(AdbStatus));
                }),
                cancellationToken);
        }

        _adbStatus = status.IsReady ? "ADB · Ready" : "ADB · Needs setup";
        _adbVersion = status.Version ?? status.ErrorMessage ?? "Platform-Tools not installed";
        OnPropertyChanged(nameof(AdbStatus));
        OnPropertyChanged(nameof(AdbVersion));
        if (status.IsReady)
        {
            await _deviceTracker.StartAsync(cancellationToken);
            await _deviceTracker.RefreshAsync(cancellationToken);
        }
    }

    private async void OnDevicesChanged(object? sender, IReadOnlyList<AndroidDevice> devices)
    {
        var savedDevices = await _deviceRepository.GetSavedDevicesAsync();
        var enrichedDevices = devices.Select(device =>
        {
            var saved = savedDevices.FirstOrDefault(item =>
                string.Equals(item.LastKnownSerial, device.Serial, StringComparison.OrdinalIgnoreCase));
            return new AndroidDevice
            {
                Serial = device.Serial,
                FriendlyName = saved?.FriendlyName,
                Endpoint = device.Endpoint,
                State = device.State,
                ConnectionType = device.ConnectionType,
                ReportedName = device.ReportedName,
                MacAddress = device.MacAddress,
                Manufacturer = device.Manufacturer,
                Brand = device.Brand,
                Model = device.Model,
                Product = device.Product,
                DeviceName = device.DeviceName,
                Board = device.Board,
                AndroidVersion = device.AndroidVersion,
                ApiLevel = device.ApiLevel,
                SecurityPatch = device.SecurityPatch,
                BuildId = device.BuildId,
                BuildType = device.BuildType,
                BuildFingerprint = device.BuildFingerprint,
                SeenAtUtc = device.SeenAtUtc
            };
        }).ToArray();
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Devices.Clear();
            foreach (var device in enrichedDevices)
                Devices.Add(device);
            OnPropertyChanged(nameof(ConnectedDeviceCount));
            if (SelectedDevice is not null)
                SelectedDevice = Devices.FirstOrDefault(device => device.Serial == SelectedDevice.Serial);
            else
                SelectedDevice = Devices.FirstOrDefault(device => device.State == DeviceState.Device);
        });

        foreach (var device in enrichedDevices)
            await _history.RecordDeviceSeenAsync(device);
        await _history.SyncSessionsAsync(enrichedDevices, _toolsManager.InstalledVersion);
    }
}

public class PageViewModel : ObservableObject
{
    public PageViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }
    public string Version => AppInfo.Version;
    public string Eyebrow => Title.ToUpperInvariant();
    public string Description => Title == "Dashboard"
        ? "Your connected Android entertainment devices, at a glance."
        : $"Manage your Android TV workflow from {Title.ToLowerInvariant()}.";
}

public sealed partial class DashboardPageViewModel : PageViewModel
{
    private readonly IDeviceRepository _repository;

    public DashboardPageViewModel(
        ObservableCollection<AndroidDevice> devices,
        IDeviceRepository repository) : base("Dashboard")
    {
        Devices = devices;
        _repository = repository;
        Devices.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasDevices));
            OnPropertyChanged(nameof(HasAnyDevices));
        };
        _ = LoadSavedAsync();
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public ObservableCollection<SavedDevice> SavedDevices { get; } = [];
    public bool HasDevices => Devices.Count > 0;
    public bool HasAnyDevices => HasDevices || SavedDevices.Count > 0;

    [RelayCommand]
    private async Task LoadSavedAsync()
    {
        SavedDevices.Clear();
        foreach (var device in await _repository.GetSavedDevicesAsync())
            SavedDevices.Add(device);
        OnPropertyChanged(nameof(HasAnyDevices));
    }
}

public sealed partial class DeviceStatusPageViewModel : PageViewModel
{
    private readonly IDeviceInspectionService _inspectionService;
    private readonly IDeveloperVerificationPolicyProvider _policyProvider;
    private CancellationTokenSource? _scanSource;

    public DeviceStatusPageViewModel(
        IDeviceInspectionService inspectionService,
        IDeveloperVerificationPolicyProvider policyProvider,
        ObservableCollection<AndroidDevice> devices) : base("Device Status")
    {
        _inspectionService = inspectionService;
        _policyProvider = policyProvider;
        Devices = devices;
        InstallationPolicy = policyProvider.GetPolicy(null).ManualInstallGuidance;
        Devices.CollectionChanged += (_, _) =>
        {
            if ((SelectedDevice is null || !Devices.Any(device => device.Serial == SelectedDevice.Serial))
                && Devices.FirstOrDefault(device => device.State == DeviceState.Device) is { } device)
                SelectedDevice = device;
        };
    }

    public ObservableCollection<AndroidDevice> Devices { get; }

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    private DeviceInspectionResult? _inspection;

    [ObservableProperty]
    private string _progressText = "Select a connected device and inspect it.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _guideText = string.Empty;

    [ObservableProperty]
    private string _installationPolicy;

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
    {
        if (value is null)
            Inspection = null;
        ProgressText = value is null
            ? "Select a connected device and inspect it."
            : $"Scanning {value.FriendlyName ?? value.Model ?? value.Serial} automatically…";
        if (value is not null)
            _ = InspectAsync();
    }

    [RelayCommand]
    private async Task InspectAsync()
    {
        if (SelectedDevice is null)
        {
            ProgressText = "Select a connected device before inspecting.";
            return;
        }

        _scanSource?.Cancel();
        _scanSource?.Dispose();
        _scanSource = new CancellationTokenSource();
        IsBusy = true;
        GuideText = string.Empty;
        var serial = SelectedDevice.Serial;
        try
        {
            var progress = new Progress<DeviceInspectionProgress>(value =>
                ProgressText = $"{value.Category}: {value.State} ({value.CompletedCategories}/{value.TotalCategories})");
            Inspection = await _inspectionService.InspectAsync(serial, progress, _scanSource.Token);
            ProgressText = $"Inspection completed at {Inspection.CapturedUtc.LocalDateTime:g}.";
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Inspection canceled.";
        }
        catch (Exception exception)
        {
            ProgressText = $"Inspection failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelInspection() => _scanSource?.Cancel();

    [RelayCommand]
    private void ShowUnverifiedInstallGuide()
    {
        GuideText = _policyProvider.GetPolicy(SelectedDevice).ManualInstallGuidance;
    }
}

public sealed partial class DebloatPageViewModel : PageViewModel
{
    private readonly IDebloatPlanner _planner;
    private readonly IDebloatExecutionService _execution;
    private readonly IConfirmationService _confirmation;
    private long? _lastExecutionId;

    public DebloatPageViewModel(
        IDebloatPlanner planner,
        IDebloatExecutionService execution,
        IConfirmationService confirmation,
        ObservableCollection<AndroidDevice> devices) : base("Debloat")
    {
        _planner = planner;
        _execution = execution;
        _confirmation = confirmation;
        Devices = devices;
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public IReadOnlyList<DebloatPreset> Presets { get; } = Enum.GetValues<DebloatPreset>();

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    private DebloatPreset _selectedPreset = DebloatPreset.Simple;

    [ObservableProperty]
    private DebloatPlan? _plan;

    [ObservableProperty]
    private string _status = "Generate a preview before changing anything.";

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
    {
        Plan = null;
        Status = value is null ? "Select a connected device." : $"Ready to analyze {value.Serial}.";
    }

    [RelayCommand]
    private async Task CreatePlanAsync()
    {
        if (SelectedDevice is null)
        {
            Status = "Select a connected device before creating a plan.";
            return;
        }
        try
        {
            Status = $"Analyzing packages on {SelectedDevice.Serial}…";
            Plan = await _planner.CreatePlanAsync(SelectedDevice.Serial, SelectedPreset);
            Status = $"{Plan.Items.Count(item => item.Selected)} package(s) selected; review the plan before execution.";
        }
        catch (Exception exception)
        {
            Status = $"Plan failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task ExecutePlanAsync()
    {
        if (Plan is null || SelectedDevice is null)
        {
            Status = "Create a plan and select its target first.";
            return;
        }
        var selected = Plan.Items.Count(item => item.Selected);
        if (!_confirmation.Confirm(
                $"Run {Plan.Preset} debloat",
                $"This will disable {selected} package(s) on target:\n{Plan.Serial}\n\nUnknown and critical packages are excluded. Continue?"))
        {
            Status = "Debloat canceled.";
            return;
        }
        try
        {
            var result = await _execution.ExecuteAsync(Plan);
            _lastExecutionId = result.ExecutionId;
            Status = $"Debloat {result.Status.ToLowerInvariant()}: {result.SuccessfulActions} succeeded, {result.FailedActions} failed.";
        }
        catch (Exception exception)
        {
            Status = $"Debloat failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task RestoreLastAsync()
    {
        if (_lastExecutionId is not { } executionId || SelectedDevice is null)
        {
            Status = "No debloat execution is available to restore.";
            return;
        }
        try
        {
            var result = await _execution.RestoreAsync(executionId, SelectedDevice.Serial);
            Status = $"Restore {result.Status.ToLowerInvariant()}: {result.RestoredActions} restored, {result.FailedActions} failed.";
        }
        catch (Exception exception)
        {
            Status = $"Restore failed: {exception.Message}";
        }
    }
}

public sealed partial class DevicesPageViewModel : ObservableObject
{
    private readonly IDeviceRepository _repository;
    private readonly IAdbConnectionService _connectionService;
    private readonly IConfirmationService _confirmation;

    public DevicesPageViewModel(
        ObservableCollection<AndroidDevice> devices,
        IDeviceRepository repository,
        IAdbConnectionService connectionService,
        IConfirmationService confirmation)
    {
        Devices = devices;
        _repository = repository;
        _connectionService = connectionService;
        _confirmation = confirmation;
        _ = LoadSavedAsync();
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public ObservableCollection<SavedDevice> SavedDevices { get; } = [];

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    private SavedDevice? _selectedSavedDevice;

    [ObservableProperty]
    private string _friendlyName = string.Empty;

    [ObservableProperty]
    private string _host = string.Empty;

    [ObservableProperty]
    private string _port = "5555";

    [ObservableProperty]
    private string _savedNotes = string.Empty;

    [ObservableProperty]
    private string _savedName = string.Empty;

    [ObservableProperty]
    private string _saveMessage = "Select a live device to save it for later.";

    partial void OnSelectedSavedDeviceChanged(SavedDevice? value)
    {
        SavedNotes = value?.Notes ?? string.Empty;
        SavedName = value?.FriendlyName ?? string.Empty;
    }

    [RelayCommand]
    private async Task SaveDeviceAsync()
    {
        if (SelectedDevice is null)
        {
            SaveMessage = "Select a live device first.";
            return;
        }
        var savedName = string.IsNullOrWhiteSpace(FriendlyName)
            ? SelectedDevice.Model ?? SelectedDevice.Serial
            : FriendlyName.Trim();
        await _repository.UpsertAsync(new SavedDevice
        {
            FriendlyName = savedName,
            Manufacturer = SelectedDevice.Manufacturer,
            Model = SelectedDevice.Model,
            LastKnownSerial = SelectedDevice.Serial,
            LastKnownEndpoint = SelectedDevice.Endpoint,
            ReportedName = SelectedDevice.ReportedName,
            MacAddress = SelectedDevice.MacAddress,
            PreferredConnectionType = SelectedDevice.ConnectionType,
            IsFavorite = false
        });
        SaveMessage = $"{savedName} saved to your device list.";
        await LoadSavedAsync();
    }

    [RelayCommand]
    private async Task ConnectAndSaveAsync()
    {
        if (!AdbParsers.TryParseEndpoint(Host, Port, out var endpoint, out var error))
        {
            SaveMessage = error;
            return;
        }
        SaveMessage = $"Connecting to {endpoint}…";
        var result = await _connectionService.ConnectAsync(endpoint);
        if (!result.IsSuccess)
        {
            SaveMessage = string.IsNullOrWhiteSpace(result.StandardError)
                ? "Connection failed."
                : result.StandardError.Trim();
            return;
        }
        var name = string.IsNullOrWhiteSpace(FriendlyName) ? endpoint : FriendlyName.Trim();
        await _repository.UpsertAsync(new SavedDevice
        {
            FriendlyName = name,
            LastKnownSerial = endpoint,
            LastKnownEndpoint = endpoint,
            PreferredConnectionType = ConnectionType.Network,
            IsFavorite = false
        });
        SaveMessage = $"{name} connected and saved.";
        await LoadSavedAsync();
    }

    [RelayCommand]
    private async Task LoadSavedAsync()
    {
        try
        {
            SavedDevices.Clear();
            foreach (var device in await _repository.GetSavedDevicesAsync())
                SavedDevices.Add(device);
        }
        catch (Exception exception)
        {
            SaveMessage = $"Saved devices unavailable: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task ReconnectAsync(SavedDevice? device)
    {
        if (device?.LastKnownEndpoint is not { Length: > 0 } endpoint)
        {
            SaveMessage = "This device has no reusable endpoint. Use Pair Wireless or Add Device.";
            return;
        }
        SaveMessage = $"Reconnecting to {endpoint}…";
        var result = await _connectionService.ConnectAsync(endpoint);
        SaveMessage = result.IsSuccess ? $"{device.FriendlyName} connected." : result.StandardError.Trim();
    }

    [RelayCommand]
    private async Task ForgetAsync(SavedDevice? device)
    {
        if (device is null)
            return;
        if (!_confirmation.Confirm("Forget saved device",
                $"Remove {device.FriendlyName} from saved devices?\n\nConnection, inspection, and script history will be retained."))
            return;
        await _repository.DeleteAsync(device.Id);
        await LoadSavedAsync();
        SaveMessage = $"{device.FriendlyName} forgotten. History was retained.";
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(SavedDevice? device)
    {
        if (device is null)
            return;
        device.IsFavorite = !device.IsFavorite;
        await _repository.UpsertAsync(device);
        await LoadSavedAsync();
    }

    [RelayCommand]
    private async Task SaveNotesAsync()
    {
        if (SelectedSavedDevice is null)
            return;
        SelectedSavedDevice.Notes = SavedNotes.Trim();
        await _repository.UpsertAsync(SelectedSavedDevice);
        await LoadSavedAsync();
        SaveMessage = "Device notes saved.";
    }

    [RelayCommand]
    private async Task RenameSavedAsync()
    {
        if (SelectedSavedDevice is null || string.IsNullOrWhiteSpace(SavedName))
        {
            SaveMessage = "Select a saved device and enter a friendly name.";
            return;
        }
        SelectedSavedDevice.FriendlyName = SavedName.Trim();
        await _repository.UpsertAsync(SelectedSavedDevice);
        await LoadSavedAsync();
        SaveMessage = "Friendly name updated.";
    }
}

public sealed class AboutPageViewModel : PageViewModel
{
    public AboutPageViewModel() : base("About")
    {
    }

    public string ProductLine => "Android TV / Google TV Device Management Toolbox";
    public string Mission => "A faster, safer, more modern way to manage the ADB-capable devices in your living room.";
    public string PlatformToolsNote => "Official Android SDK Platform-Tools are downloaded directly from Google and kept in LocalAppData.";
}

public sealed partial class ConnectionsPageViewModel : PageViewModel
{
    private readonly IAdbConnectionService _connectionService;
    private readonly IConnectionHistoryRepository _history;
    private readonly IDeviceRepository _deviceRepository;

    public ConnectionsPageViewModel(
        IAdbConnectionService connectionService,
        IConnectionHistoryRepository history,
        IDeviceRepository deviceRepository) : base("Connections")
    {
        _connectionService = connectionService;
        _history = history;
        _deviceRepository = deviceRepository;
        Host = string.Empty;
        Port = "5555";
        PairingPort = string.Empty;
        PairingCode = string.Empty;
    }

    [ObservableProperty]
    private string _host;

    [ObservableProperty]
    private string _port;

    [ObservableProperty]
    private string _pairingPort;

    [ObservableProperty]
    private string _pairingCode;

    [ObservableProperty]
    private string _debugPort = "5555";

    [ObservableProperty]
    private string _friendlyName = string.Empty;

    [ObservableProperty]
    private string _message = "Ready to connect.";

    public ObservableCollection<ConnectionHistoryItem> History { get; } = [];

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (!AdbParsers.TryParseEndpoint(Host, Port, out var endpoint, out var error))
        {
            Message = error;
            return;
        }

        Message = $"Connecting to {endpoint}…";
        var result = await _connectionService.ConnectAsync(endpoint);
        Message = result.IsSuccess
            ? $"Connected: {result.StandardOutput.Trim()}"
            : $"Connection failed: {result.StandardError.Trim()}";
    }

    [RelayCommand]
    private async Task PairAsync()
    {
        if (!AdbParsers.TryParseEndpoint(Host, PairingPort, out var endpoint, out var error))
        {
            Message = error;
            return;
        }
        if (PairingCode.Trim().Length != 6 || !PairingCode.All(char.IsDigit))
        {
            Message = "Enter the six-digit Wireless Debugging pairing code.";
            return;
        }

        Message = $"Pairing with {endpoint}…";
        var result = await _connectionService.PairAsync(endpoint, PairingCode.Trim());
        PairingCode = string.Empty;
        Message = result.IsSuccess
            ? $"Pairing accepted: {result.StandardOutput.Trim()} Enter the device's debugging port to connect."
            : $"Pairing failed: {result.StandardError.Trim()}";
    }

    [RelayCommand]
    private async Task PairConnectAndSaveAsync()
    {
        if (!AdbParsers.TryParseEndpoint(Host, PairingPort, out var pairingEndpoint, out var error))
        {
            Message = error;
            return;
        }
        if (PairingCode.Trim().Length != 6 || !PairingCode.All(char.IsDigit))
        {
            Message = "Enter the six-digit Wireless Debugging pairing code.";
            return;
        }
        if (!AdbParsers.TryParseEndpoint(Host, DebugPort, out var debugEndpoint, out error))
        {
            Message = $"Debugging endpoint: {error}";
            return;
        }

        Message = $"Pairing with {pairingEndpoint}…";
        var pairResult = await _connectionService.PairAsync(pairingEndpoint, PairingCode.Trim());
        PairingCode = string.Empty;
        if (!pairResult.IsSuccess)
        {
            Message = $"Pairing failed: {pairResult.StandardError.Trim()}";
            return;
        }

        Message = $"Pairing accepted. Connecting to {debugEndpoint}…";
        var connectResult = await _connectionService.ConnectAsync(debugEndpoint);
        if (!connectResult.IsSuccess)
        {
            Message = $"Paired, but connection failed: {connectResult.StandardError.Trim()}";
            return;
        }
        var name = string.IsNullOrWhiteSpace(FriendlyName) ? debugEndpoint : FriendlyName.Trim();
        await _deviceRepository.UpsertAsync(new SavedDevice
        {
            FriendlyName = name,
            LastKnownSerial = debugEndpoint,
            LastKnownEndpoint = debugEndpoint,
            PreferredConnectionType = ConnectionType.WirelessDebugging
        });
        Message = $"{name} paired, connected, and saved.";
    }

    [RelayCommand]
    private async Task RefreshHistoryAsync()
    {
        History.Clear();
        foreach (var item in await _history.GetRecentAsync())
            History.Add(item);
        Message = $"{History.Count} recent connection sessions.";
    }
}

public sealed partial class InstallApkPageViewModel : PageViewModel
{
    private readonly IApkInstaller _installer;
    private readonly IDeveloperVerificationPolicyProvider _policyProvider;

    public InstallApkPageViewModel(
        IApkInstaller installer,
        IDeveloperVerificationPolicyProvider policyProvider) : base("Install APK")
    {
        _installer = installer;
        _policyProvider = policyProvider;
        InstallationInfo = "ADB installation is independent from Android's manual unverified-developer flow.";
    }

    [ObservableProperty]
    private string _targetSerial = string.Empty;

    [ObservableProperty]
    private string _apkPath = string.Empty;

    [ObservableProperty]
    private string _output = "Select an APK and enter the target device serial.";

    [ObservableProperty]
    private string _installationInfo;

    [RelayCommand]
    private void BrowseApk()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Android packages (*.apk)|*.apk|All files (*.*)|*.*",
            Multiselect = true,
            Title = "Select APK package(s)"
        };
        if (dialog.ShowDialog() == true)
            ApkPath = string.Join(Environment.NewLine, dialog.FileNames);
    }

    [RelayCommand]
    private void ShowVerificationGuide()
    {
        InstallationInfo = _policyProvider.GetPolicy(null).ManualInstallGuidance;
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetSerial) || string.IsNullOrWhiteSpace(ApkPath))
        {
            Output = "Target serial and APK path are required.";
            return;
        }
        var paths = ApkPath.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (paths.Any(path => !File.Exists(path)))
        {
            Output = "One or more selected APK files do not exist.";
            return;
        }

        var results = new List<string>();
        for (var index = 0; index < paths.Length; index++)
        {
            Output = $"Installing {index + 1} of {paths.Length} · {Path.GetFileName(paths[index])}…";
            var result = await _installer.InstallAsync(TargetSerial.Trim(), paths[index]);
            results.Add(result.IsSuccess
                ? $"✓ {Path.GetFileName(paths[index])}"
                : $"✕ {Path.GetFileName(paths[index])}: {result.StandardError.Trim()}");
        }
        Output = string.Join(Environment.NewLine, results);
    }
}

public sealed partial class ApplicationsPageViewModel : PageViewModel
{
    private readonly IPackageManager _packageManager;
    private readonly IPackageInventoryService _inventoryService;
    private readonly IPackageIconService _iconService;
    private readonly IPackagePreferenceRepository _preferences;
    private readonly IConfirmationService _confirmation;
    private readonly ISettingsStore _settings;
    private readonly Task _settingsLoaded;
    private readonly SemaphoreSlim _iconThrottle = new(4, 4);
    private CancellationTokenSource? _iconSource;
    private bool _updatingIconSetting;

    public ApplicationsPageViewModel(
        IPackageManager packageManager,
        IPackageInventoryService inventoryService,
        IPackageIconService iconService,
        IPackagePreferenceRepository preferences,
        IConfirmationService confirmation,
        ISettingsStore settings,
        ObservableCollection<AndroidDevice> devices) : base("Applications")
    {
        _packageManager = packageManager;
        _inventoryService = inventoryService;
        _iconService = iconService;
        _preferences = preferences;
        _confirmation = confirmation;
        _settings = settings;
        _settingsLoaded = LoadIconSettingAsync();
        Devices = devices;
        Devices.CollectionChanged += (_, _) =>
        {
            var targetChanged = false;
            if ((string.IsNullOrWhiteSpace(TargetSerial) || !Devices.Any(device => device.Serial == TargetSerial))
                && Devices.FirstOrDefault(device => device.State == DeviceState.Device) is { } device)
            {
                TargetSerial = device.Serial;
                targetChanged = true;
            }
            if (targetChanged || (!string.IsNullOrWhiteSpace(TargetSerial) && Packages.Count == 0))
                _ = RefreshAsync();
        };
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public ObservableCollection<PackageInventoryEntry> Packages { get; } = [];
    public IReadOnlyList<string> Filters { get; } = ["All", "User", "System", "Enabled", "Disabled", "Uninstalled"];

    [ObservableProperty]
    private string _targetSerial = string.Empty;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private PackageInventoryEntry? _selectedPackage;

    [ObservableProperty]
    private string _message = "Waiting for a connected device…";

    [ObservableProperty]
    private bool _showPackageIcons;

    [ObservableProperty]
    private string _iconStatus = "Package icons are disabled for faster scans.";

    [ObservableProperty]
    private string _selectedFilter = "All";

    [ObservableProperty]
    private string _permission = string.Empty;

    [ObservableProperty]
    private string _note = string.Empty;

    public IReadOnlyList<PackageOverride> OverrideOptions { get; } =
        [PackageOverride.AlwaysKeep, PackageOverride.NeverSuggest, PackageOverride.UserApproved, PackageOverride.None];

    [ObservableProperty]
    private PackageOverride _selectedOverride = PackageOverride.None;

    public IEnumerable<PackageInventoryEntry> FilteredPackages =>
        Packages.Where(package => string.IsNullOrWhiteSpace(Search)
            || package.PackageName.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || (package.Label?.Contains(Search, StringComparison.OrdinalIgnoreCase) ?? false))
        .Where(package => SelectedFilter switch
        {
            "User" => !package.IsSystem,
            "System" => package.IsSystem,
            "Enabled" => package.IsEnabled,
            "Disabled" => !package.IsEnabled,
            "Uninstalled" => package.IsUninstalledForUser,
            _ => true
        });

    partial void OnSearchChanged(string value) => OnPropertyChanged(nameof(FilteredPackages));
    partial void OnSelectedFilterChanged(string value) => OnPropertyChanged(nameof(FilteredPackages));

    partial void OnShowPackageIconsChanged(bool value)
    {
        if (_updatingIconSetting)
            return;

        if (value && !_confirmation.Confirm(
                "Enable package icons",
                "Icons are loaded from package APKs after scanning. This can add extra load time and temporary ADB traffic. Continue?"))
        {
            _updatingIconSetting = true;
            ShowPackageIcons = false;
            _updatingIconSetting = false;
            return;
        }

        _ = _settings.SetAsync("applications.showPackageIcons", value.ToString());
        if (value)
        {
            IconStatus = "Icons enabled; they will load in the background after scanning.";
            if (Packages.Count > 0 && !string.IsNullOrWhiteSpace(TargetSerial))
                StartIconLoading(TargetSerial);
        }
        else
        {
            _iconSource?.Cancel();
            IconStatus = "Package icons are disabled for faster scans.";
            for (var index = 0; index < Packages.Count; index++)
                Packages[index] = Packages[index] with { IconPath = null };
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await _settingsLoaded;
        if (string.IsNullOrWhiteSpace(TargetSerial))
        {
            Message = "Target serial is required.";
            return;
        }
        Message = "Loading package list…";
        var inventory = await _inventoryService.GetInventoryAsync(TargetSerial.Trim());
        Packages.Clear();
        foreach (var package in inventory.Packages)
            Packages.Add(package);
        OnPropertyChanged(nameof(FilteredPackages));
        Message = inventory.ErrorMessage is null
            ? $"{Packages.Count} packages loaded."
            : $"{Packages.Count} packages loaded with warnings: {inventory.ErrorMessage}";
        if (ShowPackageIcons)
            StartIconLoading(TargetSerial.Trim());
    }

    private async Task LoadIconSettingAsync()
    {
        var value = await _settings.GetAsync("applications.showPackageIcons");
        _updatingIconSetting = true;
        ShowPackageIcons = string.Equals(value, bool.TrueString, StringComparison.OrdinalIgnoreCase);
        _updatingIconSetting = false;
        IconStatus = ShowPackageIcons
            ? "Icons enabled; they load asynchronously after scanning."
            : "Package icons are disabled for faster scans.";
    }

    private void StartIconLoading(string serial)
    {
        _iconSource?.Cancel();
        _iconSource?.Dispose();
        _iconSource = new CancellationTokenSource();
        var token = _iconSource.Token;
        IconStatus = "Loading package icons in the background…";
        _ = LoadIconsAsync(serial, token);
    }

    private async Task LoadIconsAsync(string serial, CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(Packages.ToArray().Select(package =>
                LoadIconAsync(serial, package, cancellationToken)));
            if (!cancellationToken.IsCancellationRequested && serial == TargetSerial)
                IconStatus = "Package icons loaded.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            IconStatus = $"Some package icons could not be loaded: {exception.Message}";
        }
    }

    private async Task LoadIconAsync(
        string serial,
        PackageInventoryEntry package,
        CancellationToken cancellationToken)
    {
        await _iconThrottle.WaitAsync(cancellationToken);
        try
        {
            var iconPath = await _iconService.GetIconPathAsync(serial, package, cancellationToken);
            if (iconPath is null || cancellationToken.IsCancellationRequested)
                return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
                return;
            await dispatcher.InvokeAsync(() =>
            {
                if (serial != TargetSerial)
                    return;
                var index = Packages.IndexOf(package);
                if (index >= 0)
                    Packages[index] = package with { IconPath = iconPath };
            });
        }
        finally
        {
            _iconThrottle.Release();
        }
    }

    [RelayCommand]
    private Task LaunchAsync() => RunActionAsync("Launch", (serial, package) => _packageManager.LaunchAsync(serial, package));

    [RelayCommand]
    private Task ForceStopAsync() => RunActionAsync("Force stop", (serial, package) => _packageManager.ForceStopAsync(serial, package));

    [RelayCommand]
    private Task DisableAsync() => RunActionAsync("Disable", (serial, package) => _packageManager.DisableAsync(serial, package), true);

    [RelayCommand]
    private Task EnableAsync() => RunActionAsync("Enable", (serial, package) => _packageManager.EnableAsync(serial, package));

    [RelayCommand]
    private Task UninstallAsync() => RunActionAsync("Uninstall for user", (serial, package) => _packageManager.UninstallForUserAsync(serial, package), true);

    [RelayCommand]
    private Task RestoreAsync() => RunActionAsync("Restore package", (serial, package) => _packageManager.RestoreAsync(serial, package));

    [RelayCommand]
    private Task ClearDataAsync() => RunActionAsync("Clear data", (serial, package) => _packageManager.ClearDataAsync(serial, package), true);

    [RelayCommand]
    private Task ClearCacheAsync() => RunActionAsync("Clear cache", (serial, package) => _packageManager.ClearCacheAsync(serial, package), true);

    [RelayCommand]
    private Task OpenAppSettingsAsync() => RunActionAsync("Open app settings", (serial, package) =>
        _packageManager.OpenAppSettingsAsync(serial, package));

    [RelayCommand]
    private async Task ViewDetailsAsync()
    {
        if (SelectedPackage is null || string.IsNullOrWhiteSpace(TargetSerial))
        {
            Message = "Select a package and enter a target serial.";
            return;
        }
        var details = await _inventoryService.GetDetailsAsync(TargetSerial.Trim(), SelectedPackage.PackageName);
        Message = details is null
            ? "Package details were unavailable."
            : $"{details.PackageName} · {details.VersionName ?? "version unknown"} · " +
              $"{details.ApkPaths.Count} APK path(s) · installed={details.IsInstalled}, enabled={details.IsEnabled}";
    }

    [RelayCommand]
    private async Task SaveOverrideAsync()
    {
        if (SelectedPackage is null || string.IsNullOrWhiteSpace(TargetSerial))
        {
            Message = "Select a package and enter a target serial.";
            return;
        }
        await _preferences.SetOverrideAsync(TargetSerial.Trim(), SelectedPackage.PackageName, SelectedOverride, Note);
        Message = SelectedOverride == PackageOverride.None
            ? "Package override cleared."
            : $"Package preference saved: {SelectedOverride}.";
    }

    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        if (SelectedPackage is null || string.IsNullOrWhiteSpace(TargetSerial) || string.IsNullOrWhiteSpace(Note))
        {
            Message = "Select a package, target, and enter a note.";
            return;
        }
        await _preferences.SetNoteAsync(TargetSerial.Trim(), SelectedPackage.PackageName, Note.Trim());
        Message = "Package note saved.";
    }

    [RelayCommand]
    private Task GrantPermissionAsync() => RunPermissionActionAsync("Grant permission",
        (serial, package, permission) => _packageManager.GrantPermissionAsync(serial, package, permission));

    [RelayCommand]
    private Task RevokePermissionAsync() => RunPermissionActionAsync("Revoke permission",
        (serial, package, permission) => _packageManager.RevokePermissionAsync(serial, package, permission));

    [RelayCommand]
    private Task FullUninstallAsync()
    {
        if (SelectedPackage?.IsSystem == true)
        {
            Message = "Full uninstall is blocked for system packages. Use Disable or Uninstall for user 0.";
            return Task.CompletedTask;
        }
        return RunActionAsync("Full uninstall", (serial, package) => _packageManager.FullUninstallAsync(serial, package), true);
    }

    private async Task RunActionAsync(
        string action,
        Func<string, string, Task<AdbCommandResult>> operation,
        bool destructive = false)
    {
        if (SelectedPackage is null || string.IsNullOrWhiteSpace(TargetSerial))
        {
            Message = "Select a package and enter a target serial.";
            return;
        }
        var serial = TargetSerial.Trim();
        var packageName = SelectedPackage.PackageName;
        if (destructive && !_confirmation.Confirm(
                $"{action} · confirm target",
                $"{action} applies to:\n\n{packageName}\n\nTarget device:\n{serial}\n\nContinue?"))
        {
            Message = "Operation canceled.";
            return;
        }
        Message = $"{action} · {packageName}…";
        var result = await operation(serial, packageName);
        Message = result.IsSuccess ? $"{action} completed." : result.StandardError.Trim();
    }

    private async Task RunPermissionActionAsync(
        string action,
        Func<string, string, string, Task<AdbCommandResult>> operation)
    {
        if (string.IsNullOrWhiteSpace(Permission))
        {
            Message = "Enter an Android permission name.";
            return;
        }
        if (SelectedPackage is null || string.IsNullOrWhiteSpace(TargetSerial))
        {
            Message = "Select a package and enter a target serial.";
            return;
        }
        var serial = TargetSerial.Trim();
        var package = SelectedPackage.PackageName;
        var permission = Permission.Trim();
        if (!_confirmation.Confirm($"{action} · confirm target",
                $"{action} applies to:\n\n{package}\n{permission}\n\nTarget device:\n{serial}\n\nContinue?"))
        {
            Message = "Operation canceled.";
            return;
        }
        var result = await operation(serial, package, permission);
        Message = result.IsSuccess ? $"{action} completed." : result.StandardError.Trim();
    }
}

public sealed partial class ToolsPageViewModel : PageViewModel
{
    private readonly IDeviceToolsService _toolsService;
    private readonly IAdbCommandService _commandService;
    private CancellationTokenSource? _commandSource;

    public ToolsPageViewModel(
        IDeviceToolsService toolsService,
        IAdbCommandService commandService,
        ObservableCollection<AndroidDevice> devices) : base("Tools")
    {
        _toolsService = toolsService;
        _commandService = commandService;
        Devices = devices;
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public IReadOnlyList<string> Presets { get; } =
        ["Get Properties", "CPU Info", "Memory Info", "Disk Usage", "Package List", "Disabled Packages",
         "Features", "Display Info", "Network Info", "Running Services", "Process List"];

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    private string _selectedPreset = "Get Properties";

    [ObservableProperty]
    private string _targetSerial = string.Empty;

    [ObservableProperty]
    private string _shellCommand = "getprop ro.product.model";

    [ObservableProperty]
    private string _output = "Targeted tools keep every operation explicit.";

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
    {
        if (value is not null)
            TargetSerial = value.Serial;
    }

    partial void OnSelectedPresetChanged(string value)
    {
        ShellCommand = value switch
        {
            "Get Properties" => "getprop",
            "CPU Info" => "cat /proc/cpuinfo",
            "Memory Info" => "cat /proc/meminfo",
            "Disk Usage" => "df -h",
            "Package List" => "pm list packages",
            "Disabled Packages" => "pm list packages -d",
            "Features" => "pm list features",
            "Display Info" => "dumpsys display",
            "Network Info" => "ip addr",
            "Running Services" => "dumpsys activity services",
            "Process List" => "ps -A",
            _ => ShellCommand
        };
    }

    [RelayCommand]
    private async Task RunShellAsync()
    {
        if (!HasTarget())
            return;
        _commandSource?.Cancel();
        _commandSource?.Dispose();
        _commandSource = new CancellationTokenSource();
        var serial = TargetSerial.Trim();
        var command = ShellCommand.Trim();
        if (command.Length == 0)
        {
            Output = "Enter a shell command.";
            return;
        }
        Output = $"Running on {serial}…";
        try
        {
            var result = await _commandService.ExecuteAsync(serial, ["shell", command],
                TimeSpan.FromMinutes(5), _commandSource.Token);
            Output = result.IsSuccess ? result.StandardOutput.Trim() : result.StandardError.Trim();
            History.Insert(0, new(serial, command, result, DateTimeOffset.UtcNow));
            if (History.Count > 20)
                History.RemoveAt(History.Count - 1);
        }
        catch (OperationCanceledException)
        {
            Output = "Command canceled.";
        }
    }

    public ObservableCollection<AdbCommandHistoryItem> History { get; } = [];

    [RelayCommand]
    private void CancelShell() => _commandSource?.Cancel();

    [RelayCommand]
    private async Task RebootAsync()
    {
        if (!HasTarget())
            return;
        var result = await _toolsService.RebootAsync(TargetSerial.Trim());
        Output = result.IsSuccess ? "Reboot command sent." : result.StandardError.Trim();
    }

    [RelayCommand]
    private async Task ScreenshotAsync()
    {
        if (!HasTarget())
            return;
        try
        {
            var path = await _toolsService.CaptureScreenshotAsync(TargetSerial.Trim(), TargetSerial.Trim());
            Output = $"Screenshot saved to {path}";
        }
        catch (Exception exception)
        {
            Output = exception.Message;
        }
    }

    private bool HasTarget()
    {
        if (!string.IsNullOrWhiteSpace(TargetSerial))
            return true;
        Output = "Target serial is required.";
        return false;
    }
}

public sealed partial class ScriptsPageViewModel : PageViewModel
{
    private readonly IScriptExecutionService _executionService;
    private long? _lastExecutionId;

    public ScriptsPageViewModel(
        IScriptExecutionService executionService,
        ObservableCollection<AndroidDevice> devices) : base("Scripts")
    {
        _executionService = executionService;
        Devices = devices;
    }

    public ObservableCollection<AndroidDevice> Devices { get; }

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    private string _targetSerial = string.Empty;

    [ObservableProperty]
    private string _scriptJson = """
        {
          "schemaVersion": 1,
          "name": "Example safe action",
          "description": "Preview this before it can ever run.",
          "actions": [
            { "type": "disablePackage", "package": "com.example.package", "reversible": true }
          ]
        }
        """;

    [ObservableProperty]
    private string _preview = "Scripts are preview-only until you validate them.";

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
    {
        if (value is not null)
            TargetSerial = value.Serial;
    }

    [RelayCommand]
    private void ValidateScript()
    {
        try
        {
            var script = ScriptDefinitionParser.Parse(ScriptJson);
            var reversible = script.Actions.Count(action => action.Reversible);
            var advanced = script.Actions.Count(action => action.IsAdvanced);
            Preview = $"{script.Name}\n\n{script.Description}\n\n" +
                      $"{script.Actions.Count} action(s) · {reversible} reversible · {advanced} advanced\n\n" +
                      string.Join("\n", script.Actions.Select((action, index) =>
                          $"{index + 1}. {action.Type} {(action.Package ?? action.Path ?? action.Value ?? string.Empty)}"));
        }
        catch (Exception exception)
        {
            Preview = $"Validation failed\n\n{exception.Message}";
        }
    }

    [RelayCommand]
    private async Task RunScriptAsync()
    {
        try
        {
            var script = ScriptDefinitionParser.Parse(ScriptJson);
            if (string.IsNullOrWhiteSpace(TargetSerial))
            {
                Preview = "Select or enter a target device before running.";
                return;
            }

            var target = SelectedDevice ?? new AndroidDevice
            {
                Serial = TargetSerial.Trim(),
                State = DeviceState.Device,
                ConnectionType = ConnectionType.Unknown
            };
            var result = await _executionService.ExecuteAsync(script, target);
            _lastExecutionId = result.ExecutionId;
            Preview = $"Execution {result.Status.ToLowerInvariant()}\n\n" +
                      $"{result.SuccessfulActions} action(s) succeeded · {result.FailedActions} failed\n" +
                      (result.CanUndo ? "This execution can be undone." : "No reversible actions were recorded.");
        }
        catch (Exception exception)
        {
            Preview = $"Execution failed\n\n{exception.Message}";
        }
    }

    [RelayCommand]
    private async Task UndoLastAsync()
    {
        if (_lastExecutionId is not { } executionId || string.IsNullOrWhiteSpace(TargetSerial))
        {
            Preview = "Run a script successfully before requesting undo.";
            return;
        }

        try
        {
            var result = await _executionService.UndoAsync(executionId, TargetSerial.Trim());
            Preview = $"Undo {result.Status.ToLowerInvariant()}\n\n" +
                      $"{result.RestoredActions} action(s) restored · {result.FailedActions} failed";
        }
        catch (Exception exception)
        {
            Preview = $"Undo failed\n\n{exception.Message}";
        }
    }
}

public sealed partial class SettingsPageViewModel : PageViewModel
{
    private readonly IAdbToolsManager _toolsManager;
    private readonly ILocalAppDataPaths _paths;
    private readonly ISettingsStore _settings;

    public SettingsPageViewModel(
        IAdbToolsManager toolsManager,
        ILocalAppDataPaths paths,
        ISettingsStore settings) : base("Settings")
    {
        _toolsManager = toolsManager;
        _paths = paths;
        _settings = settings;
        _paths.EnsureCreated();
        _ = LoadAsync();
    }

    public string DataPath => Path.GetDirectoryName(_paths.DatabasePath) ?? _paths.Root;
    public string ToolsPath => _paths.ToolsPath;
    public string LogsPath => _paths.LogsPath;

    [ObservableProperty]
    private string _status = "Platform-Tools status is checked on startup.";

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _closeToTray = true;

    [ObservableProperty]
    private bool _rememberSelectedDevice = true;

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    [ObservableProperty]
    private AppTheme _selectedTheme = AppTheme.Dark;

    private async Task LoadAsync()
    {
        StartMinimized = await ReadBoolAsync("general.startMinimized", false);
        MinimizeToTray = await ReadBoolAsync("general.minimizeToTray", true);
        CloseToTray = await ReadBoolAsync("general.closeToTray", true);
        RememberSelectedDevice = await ReadBoolAsync("general.rememberSelectedDevice", true);
        if (Enum.TryParse<AppTheme>(await _settings.GetAsync("appearance.theme"), true, out var theme))
            SelectedTheme = theme;
    }

    partial void OnStartMinimizedChanged(bool value) => _ = SaveBoolAsync("general.startMinimized", value);
    partial void OnMinimizeToTrayChanged(bool value) => _ = SaveBoolAsync("general.minimizeToTray", value);
    partial void OnCloseToTrayChanged(bool value) => _ = SaveBoolAsync("general.closeToTray", value);
    partial void OnRememberSelectedDeviceChanged(bool value) => _ = SaveBoolAsync("general.rememberSelectedDevice", value);
    partial void OnSelectedThemeChanged(AppTheme value)
    {
        ThemeManager.Apply(value);
        _ = _settings.SetAsync("appearance.theme", value.ToString());
    }

    private async Task<bool> ReadBoolAsync(string key, bool fallback)
        => bool.TryParse(await _settings.GetAsync(key), out var value) ? value : fallback;

    private Task SaveBoolAsync(string key, bool value) => _settings.SetAsync(key, value.ToString());

    [RelayCommand]
    private async Task RepairPlatformToolsAsync()
    {
        Status = "Downloading official Google Platform-Tools…";
        var result = await _toolsManager.InstallOrRepairAsync();
        Status = result.IsReady
            ? $"Platform-Tools {result.Version} ready."
            : $"Platform-Tools repair failed: {result.ErrorMessage}";
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        _paths.EnsureCreated();
        Process.Start(new ProcessStartInfo { FileName = DataPath, UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenToolsFolder()
    {
        _paths.EnsureCreated();
        Process.Start(new ProcessStartInfo { FileName = ToolsPath, UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        _paths.EnsureCreated();
        Process.Start(new ProcessStartInfo { FileName = LogsPath, UseShellExecute = true });
    }
}
