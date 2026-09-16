using AndroidTVManager.App.ViewModels;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class AppInstallerPageViewModelTests
{
    [Fact]
    public async Task No_authorized_target_prevents_installation()
    {
        var bulk = new FakeBulkApkService();
        var vm = new InstallApkPageViewModel(bulk, new FakePolicy());
        vm.SetSelectedPaths([Path.Combine(Path.GetTempPath(), "app.apk")]);
        await vm.AnalyzeCommand.ExecuteAsync(null);

        vm.SelectedDevice = null;
        vm.InstallCommand.CanExecute(null).Should().BeFalse();
        vm.TargetLabel.Should().Contain("No authorized target");
        bulk.InstallCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Selected_authorized_device_is_used_for_install()
    {
        var bulk = new FakeBulkApkService();
        var vm = new InstallApkPageViewModel(bulk, new FakePolicy())
        {
            SelectedDevice = new AndroidDevice
            {
                Serial = "192.168.1.50:5555",
                Model = "SHIELD Android TV",
                State = DeviceState.Device,
                ConnectionType = ConnectionType.Network
            }
        };
        vm.SetSelectedPaths([Path.Combine(Path.GetTempPath(), "app.apk")]);
        await vm.AnalyzeCommand.ExecuteAsync(null);
        vm.InstallCommand.CanExecute(null).Should().BeTrue();
        await vm.InstallCommand.ExecuteAsync(null);

        bulk.InstallCalled.Should().BeTrue();
        bulk.InstalledSerial.Should().Be("192.168.1.50:5555");
        vm.TargetLabel.Should().Contain("SHIELD Android TV");
    }

    private sealed class FakeBulkApkService : IBulkApkService
    {
        public bool InstallCalled { get; private set; }
        public string? InstalledSerial { get; private set; }

        public Task<BulkInstallPackageSet> PrepareAsync(
            IReadOnlyList<string> paths,
            IReadOnlyList<string>? deviceAbis = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new BulkInstallPackageSet(
                [
                    new(
                        "app",
                        "app.apk",
                        [new("app.apk", "app.apk", 1, ApkContainerKind.Apk, true, "com.example.app")],
                        "com.example.app")
                ],
                []));

        public Task<BulkInstallResult> InstallAsync(
            string serial,
            BulkInstallPackageSet packageSet,
            IProgress<BulkInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InstallCalled = true;
            InstalledSerial = serial;
            return Task.FromResult(new BulkInstallResult(
                [new(packageSet.Groups[0], BulkInstallItemStatus.Succeeded)],
                false));
        }

        public void Cleanup(BulkInstallPackageSet packageSet)
        {
        }
    }

    private sealed class FakePolicy : IDeveloperVerificationPolicyProvider
    {
        public DeveloperVerificationPolicy GetPolicy(AndroidDevice? device)
            => new("ADB installation guidance.", false);
    }
}
