using AndroidTVManager.Infrastructure.Adb;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class AdvancedDiagnosticsTests
{
    [Fact]
    public async Task Parses_decoder_and_encoder_names_from_codec_dump()
    {
        var runner = new FakeAdbProcessRunner();
        runner.Responses["shell dumpsys media.codec"] = new(
            "adb.exe",
            [],
            0,
            "codec c2.android.avc.decoder\ncodec OMX.google.aac.encoder",
            string.Empty,
            TimeSpan.Zero);

        var result = await new CodecInspectionService(runner).InspectAsync("tv-serial");

        result.Codecs.Should().HaveCount(2);
        result.Codecs.Should().Contain(codec => codec.Name == "c2.android.avc.decoder" && codec.Type == "Decoder");
        result.Codecs.Should().Contain(codec => codec.Name == "OMX.google.aac.encoder" && codec.Type == "Encoder");
    }

    [Fact]
    public async Task Limits_file_operations_to_shared_storage()
    {
        var service = new DeviceFileService(new FakeAdbProcessRunner());

        var action = () => service.ListAsync("tv-serial", "/system");

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*shared storage*");
    }
}
