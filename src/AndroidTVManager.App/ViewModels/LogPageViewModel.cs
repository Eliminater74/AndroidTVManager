using System.Collections.ObjectModel;
using AndroidTVManager.App.Services;
using AndroidTVManager.Core.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVManager.App.ViewModels;

public sealed partial class LogPageViewModel : PageViewModel
{
    private const int MaximumVisibleEntries = 2000;
    private readonly ILogViewerService _logViewer;
    private readonly IConfirmationService _confirmation;

    public LogPageViewModel(
        ILogViewerService logViewer,
        IConfirmationService confirmation) : base("Application Logs")
    {
        _logViewer = logViewer;
        _confirmation = confirmation;
        _logViewer.EntryWritten += OnEntryWritten;
        _ = RefreshAsync();
    }

    public ObservableCollection<string> Entries { get; } = [];

    [ObservableProperty]
    private string _status = "Loading the current log…";

    [ObservableProperty]
    private bool _isBusy;

    public string LogPath => _logViewer.CurrentLogPath;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            var entries = await _logViewer.ReadCurrentAsync();
            Entries.Clear();
            foreach (var entry in entries.TakeLast(MaximumVisibleEntries).Reverse())
                Entries.Add(entry);
            Status = $"{Entries.Count} recent entries loaded.";
        }
        catch (Exception exception)
        {
            Status = $"Could not read the log: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        if (!_confirmation.Confirm(
                "Clear application log",
                "This permanently removes the current Android TV Manager log files. Continue?"))
        {
            Status = "Log clear canceled.";
            return;
        }

        try
        {
            await _logViewer.ClearAsync();
            Entries.Clear();
            Status = "Application log cleared.";
        }
        catch (Exception exception)
        {
            Status = $"Could not clear the log: {exception.Message}";
        }
    }

    private void OnEntryWritten(object? sender, string entry)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(() => AddEntry(entry));
            return;
        }

        AddEntry(entry);
    }

    private void AddEntry(string entry)
    {
        Entries.Insert(0, entry);
        while (Entries.Count > MaximumVisibleEntries)
            Entries.RemoveAt(Entries.Count - 1);
        Status = $"{Entries.Count} recent entries · live";
    }
}
