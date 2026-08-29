using System.Windows;
using AndroidTVManager.App.ViewModels;

namespace AndroidTVManager.App;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
    }

    public MainWindowViewModel ViewModel { get; }
}