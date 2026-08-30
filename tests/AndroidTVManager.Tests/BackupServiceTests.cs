using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Adb;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public async Task Reports_modern_backup_capabilities_without_attempting_a_full_image()
    {
        var runner = new FakeAdbProcessRunner();
        runner.Responses["shell test -d /sdcard && echo available"]
            = new("adb.exe", [], 0, "available", string.Empty, TimeSpan.Zero);
        var service = CreateService(runner);
        var device = Device(apiLevel: 34);

        var capabilities = await service.GetCapabilitiesAsync(device);

        capabilities.Single(item => item.Kind == BackupKind.PackageApks).State
            .Should().Be(CapabilityState.Supported);
        capabilities.Single(item => item.Kind == BackupKind.LegacyAppData).State
            .Should().Be(CapabilityState.Unsupported);
        capabilities.Single(item => item.Kind == BackupKind.FullDeviceImage).State
            .Should().Be(CapabilityState.Unsupported);
        runner.Calls.Should().NotContain(call =>
            call.Arguments.Any(argument => argument.Contains("fastboot", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Restores_single_and_split_apks_from_backup_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "AndroidTVManagerBackupTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "apks", "com.example.single"));
            Directory.CreateDirectory(Path.Combine(root, "apks", "com.example.split"));
            await File.WriteAllTextAsync(Path.Combine(root, "apks", "com.example.single", "base.apk"), "apk");
            await File.WriteAllTextAsync(Path.Combine(root, "apks", "com.example.split", "base.apk"), "apk");
            await File.WriteAllTextAsync(Path.Combine(root, "apks", "com.example.split", "config.tv.apk"), "apk");

            var runner = new FakeAdbProcessRunner();
            var service = CreateService(runner);
            var result = await service.RestoreApksAsync("tv-1", root);

            result.RestoredPackages.Should().Be(2);
            result.FailedPackages.Should().Be(0);
            runner.Calls.Any(call => call.Arguments.SequenceEqual(
                ["install", "-r", Path.Combine(root, "apks", "com.example.single", "base.apk")]))
                .Should().BeTrue();
            runner.Calls.Any(call => call.Arguments.SequenceEqual(
                ["install-multiple", "-r",
                    Path.Combine(root, "apks", "com.example.split", "base.apk"),
                    Path.Combine(root, "apks", "com.example.split", "config.tv.apk")]))
                .Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static DeviceBackupService CreateService(FakeAdbProcessRunner runner)
        => new(
            runner,
            new UnsupportedInspectionService(),
            new UnsupportedConfigurationService(),
            new UnsupportedInventoryService(),
            new NoopLogger());

    private static AndroidDevice Device(int apiLevel)
        => new()
        {
            Serial = "tv-1",
            FriendlyName = "Test TV",
            State = DeviceState.Device,
            ApiLevel = apiLevel,
            ConnectionType = ConnectionType.Usb
        };

    private sealed class UnsupportedInspectionService : IDeviceInspectionService
    {
        public Task<DeviceInspectionResult> InspectAsync(
            string serial,
            IProgress<DeviceInspectionProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class UnsupportedConfigurationService : IConfigurationExplorerService
    {
        public Task<ConfigurationSnapshot> InspectAsync(
            string serial,
            string? friendlyDeviceName = null,
            IProgress<ConfigurationInspectionProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class UnsupportedInventoryService : IPackageInventoryService
    {
        public Task<PackageInventoryResult> GetInventoryAsync(
            string serial,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PackageInventoryEntry?> GetDetailsAsync(
            string serial,
            string packageName,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopLogger : IAppLogger
    {
        public void Information(string source, string message)
        {
        }

        public void Warning(string source, string message)
        {
        }

        public void Error(string source, string message, Exception? exception = null)
        {
        }
    }
}
