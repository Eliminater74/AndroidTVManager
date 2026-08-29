using System.Windows;
using AndroidTVManager.App.Tray;
using AndroidTVManager.App.ViewModels;

namespace AndroidTVManager.App;

public partial class MainWindow : Window
{
    private readonly TrayService _trayService;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _trayService = new TrayService(this);
    }

    public MainWindowViewModel ViewModel { get; }
}