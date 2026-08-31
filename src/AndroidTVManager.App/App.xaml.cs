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

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
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
            var settings = _services.GetRequiredService<ISettingsStore>();
            var savedTheme = await settings.GetAsync("appearance.theme");
            ThemeManager.Apply(Enum.TryParse<AppTheme>(savedTheme, true, out var theme) ? theme : AppTheme.Dark);
            _services.GetRequiredService<IAppLogger>().Information("Application", $"Starting Android TV Manager {AppInfo.Version}.");

            var window = _services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
            await window.ViewModel.InitializeRuntimeAsync();
        }
        catch (Exception exception)
        {
            _services?.GetService<IAppLogger>()?.Error("Application", "Application startup failed.", exception);
            System.Windows.MessageBox.Show(
                $"Android TV Manager could not start.\n\n{exception.Message}",
                "Startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_services?.GetService<AndroidTVManager.Core.Abstractions.IAdbDeviceTracker>() is { } tracker)
            {
                await tracker.StopAsync();
                if (_services.GetService<AndroidTVManager.Core.Abstractions.IConnectionHistoryRepository>() is { } history)
                    await history.RecoverOpenSessionsAsync();
            }
        }
        catch (Exception exception)
        {
            _services?.GetService<IAppLogger>()?.Error("Application", "Application shutdown cleanup failed.", exception);
        }
        finally
        {
            _services?.Dispose();
            if (_ownsInstanceMutex)
                _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();
            base.OnExit(e);
        }
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

