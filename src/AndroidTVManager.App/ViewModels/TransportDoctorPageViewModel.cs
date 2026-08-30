using System.Collections.ObjectModel;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVManager.App.ViewModels;

public sealed partial class TransportDoctorPageViewModel : PageViewModel
{
    private readonly ITransportDoctorService _service;
    private CancellationTokenSource? _probeSource;

    public TransportDoctorPageViewModel(
        ITransportDoctorService service,
        ObservableCollection<AndroidDevice> devices) : base("ADB Transport Doctor")
    {
        _service = service;
        Devices = devices;
        SelectedDevice = devices.FirstOrDefault(device => device.State == DeviceState.Device);
        devices.CollectionChanged += (_, _) =>
        {
            if (SelectedDevice is null)
                SelectedDevice = devices.FirstOrDefault(device => device.State == DeviceState.Device);
        };
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public IReadOnlyList<int> ProbeCounts { get; } = [10, 25, 50];

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    private int _selectedProbeCount = 10;

    [ObservableProperty]
    private TransportDoctorResult? _result;

    [ObservableProperty]
    private string _status = "Select a connected device to test its ADB transport.";

    [ObservableProperty]
    private bool _isBusy;

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
    {
        _probeSource?.Cancel();
        Result = null;
        Status = value is null
            ? "Select a connected device to test its ADB transport."
            : $"Ready to test {value.FriendlyName}.";
    }

    [RelayCommand]
    private async Task RunTestAsync()
    {
        if (SelectedDevice is null || SelectedDevice.State != DeviceState.Device)
        {
            Status = "Select a connected device before running a transport test.";
            return;
        }
        _probeSource?.Cancel();
        _probeSource?.Dispose();
        _probeSource = new CancellationTokenSource();
        IsBusy = true;
        Status = $"Running {SelectedProbeCount} ADB transport probes…";
        try
        {
            Result = await _service.RunAsync(SelectedDevice, SelectedProbeCount, _probeSource.Token);
            Status = Result.IsStable
                ? $"Transport stable: {Result.SuccessfulProbes}/{Result.Probes.Count} probes succeeded."
                : $"Transport instability detected: {Result.FailedProbes} probe(s) failed.";
        }
        catch (OperationCanceledException)
        {
            Status = "Transport test canceled.";
        }
        catch (Exception exception)
        {
            Status = $"Transport test failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _probeSource?.Cancel();
        Status = "Canceling transport test…";
    }
}
