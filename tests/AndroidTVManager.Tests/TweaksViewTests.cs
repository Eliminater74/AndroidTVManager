using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AndroidTVManager.App.ViewModels;
using AndroidTVManager.App.Views.Pages;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class TweaksViewTests
{
    [Fact]
    public void Guided_views_load_resources_and_render_on_a_wpf_dispatcher()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application();
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri("/AndroidTVManager;component/Themes/Base.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri("/AndroidTVManager;component/Resources/Theme.xaml", UriKind.Relative) });
                System.Windows.Controls.UserControl[] views =
                [
                    new TweaksPageView { DataContext = new TweaksPageViewModel(null!, null!) },
                    new RecoveryPageView { DataContext = new RecoveryPageViewModel(null!, null!) }
                ];
                foreach (var view in views)
                {
                    view.Measure(new Size(850, double.PositiveInfinity));
                    view.Arrange(new Rect(new Point(), view.DesiredSize));
                    view.UpdateLayout();
                    if (view.DataContext is RecoveryPageViewModel recovery)
                    {
                        recovery.CancelCommand.CanExecute(null).Should().BeFalse();
                        foreach (var button in Descendants(view).OfType<System.Windows.Controls.Button>())
                        {
                            button.Command.Should().NotBeNull($"{button.Content} must resolve its command binding");
                            button.IsEnabled.Should().Be(button.Command!.CanExecute(null));
                        }
                    }
                    view.ActualHeight.Should().BeGreaterThan(400);
                    var bitmap = new RenderTargetBitmap(850, (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(view);
                    var captureVariable = view is RecoveryPageView ? "ATM_RECOVERY_CAPTURE_PATH" : "ATM_UI_CAPTURE_PATH";
                    if (Environment.GetEnvironmentVariable(captureVariable) is { Length: > 0 } path)
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(path);
                        encoder.Save(stream);
                    }
                }
                app.Shutdown();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(15)).Should().BeTrue();
        failure.Should().BeNull();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
