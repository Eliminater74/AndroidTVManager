using System.Windows;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AndroidTVManager.App.Tray;
using AndroidTVManager.App.ViewModels;
using AndroidTVManager.Core.Abstractions;

namespace AndroidTVManager.App;

public partial class MainWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    private readonly TrayService _trayService;
    private readonly ISettingsStore _settings;

    public MainWindow(MainWindowViewModel viewModel, IAdbProcessRunner runner, ISettingsStore settings)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        SourceInitialized += OnSourceInitialized;
        ThemeManager.ThemeChanged += OnThemeChanged;
        _settings = settings;
        _trayService = new TrayService(this, runner, settings);
        Closed += OnClosed;
    }

    public MainWindowViewModel ViewModel { get; }

    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        SourceInitialized -= OnSourceInitialized;
        ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e) => ApplyWindowChromeTheme();

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyWindowChromeTheme();

    private void ApplyWindowChromeTheme()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        var useDarkMode = ThemeManager.CurrentTheme == AppTheme.White ? 0 : 1;
        try
        {
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref useDarkMode, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}
