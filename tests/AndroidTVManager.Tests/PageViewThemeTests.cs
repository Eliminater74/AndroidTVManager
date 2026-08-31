using System.Xml.Linq;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class PageViewThemeTests
{
    [Fact]
    public void Page_views_have_code_behind_that_initializes_xaml()
    {
        var pageViewPath = Path.Combine(
            FindSolutionRoot().FullName,
            "src",
            "AndroidTVManager.App",
            "Views",
            "Pages");

        var missingInitializers = Directory
            .EnumerateFiles(pageViewPath, "*.xaml")
            .Select(path => new { Xaml = path, CodeBehind = path + ".cs" })
            .Where(file => !File.Exists(file.CodeBehind)
                           || !File.ReadAllText(file.CodeBehind)
                               .Contains("InitializeComponent();", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file.Xaml))
            .ToList();

        missingInitializers.Should().BeEmpty(
            "separate WPF page views render blank when their generated XAML content is not initialized");
    }

    [Fact]
    public void Shared_theme_covers_native_controls_used_by_pages()
    {
        var themePath = Path.Combine(
            FindSolutionRoot().FullName,
            "src",
            "AndroidTVManager.App",
            "Resources",
            "Theme.xaml");

        var theme = XDocument.Load(themePath);
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var implicitTargets = theme
            .Descendants()
            .Where(element => element.Name.LocalName == "Style"
                              && element.Attribute(xNamespace + "Key") is null)
            .Select(element => element.Attribute("TargetType")?.Value)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .ToHashSet(StringComparer.Ordinal);

        implicitTargets.Should().Contain([
            "Button",
            "DataGrid",
            "TabControl",
            "TabItem",
            "ListView",
            "ListViewItem",
            "GridViewColumnHeader"
        ]);
    }

    private static DirectoryInfo FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AndroidTVManager.sln")))
            directory = directory.Parent;

        return directory ?? throw new DirectoryNotFoundException("Could not locate AndroidTVManager.sln.");
    }
}
