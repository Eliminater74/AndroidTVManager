using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Adb;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class RemoteControlTests
{
    [Fact]
    public async Task Sends_typed_keyevent_arguments()
    {
        var runner = new FakeAdbProcessRunner();
        var service = new RemoteControlService(runner);

        var result = await service.PressAsync("tv-serial", RemoteKey.Home);

        result.IsSuccess.Should().BeTrue();
        runner.Calls.Should().ContainSingle();
        runner.Calls[0].Arguments.Should().Equal("shell", "input", "keyevent", "KEYCODE_HOME");
    }

    [Fact]
    public async Task Encodes_spaces_and_shell_sensitive_text_without_shell_concatenation()
    {
        var runner = new FakeAdbProcessRunner();
        var service = new RemoteControlService(runner);

        await service.TypeTextAsync("tv-serial", "hello world & 100%");

        runner.Calls[0].Arguments.Should().Equal(
            "shell", "input", "text", "hello%sworld%s%26%s100%25");
    }
}
