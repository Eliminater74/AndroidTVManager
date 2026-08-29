using System.Windows.Threading;
using System.Windows;
using AndroidTVManager.App.ViewModels;
using AndroidTVManager.Infrastructure;
using AndroidTVManager.Core.Abstractions;
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
        _services.GetRequiredService<IAppLogger>().Information("Application", $"Starting Android TV Manager {AppInfo.Version}.");

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
        _ = window.ViewModel.InitializeRuntimeAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_services?.GetService<AndroidTVManager.Core.Abstractions.IAdbDeviceTracker>() is { } tracker)
        {
            await tracker.StopAsync();
            if (_services.GetService<AndroidTVManager.Core.Abstractions.IConnectionHistoryRepository>() is { } history)
                await history.RecoverOpenSessionsAsync();
        }
        _services?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _services?.GetService<IAppLogger>()?.Error("Application", "Unhandled UI exception.", e.Exception);
        System.Windows.MessageBox.Show(
            $"Android TV Manager encountered an unexpected error.\n\n{e.Exception.Message}",
            "Unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}

