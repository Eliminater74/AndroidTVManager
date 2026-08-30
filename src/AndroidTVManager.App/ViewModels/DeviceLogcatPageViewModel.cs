using System.Collections.ObjectModel;
using System.IO;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVManager.App.ViewModels;

public sealed partial class DeviceLogcatPageViewModel : PageViewModel
{
    private const int DefaultMaxLines = 25000;
    private readonly IDeviceLogcatService _logcat;
    private readonly Action<AndroidDevice?> _selectTarget;
    private CancellationTokenSource? _source;
    private IAdbProcessSession? _session;

    public DeviceLogcatPageViewModel(
        IDeviceLogcatService logcat,
        ObservableCollection<AndroidDevice> devices,
        Action<AndroidDevice?> selectTarget) : base("Device Logcat")
    {
        _logcat = logcat;
        Devices = devices;
        _selectTarget = selectTarget;
        Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FilteredEntries));
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public ObservableCollection<string> Entries { get; } = [];
    public IEnumerable<string> FilteredEntries => Entries.Where(Matches);

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    private string _packageFilter = string.Empty;

    [ObservableProperty]
    private string _tagFilter = string.Empty;

    [ObservableProperty]
    private string _severityFilter = "All";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _status = "Select a connected device and start the live logcat stream.";

    public IReadOnlyList<string> SeverityOptions { get; } = ["All", "Verbose", "Debug", "Info", "Warn", "Error", "Fatal"];

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
        => _selectTarget(value);

    partial void OnPackageFilterChanged(string value)
        => OnPropertyChanged(nameof(FilteredEntries));

    partial void OnTagFilterChanged(string value)
        => OnPropertyChanged(nameof(FilteredEntries));

    partial void OnSeverityFilterChanged(string value)
        => OnPropertyChanged(nameof(FilteredEntries));

    [RelayCommand]
    private async Task StartAsync()
    {
        if (!TryGetSerial(out var serial))
            return;
        await StopAsync();
        _source = new CancellationTokenSource();
        try
        {
            _session = await _logcat.StartAsync(serial, new LogcatOptions(MaxLines: DefaultMaxLines), _source.Token);
            IsRunning = true;
            Status = "Live logcat is running.";
            _ = ConsumeAsync(_session, _source.Token);
        }
        catch (Exception exception)
        {
            Status = $"Logcat could not start: {exception.Message}";
            _source.Dispose();
            _source = null;
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        _source?.Cancel();
        if (_session is not null)
        {
            await _session.StopAsync();
            await _session.DisposeAsync();
            _session = null;
        }
        _source?.Dispose();
        _source = null;
        IsRunning = false;
    }

    [RelayCommand]
    private async Task ClearDeviceLogAsync()
    {
        if (!TryGetSerial(out var serial))
            return;
        var result = await _logcat.ClearAsync(serial);
        Status = result.IsSuccess ? "Device logcat buffer cleared." : $"Clear failed: {Error(result)}";
        Entries.Clear();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*",
            FileName = $"logcat-{DateTime.Now:yyyyMMdd-HHmmss}.log"
        };
        if (dialog.ShowDialog() != true)
            return;
        await File.WriteAllLinesAsync(dialog.FileName, Entries);
        Status = $"Saved {Entries.Count:N0} logcat line(s).";
    }

    [RelayCommand]
    private async Task CaptureAroundProblemAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*",
            FileName = $"logcat-capture-{DateTime.Now:yyyyMMdd-HHmmss}.log"
        };
        if (dialog.ShowDialog() != true)
            return;
        var lines = Entries.Skip(Math.Max(0, Entries.Count - 500)).ToArray();
        await File.WriteAllLinesAsync(dialog.FileName, lines);
        Status = $"Captured the last {lines.Length:N0} logcat line(s).";
    }

    private async Task ConsumeAsync(IAdbProcessSession session, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var line in session.ReadStandardOutputAsync(cancellationToken))
            {
                if (!Matches(line))
                    continue;
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Entries.Add(line);
                    while (Entries.Count > DefaultMaxLines)
                        Entries.RemoveAt(0);
                });
            }
            var result = await session.Completion;
            if (!cancellationToken.IsCancellationRequested)
                Status = result.IsSuccess ? "Logcat stream ended." : $"Logcat ended: {Error(result)}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Status = $"Logcat stream failed: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_session, session))
                IsRunning = false;
        }
    }

    private bool Matches(string line)
    {
        if (!string.IsNullOrWhiteSpace(PackageFilter)
            && !line.Contains(PackageFilter, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(TagFilter)
            && !line.Contains(TagFilter, StringComparison.OrdinalIgnoreCase))
            return false;
        if (SeverityFilter != "All")
        {
            var marker = SeverityFilter switch
            {
                "Verbose" => "V/",
                "Debug" => "D/",
                "Info" => "I/",
                "Warn" => "W/",
                "Error" => "E/",
                "Fatal" => "F/",
                _ => string.Empty
            };
            if (!line.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private bool TryGetSerial(out string serial)
    {
        serial = SelectedDevice?.Serial.Trim() ?? string.Empty;
        if (SelectedDevice?.State == DeviceState.Device && serial.Length > 0)
            return true;
        Status = "Select a connected and authorized device first.";
        return false;
    }

    private static string Error(AdbCommandResult result)
        => result.StandardError.Trim() is { Length: > 0 } error ? error : "ADB command failed.";
}
