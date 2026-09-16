using AndroidTVManager.Core.Models;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DeviceFamiliesTests
{
    [Fact]
    public void Shield_still_matches_nvidia_identity_and_darcy_product()
    {
        DeviceFamilies.IsShieldTv(Device(
            manufacturer: "NVIDIA",
            brand: "NVIDIA",
            model: "SHIELD Android TV",
            product: "darcy",
            deviceName: "darcy")).Should().BeTrue();
        DeviceFamilies.Matches(DeviceFamilies.NvidiaShieldTv, Device(
            manufacturer: "NVIDIA",
            product: "darcy",
            deviceName: "darcy")).Should().BeTrue();
    }

    [Fact]
    public void Streamer_matches_kirkwood_fingerprint_only_for_the_streamer_family()
    {
        var streamer = Device(
            manufacturer: "Google",
            brand: "google",
            model: "Google TV Streamer",
            product: "kirkwood",
            deviceName: "kirkwood",
            board: "kirkwood",
            fingerprint: "google/kirkwood/kirkwood:14/UTT3.240625.001.K5/12147201:user/release-keys");

        DeviceFamilies.IsGoogleTvStreamer4K(streamer).Should().BeTrue();
        DeviceFamilies.IsChromecastWithGoogleTv4K(streamer).Should().BeFalse();
        DeviceFamilies.IsOnnGoogleTv4kBox(streamer).Should().BeFalse();
        DeviceFamilies.IsShieldTv(streamer).Should().BeFalse();
        DeviceFamilies.Matches(DeviceFamilies.GoogleTvStreamer4K, streamer).Should().BeTrue();
        DeviceFamilies.Matches(DeviceFamilies.ChromecastWithGoogleTv4K, streamer).Should().BeFalse();
    }

    [Fact]
    public void Chromecast_4k_matches_sabrina_prod_stable_without_requiring_model_4k_text()
    {
        var chromecast = Device(
            manufacturer: "Google",
            brand: "google",
            model: "Chromecast",
            product: "sabrina_prod_stable",
            deviceName: "sabrina",
            fingerprint: "google/sabrina_prod_stable/sabrina:12/STTE.240615.007/12033466:user/release-keys");

        DeviceFamilies.IsChromecastWithGoogleTv4K(chromecast).Should().BeTrue();
        DeviceFamilies.IsGoogleTvStreamer4K(chromecast).Should().BeFalse();
        DeviceFamilies.IsOnnGoogleTv4kBox(chromecast).Should().BeFalse();
        DeviceFamilies.Matches(DeviceFamilies.ChromecastWithGoogleTv4K, chromecast).Should().BeTrue();
        DeviceFamilies.Matches(DeviceFamilies.GoogleTvStreamer4K, chromecast).Should().BeFalse();
    }

    [Fact]
    public void Onn_4k_box_matches_yoc_fingerprint_and_not_other_onn_generations()
    {
        var yoc = Device(
            manufacturer: "onn",
            brand: "onn",
            model: "onn. 4K Streaming Box",
            product: "onn_4k_gtv",
            deviceName: "YOC",
            fingerprint: "onn/onn_4k_gtv/YOC:12/SGZ2.230609.049.A1/11261715:user/release-keys");

        DeviceFamilies.IsOnnGoogleTv4kBox(yoc).Should().BeTrue();
        DeviceFamilies.IsGoogleTvStreamer4K(yoc).Should().BeFalse();
        DeviceFamilies.IsChromecastWithGoogleTv4K(yoc).Should().BeFalse();
        DeviceFamilies.Matches(DeviceFamilies.OnnGoogleTv4kBox, yoc).Should().BeTrue();
    }

    [Theory]
    [InlineData("askey", "Onn", "sti6140d360", "dopinder", "dopinder", "Onn/sti6140d360/sti6140d360:12/SC/20240424:user/release-keys")]
    [InlineData("SDMC", "onn", "4K Pro Streaming Box Google TV", "jarvis", "jarvis", "onn/jarvis/SNA:14/URO4.260304.011.B1/15051976:user/release-keys")]
    [InlineData("onn", "onn", "onn. Full HD Streaming Stick", "XNA", "XNA", "onn/onn_fhd/XNA:12/dummy:user/release-keys")]
    public void Unrelated_onn_models_do_not_match_the_2023_4k_box(
        string manufacturer,
        string brand,
        string model,
        string product,
        string deviceName,
        string fingerprint)
    {
        var device = Device(manufacturer, brand, model, product, deviceName, fingerprint: fingerprint);

        DeviceFamilies.IsOnnGoogleTv4kBox(device).Should().BeFalse();
        DeviceFamilies.Matches(DeviceFamilies.OnnGoogleTv4kBox, device).Should().BeFalse();
    }

    [Fact]
    public void Unrelated_google_tv_device_does_not_match_chromecast_or_streamer()
    {
        var emulator = Device(
            manufacturer: "Google",
            brand: "google",
            model: "sdk_google_atv64_x86_64",
            product: "sdk_google_atv64_x86_64",
            deviceName: "emu64a");
        var chromecastHd = Device(
            manufacturer: "Google",
            brand: "google",
            model: "Chromecast HD",
            product: "boreal",
            deviceName: "boreal",
            fingerprint: "google/boreal/boreal:12/dummy:user/release-keys");
        var vagueGoogleTv = Device(
            manufacturer: "Google",
            brand: "google",
            model: "Chromecast with Google TV");

        DeviceFamilies.IsGoogleAtvEmulator(emulator).Should().BeTrue();
        DeviceFamilies.IsGoogleTvStreamer4K(emulator).Should().BeFalse();
        DeviceFamilies.IsChromecastWithGoogleTv4K(emulator).Should().BeFalse();
        DeviceFamilies.IsChromecastWithGoogleTv4K(chromecastHd).Should().BeFalse();
        DeviceFamilies.IsGoogleTvStreamer4K(chromecastHd).Should().BeFalse();
        DeviceFamilies.IsChromecastWithGoogleTv4K(vagueGoogleTv).Should().BeFalse();
        DeviceFamilies.IsGoogleTvStreamer4K(vagueGoogleTv).Should().BeFalse();
    }

    [Theory]
    [InlineData("NVIDIA SHIELD Android TV darcy")]
    [InlineData("Google TV Streamer kirkwood")]
    [InlineData("Chromecast with Google TV 4K sabrina")]
    [InlineData("onn. Google TV 4K Box YOC")]
    public void Friendly_name_alone_cannot_activate_any_hardware_family(string friendlyName)
    {
        var device = new AndroidDevice
        {
            Serial = "alias-only",
            FriendlyName = friendlyName,
            ReportedName = friendlyName
        };

        DeviceFamilies.IsShieldTv(device).Should().BeFalse();
        DeviceFamilies.IsGoogleTvStreamer4K(device).Should().BeFalse();
        DeviceFamilies.IsChromecastWithGoogleTv4K(device).Should().BeFalse();
        DeviceFamilies.IsOnnGoogleTv4kBox(device).Should().BeFalse();
        DeviceFamilies.Matches(DeviceFamilies.NvidiaShieldTv, device).Should().BeFalse();
        DeviceFamilies.Matches(DeviceFamilies.GoogleTvStreamer4K, device).Should().BeFalse();
        DeviceFamilies.Matches(DeviceFamilies.ChromecastWithGoogleTv4K, device).Should().BeFalse();
        DeviceFamilies.Matches(DeviceFamilies.OnnGoogleTv4kBox, device).Should().BeFalse();
    }

    [Fact]
    public void Vague_substrings_do_not_activate_hardware_families()
    {
        var googleTv = Device(manufacturer: "TCL", brand: "TCL", model: "Google TV 4K");
        var onnTv = Device(manufacturer: "onn", brand: "onn", model: "onn. 4K UHD TV");

        DeviceFamilies.IsGoogleTvStreamer4K(googleTv).Should().BeFalse();
        DeviceFamilies.IsChromecastWithGoogleTv4K(googleTv).Should().BeFalse();
        DeviceFamilies.IsOnnGoogleTv4kBox(onnTv).Should().BeFalse();
    }

    private static AndroidDevice Device(
        string? manufacturer = null,
        string? brand = null,
        string? model = null,
        string? product = null,
        string? deviceName = null,
        string? board = null,
        string? fingerprint = null)
        => new()
        {
            Serial = "family-test",
            Manufacturer = manufacturer,
            Brand = brand,
            Model = model,
            Product = product,
            DeviceName = deviceName,
            Board = board,
            BuildFingerprint = fingerprint
        };
}
