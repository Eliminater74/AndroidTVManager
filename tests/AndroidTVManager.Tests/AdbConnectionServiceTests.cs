using AndroidTVManager.Infrastructure.Adb;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class AdbConnectionServiceTests
{
    [Theory]
    [InlineData("connected to tablet:37123", true)]
    [InlineData("already connected to tablet:37123", true)]
    [InlineData("failed to connect to tablet:37123", false)]
    [InlineData("cannot connect to tablet:37123", false)]
    [InlineData("", false)]
    public async Task Connect_requires_adb_acknowledgement_even_with_zero_exit(string output, bool success)
    {
        var runner = new FakeAdbProcessRunner();
        runner.Responses["connect tablet:37123"] = new("adb.exe", [], 0, output, "", TimeSpan.Zero);
        var result = await new AdbConnectionService(runner).ConnectAsync("tablet:37123");
        result.IsSuccess.Should().Be(success);
        if (!success) result.StandardError.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("Successfully paired to tablet:41001", true)]
    [InlineData("Failed: protocol fault", false)]
    public async Task Pair_requires_its_own_acknowledgement(string output, bool success)
    {
        var runner = new FakeAdbProcessRunner();
        runner.Responses["pair tablet:41001 000000"] = new("adb.exe", [], 0, output, "", TimeSpan.Zero);
        (await new AdbConnectionService(runner).PairAsync("tablet:41001", "000000")).IsSuccess.Should().Be(success);
    }
}
