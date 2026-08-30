using System.Drawing;
using System.IO;
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
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Icon? _customIcon;
    private bool _allowClose;
    private bool _disposed;
    private bool _minimizeToTray = true;
    private bool _closeToTray = true;

    public TrayService(MainWindow window, IAdbProcessRunner runner, ISettingsStore settings)
    {
        _window = window;
        _runner = runner;
        _settings = settings;
        _menu = BuildMenu();
        _customIcon = TryLoadIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _customIcon ?? SystemIcons.Application,
            Text = "Android TV Manager",
            Visible = true,
            ContextMenuStrip = _menu
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
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _customIcon?.Dispose();
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
        menu.Renderer = new ThemeRenderer(ThemeManager.CurrentTheme == AppTheme.White);
        ApplyMenuTheme(menu);
        ThemeManager.ThemeChanged += OnThemeChanged;
        return menu;
    }

    private static Icon? TryLoadIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "AndroidTVManager.ico");
        try
        {
            return File.Exists(path) ? new Icon(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyMenuTheme(_menu);

    private static void ApplyMenuTheme(Forms.ContextMenuStrip menu)
    {
        var light = ThemeManager.CurrentTheme == AppTheme.White;
        menu.Renderer = new ThemeRenderer(light);
        menu.BackColor = light ? Color.White : Color.FromArgb(17, 24, 39);
        menu.ForeColor = light ? Color.FromArgb(22, 32, 51) : Color.FromArgb(244, 247, 255);
        foreach (Forms.ToolStripItem item in menu.Items)
        {
            item.BackColor = menu.BackColor;
            item.ForeColor = menu.ForeColor;
        }
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

    private sealed class ThemeRenderer : Forms.ToolStripProfessionalRenderer
    {
        public ThemeRenderer(bool light) : base(new ThemeColors(light))
        {
        }
    }

    private sealed class ThemeColors : Forms.ProfessionalColorTable
    {
        private readonly bool _light;

        public ThemeColors(bool light) => _light = light;

        public override Color ToolStripDropDownBackground => _light ? Color.White : Color.FromArgb(17, 24, 39);
        public override Color MenuItemSelected => _light ? Color.FromArgb(232, 238, 247) : Color.FromArgb(27, 41, 64);
        public override Color MenuItemBorder => _light ? Color.FromArgb(203, 214, 229) : Color.FromArgb(38, 52, 77);
        public override Color MenuBorder => _light ? Color.FromArgb(203, 214, 229) : Color.FromArgb(38, 52, 77);
        public override Color SeparatorDark => _light ? Color.FromArgb(203, 214, 229) : Color.FromArgb(38, 52, 77);
        public override Color SeparatorLight => ToolStripDropDownBackground;
        public override Color ImageMarginGradientBegin => ToolStripDropDownBackground;
        public override Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
        public override Color ImageMarginGradientEnd => ToolStripDropDownBackground;
    }
}
