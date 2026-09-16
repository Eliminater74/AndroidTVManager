using AndroidTVManager.App.Services;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class ConfirmationDialogTests
{
    [Fact]
    public void Message_viewport_stays_inside_the_work_area()
    {
        WpfConfirmationService.MessageViewportHeight(1080).Should().Be(420);
        WpfConfirmationService.MessageViewportHeight(720).Should().BeInRange(180, 420);
        WpfConfirmationService.MessageViewportHeight(720).Should().BeLessThan(720 * 0.6);
        WpfConfirmationService.MessageViewportHeight(200).Should().Be(180);
    }

    [Fact]
    public void Dialog_width_is_fixed()
    {
        WpfConfirmationService.DialogWidth.Should().Be(560);
    }
}
