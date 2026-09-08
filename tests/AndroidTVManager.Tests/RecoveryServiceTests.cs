using System.IO.Compression;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Adb;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class RecoveryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AndroidTVManagerTests", Guid.NewGuid().ToString("N"));
    public RecoveryServiceTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Parses_all_recovery_modes_with_tabs_without_selecting_first_device()
    {
        var targets = RecoveryTransportParser.Parse("List of devices attached\nother\tdevice product:tablet\nchosen\tsideload\nthird\trecovery\nfourth\tunauthorized\n* daemon started successfully *");
        targets.Should().HaveCount(4);
        targets.Single(t => t.Serial == "chosen").Mode.Should().Be(RecoveryMode.Sideload);
        RecoveryTransportParser.Parse("chosen\tfastboot", true).Single().Mode.Should().Be(RecoveryMode.Fastboot);
    }

    [Theory]
    [InlineData(0, "Total xfer: 1.00x", true)]
    [InlineData(1, "47% adb: failed to read command: Success", false)]
    public async Task Sideload_targets_only_selected_serial_and_never_assumes_device_installation(int exit, string output, bool succeeded)
    {
        var adb = new AdbFake { Output = output, Exit = exit };
        var service = new RecoveryService(adb, new FastbootFake());
        var zip = await service.InspectFileAsync(Zip(), RecoveryFileKind.SideloadZip);
        var result = await service.SideloadAsync(new("chosen", RecoveryMode.Sideload), zip);
        result.CommandSucceeded.Should().Be(succeeded);
        result.Message.Should().Contain(succeeded ? "Confirm installation" : "Inspect recovery");
        adb.DeviceCalls.Should().ContainSingle(call => call.Serial == "chosen" && call.Arguments[0] == "sideload" && call.Arguments[1] == zip.Path);
        result.Output.Should().NotContain(zip.Path);
    }

    [Fact]
    public async Task Missing_original_serial_never_falls_back_to_other_device()
    {
        var adb = new AdbFake { Listing = "other\tsideload" };
        var service = new RecoveryService(adb, new FastbootFake());
        var zip = await service.InspectFileAsync(Zip(), RecoveryFileKind.SideloadZip);
        await FluentActions.Awaiting(() => service.SideloadAsync(new("chosen", RecoveryMode.Sideload), zip))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*missing or ambiguous*");
        adb.DeviceCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Guided_sideload_reboots_android_then_sends_to_same_serial_without_wiping_or_final_reboot()
    {
        var adb = new AdbFake { Listing = "chosen\tdevice\nother\tsideload" };
        var service = new RecoveryService(adb, new FastbootFake());
        var zip = await service.InspectFileAsync(Zip(), RecoveryFileKind.SideloadZip);
        await service.SideloadAsync(new("chosen", RecoveryMode.Android), zip);
        adb.DeviceCalls.Should().HaveCount(2);
        adb.DeviceCalls[0].Arguments.Should().Equal("reboot", "recovery");
        adb.DeviceCalls[1].Arguments.Should().Equal("sideload", zip.Path);
        adb.DeviceCalls.Should().OnlyContain(call => call.Serial == "chosen");
    }

    [Fact]
    public async Task Failed_reboot_never_proceeds_to_sideload()
    {
        var adb = new AdbFake { Listing = "chosen\tdevice", Exit = 1 };
        var service = new RecoveryService(adb, new FastbootFake());
        var zip = await service.InspectFileAsync(Zip(), RecoveryFileKind.SideloadZip);
        await FluentActions.Awaiting(() => service.SideloadAsync(new("chosen", RecoveryMode.Android), zip))
            .Should().ThrowAsync<InvalidOperationException>();
        adb.DeviceCalls.Should().ContainSingle(call => call.Arguments[0] == "reboot");
    }

    [Fact]
    public async Task Changed_file_is_rejected_before_contacting_a_device()
    {
        var adb = new AdbFake();
        var service = new RecoveryService(adb, new FastbootFake());
        var path = Zip();
        var zip = await service.InspectFileAsync(path, RecoveryFileKind.SideloadZip);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update)) archive.CreateEntry("changed");
        await FluentActions.Awaiting(() => service.SideloadAsync(new("chosen", RecoveryMode.Sideload), zip))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*file changed*");
        adb.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("shield", "yes", "0x10000")]
    [InlineData("dragon", "no", "0x10000")]
    [InlineData("dragon", "", "0x10000")]
    [InlineData("dragon", "yes", "0x1")]
    [InlineData("dragon", "yes", "unavailable")]
    public async Task Recovery_flash_requires_pixel_c_unlocked_and_sufficient_partition(string product, string unlocked, string size)
    {
        var fastboot = new FastbootFake { Listing = "chosen\tfastboot", Product = product, Unlocked = unlocked, Size = size };
        var service = new RecoveryService(new AdbFake { Listing = "" }, fastboot);
        var image = await service.InspectFileAsync(Image(), RecoveryFileKind.RecoveryImage);
        await FluentActions.Awaiting(() => service.FlashPixelCRecoveryAsync(new("chosen", RecoveryMode.Fastboot), image))
            .Should().ThrowAsync<InvalidOperationException>();
        fastboot.Calls.Should().NotContain(args => args.Contains("flash"));
    }

    [Fact]
    public async Task Verified_pixel_c_flash_uses_only_recovery_and_leaves_reboot_to_device_menu()
    {
        var fastboot = new FastbootFake { Listing = "chosen\tfastboot\nother\tfastboot" };
        var adb = new AdbFake { Listing = "" };
        var service = new RecoveryService(adb, fastboot);
        var image = await service.InspectFileAsync(Image(), RecoveryFileKind.RecoveryImage);
        var result = await service.FlashPixelCRecoveryAsync(new("chosen", RecoveryMode.Fastboot), image);
        result.CommandSucceeded.Should().BeTrue();
        fastboot.Calls.Single(args => args.Contains("flash")).Should().Equal("-s", "chosen", "flash", "recovery", image.Path);
        fastboot.Calls.Should().NotContain(args => args.Contains("erase") || args.Contains("reboot") || args.Contains("unlock"));
        adb.DeviceCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_flash_stops_with_error_and_no_reboot()
    {
        var fastboot = new FastbootFake { Listing = "chosen\tfastboot", FlashExit = 1 };
        var service = new RecoveryService(new AdbFake { Listing = "" }, fastboot);
        var image = await service.InspectFileAsync(Image(), RecoveryFileKind.RecoveryImage);
        await FluentActions.Awaiting(() => service.FlashPixelCRecoveryAsync(new("chosen", RecoveryMode.Fastboot), image))
            .Should().ThrowAsync<InvalidOperationException>();
        fastboot.Calls.Should().NotContain(args => args.Contains("reboot"));
    }

    [Fact]
    public async Task Invalid_zip_and_image_are_rejected()
    {
        var service = new RecoveryService(new AdbFake(), new FastbootFake());
        var path = Path.Combine(_root, "invalid.img");
        await File.WriteAllTextAsync(path, "not an Android image");
        await FluentActions.Awaiting(() => service.InspectFileAsync(path, RecoveryFileKind.RecoveryImage)).Should().ThrowAsync<InvalidDataException>();
        var zip = Path.Combine(_root, "not-an-update.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntry("notes.txt");
        await FluentActions.Awaiting(() => service.InspectFileAsync(zip, RecoveryFileKind.SideloadZip)).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_sideload_never_sends_package()
    {
        var adb = new AdbFake { Listing = "chosen\trecovery" };
        var service = new RecoveryService(adb, new FastbootFake());
        var zip = await service.InspectFileAsync(Zip(), RecoveryFileKind.SideloadZip);
        using var cancellation = new CancellationTokenSource();
        await FluentActions.Awaiting(() => service.SideloadAsync(new("chosen", RecoveryMode.Recovery), zip,
            new CallbackProgress(message => { if (message.StartsWith("Waiting")) cancellation.Cancel(); }), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        adb.DeviceCalls.Should().BeEmpty();
    }

    private string Image()
    {
        var path = Path.Combine(_root, "recovery with spaces.img");
        File.WriteAllBytes(path, "ANDROID!test-image"u8.ToArray());
        return path;
    }
    private string Zip()
    {
        var path = Path.Combine(_root, "lineage with spaces.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        archive.CreateEntry("META-INF/com/android/metadata");
        return path;
    }
    private sealed class CallbackProgress(Action<string> callback) : IProgress<string>
    { public void Report(string value) => callback(value); }

    private sealed class AdbFake : IAdbProcessRunner
    {
        public string Listing { get; set; } = "other\tdevice\nchosen\tsideload";
        public int Exit { get; init; }
        public string Output { get; init; } = "";
        public int Calls { get; private set; }
        public List<(string Serial, IReadOnlyList<string> Arguments)> DeviceCalls { get; } = [];
        public Task<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        { Calls++; cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new AdbCommandResult("adb.exe", arguments, 0, Listing, "", TimeSpan.Zero)); }
        public Task<AdbCommandResult> RunForDeviceAsync(string serial, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            Calls++; cancellationToken.ThrowIfCancellationRequested(); DeviceCalls.Add((serial, arguments));
            if (arguments[0] == "reboot") Listing = "chosen\tsideload";
            return Task.FromResult(new AdbCommandResult("adb.exe", arguments, Exit, Output, "", TimeSpan.Zero));
        }
    }
    private sealed class FastbootFake : IFastbootProcessRunner
    {
        public string Listing { get; init; } = "";
        public string Product { get; init; } = "dragon";
        public string Unlocked { get; init; } = "yes";
        public string Size { get; init; } = "0x10000";
        public int FlashExit { get; init; }
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public Task<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Calls.Add(arguments);
            if (arguments[0] == "devices") return Task.FromResult(new AdbCommandResult("fastboot.exe", arguments, 0, Listing, "", TimeSpan.Zero));
            if (arguments[2] == "flash") return Task.FromResult(new AdbCommandResult("fastboot.exe", arguments, FlashExit, "", "flash result", TimeSpan.Zero));
            var key = arguments[3];
            var value = key switch { "product" => Product, "unlocked" => Unlocked, _ => Size };
            return Task.FromResult(new AdbCommandResult("fastboot.exe", arguments, 0, "", $"(bootloader) {key}: {value}\nFinished. Total time: 0.01s", TimeSpan.Zero));
        }
    }
}
