using System.Windows;
using System.IO;
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
        _settings = settings;
        _trayService = new TrayService(this, runner, settings);
    }

    public MainWindowViewModel ViewModel { get; }

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