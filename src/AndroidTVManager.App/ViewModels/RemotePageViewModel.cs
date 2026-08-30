using System.Collections.ObjectModel;
using System.Text.Json;
using AndroidTVManager.App.Services;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVManager.App.ViewModels;

public sealed partial class RemotePageViewModel : PageViewModel
{
    private readonly IRemoteControlService _remote;
    private readonly IPackageManager _packages;
    private readonly ISettingsStore _settings;
    private readonly Action<AndroidDevice?> _selectTarget;

    public RemotePageViewModel(
        IRemoteControlService remote,
        IPackageManager packages,
        ISettingsStore settings,
        ObservableCollection<AndroidDevice> devices,
        Action<AndroidDevice?> selectTarget) : base("Remote")
    {
        _remote = remote;
        _packages = packages;
        _settings = settings;
        _selectTarget = selectTarget;
        Devices = devices;
        _ = LoadFavoritesAsync();
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public ObservableCollection<RemoteFavorite> Favorites { get; } = [];

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private string _favoriteLabel = string.Empty;

    [ObservableProperty]
    private string _favoritePackage = string.Empty;

    [ObservableProperty]
    private string _status = "Select an authorized device to use the remote.";

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
        => _selectTarget(value);

    [RelayCommand]
    private async Task PressAsync(RemoteKey key)
    {
        if (!TryGetSerial(out var serial))
            return;
        var result = await _remote.PressAsync(serial, key);
        Status = result.IsSuccess ? $"{key} sent." : $"Remote command failed: {Error(result)}";
    }

    [RelayCommand]
    private async Task RepeatAsync(RemoteKey key)
    {
        if (!TryGetSerial(out var serial))
            return;
        for (var index = 0; index < 5; index++)
        {
            var result = await _remote.PressAsync(serial, key);
            if (!result.IsSuccess)
            {
                Status = $"Remote repeat failed: {Error(result)}";
                return;
            }
            await Task.Delay(45);
        }
        Status = $"{key} repeated 5 times.";
    }

    [RelayCommand]
    private async Task TypeTextAsync()
    {
        if (!TryGetSerial(out var serial) || string.IsNullOrEmpty(Text))
            return;
        var result = await _remote.TypeTextAsync(serial, Text);
        Status = result.IsSuccess ? "Text sent." : $"Text command failed: {Error(result)}";
    }

    [RelayCommand]
    private async Task AddFavoriteAsync()
    {
        if (string.IsNullOrWhiteSpace(FavoriteLabel) || string.IsNullOrWhiteSpace(FavoritePackage))
        {
            Status = "Favorite label and package name are required.";
            return;
        }
        Favorites.Add(new(FavoriteLabel.Trim(), FavoritePackage.Trim()));
        FavoriteLabel = string.Empty;
        FavoritePackage = string.Empty;
        await SaveFavoritesAsync();
        Status = "Remote favorite saved.";
    }

    [RelayCommand]
    private async Task LaunchFavoriteAsync(RemoteFavorite favorite)
    {
        if (!TryGetSerial(out var serial))
            return;
        var result = await _packages.LaunchAsync(serial, favorite.PackageName);
        Status = result.IsSuccess ? $"{favorite.Label} launched." : $"Launch failed: {Error(result)}";
    }

    [RelayCommand]
    private async Task RemoveFavoriteAsync(RemoteFavorite favorite)
    {
        Favorites.Remove(favorite);
        await SaveFavoritesAsync();
    }

    private bool TryGetSerial(out string serial)
    {
        serial = SelectedDevice?.Serial.Trim() ?? string.Empty;
        if (SelectedDevice?.State == DeviceState.Device && serial.Length > 0)
            return true;
        Status = "Select a connected and authorized device first.";
        return false;
    }

    private async Task LoadFavoritesAsync()
    {
        var json = await _settings.GetAsync("remote.favorites");
        if (string.IsNullOrWhiteSpace(json))
            return;
        try
        {
            foreach (var favorite in JsonSerializer.Deserialize<List<RemoteFavorite>>(json) ?? [])
                Favorites.Add(favorite);
        }
        catch (JsonException)
        {
            Status = "Saved remote favorites could not be loaded.";
        }
    }

    private Task SaveFavoritesAsync()
        => _settings.SetAsync("remote.favorites", JsonSerializer.Serialize(Favorites));

    private static string Error(AdbCommandResult result)
        => result.StandardError.Trim() is { Length: > 0 } error ? error : "ADB command failed.";
}
