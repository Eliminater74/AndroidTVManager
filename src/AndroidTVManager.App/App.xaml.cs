using System.Windows.Threading;
using System.Windows;
using System.Runtime.InteropServices;
using AndroidTVManager.App.ViewModels;
using AndroidTVManager.App.Services;
using AndroidTVManager.Infrastructure;
using AndroidTVManager.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AndroidTVManager.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instanceMutex = new Mutex(true, "AndroidTVManager.SingleInstance", out var isFirstInstance);
        _ownsInstanceMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            BringExistingWindowToFront();
            Shutdown();
            return;
        }

        var services = new ServiceCollection();
        services.AddAndroidTVManagerInfrastructure();
        services.AddSingleton<IConfirmationService, WpfConfirmationService>();
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
        if (_ownsInstanceMutex)
            _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
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

    private static void BringExistingWindowToFront()
    {
        var handle = FindWindow(null, "Android TV Manager");
        if (handle == IntPtr.Zero)
            return;
        ShowWindow(handle, 9);
        SetForegroundWindow(handle);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);
}

