using System.Collections.ObjectModel;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Core.Adb;

public sealed class AdbDeviceSession : IAdbDeviceSession
{
    private readonly IAdbConnectionService _connection;
    private readonly IAdbDeviceTracker _tracker;
    private readonly IAppLogger _logger;
    private AndroidDevice? _selectedDevice;
    private string? _preferredTarget;
    private int _generation;

    public AdbDeviceSession(
        IAdbConnectionService connection,
        IAdbDeviceTracker tracker,
        IAppLogger logger)
    {
        _connection = connection;
        _tracker = tracker;
        _logger = logger;
        Devices = [];
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public AndroidDevice? SelectedDevice => _selectedDevice;
    public string? SelectedSerial => _selectedDevice?.Serial;
    public string? PreferredTarget => _preferredTarget;
    public int Generation => _generation;
    public event EventHandler? Changed;

    public void Prefer(string serialOrEndpoint)
    {
        if (string.IsNullOrWhiteSpace(serialOrEndpoint))
            return;

        _preferredTarget = serialOrEndpoint.Trim();
        if (DeviceSelection.Find(Devices, _preferredTarget) is { } device)
            SelectInternal(device);
        else
            RaiseChanged();
    }

    public void Select(AndroidDevice? device) => SelectInternal(device);

    public void ReplaceLiveDevices(IReadOnlyList<AndroidDevice> devices)
    {
        var previousSerial = _selectedDevice?.Serial;
        var preferred = _preferredTarget;
        Devices.Clear();
        foreach (var device in devices)
            Devices.Add(device);

        _selectedDevice = DeviceSelection.Resolve(Devices, previousSerial, preferred);
        if (preferred is not null
            && _selectedDevice is not null
            && DeviceSelection.Matches(_selectedDevice, preferred))
            _preferredTarget = null;

        _generation++;
        RaiseChanged();
    }

    public bool CanDisconnect(AndroidDevice? device = null)
        => (device ?? _selectedDevice)?.CanDisconnect == true;

    public async Task<DeviceDisconnectResult> DisconnectAsync(
        AndroidDevice? device = null,
        CancellationToken cancellationToken = default)
    {
        device ??= _selectedDevice;
        if (device is null || !device.CanDisconnect)
            return new DeviceDisconnectResult(false, false, "This target cannot be disconnected.", null);

        var endpoint = device.DisconnectEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            return new DeviceDisconnectResult(false, false, "This target has no network endpoint.", null);

        if (_preferredTarget is not null && DeviceSelection.Matches(device, _preferredTarget))
            _preferredTarget = null;

        var result = await _connection.DisconnectAsync(endpoint, cancellationToken);
        if (!result.IsSuccess)
        {
            var detail = FirstLine(result.StandardError, result.StandardOutput, "ADB did not disconnect the device.");
            _logger.Warning("Devices", $"Disconnect {endpoint} failed: {result.StandardError}");
            return new DeviceDisconnectResult(true, false, $"Disconnect failed: {detail}", endpoint);
        }

        _logger.Information("Devices", $"Disconnected {endpoint}.");
        await _tracker.RefreshAsync(cancellationToken);
        return new DeviceDisconnectResult(
            true,
            true,
            $"{device.DisplayLabel} disconnected. Saved devices were left in the list.",
            endpoint);
    }

    private void SelectInternal(AndroidDevice? device)
    {
        if (ReferenceEquals(_selectedDevice, device))
            return;
        _selectedDevice = device;
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private static string FirstLine(string? error, string? output, string fallback)
        => (error ?? output)?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
           ?? fallback;
}
