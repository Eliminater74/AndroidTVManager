using AndroidTVManager.App;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class AppInfoTests
{
    [Fact]
    public void Application_identity_comes_from_build_metadata()
    {
        AppInfo.ProductName.Should().Be("Android TV Manager");
        AppInfo.DeveloperName.Should().Be("Eliminater74");
        AppInfo.Description.Should().Contain("Android TV");
    }

    [Fact]
    public void Version_and_channel_are_derived_from_the_current_build()
    {
        AppInfo.Version.Should().Be("1.0.0-B3");
        AppInfo.ReleaseChannel.Should().Be("Beta 3");
        AppInfo.Version.Should().NotContain("B1");
        Assert.InRange(AppInfo.BuildIdentifier.Length, 0, 8);
    }
}
