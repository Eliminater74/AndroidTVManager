using System.Windows;
using System.Windows.Controls;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfButton = System.Windows.Controls.Button;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace AndroidTVManager.App.Services;

public interface IConfirmationService
{
    bool Confirm(string title, string message, string confirmLabel = "Continue");
}

public sealed class WpfConfirmationService : IConfirmationService
{
    public const double DialogWidth = 560;

    public static double MessageViewportHeight(double workAreaHeight)
        => Math.Clamp(workAreaHeight * 0.55, 180, 420);

    public bool Confirm(string title, string message, string confirmLabel = "Continue")
        => CreateDialog(title, message, confirmLabel).ShowDialog() == true;

    internal static Window CreateDialog(string title, string message, string confirmLabel = "Continue")
    {
        var workAreaHeight = SystemParameters.WorkArea.Height;
        if (workAreaHeight <= 0)
            workAreaHeight = 900;

        var window = new Window
        {
            Title = title,
            Width = DialogWidth,
            MinWidth = DialogWidth,
            MaxWidth = DialogWidth,
            MinHeight = 220,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = System.Windows.Application.Current?.MainWindow,
            Background = Brush("WindowBackground"),
            Foreground = Brush("TextPrimary"),
            ShowInTaskbar = false
        };

        var layout = new StackPanel { Margin = new Thickness(24) };
        layout.Children.Add(new TextBlock
        {
            Text = "CONFIRM TARGETED ACTION",
            Foreground = Brush("VioletAccent"),
            FontWeight = FontWeights.Bold,
            FontSize = 11
        });

        layout.Children.Add(new ScrollViewer
        {
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            Margin = new Thickness(0, 14, 0, 16),
            MaxHeight = MessageViewportHeight(workAreaHeight),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Focusable = true
        });

        var buttons = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right
        };
        var cancel = new WpfButton { Content = "Cancel", Style = ButtonStyle("GhostButtonStyle") };
        cancel.Click += (_, _) => window.DialogResult = false;
        var confirm = new WpfButton
        {
            Content = string.IsNullOrWhiteSpace(confirmLabel) ? "Continue" : confirmLabel,
            Style = ButtonStyle("AccentButtonStyle"),
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true
        };
        confirm.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        layout.Children.Add(buttons);

        window.Content = layout;
        return window;
    }

    private static WpfBrush Brush(string key)
        => System.Windows.Application.Current?.TryFindResource(key) as WpfBrush
           ?? new WpfSolidColorBrush(WpfColor.FromRgb(0xF4, 0xF7, 0xFF));

    private static Style? ButtonStyle(string key)
        => System.Windows.Application.Current?.TryFindResource(key) as Style;
}
