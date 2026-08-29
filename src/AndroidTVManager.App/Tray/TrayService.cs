using System.Drawing;
using System.Windows;
using AndroidTVManager.Core.Abstractions;
using Forms = System.Windows.Forms;

namespace AndroidTVManager.App.Tray;

public sealed class TrayService : IDisposable
{
    private readonly MainWindow _window;
    private readonly IAdbProcessRunner _runner;
    private readonly ISettingsStore _settings;
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _allowClose;
    private bool _disposed;
    private bool _minimizeToTray = true;
    private bool _closeToTray = true;

    public TrayService(MainWindow window, IAdbProcessRunner runner, ISettingsStore settings)
    {
        _window = window;
        _runner = runner;
        _settings = settings;
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Android TV Manager",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => Restore();
        _window.StateChanged += OnWindowStateChanged;
        _window.Closing += OnWindowClosing;
        _ = LoadSettingsAsync();
        _window.Closed += (_, _) => Dispose();
    }

    public void ExitApplication()
    {
        _allowClose = true;
        _notifyIcon.Visible = false;
        _window.Close();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _window.StateChanged -= OnWindowStateChanged;
        _window.Closing -= OnWindowClosing;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Android TV Manager", null, (_, _) => Restore()).Font =
            new Font(menu.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add("Open", null, (_, _) => Restore());
        menu.Items.Add("Settings", null, (_, _) => RestoreTo("Settings"));
        menu.Items.Add("Restart ADB Server", null, (_, _) => _ = RestartAdbServerAsync());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        return menu;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized)
        {
            if (!_minimizeToTray)
                return;
            _window.Hide();
            _notifyIcon.ShowBalloonTip(1200, "Android TV Manager", "Still running in the notification area.", Forms.ToolTipIcon.Info);
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose || !_closeToTray)
            return;
        e.Cancel = true;
        _window.Hide();
    }

    private async Task LoadSettingsAsync()
    {
        _minimizeToTray = await ReadBoolAsync("general.minimizeToTray", true);
        _closeToTray = await ReadBoolAsync("general.closeToTray", true);
    }

    private async Task<bool> ReadBoolAsync(string key, bool fallback)
        => bool.TryParse(await _settings.GetAsync(key), out var value) ? value : fallback;

    private void Restore()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void RestoreTo(string page)
    {
        Restore();
        if (_window.ViewModel.Navigation.FirstOrDefault(item => item.Label == page) is { } item)
            _window.ViewModel.SelectedNavigation = item;
    }

    private async Task RestartAdbServerAsync()
    {
        await _runner.RunAsync(["kill-server"], TimeSpan.FromSeconds(20));
        await _runner.RunAsync(["start-server"], TimeSpan.FromSeconds(20));
    }
}
