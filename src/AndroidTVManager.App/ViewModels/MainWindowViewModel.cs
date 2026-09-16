using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using AndroidTVManager.App.Services;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Core.Scripts;
using AndroidTVManager.Infrastructure.Database;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVManager.App.ViewModels;

public sealed record NavigationEntry(string Label, string Glyph);
public sealed record UpdateIntervalOption(int Hours, string Label);

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<string, object> _pages;
    private readonly IAdbToolsManager _toolsManager;
    private readonly IAdbDeviceSession _deviceSession;
    private readonly IAdbDeviceTracker _deviceTracker;
    private readonly IConnectionHistoryRepository _history;
    private readonly IAdbConnectionService _connectionService;
    private readonly IPackageManager _packageManager;
    private readonly IDeviceToolsService _toolsService;
    private readonly IDeviceRepository _deviceRepository;
    private readonly ILocalAppDataPaths _paths;
    private readonly IConfirmationService _confirmation;
    private readonly ISettingsStore _settingsStore;
    private readonly IScriptExecutionService _scriptExecutionService;
    private readonly IDeviceInspectionService _inspectionService;
    private readonly IDeviceBackupService _backupService;
    private readonly IDisplayDiagnosticsService _displayDiagnosticsService;
    private readonly IDisplayDiagnosticsSnapshotStore _displaySnapshots;
    private readonly ITransportDoctorService _transportDoctorService;
    private readonly IConfigurationExplorerService _configurationService;
    private readonly IConfigurationSnapshotStore _configurationSnapshots;
    private readonly IDebloatPlanner _debloatPlanner;
    private readonly IDebloatCleanupService _debloatCleanup;
    private readonly IDebloatExecutionService _debloatExecutionService;
    private readonly IAdbCommandService _commandService;
    private readonly IPackageInventoryService _packageInventoryService;
    private readonly IPackageIconService _packageIconService;
    private readonly IPackagePreferenceRepository _packagePreferences;
    private readonly IPackageClassifier _packageClassifier;
    private readonly IPackageReferenceCatalog _packageReferenceCatalog;
    private readonly IReferencePackageDumpService _referencePackageDumpService;
    private readonly IDeveloperVerificationPolicyProvider _verificationPolicy;
    private readonly ILogViewerService _logViewer;
    private readonly IUpdateService _updates;
    private readonly IDeploymentProfileRepository _deploymentProfiles;
    private readonly IDeploymentProfileStorage _profileStorage;
    private readonly IDeploymentProfileService _profileDeployment;
    private readonly IBulkApkService _bulkApkService;
    private readonly IRemoteControlService _remoteControl;
    private readonly IDeviceLogcatService _deviceLogcat;
    private readonly IDiagnosticBundleService _diagnosticBundles;
    private readonly INetworkDiagnosticsService _networkDiagnostics;
    private readonly ICodecInspectionService _codecInspection;
    private readonly IBootInspectionService _bootInspection;
    private readonly IDeviceFileService _deviceFiles;
    private readonly IDeviceComparisonService _deviceComparison;
    private readonly IScreenRecordingService _screenRecording;
    private readonly IDeviceTweakService _tweaks;
    private readonly IRecoveryService _recovery;
    private readonly IAppLogger _logger;
    private object _currentPage;
    private NavigationEntry _selectedNavigation;
    private string _adbStatus = "ADB · Checking";
    private string _adbVersion = "Checking managed Platform-Tools…";
    private bool _sessionsRecovered;
    private readonly SemaphoreSlim _deviceChangeLock = new(1, 1);
    private bool _suppressDevicePropagation;

    public MainWindowViewModel(
        IAdbToolsManager toolsManager,
        IAdbDeviceSession deviceSession,
        IAdbDeviceTracker deviceTracker,
        IConnectionHistoryRepository history,
        IAdbConnectionService connectionService,
        IPackageManager packageManager,
        IDeviceToolsService toolsService,
        IDeviceRepository deviceRepository,
        ILocalAppDataPaths paths,
        IConfirmationService confirmation,
        ISettingsStore settingsStore,
        IScriptExecutionService scriptExecutionService,
        IDeviceInspectionService inspectionService,
        IDeviceBackupService backupService,
        IDisplayDiagnosticsService displayDiagnosticsService,
        IDisplayDiagnosticsSnapshotStore displaySnapshots,
        ITransportDoctorService transportDoctorService,
        IConfigurationExplorerService configurationService,
        IConfigurationSnapshotStore configurationSnapshots,
        IDebloatPlanner debloatPlanner,
        IDebloatCleanupService debloatCleanup,
        IDebloatExecutionService debloatExecutionService,
        IAdbCommandService commandService,
        IPackageInventoryService packageInventoryService,
        IPackageIconService packageIconService,
        IDeveloperVerificationPolicyProvider verificationPolicy,
        IPackagePreferenceRepository packagePreferences,
        IPackageClassifier packageClassifier,
        IPackageReferenceCatalog packageReferenceCatalog,
        IReferencePackageDumpService referencePackageDumpService,
        ILogViewerService logViewer,
        IUpdateService updates,
        IDeploymentProfileRepository deploymentProfiles,
        IDeploymentProfileStorage profileStorage,
        IDeploymentProfileService profileDeployment,
        IBulkApkService bulkApkService,
        IRemoteControlService remoteControl,
        IDeviceLogcatService deviceLogcat,
        IDiagnosticBundleService diagnosticBundles,
        INetworkDiagnosticsService networkDiagnostics,
        ICodecInspectionService codecInspection,
        IBootInspectionService bootInspection,
        IDeviceFileService deviceFiles,
        IDeviceComparisonService deviceComparison,
        IScreenRecordingService screenRecording,
        IDeviceTweakService tweaks,
        IRecoveryService recovery,
        IAppLogger logger)
    {
        _toolsManager = toolsManager;
        _deviceSession = deviceSession;
        _deviceTracker = deviceTracker;
        _history = history;
        _connectionService = connectionService;
        _packageManager = packageManager;
        _toolsService = toolsService;
        _deviceRepository = deviceRepository;
        _paths = paths;
        _confirmation = confirmation;
        _settingsStore = settingsStore;
        _scriptExecutionService = scriptExecutionService;
        _inspectionService = inspectionService;
        _backupService = backupService;
        _displayDiagnosticsService = displayDiagnosticsService;
        _displaySnapshots = displaySnapshots;
        _transportDoctorService = transportDoctorService;
        _configurationService = configurationService;
        _configurationSnapshots = configurationSnapshots;
        _debloatPlanner = debloatPlanner;
        _debloatCleanup = debloatCleanup;
        _debloatExecutionService = debloatExecutionService;
        _commandService = commandService;
        _packageInventoryService = packageInventoryService;
        _packageIconService = packageIconService;
        _packagePreferences = packagePreferences;
        _packageClassifier = packageClassifier;
        _packageReferenceCatalog = packageReferenceCatalog;
        _referencePackageDumpService = referencePackageDumpService;
        _verificationPolicy = verificationPolicy;
        _logViewer = logViewer;
        _updates = updates;
        _deploymentProfiles = deploymentProfiles;
        _profileStorage = profileStorage;
        _profileDeployment = profileDeployment;
        _bulkApkService = bulkApkService;
        _remoteControl = remoteControl;
        _deviceLogcat = deviceLogcat;
        _diagnosticBundles = diagnosticBundles;
        _networkDiagnostics = networkDiagnostics;
        _codecInspection = codecInspection;
        _bootInspection = bootInspection;
        _deviceFiles = deviceFiles;
        _deviceComparison = deviceComparison;
        _screenRecording = screenRecording;
        _tweaks = tweaks;
        _recovery = recovery;
        _logger = logger;
        Navigation = new ObservableCollection<NavigationEntry>
        {
            new("Dashboard", "⌂"),
            new("Devices", "◉"),
            new("Device Status", "◈"),
            new("Display Diagnostics", "▣"),
            new("ADB Transport Doctor", "⌁"),
            new("Configuration Explorer", "≋"),
            new("Connections", "↔"),
            new("App Installer", "＋"),
            new("Deployment Profiles", "▤"),
            new("Remote", "⌨"),
            new("Device Logcat", "≡"),
            new("Diagnostic Bundles", "▥"),
            new("Advanced Diagnostics", "◫"),
            new("Device Comparison", "⇄"),
            new("Applications", "▦"),
            new("Debloat", "◌"),
            new("Tweaks", "◐"),
            new("Backup / Restore", "⇩"),
            new("Recovery / Sideload", "⇧"),
            new("Scripts", "◇"),
            new("Tools", "⚙"),
            new("Logs", "≡"),
            new("Settings", "☷"),
            new("About", "?")
        };

        _pages = Navigation.ToDictionary(item => item.Label, item => (object)CreatePage(item));
        _selectedNavigation = Navigation[0];
        _currentPage = _pages[_selectedNavigation.Label];
        _deviceSession.Changed += OnDeviceSessionChanged;
        _deviceTracker.DevicesChanged += OnDevicesChanged;
    }

    public ObservableCollection<NavigationEntry> Navigation { get; }
    public IEnumerable<NavigationEntry> MainNavigation
        => Navigation.Where(item => item.Label != "Settings");
    public IEnumerable<NavigationEntry> SecondaryNavigation
        => Navigation.Where(item => item.Label == "Settings");
    public ObservableCollection<AndroidDevice> Devices => _deviceSession.Devices;

    private object CreatePage(NavigationEntry entry) => entry.Label switch
    {
        "Dashboard" => new DashboardPageViewModel(Devices, _deviceRepository),
        "Devices" => new DevicesPageViewModel(
            _deviceSession,
            _deviceRepository,
            _connectionService,
            _confirmation,
            _deviceTracker),
        "Device Status" => new DeviceStatusPageViewModel(_inspectionService, _verificationPolicy, Devices),
        "Display Diagnostics" => new DisplayDiagnosticsPageViewModel(
            _displayDiagnosticsService,
            _displaySnapshots,
            Devices),
        "ADB Transport Doctor" => new TransportDoctorPageViewModel(
            _transportDoctorService,
            Devices),
        "Configuration Explorer" => new ConfigurationPageViewModel(
            _configurationService,
            _configurationSnapshots,
            Devices,
            device => SelectedDevice = device),
        "Connections" => new ConnectionsPageViewModel(
            _connectionService,
            _history,
            _deviceRepository,
            _deviceTracker,
            PreferTarget),
        "Tweaks" => new TweaksPageViewModel(_tweaks, _confirmation),
        "Recovery / Sideload" => new RecoveryPageViewModel(_recovery, _confirmation),
        "App Installer" => new InstallApkPageViewModel(_bulkApkService, _verificationPolicy),
        "Deployment Profiles" => new DeploymentProfilesPageViewModel(
            _deploymentProfiles,
            _profileStorage,
            _profileDeployment,
            _bulkApkService,
            _confirmation,
            Devices,
            device => SelectedDevice = device),
        "Remote" => new RemotePageViewModel(
            _remoteControl,
            _packageManager,
            _settingsStore,
            Devices,
            device => SelectedDevice = device),
        "Device Logcat" => new DeviceLogcatPageViewModel(
            _deviceLogcat,
            Devices,
            device => SelectedDevice = device),
        "Diagnostic Bundles" => new DiagnosticBundlePageViewModel(
            _diagnosticBundles,
            Devices,
            device => SelectedDevice = device),
        "Advanced Diagnostics" => new AdvancedDiagnosticsPageViewModel(
            _networkDiagnostics,
            _codecInspection,
            _bootInspection,
            _deviceFiles,
            _screenRecording,
            _confirmation,
            Devices,
            device => SelectedDevice = device),
        "Device Comparison" => new DeviceComparisonPageViewModel(
            _deviceComparison,
            Devices,
            device => SelectedDevice = device),
        "Applications" => new ApplicationsPageViewModel(_packageManager, _packageInventoryService, _packageIconService,
            _packagePreferences, _packageClassifier, _packageReferenceCatalog, _referencePackageDumpService,
            _confirmation, _settingsStore, Devices),
        "Debloat" => new DebloatPageViewModel(
            _debloatPlanner,
            _debloatCleanup,
            _debloatExecutionService,
            _confirmation,
            _packageIconService,
            _settingsStore,
            _packageReferenceCatalog,
            Devices),
        "Backup / Restore" => new BackupPageViewModel(_backupService, _paths, Devices),
        "Scripts" => new ScriptsPageViewModel(_scriptExecutionService, _confirmation, Devices),
        "Tools" => new ToolsPageViewModel(_toolsService, _commandService, Devices),
        "Logs" => new LogPageViewModel(_logViewer, _confirmation),
        "Settings" => new SettingsPageViewModel(
            _toolsManager,
            _paths,
            _settingsStore,
            _updates,
            _deviceTracker,
            _confirmation),
        "About" => new AboutPageViewModel(_toolsManager, _paths),
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
        get => _deviceSession.SelectedDevice;
        set => _deviceSession.Select(value);
    }

    [RelayCommand(CanExecute = nameof(CanDisconnectDevice))]
    private async Task DisconnectDeviceAsync(AndroidDevice? device)
    {
        device ??= SelectedDevice;
        if (device is null || !device.CanDisconnect)
            return;

        var result = await _deviceSession.DisconnectAsync(device);
        if (_pages.TryGetValue("Devices", out var devicesPage) && devicesPage is DevicesPageViewModel devices)
            devices.SaveMessage = result.Message;
    }

    private bool CanDisconnectDevice(AndroidDevice? device)
        => _deviceSession.CanDisconnect(device);

    [RelayCommand]
    private void SelectDevice(AndroidDevice? device)
    {
        if (device is null)
            return;
        PreferTarget(device.Serial);
    }

    private void PreferTarget(string serialOrEndpoint)
        => _deviceSession.Prefer(serialOrEndpoint);

    private void OnDeviceSessionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SelectedDevice));
        OnPropertyChanged(nameof(ConnectedDeviceCount));
        DisconnectDeviceCommand.NotifyCanExecuteChanged();
        if (!_suppressDevicePropagation)
            PropagateSelectedDevice(_deviceSession.SelectedDevice);
    }

    private void PropagateSelectedDevice(AndroidDevice? value)
    {
        if (_pages.TryGetValue("Devices", out var devicesPage) && devicesPage is DevicesPageViewModel devices)
            devices.SelectedDevice = value;
        if (_pages.TryGetValue("Device Status", out var statusPage) && statusPage is DeviceStatusPageViewModel status)
            status.SelectedDevice = value;
        if (_pages.TryGetValue("Tweaks", out var tweaksPage) && tweaksPage is TweaksPageViewModel tweaks)
            tweaks.SelectedDevice = value;
        if (_pages.TryGetValue("Configuration Explorer", out var page)
            && page is ConfigurationPageViewModel configuration)
            configuration.SelectedDevice = value;
        if (_pages.TryGetValue("Backup / Restore", out var backupPage)
            && backupPage is BackupPageViewModel backup)
            backup.SelectedDevice = value;
        if (_pages.TryGetValue("Display Diagnostics", out var displayPage)
            && displayPage is DisplayDiagnosticsPageViewModel display)
            display.SelectedDevice = value;
        if (_pages.TryGetValue("ADB Transport Doctor", out var transportPage)
            && transportPage is TransportDoctorPageViewModel transport)
            transport.SelectedDevice = value;
        if (_pages.TryGetValue("Deployment Profiles", out var profilesPage)
            && profilesPage is DeploymentProfilesPageViewModel profiles)
            profiles.SelectedDevice = value;
        if (_pages.TryGetValue("Remote", out var remotePage)
            && remotePage is RemotePageViewModel remote)
            remote.SelectedDevice = value;
        if (_pages.TryGetValue("Device Logcat", out var logcatPage)
            && logcatPage is DeviceLogcatPageViewModel logcat)
            logcat.SelectedDevice = value;
        if (_pages.TryGetValue("Diagnostic Bundles", out var bundlePage)
            && bundlePage is DiagnosticBundlePageViewModel bundle)
            bundle.SelectedDevice = value;
        if (_pages.TryGetValue("Advanced Diagnostics", out var advancedPage)
            && advancedPage is AdvancedDiagnosticsPageViewModel advanced)
            advanced.SelectedDevice = value;
        if (_pages.TryGetValue("Debloat", out var debloatPage) && debloatPage is DebloatPageViewModel debloat)
            debloat.SelectedDevice = value;
        if (_pages.TryGetValue("Tools", out var toolsPage) && toolsPage is ToolsPageViewModel tools)
            tools.SelectedDevice = value;
        if (_pages.TryGetValue("Scripts", out var scriptsPage) && scriptsPage is ScriptsPageViewModel scripts)
            scripts.SelectedDevice = value;
        if (_pages.TryGetValue("Applications", out var applicationsPage)
            && applicationsPage is ApplicationsPageViewModel applications)
            applications.TargetSerial = value?.Serial ?? string.Empty;
        if (_pages.TryGetValue("App Installer", out var installPage)
            && installPage is InstallApkPageViewModel install)
            install.SelectedDevice = value;
        if (_pages.TryGetValue("Device Comparison", out var comparisonPage)
            && comparisonPage is DeviceComparisonPageViewModel comparison
            && comparison.LeftDevice is null)
            comparison.LeftDevice = value;
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
        "Display Diagnostics" => "Capture, compare, and monitor display, HDMI, HDR, HDCP, and CEC evidence.",
        "ADB Transport Doctor" => "Measure ADB transport stability and capture failed probe evidence.",
        "Configuration Explorer" => "Read-only runtime properties, partition files, and configuration provenance.",
        "Connections" => "Connect over network or pair Android Wireless Debugging.",
        "App Installer" => "Install APK, split APK, APKS, APKM, and XAPK packages on the selected device.",
        "Deployment Profiles" => "Repeatable APK, package, and script setup plans for connected devices.",
        "Remote" => "Send safe typed ADB remote-control commands to the selected device.",
        "Device Logcat" => "Stream, filter, save, and capture real device logcat output.",
        "Diagnostic Bundles" => "Collect redacted or full device evidence into a shareable ZIP.",
        "Advanced Diagnostics" => "Inspect network, media codecs, boot state, and shared device storage.",
        "Device Comparison" => "Compare two connected devices across build, package, display, security, and capability evidence.",
        "Applications" => "Inspect and manage installed packages.",
        "Debloat" => "Device-specific one-click cleanup profiles, with Custom for package-by-package review.",
        "Tweaks" => "Verified animation timing controls and NVIDIA Shield settings guidance.",
        "Recovery / Sideload" => "Guided recovery preparation and file-based Android update sideloading.",
        "Backup / Restore" => "Create safe device backups and restore APKs.",
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
        await _deviceChangeLock.WaitAsync();
        try
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
                _suppressDevicePropagation = true;
                try
                {
                    _deviceSession.ReplaceLiveDevices(enrichedDevices);
                }
                finally
                {
                    _suppressDevicePropagation = false;
                }
                PropagateSelectedDevice(SelectedDevice);
                OnPropertyChanged(nameof(ConnectedDeviceCount));
            });

            foreach (var device in enrichedDevices)
                await _history.RecordDeviceSeenAsync(device);
            await _history.SyncSessionsAsync(enrichedDevices, _toolsManager.InstalledVersion);
        }
        catch (Exception exception)
        {
            _logger.Error("Devices", "Could not process an ADB device update.", exception);
        }
        finally
        {
            _deviceChangeLock.Release();
        }
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
            if (SelectedDevice is not null)
                SelectedDevice = Devices.FirstOrDefault(device => device.Serial == SelectedDevice.Serial);
        };
    }

    public ObservableCollection<AndroidDevice> Devices { get; }

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoverageSummary), nameof(EvidenceEntries))]
    [NotifyCanExecuteChangedFor(nameof(ExportInspectionCommand))]
    private DeviceInspectionResult? _inspection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EvidenceEntries))]
    private string _evidenceSearch = "";

    [ObservableProperty]
    private InspectionCommandEvidence? _selectedEvidence;

    public string CoverageSummary => Inspection is null ? "Deep scan adds hardware, input, media, storage, users and vendor-service evidence. Read-only; no root or reboot."
        : DeepInspectionCatalog.Describe(Inspection);

    public IEnumerable<InspectionCommandEvidence> EvidenceEntries => (Inspection?.Commands ?? [])
        .Where(item => string.IsNullOrWhiteSpace(EvidenceSearch)
            || string.Join("\n", item.Command, item.Category, item.StandardOutput, item.StandardError)
                .Contains(EvidenceSearch, StringComparison.OrdinalIgnoreCase));

    partial void OnInspectionChanged(DeviceInspectionResult? value) => SelectedEvidence = value?.Commands.FirstOrDefault();

    private bool CanExportInspection() => Inspection is not null;

    [RelayCommand(CanExecute = nameof(CanExportInspection))]
    private async Task ExportInspectionAsync()
    {
        var snapshot = Inspection;
        if (snapshot is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save device report — includes device identifiers and raw diagnostics; review before sharing",
            FileName = "device-inspection.json", Filter = "Device inspection JSON (*.json)|*.json"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await Task.Run(async () =>
            {
                await using var stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                await System.Text.Json.JsonSerializer.SerializeAsync(stream, snapshot,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            });
            if (ReferenceEquals(Inspection, snapshot)) ProgressText = "Report saved. It includes identifiers and raw diagnostics; review before sharing.";
        }
        catch (Exception exception) { ProgressText = $"Report export failed: {exception.Message}"; }
    }

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
        _scanSource?.Cancel();
        _scanSource = null;
        IsBusy = false;
        Inspection = null;
        ProgressText = value is null
            ? "Select a connected device and inspect it."
            : $"Scanning {value.FriendlyName ?? value.Model ?? value.Serial} automatically…";
        if (value is not null)
            _ = InspectAsync();
    }

    [RelayCommand]
    private Task InspectAsync() => ScanAsync(false);

    [RelayCommand]
    private Task DeepInspectAsync() => ScanAsync(true);

    private async Task ScanAsync(bool deepScan)
    {
        if (SelectedDevice is null || SelectedDevice.State != DeviceState.Device)
        {
            ProgressText = "Select a connected device before inspecting.";
            return;
        }

        _scanSource?.Cancel();
        using var source = new CancellationTokenSource();
        _scanSource = source;
        IsBusy = true;
        GuideText = string.Empty;
        Inspection = null;
        var serial = SelectedDevice.Serial;
        bool IsCurrent() => ReferenceEquals(_scanSource, source) && SelectedDevice?.Serial == serial;
        try
        {
            var progress = new Progress<DeviceInspectionProgress>(value =>
            {
                if (IsCurrent() && !source.IsCancellationRequested)
                    ProgressText = $"{value.Category}: {value.State} ({value.CompletedCategories}/{value.TotalCategories})";
            });
            var result = await _inspectionService.InspectAsync(serial, progress, source.Token, deepScan);
            if (!IsCurrent() || source.IsCancellationRequested) return;
            Inspection = result;
            ProgressText = $"{(deepScan ? "Deep scan" : "Inspection")} finished at {Inspection.CapturedUtc.LocalDateTime:g}. Review command coverage below.";
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent()) ProgressText = "Inspection canceled.";
        }
        catch (Exception exception)
        {
            if (IsCurrent()) ProgressText = $"Inspection failed: {exception.Message}";
        }
        finally
        {
            if (IsCurrent()) { _scanSource = null; IsBusy = false; }
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
    private readonly IDebloatCleanupService _cleanup;
    private readonly IDebloatExecutionService _execution;
    private readonly IConfirmationService _confirmation;
    private readonly IPackageIconService _iconService;
    private readonly ISettingsStore _settings;
    private readonly IPackageReferenceCatalog _referenceCatalog;
    private readonly Task _settingsLoaded;
    private readonly SemaphoreSlim _iconThrottle = new(4, 4);
    private CancellationTokenSource? _iconSource;
    private CancellationTokenSource? _scanSource;

    public DebloatPageViewModel(
        IDebloatPlanner planner,
        IDebloatCleanupService cleanup,
        IDebloatExecutionService execution,
        IConfirmationService confirmation,
        IPackageIconService iconService,
        ISettingsStore settings,
        IPackageReferenceCatalog referenceCatalog,
        ObservableCollection<AndroidDevice> devices) : base("Debloat")
    {
        _planner = planner;
        _cleanup = cleanup;
        _execution = execution;
        _confirmation = confirmation;
        _iconService = iconService;
        _settings = settings;
        _referenceCatalog = referenceCatalog;
        _settingsLoaded = LoadIconSettingAsync();
        Devices = devices;
        AvailableReferenceProfiles = _referenceCatalog.GetProfiles();
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public IReadOnlyList<DebloatPreset> Presets { get; } = Enum.GetValues<DebloatPreset>();
    public ObservableCollection<DebloatPlanItemViewModel> PlanItems { get; } = [];
    public ObservableCollection<DebloatPlanItemViewModel> ProfileDetailItems { get; } = [];
    public ObservableCollection<PackageReferenceProfileMatch> ReferenceProfiles { get; } = [];
    public IReadOnlyList<PackageReferenceProfileMatch> AvailableReferenceProfiles { get; }
    public string AvailableProfileSummary => $"{AvailableReferenceProfiles.Count} reference profile(s) loaded";

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    private DebloatPreset _selectedPreset = DebloatPreset.Medium;

    [ObservableProperty]
    private DebloatPlan? _plan;

    [ObservableProperty]
    private DebloatCleanupOverview? _overview;

    [ObservableProperty]
    private DebloatCleanupResult? _lastResult;

    [ObservableProperty]
    private string _status = "Connect a device to load cleanup profiles.";

    [ObservableProperty]
    private string _iconStatus = string.Empty;

    [ObservableProperty]
    private bool _showPackageIcons;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isCustomMode;

    [ObservableProperty]
    private bool _showProfileDetails;

    [ObservableProperty]
    private DebloatCleanupKind _inspectedKind = DebloatCleanupKind.Recommended;

    public bool HasAuthorizedTarget
        => SelectedDevice is { State: DeviceState.Device };

    public bool IsSimplePreset => SelectedPreset == DebloatPreset.Simple;
    public bool IsMediumPreset => SelectedPreset == DebloatPreset.Medium;
    public bool IsAggressivePreset => SelectedPreset == DebloatPreset.Aggressive;
    public int SelectedCount => PlanItems.Count(item => item.IsSelected);
    public string PlanProfileSummary => Plan?.ReferenceSummary is { } summary
        ? $"{summary.TotalPackages} packages · {summary.BaselineMatches} reference matches · " +
          $"{summary.UnknownPackages} unknown"
        : "No device profile analysis has been generated yet.";

    public string DeviceHeading
        => SelectedDevice?.DisplayLabel ?? "No device selected";

    public string DetectedProfileText
        => Overview?.DetectedProfileLabel
            ?? (HasAuthorizedTarget ? "Scanning device profile…" : "Connect a device");

    public string ProfileDetailText => Overview?.ProfileDetail ?? string.Empty;

    public string ScanSummary
        => Overview is null
            ? HasAuthorizedTarget ? "Waiting for a live package scan." : "No authorized target is selected."
            : $"{Overview.PackagesScanned} packages scanned"
              + (Overview.UnknownLeftUntouched > 0
                  ? $" · {Overview.UnknownLeftUntouched} unknown packages were left untouched."
                  : string.Empty);

    public string ProtectedSummary
        => Overview is null ? string.Empty : string.Join(Environment.NewLine, Overview.ProtectedHighlights);

    public DebloatCleanupProfileState? SafeProfile => Overview?.Profile(DebloatCleanupKind.Safe);
    public DebloatCleanupProfileState? RecommendedProfile => Overview?.Profile(DebloatCleanupKind.Recommended);
    public DebloatCleanupProfileState? DeepProfile => Overview?.Profile(DebloatCleanupKind.Deep);

    public string LastCleanupSummary
        => LastResult is null
            ? string.Empty
            : $"Last cleanup:{Environment.NewLine}{LastResult.ProfileName}{Environment.NewLine}{LastResult.DeviceLabel}{Environment.NewLine}{LastResult.Succeeded + LastResult.Failed} changes · {LastResult.CompletedUtc.ToLocalTime():g}";

    public bool HasLastResult => LastResult is not null;
    public bool CanRestoreLast => LastResult is { CanRestore: true, ExecutionId: not null };

    public string SafeActionLabel => ProfileActionLabel(SafeProfile, "Run Safe Cleanup");
    public string RecommendedActionLabel => ProfileActionLabel(RecommendedProfile, "Run Recommended Cleanup");
    public string DeepActionLabel => ProfileActionLabel(DeepProfile, "Review / Run Deep Cleanup");
    public bool CanRunSafe => CanRun(SafeProfile);
    public bool CanRunRecommended => CanRun(RecommendedProfile);
    public bool CanRunDeep => CanRun(DeepProfile);

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
    {
        _iconSource?.Cancel();
        ResetWorkspace(value is null
            ? "Connect a device to load cleanup profiles."
            : $"Loading cleanup profiles for {value.DisplayLabel}…");
        NotifyProfileState();
        if (value is { State: DeviceState.Device })
            _ = RefreshOverviewAsync();
    }

    partial void OnSelectedPresetChanged(DebloatPreset value)
    {
        OnPropertyChanged(nameof(IsSimplePreset));
        OnPropertyChanged(nameof(IsMediumPreset));
        OnPropertyChanged(nameof(IsAggressivePreset));
        if (IsCustomMode)
        {
            Plan = null;
            PlanItems.Clear();
            Status = $"Custom preset set to {value}. Create a preview to inspect packages.";
        }
    }

    partial void OnPlanChanged(DebloatPlan? value)
    {
        ReferenceProfiles.Clear();
        foreach (var profile in value?.ReferenceSummary?.ProfileMatches ?? Overview?.ActiveProfiles ?? [])
            ReferenceProfiles.Add(profile);
        OnPropertyChanged(nameof(PlanProfileSummary));
    }

    partial void OnOverviewChanged(DebloatCleanupOverview? value)
    {
        if (Plan is null)
        {
            ReferenceProfiles.Clear();
            foreach (var profile in value?.ActiveProfiles ?? [])
                ReferenceProfiles.Add(profile);
        }
        NotifyProfileState();
    }

    partial void OnLastResultChanged(DebloatCleanupResult? value)
    {
        OnPropertyChanged(nameof(LastCleanupSummary));
        OnPropertyChanged(nameof(HasLastResult));
        OnPropertyChanged(nameof(CanRestoreLast));
    }

    partial void OnIsBusyChanged(bool value)
        => NotifyProfileState();

    [RelayCommand]
    private void SelectPreset(DebloatPreset preset)
        => SelectedPreset = preset;

    [RelayCommand]
    private void OpenCustom()
    {
        IsCustomMode = true;
        Status = "Custom cleanup: inspect packages, or create a preview with Simple / Medium / Aggressive.";
        if (PlanItems.Count == 0 && HasAuthorizedTarget)
            _ = CreatePlanAsync();
    }

    [RelayCommand]
    private void CloseCustom()
    {
        IsCustomMode = false;
        Status = Overview is null ? "Connect a device to load cleanup profiles." : "Choose a cleanup profile.";
    }

    [RelayCommand]
    private void ViewProfilePackages(DebloatCleanupKind kind)
    {
        if (Overview is null)
            return;
        InspectedKind = kind;
        var plan = _cleanup.CreateProfilePlan(Overview, kind);
        ProfileDetailItems.Clear();
        foreach (var item in plan.Items.Where(item => item.Selected))
            ProfileDetailItems.Add(new DebloatPlanItemViewModel(item));
        ShowProfileDetails = true;
        Status = ProfileDetailItems.Count == 0
            ? $"{kind} has no packages to inspect."
            : $"{ProfileDetailItems.Count} package(s) selected by {kind}. Inspection is optional.";
    }

    [RelayCommand]
    private void CloseProfileDetails()
    {
        ShowProfileDetails = false;
        ProfileDetailItems.Clear();
    }

    [RelayCommand]
    private async Task RefreshOverviewAsync()
    {
        if (!HasAuthorizedTarget)
        {
            Status = "Connect a device to load cleanup profiles.";
            return;
        }

        _scanSource?.Cancel();
        _scanSource = new CancellationTokenSource();
        var token = _scanSource.Token;
        var target = SelectedDevice!;
        IsBusy = true;
        try
        {
            await _settingsLoaded;
            Status = $"Scanning packages on {target.DisplayLabel}…";
            var overview = await _cleanup.CreateOverviewAsync(target.Serial, target, token);
            if (token.IsCancellationRequested || SelectedDevice?.Serial != target.Serial)
                return;
            Overview = overview;
            Status = overview.UnknownLeftUntouched > 0
                ? $"{overview.UnknownLeftUntouched} unknown packages were left untouched."
                : "Cleanup profiles are ready.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Status = $"Profile scan failed: {exception.Message}";
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunCleanupAsync(DebloatCleanupKind kind)
    {
        if (!HasAuthorizedTarget)
        {
            Status = "Connect a device before running cleanup.";
            return;
        }
        if (kind == DebloatCleanupKind.Custom)
        {
            OpenCustom();
            return;
        }

        IsBusy = true;
        try
        {
            var target = SelectedDevice!;
            var overview = await _cleanup.CreateOverviewAsync(target.Serial, target);
            if (SelectedDevice?.Serial != target.Serial)
            {
                Status = "The selected device changed. Cleanup was not started.";
                Overview = overview;
                return;
            }
            Overview = overview;
            var profile = overview.Profile(kind);
            if (profile is null || !profile.HasActions)
            {
                Status = profile?.StatusText ?? "Nothing to clean.";
                return;
            }

            var plan = _cleanup.CreateProfilePlan(overview, kind);
            if (!_confirmation.Confirm(
                    profile.DisplayName,
                    BuildConfirmation(target, profile, plan),
                    "Run Cleanup"))
            {
                Status = $"{profile.DisplayName} canceled.";
                return;
            }

            var live = await _cleanup.CreateOverviewAsync(target.Serial, SelectedDevice);
            if (SelectedDevice?.Serial != target.Serial
                || !string.Equals(live.BuildFingerprint, overview.BuildFingerprint, StringComparison.Ordinal)
                || !SameActions(plan, _cleanup.CreateProfilePlan(live, kind)))
            {
                Overview = live;
                Status = "The device state changed since this cleanup was prepared. AndroidTVManager rebuilt the cleanup plan. Review the updated result.";
                return;
            }

            var result = await _execution.ExecuteAsync(_cleanup.CreateProfilePlan(live, kind));
            LastResult = new(
                kind,
                profile.DisplayName,
                target.DisplayLabel,
                target.Serial,
                DateTimeOffset.UtcNow,
                result.SuccessfulActions,
                result.FailedActions,
                profile.AlreadyCleanedCount,
                result.CanUndo,
                result.ExecutionId,
                FormatExecution(profile.DisplayName, result, profile.AlreadyCleanedCount));
            Overview = await _cleanup.CreateOverviewAsync(target.Serial, SelectedDevice);
            Status = LastResult.Summary;
        }
        catch (Exception exception)
        {
            Status = exception.Message.Contains("device state changed", StringComparison.OrdinalIgnoreCase)
                ? exception.Message
                : $"Cleanup failed: {exception.Message}";
            if (HasAuthorizedTarget)
            {
                try { Overview = await _cleanup.CreateOverviewAsync(SelectedDevice!.Serial, SelectedDevice); }
                catch { /* keep the failure message */ }
            }
        }
        finally
        {
            IsBusy = false;
        }
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
            IsCustomMode = true;
            Status = $"Analyzing packages on {SelectedDevice.Serial}…";
            var target = SelectedDevice;
            Plan = null;
            PlanItems.Clear();
            await _settingsLoaded;
            Plan = await _planner.CreatePlanAsync(target.Serial, SelectedPreset, target);
            ApplyPlan(Plan);
            Status = SelectedCount == 0 && SelectedPreset == DebloatPreset.Simple
                ? "Simple selected 0 packages. Keep and Critical stay locked; this can be correct on a stock Shield. Use Medium or Aggressive for catalog-reviewed candidates."
                : $"{SelectedCount} package(s) pre-selected; review or change the checks before execution.";
            if (ShowPackageIcons)
                StartIconLoading(Plan);
        }
        catch (Exception exception)
        {
            Status = $"Plan failed: {exception.Message}";
        }
    }

    private async Task LoadIconSettingAsync()
    {
        var value = await _settings.GetAsync("applications.showPackageIcons");
        ShowPackageIcons = value is null
            || string.Equals(value, bool.TrueString, StringComparison.OrdinalIgnoreCase);
    }

    private void StartIconLoading(DebloatPlan plan)
    {
        _iconSource?.Cancel();
        _iconSource?.Dispose();
        _iconSource = new CancellationTokenSource();
        IconStatus = "Loading candidate icons in the background…";
        _ = LoadPlanIconsAsync(plan, _iconSource.Token);
    }

    private async Task LoadPlanIconsAsync(DebloatPlan plan, CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(plan.Items.Select(item =>
                LoadPlanIconAsync(plan.Serial, item.Package, cancellationToken)));
            if (!cancellationToken.IsCancellationRequested && ReferenceEquals(Plan, plan))
                IconStatus = "Candidate icons loaded.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            IconStatus = $"Some candidate icons could not be loaded: {exception.Message}";
        }
    }

    private async Task LoadPlanIconAsync(
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
                if (Plan is null || Plan.Serial != serial)
                    return;
                var item = PlanItems.FirstOrDefault(candidate =>
                    candidate.Package.PackageName.Equals(package.PackageName, StringComparison.OrdinalIgnoreCase));
                if (item is not null)
                    item.UpdatePackage(item.Package with { IconPath = iconPath });
            });
        }
        finally
        {
            _iconThrottle.Release();
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
        if (SelectedDevice.Serial != Plan.Serial)
        {
            Status = "The selected device changed. Create a new Custom preview.";
            return;
        }
        var selected = SelectedCount;
        if (!_confirmation.Confirm(
                $"Run {Plan.Preset} custom cleanup",
                $"This will change {selected} package(s) on target:{Environment.NewLine}{Plan.Serial}{Environment.NewLine}{Environment.NewLine}Critical packages remain locked. Manually selected Unknown packages require your review.",
                "Run Cleanup"))
        {
            Status = "Custom cleanup canceled.";
            return;
        }
        try
        {
            var executionPlan = Plan with
            {
                Items = PlanItems.Select(item => item.ToModel()).ToArray(),
                Warnings = Plan.Warnings.Concat(
                    PlanItems.Any(item => item.IsSelected && item.RequiresManualReview)
                        ? ["One or more Unknown packages were manually selected."]
                        : []).ToArray()
            };
            var result = await _execution.ExecuteAsync(executionPlan);
            LastResult = new(
                DebloatCleanupKind.Custom,
                $"Custom {Plan.Preset}",
                SelectedDevice.DisplayLabel,
                Plan.Serial,
                DateTimeOffset.UtcNow,
                result.SuccessfulActions,
                result.FailedActions,
                0,
                result.CanUndo,
                result.ExecutionId,
                FormatExecution($"Custom {Plan.Preset}", result, 0));
            var summary = LastResult.Summary;
            await RefreshPlanAfterMutationAsync(summary);
            if (HasAuthorizedTarget)
                Overview = await _cleanup.CreateOverviewAsync(SelectedDevice.Serial, SelectedDevice);
        }
        catch (Exception exception)
        {
            Status = $"Debloat failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task RestoreLastAsync()
    {
        if (LastResult is not { ExecutionId: { } executionId, CanRestore: true } last || SelectedDevice is null)
        {
            Status = "No cleanup execution is available to restore.";
            return;
        }
        if (SelectedDevice.Serial != last.Serial)
        {
            Status = "Restore Last Cleanup is locked to the original device.";
            return;
        }
        if (!_confirmation.Confirm(
                $"Restore {last.ProfileName}?",
                $"{last.Succeeded + last.Failed} previous package changes will be reversed where Android permits it.",
                "Restore"))
        {
            Status = "Restore canceled.";
            return;
        }
        try
        {
            var result = await _execution.RestoreAsync(executionId, SelectedDevice.Serial);
            var summary = $"Restore {result.Status.ToLowerInvariant()}: {result.RestoredActions} restored, {result.FailedActions} failed.";
            if (IsCustomMode)
                await RefreshPlanAfterMutationAsync(summary);
            if (HasAuthorizedTarget)
                Overview = await _cleanup.CreateOverviewAsync(SelectedDevice.Serial, SelectedDevice);
            Status = summary;
            LastResult = last with
            {
                CanRestore = false,
                Summary = summary,
                CompletedUtc = DateTimeOffset.UtcNow
            };
        }
        catch (Exception exception)
        {
            Status = $"Restore failed: {exception.Message}";
        }
    }

    private async Task RefreshPlanAfterMutationAsync(string summary)
    {
        if (SelectedDevice is null)
        {
            Status = summary;
            return;
        }

        try
        {
            Plan = await _planner.CreatePlanAsync(SelectedDevice.Serial, SelectedPreset, SelectedDevice);
            ApplyPlan(Plan);
            Status = $"{summary} Preview refreshed: {SelectedCount} package(s) selected.";
            if (ShowPackageIcons)
                StartIconLoading(Plan);
        }
        catch (Exception exception)
        {
            Status = $"{summary} Preview refresh failed: {exception.Message}";
        }
    }

    private void ApplyPlan(DebloatPlan plan)
    {
        PlanItems.Clear();
        foreach (var item in plan.Items)
        {
            var itemViewModel = new DebloatPlanItemViewModel(item);
            itemViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(DebloatPlanItemViewModel.IsSelected))
                    OnPropertyChanged(nameof(SelectedCount));
            };
            PlanItems.Add(itemViewModel);
        }
        OnPropertyChanged(nameof(SelectedCount));
    }

    private void ResetWorkspace(string status)
    {
        Plan = null;
        PlanItems.Clear();
        ProfileDetailItems.Clear();
        ReferenceProfiles.Clear();
        Overview = null;
        ShowProfileDetails = false;
        IconStatus = string.Empty;
        Status = status;
    }

    private void NotifyProfileState()
    {
        OnPropertyChanged(nameof(HasAuthorizedTarget));
        OnPropertyChanged(nameof(DeviceHeading));
        OnPropertyChanged(nameof(DetectedProfileText));
        OnPropertyChanged(nameof(ProfileDetailText));
        OnPropertyChanged(nameof(ScanSummary));
        OnPropertyChanged(nameof(ProtectedSummary));
        OnPropertyChanged(nameof(SafeProfile));
        OnPropertyChanged(nameof(RecommendedProfile));
        OnPropertyChanged(nameof(DeepProfile));
        OnPropertyChanged(nameof(SafeActionLabel));
        OnPropertyChanged(nameof(RecommendedActionLabel));
        OnPropertyChanged(nameof(DeepActionLabel));
        OnPropertyChanged(nameof(CanRunSafe));
        OnPropertyChanged(nameof(CanRunRecommended));
        OnPropertyChanged(nameof(CanRunDeep));
    }

    private bool CanRun(DebloatCleanupProfileState? profile)
        => HasAuthorizedTarget && !IsBusy && profile is { HasActions: true };

    private string ProfileActionLabel(DebloatCleanupProfileState? profile, string runLabel)
    {
        if (!HasAuthorizedTarget)
            return "Connect a device";
        if (profile is null)
            return IsBusy ? "Scanning…" : "Unavailable";
        return string.IsNullOrWhiteSpace(profile.ActionLabel) ? runLabel : profile.ActionLabel;
    }

    private static string BuildConfirmation(
        AndroidDevice target,
        DebloatCleanupProfileState profile,
        DebloatPlan plan)
    {
        var lines = new List<string>
        {
            "Target:",
            $"{target.DisplayLabel} · {target.Serial}",
            string.Empty,
            $"{profile.ActionCount} packages will be changed.",
            string.Empty,
            $"{profile.DisableCount} Disable",
            $"{profile.UninstallCount} Uninstall for User 0",
            string.Empty,
            "Protected packages will remain untouched."
        };
        if (profile.FeatureImpacts.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Possible impact:");
            lines.AddRange(profile.FeatureImpacts.Take(4));
        }
        var names = plan.Items.Where(item => item.Selected).Select(item => item.Package.PackageName).Take(8).ToArray();
        if (names.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Packages:");
            lines.AddRange(names);
            if (profile.ActionCount > names.Length)
                lines.Add($"… and {profile.ActionCount - names.Length} more");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static bool SameActions(DebloatPlan left, DebloatPlan right)
    {
        static string Key(DebloatPlanItem item)
            => $"{item.Package.PackageName}|{item.Action}|{item.Package.IsEnabled}|{item.Package.IsInstalled}";
        var first = left.Items.Where(item => item.Selected).Select(Key).OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        var second = right.Items.Where(item => item.Selected).Select(Key).OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        return first.SequenceEqual(second, StringComparer.OrdinalIgnoreCase);
    }

    private static string FormatExecution(string profileName, ScriptExecutionResult result, int alreadyCleaned)
    {
        if (result.FailedActions == 0)
            return $"✓ {profileName} completed{Environment.NewLine}{Environment.NewLine}{result.SuccessfulActions} succeeded{Environment.NewLine}{alreadyCleaned} already cleaned{Environment.NewLine}0 failed{Environment.NewLine}0 verification failures{Environment.NewLine}No unverified change is counted as successful.";
        return $"{profileName} partially completed{Environment.NewLine}{Environment.NewLine}{result.SuccessfulActions} succeeded{Environment.NewLine}{result.FailedActions} failed{Environment.NewLine}No unverified change is counted as successful.";
    }
}

public sealed partial class DevicesPageViewModel : ObservableObject
{
    private readonly IAdbDeviceSession _deviceSession;
    private readonly IDeviceRepository _repository;
    private readonly IAdbConnectionService _connectionService;
    private readonly IConfirmationService _confirmation;
    private readonly IAdbDeviceTracker _deviceTracker;

    public DevicesPageViewModel(
        IAdbDeviceSession deviceSession,
        IDeviceRepository repository,
        IAdbConnectionService connectionService,
        IConfirmationService confirmation,
        IAdbDeviceTracker deviceTracker)
    {
        _deviceSession = deviceSession;
        Devices = deviceSession.Devices;
        _repository = repository;
        _connectionService = connectionService;
        _confirmation = confirmation;
        _deviceTracker = deviceTracker;
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

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
    {
        _deviceSession.Select(value);
        DisconnectCommand.NotifyCanExecuteChanged();
    }

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
        await _deviceTracker.RefreshAsync();
        _deviceSession.Prefer(endpoint);
        await LoadSavedAsync();
    }

    [RelayCommand(CanExecute = nameof(CanDisconnectDevice))]
    private async Task DisconnectAsync(AndroidDevice? device)
    {
        device ??= SelectedDevice;
        if (device is null || !_deviceSession.CanDisconnect(device))
            return;

        SaveMessage = $"Disconnecting {device.DisconnectEndpoint}…";
        var result = await _deviceSession.DisconnectAsync(device);
        SaveMessage = result.Message;
    }

    private bool CanDisconnectDevice(AndroidDevice? device)
        => _deviceSession.CanDisconnect(device);

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
        if (!result.IsSuccess)
            return;
        await _deviceTracker.RefreshAsync();
        _deviceSession.Prefer(endpoint);
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

public sealed partial class AboutPageViewModel : PageViewModel
{
    private readonly IAdbToolsManager _toolsManager;
    private readonly ILocalAppDataPaths _paths;

    public AboutPageViewModel(IAdbToolsManager toolsManager, ILocalAppDataPaths paths) : base("About")
    {
        _toolsManager = toolsManager;
        _paths = paths;
        _ = LoadPlatformToolsAsync();
    }

    public string ProductName => AppInfo.ProductName;
    public string DeveloperName => AppInfo.DeveloperName;
    public string ProductDescription => AppInfo.Description;
    public string ReleaseChannel => AppInfo.ReleaseChannel;
    public string InformationalVersion => AppInfo.InformationalVersion;
    public string Build => string.IsNullOrWhiteSpace(AppInfo.BuildIdentifier)
        ? "Build information unavailable"
        : AppInfo.BuildIdentifier;
    public string RuntimeVersion => Environment.Version.ToString();
    public string Platform => $"Windows {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}";
    public int DatabaseSchemaVersion => DatabaseMigrations.CurrentVersion;
    public string ManagedToolsLocation => _paths.ToolsPath;
    public string ProductLine => "Independent Android TV / Google TV device management toolbox";
    public string Mission => "Inspect, understand, and safely manage ADB-capable devices in your living room.";

    [ObservableProperty]
    private string _platformToolsVersion = "Checking managed Platform-Tools…";

    [ObservableProperty]
    private string _copyStatus = string.Empty;

    [RelayCommand]
    private void CopyAppInformation()
    {
        var summary = string.Join(Environment.NewLine,
            ProductName,
            $"Developer: {DeveloperName}",
            $"Version: {Version}",
            $"Release: {ReleaseChannel}",
            $"Build: {Build}",
            $".NET: {RuntimeVersion}",
            $"Platform: {Platform}",
            $"ADB: {PlatformToolsVersion}",
            $"Database Schema: {DatabaseSchemaVersion}");
        try
        {
            System.Windows.Clipboard.SetText(summary);
            CopyStatus = "Application information copied.";
        }
        catch (Exception exception)
        {
            CopyStatus = $"Could not copy application information: {exception.Message}";
        }
    }

    private async Task LoadPlatformToolsAsync()
    {
        try
        {
            var status = await _toolsManager.GetStatusAsync();
            PlatformToolsVersion = status.IsReady && !string.IsNullOrWhiteSpace(status.Version)
                ? status.Version
                : "Not installed";
        }
        catch
        {
            PlatformToolsVersion = "Not installed";
        }
    }
}

public sealed partial class ConnectionsPageViewModel : PageViewModel
{
    private readonly IAdbConnectionService _connectionService;
    private readonly IConnectionHistoryRepository _history;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IAdbDeviceTracker _deviceTracker;
    private readonly Action<string> _preferTarget;

    public ConnectionsPageViewModel(
        IAdbConnectionService connectionService,
        IConnectionHistoryRepository history,
        IDeviceRepository deviceRepository,
        IAdbDeviceTracker deviceTracker,
        Action<string> preferTarget) : base("Connections")
    {
        _connectionService = connectionService;
        _history = history;
        _deviceRepository = deviceRepository;
        _deviceTracker = deviceTracker;
        _preferTarget = preferTarget;
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
        if (!result.IsSuccess)
            return;
        await _deviceTracker.RefreshAsync();
        _preferTarget(endpoint);
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
        await _deviceTracker.RefreshAsync();
        _preferTarget(debugEndpoint);
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
    private readonly IBulkApkService _bulkApk;
    private readonly IDeveloperVerificationPolicyProvider _policyProvider;
    private readonly List<string> _selectedPaths = [];
    private BulkInstallPackageSet? _packageSet;
    private CancellationTokenSource? _operation;

    public InstallApkPageViewModel(
        IBulkApkService bulkApk,
        IDeveloperVerificationPolicyProvider policyProvider) : base("App Installer")
    {
        _bulkApk = bulkApk;
        _policyProvider = policyProvider;
        InstallationInfo = "App Installer uses ADB package installation while Android is running. Recovery / Sideload is a separate OTA workflow.";
    }

    public ObservableCollection<ApkInstallGroup> Plan { get; } = [];

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    private string _selectedPathsText = string.Empty;

    [ObservableProperty]
    private string _preview = "Browse for an APK, split set, APKS, APKM, or XAPK, then analyze it before installing.";

    [ObservableProperty]
    private string _output = "Select a target device and a package to analyze.";

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _installationInfo;

    [ObservableProperty]
    private bool _isBusy;

    public string TargetLabel
        => SelectedDevice is { State: DeviceState.Device } device
            ? $"{device.DisplayLabel} · {device.DisplaySubtitle}"
            : "No authorized target is selected. Choose a connected device in TARGET before installing.";

    public bool HasAuthorizedTarget
        => SelectedDevice is { State: DeviceState.Device };

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
        => NotifyState();

    partial void OnIsBusyChanged(bool value)
        => NotifyState();

    [RelayCommand]
    private void BrowsePackage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Android packages (*.apk;*.apks;*.apkm;*.xapk)|*.apk;*.apks;*.apkm;*.xapk|APK (*.apk)|*.apk|APKS (*.apks)|*.apks|APKM (*.apkm)|*.apkm|XAPK (*.xapk)|*.xapk|All files (*.*)|*.*",
            Multiselect = true,
            Title = "Select APK, split APKs, APKS, APKM, or XAPK"
        };
        if (dialog.ShowDialog() == true)
            SetSelectedPaths(dialog.FileNames);
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select a folder containing APKs or split-package archives.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;
        SetSelectedPaths([dialog.SelectedPath]);
    }

    public void SetSelectedPaths(IReadOnlyList<string> paths)
    {
        ResetPlan();
        _selectedPaths.Clear();
        _selectedPaths.AddRange(paths.Where(path => !string.IsNullOrWhiteSpace(path)));
        SelectedPathsText = string.Join(Environment.NewLine, _selectedPaths);
        Output = _selectedPaths.Count == 0
            ? "Select a package to analyze."
            : $"{_selectedPaths.Count} path(s) selected. Analyze before installing.";
        NotifyState();
    }

    [RelayCommand]
    private void ShowVerificationGuide()
    {
        InstallationInfo = _policyProvider.GetPolicy(SelectedDevice).ManualInstallGuidance;
    }

    [RelayCommand(CanExecute = nameof(CanAnalyzeExecute))]
    private async Task AnalyzeAsync()
    {
        if (_selectedPaths.Count == 0)
        {
            Output = "Select an APK, split set, APKS, APKM, XAPK, or folder first.";
            return;
        }

        ResetPlan();
        IsBusy = true;
        ProgressText = "Preparing package…";
        _operation = new CancellationTokenSource();
        try
        {
            var progress = new Progress<string>(message => ProgressText = message);
            _packageSet = await _bulkApk.PrepareAsync(_selectedPaths, progress: progress, cancellationToken: _operation.Token);
            Plan.Clear();
            foreach (var group in _packageSet.Groups)
                Plan.Add(group);
            Preview = BuildPreview(_packageSet);
            Output = $"{Plan.Count} install group(s) ready. Review the plan, then install.";
            ProgressText = "Analysis complete.";
        }
        catch (OperationCanceledException)
        {
            Output = "Analysis canceled. Temporary files were cleaned up.";
            ProgressText = "Canceled.";
        }
        catch (Exception exception)
        {
            Output = exception.Message;
            ProgressText = "Analysis failed.";
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstallExecute))]
    private async Task InstallAsync()
    {
        if (!HasAuthorizedTarget)
        {
            Output = "No authorized target is selected. Choose a connected device before installing.";
            return;
        }
        if (_packageSet is null)
        {
            Output = "Analyze the selected package before installing.";
            return;
        }

        IsBusy = true;
        ProgressText = "Installing…";
        _operation = new CancellationTokenSource();
        var serial = SelectedDevice!.Serial;
        try
        {
            var progress = new Progress<BulkInstallProgress>(update =>
                ProgressText = update.Stage ?? update.CurrentItem);
            var result = await _bulkApk.InstallAsync(serial, _packageSet, progress, _operation.Token);
            _packageSet = null;
            Output = FormatResult(result);
            ProgressText = result.WasCanceled ? "Canceled." : "Complete.";
        }
        catch (OperationCanceledException)
        {
            Output = "Installation canceled. Temporary files were cleaned up. Device state may have changed.";
            ProgressText = "Canceled.";
            _packageSet = null;
        }
        catch (Exception exception)
        {
            Output = exception.Message;
            ProgressText = "Install failed.";
            _packageSet = null;
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            IsBusy = false;
            NotifyState();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelExecute))]
    private void Cancel()
        => _operation?.Cancel();

    private bool CanAnalyzeExecute()
        => !IsBusy && _selectedPaths.Count > 0;

    private bool CanInstallExecute()
        => !IsBusy && HasAuthorizedTarget && _packageSet is { Groups.Count: > 0 };

    private bool CanCancelExecute()
        => IsBusy;

    private void ResetPlan()
    {
        if (_packageSet is not null)
            _bulkApk.Cleanup(_packageSet);
        _packageSet = null;
        Plan.Clear();
        Preview = "Browse for an APK, split set, APKS, APKM, or XAPK, then analyze it before installing.";
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(TargetLabel));
        OnPropertyChanged(nameof(HasAuthorizedTarget));
        AnalyzeCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private string BuildPreview(BulkInstallPackageSet packageSet)
    {
        var lines = new List<string>
        {
            "Target:",
            TargetLabel,
            string.Empty
        };
        if (packageSet.Groups.Count > 1)
        {
            lines.Add("Independent apps:");
            lines.Add(packageSet.Groups.Count.ToString());
            lines.Add(string.Empty);
        }

        foreach (var group in packageSet.Groups)
        {
            lines.Add("Source:");
            lines.Add(group.SourceName ?? group.DisplayName);
            lines.Add(string.Empty);
            lines.Add("Type:");
            lines.Add(group.ContainerLabel);
            lines.Add(string.Empty);
            lines.Add("Package:");
            lines.Add(string.IsNullOrWhiteSpace(group.PackageName) ? "Unknown" : group.PackageName);
            lines.Add(string.Empty);
            lines.Add("APK components:");
            lines.Add(group.Artifacts.Count.ToString());
            lines.Add(string.Empty);
            lines.Add("Base:");
            lines.Add(group.BaseApkName ?? "Unknown");
            if (group.Splits.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Splits:");
                lines.AddRange(group.Splits);
            }
            lines.Add(string.Empty);
            lines.Add("Size:");
            lines.Add(FormatSize(group.TotalApkBytes));
            if (group.Payloads.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Additional data:");
                lines.Add($"{group.Payloads.Count} OBB file(s) · {FormatSize(group.TotalAdditionalBytes)}");
                lines.AddRange(group.Payloads.Select(payload => $"{payload.FileName} · {FormatSize(payload.SizeBytes)}"));
            }
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static string FormatResult(BulkInstallResult result)
    {
        var lines = result.Items.Select(item =>
        {
            var prefix = item.Status switch
            {
                BulkInstallItemStatus.Succeeded => "✓",
                BulkInstallItemStatus.PartialSuccess => "!",
                BulkInstallItemStatus.Canceled => "■",
                _ => "✕"
            };
            return $"{prefix} {item.Group.DisplayName}{Environment.NewLine}{item.Message ?? item.Status.ToString()}";
        }).ToList();
        if (!string.IsNullOrWhiteSpace(result.ReconciliationMessage))
            lines.Add(result.ReconciliationMessage);
        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    private static string FormatSize(long bytes)
        => bytes >= 1024L * 1024 * 1024
            ? $"{bytes / (1024d * 1024 * 1024):0.0} GB"
            : bytes >= 1024 * 1024
                ? $"{bytes / (1024d * 1024):0.0} MB"
                : $"{Math.Max(bytes, 0)} bytes";
}

public sealed partial class ApplicationsPageViewModel : PageViewModel
{
    private readonly IPackageManager _packageManager;
    private readonly IPackageInventoryService _inventoryService;
    private readonly IPackageIconService _iconService;
    private readonly IPackagePreferenceRepository _preferences;
    private readonly IPackageClassifier _classifier;
    private readonly IPackageReferenceCatalog _referenceCatalog;
    private readonly IReferencePackageDumpService _referenceDumpService;
    private readonly IConfirmationService _confirmation;
    private readonly ISettingsStore _settings;
    private readonly Task _settingsLoaded;
    private readonly SemaphoreSlim _iconThrottle = new(4, 4);
    private CancellationTokenSource? _iconSource;
    private bool _updatingIconSetting;
    private IReadOnlyDictionary<string, PackageAssessment> _assessments =
        new Dictionary<string, PackageAssessment>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, PackageReferenceAnalysisItem> _references =
        new Dictionary<string, PackageReferenceAnalysisItem>(StringComparer.OrdinalIgnoreCase);
    private PackageInventoryResult? _lastInventory;

    public ApplicationsPageViewModel(
        IPackageManager packageManager,
        IPackageInventoryService inventoryService,
        IPackageIconService iconService,
        IPackagePreferenceRepository preferences,
        IPackageClassifier classifier,
        IPackageReferenceCatalog referenceCatalog,
        IReferencePackageDumpService referenceDumpService,
        IConfirmationService confirmation,
        ISettingsStore settings,
        ObservableCollection<AndroidDevice> devices) : base("Applications")
    {
        _packageManager = packageManager;
        _inventoryService = inventoryService;
        _iconService = iconService;
        _preferences = preferences;
        _classifier = classifier;
        _referenceCatalog = referenceCatalog;
        _referenceDumpService = referenceDumpService;
        _confirmation = confirmation;
        _settings = settings;
        _settingsLoaded = LoadIconSettingAsync();
        Devices = devices;
        Devices.CollectionChanged += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(TargetSerial)
                && Devices.Any(device => device.Serial == TargetSerial)
                && Packages.Count == 0)
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
    private PackageAssessment? _selectedAssessment;

    [ObservableProperty]
    private PackageReferenceAnalysisItem? _selectedReference;

    [ObservableProperty]
    private string _message = "Waiting for a connected device…";

    [ObservableProperty]
    private bool _showPackageIcons;

    [ObservableProperty]
    private string _iconStatus = "Package icons are disabled for faster scans.";

    [ObservableProperty]
    private string _selectedFilter = "All";

    [ObservableProperty]
    private string _referenceSummary = "Reference baseline analysis is available after scanning.";

    public IReadOnlyList<PackageReferenceMatch> SelectedReferences
        => SelectedReference?.Matches ?? [];

    public string SelectedSafetyStatus
        => SelectedAssessment is null
            ? "Safety: no package selected."
            : PackageAssessmentReferenceEnricher.IsSafetyLocked(SelectedAssessment)
                ? SelectedAssessment.IsProtected
                    ? "Safety: locked by runtime or reference protection."
                    : "Safety: locked by Critical/Keep rule."
                : SelectedAssessment.Risk == PackageRiskLevel.Unknown
                    ? "Safety: manual review required before changes."
                    : "Safety: reviewed action can be confirmed manually.";

    public string SelectedObservedOn
        => SelectedReference is { ObservedOn.Count: > 0 }
            ? string.Join(", ", SelectedReference.ObservedOn)
            : "No reference device match.";

    public string SelectedDependencies
        => SelectedReference is { Matches.Count: > 0 }
            ? string.Join(", ", SelectedReference.Matches
                .SelectMany(match => match.Dependencies)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            : "None detected.";

    public string SelectedNeededBy
        => SelectedReference is { Matches.Count: > 0 }
            ? string.Join(", ", SelectedReference.Matches
                .SelectMany(match => match.NeededBy)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            : "None recorded.";

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
    partial void OnTargetSerialChanged(string value)
    {
        if (Devices.Any(device => string.Equals(device.Serial, value, StringComparison.OrdinalIgnoreCase)))
            _ = RefreshAsync();
    }

    partial void OnSelectedPackageChanged(PackageInventoryEntry? value)
    {
        SelectedAssessment = value is not null
            && _assessments.TryGetValue(value.PackageName, out var assessment)
                ? assessment
                : null;
        SelectedReference = value is not null
            && _references.TryGetValue(value.PackageName, out var reference)
                ? reference
                : null;
        OnPropertyChanged(nameof(SelectedReferences));
        OnPropertyChanged(nameof(SelectedObservedOn));
        OnPropertyChanged(nameof(SelectedDependencies));
        OnPropertyChanged(nameof(SelectedNeededBy));
    }

    partial void OnSelectedAssessmentChanged(PackageAssessment? value)
        => OnPropertyChanged(nameof(SelectedSafetyStatus));

    partial void OnSelectedReferenceChanged(PackageReferenceAnalysisItem? value)
    {
        OnPropertyChanged(nameof(SelectedReferences));
        OnPropertyChanged(nameof(SelectedObservedOn));
        OnPropertyChanged(nameof(SelectedDependencies));
        OnPropertyChanged(nameof(SelectedNeededBy));
    }

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
        _lastInventory = inventory;
        Packages.Clear();
        foreach (var package in inventory.Packages)
            Packages.Add(package);
        var device = Devices.FirstOrDefault(candidate =>
            string.Equals(candidate.Serial, TargetSerial.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? new AndroidDevice { Serial = TargetSerial.Trim() };
        var context = PackageClassificationContexts.FromInventory(device, inventory.Packages);
        var referenceAnalysis = await _referenceCatalog.AnalyzeAsync(device, inventory.Packages);
        _references = referenceAnalysis.Packages
            .ToDictionary(reference => reference.PackageName, StringComparer.OrdinalIgnoreCase);
        _assessments = inventory.Packages
            .Select(package => PackageAssessmentReferenceEnricher.ApplyReferenceEvidence(
                _classifier.Classify(package, context),
                _references.GetValueOrDefault(package.PackageName)))
            .ToDictionary(assessment => assessment.PackageName, StringComparer.OrdinalIgnoreCase);
        ReferenceSummary = BuildReferenceSummary(referenceAnalysis.Summary);
        SelectedPackage = Packages.FirstOrDefault();
        OnPropertyChanged(nameof(FilteredPackages));
        Message = inventory.ErrorMessage is null
            ? $"{Packages.Count} packages loaded."
            : $"{Packages.Count} packages loaded with warnings: {inventory.ErrorMessage}";
        if (ShowPackageIcons)
            StartIconLoading(TargetSerial.Trim());
    }

    [RelayCommand]
    private async Task ExportReferenceDumpAsync()
    {
        if (_lastInventory is null || string.IsNullOrWhiteSpace(TargetSerial))
        {
            Message = "Refresh packages before exporting a reference dump.";
            return;
        }
        var device = Devices.FirstOrDefault(candidate =>
            string.Equals(candidate.Serial, TargetSerial.Trim(), StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            Message = "The target device is no longer connected.";
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Reference package dump (*.json)|*.json",
            DefaultExt = ".json",
            FileName = $"{SanitizeFileName(device.Manufacturer ?? "AndroidTV")}-" +
                $"{SanitizeFileName(device.Model ?? "device")}-reference.json"
        };
        if (dialog.ShowDialog() != true)
            return;
        await _referenceDumpService.ExportAsync(device, _lastInventory, dialog.FileName);
        Message = $"Reference dump exported: {dialog.FileName}";
    }

    private async Task LoadIconSettingAsync()
    {
        var value = await _settings.GetAsync("applications.showPackageIcons");
        _updatingIconSetting = true;
        ShowPackageIcons = value is null
            || string.Equals(value, bool.TrueString, StringComparison.OrdinalIgnoreCase);
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
    private Task LaunchAsync() => RunActionAsync("Launch", (serial, package, _) => _packageManager.LaunchAsync(serial, package));

    [RelayCommand]
    private Task ForceStopAsync() => RunActionAsync("Force stop", (serial, package, _) => _packageManager.ForceStopAsync(serial, package));

    [RelayCommand]
    private Task DisableAsync() => RunActionAsync("Disable", (serial, package, fingerprint) => _packageManager.DisableAsync(serial, package, expectedBuildFingerprint: fingerprint), true);

    [RelayCommand]
    private Task EnableAsync() => RunActionAsync("Enable", (serial, package, fingerprint) => _packageManager.EnableAsync(serial, package, expectedBuildFingerprint: fingerprint));

    [RelayCommand]
    private Task UninstallAsync() => RunActionAsync("Uninstall for user", (serial, package, fingerprint) => _packageManager.UninstallForUserAsync(serial, package, expectedBuildFingerprint: fingerprint), true);

    [RelayCommand]
    private Task RestoreAsync() => RunActionAsync("Restore package", (serial, package, fingerprint) => _packageManager.RestoreAsync(serial, package, expectedBuildFingerprint: fingerprint));

    [RelayCommand]
    private Task ClearDataAsync() => RunActionAsync("Clear data", (serial, package, fingerprint) => _packageManager.ClearDataAsync(serial, package, expectedBuildFingerprint: fingerprint), true);

    [RelayCommand]
    private Task ClearCacheAsync() => RunActionAsync("Clear cache", (serial, package, fingerprint) => _packageManager.ClearCacheAsync(serial, package, expectedBuildFingerprint: fingerprint), true);

    [RelayCommand]
    private Task OpenAppSettingsAsync() => RunActionAsync("Open app settings", (serial, package, _) =>
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
        (serial, package, permission, fingerprint) => _packageManager.GrantPermissionAsync(serial, package, permission, expectedBuildFingerprint: fingerprint));

    [RelayCommand]
    private Task RevokePermissionAsync() => RunPermissionActionAsync("Revoke permission",
        (serial, package, permission, fingerprint) => _packageManager.RevokePermissionAsync(serial, package, permission, expectedBuildFingerprint: fingerprint));

    [RelayCommand]
    private Task FullUninstallAsync()
    {
        if (SelectedPackage?.IsSystem == true)
        {
            Message = "Full uninstall is blocked for system packages. Use Disable or Uninstall for user 0.";
            return Task.CompletedTask;
        }
        return RunActionAsync("Full uninstall", (serial, package, fingerprint) => _packageManager.FullUninstallAsync(serial, package, expectedBuildFingerprint: fingerprint), true);
    }

    private async Task RunActionAsync(
        string action,
        Func<string, string, string?, Task<AdbCommandResult>> operation,
        bool destructive = false)
    {
        if (SelectedPackage is null || string.IsNullOrWhiteSpace(TargetSerial))
        {
            Message = "Select a package and enter a target serial.";
            return;
        }
        var serial = TargetSerial.Trim();
        var packageName = SelectedPackage.PackageName;
        if (destructive
            && SelectedAssessment is { } assessment
            && PackageAssessmentReferenceEnricher.IsSafetyLocked(assessment))
        {
            Message = $"{action} blocked: {packageName} is classified as "
                      + $"{assessment.Risk} with action {assessment.RecommendedAction}.";
            return;
        }
        if (destructive && !_confirmation.Confirm(
                $"{action} · confirm target",
                $"{action} applies to:\n\n{packageName}\n\nTarget device:\n{serial}\n\nContinue?"))
        {
            Message = "Operation canceled.";
            return;
        }
        Message = $"{action} · {packageName}…";
        var result = await operation(serial, packageName, SelectedPackage.BuildFingerprint);
        Message = result.IsSuccess ? $"{action} completed." : result.StandardError.Trim();
    }

    private async Task RunPermissionActionAsync(
        string action,
        Func<string, string, string, string?, Task<AdbCommandResult>> operation)
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
        var result = await operation(serial, package, permission, SelectedPackage.BuildFingerprint);
        Message = result.IsSuccess ? $"{action} completed." : result.StandardError.Trim();
    }

    private static string BuildReferenceSummary(PackageReferenceSummary summary)
        => $"{summary.TotalPackages} packages · {summary.BaselineMatches} reference matches · " +
           $"{summary.UnknownPackages} unknown\n" +
           string.Join(" · ", summary.OriginCounts.Select(count =>
               $"{FormatOrigin(count.Origin)} {count.Count}"));

    private static string FormatOrigin(PackageOrigin origin)
        => origin switch
        {
            PackageOrigin.AospTvCore => "AOSP TV",
            PackageOrigin.GoogleTvGms => "Google TV",
            PackageOrigin.SocPlatform => "SoC",
            PackageOrigin.RegionalOperator => "Regional",
            PackageOrigin.ThirdParty => "Third-party",
            PackageOrigin.Oem => "OEM",
            _ => "Unknown"
        };

    private static string SanitizeFileName(string value)
        => string.Concat(value.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
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
    private readonly IConfirmationService _confirmation;
    private long? _lastExecutionId;
    private string? _validatedScriptJson;

    public ScriptsPageViewModel(
        IScriptExecutionService executionService,
        IConfirmationService confirmation,
        ObservableCollection<AndroidDevice> devices) : base("Scripts")
    {
        _executionService = executionService;
        _confirmation = confirmation;
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
            _validatedScriptJson = ScriptJson;
        }
        catch (Exception exception)
        {
            _validatedScriptJson = null;
            Preview = $"Validation failed\n\n{exception.Message}";
        }
    }

    [RelayCommand]
    private async Task RunScriptAsync()
    {
        try
        {
            var script = ScriptDefinitionParser.Parse(ScriptJson);
            if (!string.Equals(_validatedScriptJson, ScriptJson, StringComparison.Ordinal))
            {
                Preview = "Validate this exact script before running it.";
                return;
            }
            if (string.IsNullOrWhiteSpace(TargetSerial))
            {
                Preview = "Select or enter a target device before running.";
                return;
            }
            if (SelectedDevice is not null
                && !string.Equals(SelectedDevice.Serial, TargetSerial.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                Preview = "The target serial does not match the selected device.";
                return;
            }
            if (!_confirmation.Confirm(
                    "Run script",
                    $"Run {script.Actions.Count} reviewed action(s) on {TargetSerial.Trim()}?\n\n" +
                    "Some actions may change packages, settings, files, or device state."))
            {
                Preview = "Script canceled.";
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
    private readonly IUpdateService _updates;
    private readonly IAdbDeviceTracker _deviceTracker;
    private readonly IConfirmationService _confirmation;

    public SettingsPageViewModel(
        IAdbToolsManager toolsManager,
        ILocalAppDataPaths paths,
        ISettingsStore settings,
        IUpdateService updates,
        IAdbDeviceTracker deviceTracker,
        IConfirmationService confirmation) : base("Settings")
    {
        _toolsManager = toolsManager;
        _paths = paths;
        _settings = settings;
        _updates = updates;
        _deviceTracker = deviceTracker;
        _confirmation = confirmation;
        _paths.EnsureCreated();
        _ = LoadAsync();
    }

    public string DataPath => Path.GetDirectoryName(_paths.DatabasePath) ?? _paths.Root;
    public string ToolsPath => _paths.ToolsPath;
    public string LogsPath => _paths.LogsPath;
    public IReadOnlyList<UpdateIntervalOption> UpdateIntervals { get; } =
        [new(0, "Never"), new(6, "Every 6 hours"), new(12, "Every 12 hours"),
         new(24, "Every day"), new(168, "Every week")];

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

    [ObservableProperty]
    private bool _updatesEnabled = true;

    [ObservableProperty]
    private bool _checkForUpdatesOnStartup = true;

    [ObservableProperty]
    private UpdateIntervalOption _selectedUpdateInterval = new(24, "Every day");

    [ObservableProperty]
    private string _updateStatus = "Update checks have not run yet.";

    [ObservableProperty]
    private UpdateRelease? _availableRelease;

    private async Task LoadAsync()
    {
        StartMinimized = await ReadBoolAsync("general.startMinimized", false);
        MinimizeToTray = await ReadBoolAsync("general.minimizeToTray", true);
        CloseToTray = await ReadBoolAsync("general.closeToTray", true);
        RememberSelectedDevice = await ReadBoolAsync("general.rememberSelectedDevice", true);
        if (Enum.TryParse<AppTheme>(await _settings.GetAsync("appearance.theme"), true, out var theme))
            SelectedTheme = theme;
        UpdatesEnabled = await ReadBoolAsync("updates.enabled", true);
        CheckForUpdatesOnStartup = await ReadBoolAsync("updates.checkOnStartup", true);
        var intervalHours = int.TryParse(await _settings.GetAsync("updates.intervalHours"), out var parsed)
            ? parsed
            : 24;
        SelectedUpdateInterval = UpdateIntervals.FirstOrDefault(item => item.Hours == intervalHours)
            ?? UpdateIntervals[3];
        if (UpdatesEnabled && CheckForUpdatesOnStartup)
            _ = CheckForUpdatesIfDueAsync(force: false);
        _ = MonitorUpdateIntervalAsync();
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

    partial void OnUpdatesEnabledChanged(bool value)
        => _ = SaveBoolAsync("updates.enabled", value);

    partial void OnCheckForUpdatesOnStartupChanged(bool value)
        => _ = SaveBoolAsync("updates.checkOnStartup", value);

    partial void OnSelectedUpdateIntervalChanged(UpdateIntervalOption value)
        => _ = _settings.SetAsync("updates.intervalHours", value.Hours.ToString());

    private async Task<bool> ReadBoolAsync(string key, bool fallback)
        => bool.TryParse(await _settings.GetAsync(key), out var value) ? value : fallback;

    private Task SaveBoolAsync(string key, bool value) => _settings.SetAsync(key, value.ToString());

    [RelayCommand]
    private Task CheckForUpdatesAsync() => CheckForUpdatesIfDueAsync(force: true);

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (AvailableRelease is null)
        {
            UpdateStatus = "Check for updates before installing.";
            return;
        }
        if (!_confirmation.Confirm(
                $"Install {AvailableRelease.Version}",
                $"{AvailableRelease.Name}\n\n{AvailableRelease.ReleaseNotes}\n\nAndroid TV Manager will close, stop adb.exe, and run the verified installer. Continue?"))
        {
            UpdateStatus = "Update canceled.";
            return;
        }

        UpdateStatus = "Stopping device tracking and adb.exe before update…";
        await _deviceTracker.StopAsync();
        var result = await _updates.DownloadAndInstallAsync(AvailableRelease);
        UpdateStatus = result.Message;
        if (result.Started)
            System.Windows.Application.Current.Shutdown();
    }

    private async Task CheckForUpdatesIfDueAsync(bool force)
    {
        if (!force && !UpdatesEnabled)
            return;
        if (!force && !CheckForUpdatesOnStartup && SelectedUpdateInterval.Hours == 0)
            return;
        var lastValue = await _settings.GetAsync("updates.lastCheckedUtc");
        if (!force && DateTimeOffset.TryParse(lastValue, out var last)
            && SelectedUpdateInterval.Hours > 0
            && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(SelectedUpdateInterval.Hours))
            return;
        UpdateStatus = "Checking GitHub for updates…";
        var result = await _updates.CheckAsync(AppInfo.Version);
        await _settings.SetAsync("updates.lastCheckedUtc", result.CheckedUtc.ToString("O"));
        AvailableRelease = result.Release;
        UpdateStatus = result.ErrorMessage is not null
            ? $"Update check failed: {result.ErrorMessage}"
            : result.IsUpdateAvailable
                ? $"Update available: {result.Release!.Version}."
                : $"You are running the latest version ({result.CurrentVersion}).";
    }

    private async Task MonitorUpdateIntervalAsync()
    {
        while (true)
        {
            var hours = SelectedUpdateInterval.Hours;
            await Task.Delay(hours > 0 ? TimeSpan.FromHours(hours) : TimeSpan.FromMinutes(5));
            if (UpdatesEnabled && hours > 0)
                await CheckForUpdatesIfDueAsync(force: false);
        }
    }

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
