using System.Windows;
using System.Windows.Controls;

namespace AndroidTVManager.App.Services;

public interface IConfirmationService
{
    bool Confirm(string title, string message);
}

public sealed class WpfConfirmationService : IConfirmationService
{
    public bool Confirm(string title, string message)
    {
        var window = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            MinHeight = 220,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = System.Windows.Application.Current.MainWindow,
            Background = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("WindowBackground"),
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("TextPrimary"),
            Padding = new Thickness(24)
        };
        var layout = new StackPanel();
        layout.Children.Add(new TextBlock
        {
            Text = "CONFIRM TARGETED ACTION",
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("VioletAccent"),
            FontWeight = FontWeights.Bold,
            FontSize = 11
        });
        layout.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 20)
        });
        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", Style = (Style)System.Windows.Application.Current.FindResource("GhostButtonStyle") };
        cancel.Click += (_, _) => window.DialogResult = false;
        var confirm = new System.Windows.Controls.Button { Content = "Continue", Style = (Style)System.Windows.Application.Current.FindResource("AccentButtonStyle"), Margin = new Thickness(8, 0, 0, 0) };
        confirm.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        layout.Children.Add(buttons);
        window.Content = layout;
        return window.ShowDialog() == true;
    }
}
