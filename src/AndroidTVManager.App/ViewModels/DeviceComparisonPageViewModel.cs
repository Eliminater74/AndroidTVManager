using System.Collections.ObjectModel;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVManager.App.ViewModels;

public sealed partial class DeviceComparisonPageViewModel : PageViewModel
{
    private readonly IDeviceComparisonService _comparison;
    private readonly Action<AndroidDevice?> _selectTarget;

    public DeviceComparisonPageViewModel(
        IDeviceComparisonService comparison,
        ObservableCollection<AndroidDevice> devices,
        Action<AndroidDevice?> selectTarget) : base("Device Comparison")
    {
        _comparison = comparison;
        Devices = devices;
        _selectTarget = selectTarget;
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public ObservableCollection<DeviceComparisonSection> Sections { get; } = [];

    [ObservableProperty]
    private AndroidDevice? _leftDevice;

    [ObservableProperty]
    private AndroidDevice? _rightDevice;

    [ObservableProperty]
    private string _status = "Select two connected devices to compare their evidence.";

    partial void OnLeftDeviceChanged(AndroidDevice? value)
        => _selectTarget(value);

    [RelayCommand]
    private async Task CompareAsync()
    {
        if (LeftDevice is null || RightDevice is null)
        {
            Status = "Select two devices first.";
            return;
        }
        try
        {
            Status = "Capturing both devices…";
            var result = await _comparison.CompareAsync(LeftDevice, RightDevice);
            Sections.Clear();
            foreach (var section in result.Sections)
                Sections.Add(section);
            Status = $"Compared {Sections.Count} sections at {result.ComparedUtc.LocalDateTime:t}.";
        }
        catch (Exception exception)
        {
            Status = $"Comparison failed: {exception.Message}";
        }
    }
}
