using System.Windows.Threading;
using System.Windows;
using AndroidTVManager.App.ViewModels;
using AndroidTVManager.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AndroidTVManager.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddAndroidTVManagerInfrastructure();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Windows.MessageBox.Show(
            $"Android TV Manager encountered an unexpected error.\n\n{e.Exception.Message}",
            "Unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}

