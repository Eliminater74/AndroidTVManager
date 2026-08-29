using System.Collections.ObjectModel;
using System.IO;
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
    private object _currentPage;
    private NavigationEntry _selectedNavigation;
    private AndroidDevice? _selectedDevice;
    private string _adbStatus = "ADB · Checking";
    private string _adbVersion = "Checking managed Platform-Tools…";

    public MainWindowViewModel(
        IAdbToolsManager toolsManager,
        IAdbDeviceTracker deviceTracker,
        IConnectionHistoryRepository history,
        IAdbConnectionService connectionService,
        IApkInstaller apkInstaller,
        IPackageManager packageManager,
        IDeviceToolsService toolsService)
    {
        _toolsManager = toolsManager;
        _deviceTracker = deviceTracker;
        _history = history;
        _connectionService = connectionService;
        _apkInstaller = apkInstaller;
        _packageManager = packageManager;
        _toolsService = toolsService;
        Navigation = new ObservableCollection<NavigationEntry>
        {
            new("Dashboard", "⌂"),
            new("Devices", "◉"),
            new("Connections", "↔"),
            new("Install APK", "＋"),
            new("Applications", "▦"),
            new("Scripts", "◇"),
            new("Tools", "⚙"),
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
    public ObservableCollection<AndroidDevice> Devices { get; }

    private object CreatePage(NavigationEntry entry) => entry.Label switch
    {
        "Devices" => new DevicesPageViewModel(Devices),
        "Connections" => new ConnectionsPageViewModel(_connectionService, _history),
        "Install APK" => new InstallApkPageViewModel(_apkInstaller),
        "Applications" => new ApplicationsPageViewModel(_packageManager),
        "Scripts" => new ScriptsPageViewModel(),
        "Tools" => new ToolsPageViewModel(_toolsService),
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
                CurrentPage = _pages[value.Label];
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

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await InitializeRuntimeAsync();
    }

    public async Task InitializeRuntimeAsync(CancellationToken cancellationToken = default)
    {
        _adbStatus = "ADB · Checking";
        OnPropertyChanged(nameof(AdbStatus));
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
            await _deviceTracker.StartAsync(cancellationToken);
    }

    private async void OnDevicesChanged(object? sender, IReadOnlyList<AndroidDevice> devices)
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Devices.Clear();
            foreach (var device in devices)
                Devices.Add(device);
            OnPropertyChanged(nameof(ConnectedDeviceCount));
            if (SelectedDevice is not null)
                SelectedDevice = Devices.FirstOrDefault(device => device.Serial == SelectedDevice.Serial);
        });

        foreach (var device in devices)
            await _history.RecordDeviceSeenAsync(device);
    }
}

public class PageViewModel : ObservableObject
{
    public PageViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }
    public string Eyebrow => Title.ToUpperInvariant();
    public string Description => Title == "Dashboard"
        ? "Your connected Android entertainment devices, at a glance."
        : $"Manage your Android TV workflow from {Title.ToLowerInvariant()}.";
}

public sealed class DevicesPageViewModel(ObservableCollection<AndroidDevice> devices) : ObservableObject
{
    public ObservableCollection<AndroidDevice> Devices { get; } = devices;
    public AndroidDevice? SelectedDevice { get; set; }
}

public sealed class AboutPageViewModel : PageViewModel
{
    public AboutPageViewModel() : base("About")
    {
    }

    public string Version => "1.0.0-B1";
    public string ProductLine => "Android TV / Google TV Device Management Toolbox";
    public string Mission => "A faster, safer, more modern way to manage the ADB-capable devices in your living room.";
    public string PlatformToolsNote => "Official Android SDK Platform-Tools are downloaded directly from Google and kept in LocalAppData.";
}

public sealed partial class ConnectionsPageViewModel : PageViewModel
{
    private readonly IAdbConnectionService _connectionService;
    private readonly IConnectionHistoryRepository _history;

    public ConnectionsPageViewModel(
        IAdbConnectionService connectionService,
        IConnectionHistoryRepository history) : base("Connections")
    {
        _connectionService = connectionService;
        _history = history;
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

    public InstallApkPageViewModel(IApkInstaller installer) : base("Install APK")
    {
        _installer = installer;
    }

    [ObservableProperty]
    private string _targetSerial = string.Empty;

    [ObservableProperty]
    private string _apkPath = string.Empty;

    [ObservableProperty]
    private string _output = "Select an APK and enter the target device serial.";

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetSerial) || string.IsNullOrWhiteSpace(ApkPath))
        {
            Output = "Target serial and APK path are required.";
            return;
        }
        if (!File.Exists(ApkPath))
        {
            Output = "The selected APK file does not exist.";
            return;
        }

        Output = $"Installing {Path.GetFileName(ApkPath)}…";
        var result = await _installer.InstallAsync(TargetSerial.Trim(), ApkPath.Trim());
        Output = result.IsSuccess ? "APK installed successfully." : result.StandardError.Trim();
    }
}

public sealed partial class ApplicationsPageViewModel : PageViewModel
{
    private readonly IPackageManager _packageManager;

    public ApplicationsPageViewModel(IPackageManager packageManager) : base("Applications")
    {
        _packageManager = packageManager;
    }

    public ObservableCollection<PackageInfo> Packages { get; } = [];

    [ObservableProperty]
    private string _targetSerial = string.Empty;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private PackageInfo? _selectedPackage;

    [ObservableProperty]
    private string _message = "Enter a target serial and refresh packages.";

    public IEnumerable<PackageInfo> FilteredPackages =>
        Packages.Where(package => string.IsNullOrWhiteSpace(Search)
            || package.PackageName.Contains(Search, StringComparison.OrdinalIgnoreCase));

    partial void OnSearchChanged(string value) => OnPropertyChanged(nameof(FilteredPackages));

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetSerial))
        {
            Message = "Target serial is required.";
            return;
        }
        Message = "Loading package list…";
        var packages = await _packageManager.ListAsync(TargetSerial.Trim());
        Packages.Clear();
        foreach (var package in packages)
            Packages.Add(package);
        OnPropertyChanged(nameof(FilteredPackages));
        Message = $"{Packages.Count} packages loaded.";
    }

    [RelayCommand]
    private Task LaunchAsync() => RunActionAsync("Launch", (serial, package) => _packageManager.LaunchAsync(serial, package));

    [RelayCommand]
    private Task ForceStopAsync() => RunActionAsync("Force stop", (serial, package) => _packageManager.ForceStopAsync(serial, package));

    [RelayCommand]
    private Task DisableAsync() => RunActionAsync("Disable", (serial, package) => _packageManager.DisableAsync(serial, package));

    [RelayCommand]
    private Task EnableAsync() => RunActionAsync("Enable", (serial, package) => _packageManager.EnableAsync(serial, package));

    [RelayCommand]
    private Task UninstallAsync() => RunActionAsync("Uninstall for user", (serial, package) => _packageManager.UninstallForUserAsync(serial, package));

    [RelayCommand]
    private Task ClearDataAsync() => RunActionAsync("Clear data", (serial, package) => _packageManager.ClearDataAsync(serial, package));

    private async Task RunActionAsync(
        string action,
        Func<string, string, Task<AdbCommandResult>> operation)
    {
        if (SelectedPackage is null || string.IsNullOrWhiteSpace(TargetSerial))
        {
            Message = "Select a package and enter a target serial.";
            return;
        }
        var serial = TargetSerial.Trim();
        var packageName = SelectedPackage.PackageName;
        Message = $"{action} · {packageName}…";
        var result = await operation(serial, packageName);
        Message = result.IsSuccess ? $"{action} completed." : result.StandardError.Trim();
    }
}

public sealed partial class ToolsPageViewModel : PageViewModel
{
    private readonly IDeviceToolsService _toolsService;

    public ToolsPageViewModel(IDeviceToolsService toolsService) : base("Tools")
    {
        _toolsService = toolsService;
    }

    [ObservableProperty]
    private string _targetSerial = string.Empty;

    [ObservableProperty]
    private string _shellCommand = "getprop ro.product.model";

    [ObservableProperty]
    private string _output = "Targeted tools keep every operation explicit.";

    [RelayCommand]
    private async Task RunShellAsync()
    {
        if (!HasTarget())
            return;
        var result = await _toolsService.ShellAsync(TargetSerial.Trim(), ShellCommand);
        Output = result.IsSuccess ? result.StandardOutput.Trim() : result.StandardError.Trim();
    }

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
    public ScriptsPageViewModel() : base("Scripts")
    {
    }

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
}
