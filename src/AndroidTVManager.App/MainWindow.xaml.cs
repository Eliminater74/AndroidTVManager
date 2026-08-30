using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AndroidTVManager.App.Tray;
using AndroidTVManager.App.ViewModels;
using AndroidTVManager.Core.Abstractions;

namespace AndroidTVManager.App;

public partial class MainWindow : Window
{
    private readonly TrayService _trayService;
    private readonly ISettingsStore _settings;

    public MainWindow(MainWindowViewModel viewModel, IAdbProcessRunner runner, ISettingsStore settings)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _settings = settings;
        _trayService = new TrayService(this, runner, settings);
        Closed += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    public MainWindowViewModel ViewModel { get; }

    private void OnViewModelPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedNavigation))
            Dispatcher.BeginInvoke(() => PageScrollViewer.ScrollToTop());
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = FindParent<ScrollViewer>(e.OriginalSource as DependencyObject);
        if (scrollViewer is null || scrollViewer == PageScrollViewer)
            return;

        var targetOffset = Math.Clamp(
            scrollViewer.VerticalOffset - e.Delta / 3.0,
            0,
            scrollViewer.ScrollableHeight);
        if (scrollViewer.ScrollableHeight > 0 && targetOffset != scrollViewer.VerticalOffset)
        {
            scrollViewer.ScrollToVerticalOffset(targetOffset);
            e.Handled = true;
            return;
        }

        if (PageScrollViewer.ScrollableHeight > 0)
        {
            PageScrollViewer.ScrollToVerticalOffset(
                Math.Clamp(PageScrollViewer.VerticalOffset - e.Delta / 3.0, 0, PageScrollViewer.ScrollableHeight));
            e.Handled = true;
        }
    }

    private static T? FindParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
                return match;
            child = child is Visual visual
                ? VisualTreeHelper.GetParent(visual)
                : LogicalTreeHelper.GetParent(child);
        }
        return null;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (bool.TryParse(await _settings.GetAsync("general.startMinimized"), out var startMinimized)
            && startMinimized)
            WindowState = WindowState.Minimized;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (ViewModel.CurrentPage is not InstallApkPageViewModel installer
            || !e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            return;

        var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
        var apks = files.Where(path => string.Equals(Path.GetExtension(path), ".apk", StringComparison.OrdinalIgnoreCase));
        installer.ApkPath = string.Join(Environment.NewLine, apks);
    }
}