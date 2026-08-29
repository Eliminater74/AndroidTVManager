using System.Collections.ObjectModel;
using System.Windows.Input;
using AndroidTVManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVManager.App.ViewModels;

public sealed record NavigationEntry(string Label, string Glyph);

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<string, object> _pages;
    private object _currentPage;
    private NavigationEntry _selectedNavigation;
    private AndroidDevice? _selectedDevice;

    public MainWindowViewModel()
    {
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
    }

    public ObservableCollection<NavigationEntry> Navigation { get; }
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

    public int ConnectedDeviceCount => 0;
    public string AdbStatus => "ADB · Checking";
    public string AdbVersion => "Platform-Tools not installed";

    [RelayCommand]
    private void Refresh()
    {
        OnPropertyChanged(nameof(AdbStatus));
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
