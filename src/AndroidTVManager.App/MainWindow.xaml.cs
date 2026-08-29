using System.Windows;
using System.IO;
using AndroidTVManager.App.Tray;
using AndroidTVManager.App.ViewModels;
using AndroidTVManager.Core.Abstractions;

namespace AndroidTVManager.App;

public partial class MainWindow : Window
{
    private readonly TrayService _trayService;

    public MainWindow(MainWindowViewModel viewModel, IAdbProcessRunner runner)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _trayService = new TrayService(this, runner);
    }

    public MainWindowViewModel ViewModel { get; }

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