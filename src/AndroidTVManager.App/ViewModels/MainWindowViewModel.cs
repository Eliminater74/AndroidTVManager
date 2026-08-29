using System.Collections.ObjectModel;
using System.Windows;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
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
    private object _currentPage;
    private NavigationEntry _selectedNavigation;
    private AndroidDevice? _selectedDevice;
    private string _adbStatus = "ADB · Checking";
    private string _adbVersion = "Checking managed Platform-Tools…";

    public MainWindowViewModel(
        IAdbToolsManager toolsManager,
        IAdbDeviceTracker deviceTracker,
        IConnectionHistoryRepository history)
    {
        _toolsManager = toolsManager;
        _deviceTracker = deviceTracker;
        _history = history;
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

        _pages = Navigation.ToDictionary(item => item.Label, item => (object)new PageViewModel(item.Label));
        _selectedNavigation = Navigation[0];
        _currentPage = _pages[_selectedNavigation.Label];
        Devices = [];
        _deviceTracker.DevicesChanged += OnDevicesChanged;
    }

    public ObservableCollection<NavigationEntry> Navigation { get; }
    public ObservableCollection<AndroidDevice> Devices { get; }
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

public sealed class PageViewModel : ObservableObject
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
