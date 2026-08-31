using AndroidTVManager.Infrastructure.Adb;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DeviceFileServiceTests
{
    [Theory]
    [InlineData("/sdcardfoo/file.txt")]
    [InlineData("/sdcard/../private/file.txt")]
    [InlineData("/storage/emulated/0/../../data")]
    public async Task Rejects_paths_outside_shared_storage_boundaries(string path)
    {
        var service = new DeviceFileService(new FakeAdbProcessRunner());

        var action = () => service.DeleteAsync("tv-1", path);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Accepts_a_file_inside_shared_storage()
    {
        var runner = new FakeAdbProcessRunner();
        var service = new DeviceFileService(runner);

        await service.DeleteAsync("tv-1", "/sdcard/Movies/demo.mp4");

        runner.Calls.Should().ContainSingle();
        runner.Calls[0].Arguments.Should().Equal("shell", "rm", "-rf", "/sdcard/Movies/demo.mp4");
    }
}
