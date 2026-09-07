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
    public void Tweaks_view_loads_resources_and_renders_on_a_wpf_dispatcher()
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
                var view = new TweaksPageView { DataContext = new TweaksPageViewModel(null!, null!) };
                view.Measure(new Size(850, double.PositiveInfinity));
                view.Arrange(new Rect(new Point(), view.DesiredSize));
                view.UpdateLayout();
                view.ActualHeight.Should().BeGreaterThan(400);
                var bitmap = new RenderTargetBitmap(850, (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(view);
                if (Environment.GetEnvironmentVariable("ATM_UI_CAPTURE_PATH") is { Length: > 0 } path)
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(path);
                    encoder.Save(stream);
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
}
