using System.IO.Compression;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Adb;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class BulkApkServiceTests
{
    [Fact]
    public async Task Prepares_apks_archive_as_one_split_group()
    {
        var root = CreateDirectory();
        try
        {
            var archivePath = Path.Combine(root, "streamer.apks");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                AddEntry(archive, "base.apk");
                AddEntry(archive, "config.arm64_v8a.apk");
            }

            var service = CreateService();
            var packageSet = await service.PrepareAsync([archivePath]);

            packageSet.Groups.Should().ContainSingle();
            packageSet.Groups[0].IsSplit.Should().BeTrue();
            packageSet.Groups[0].Artifacts.Should().HaveCount(2);
            packageSet.Groups[0].Artifacts.Should().OnlyContain(item => item.ContainerKind == ApkContainerKind.Apks);
            service.Cleanup(packageSet);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_archive_path_traversal()
    {
        var root = CreateDirectory();
        try
        {
            var archivePath = Path.Combine(root, "unsafe.apks");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
                AddEntry(archive, "../escape.apk");

            var action = () => CreateService().PrepareAsync([archivePath]);

            await action.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*unsafe path*");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Installs_split_group_with_install_multiple()
    {
        var root = CreateDirectory();
        try
        {
            var basePath = Path.Combine(root, "base.apk");
            var splitPath = Path.Combine(root, "config.apk");
            await File.WriteAllTextAsync(basePath, "base");
            await File.WriteAllTextAsync(splitPath, "split");
            var runner = new FakeAdbProcessRunner();
            var service = new BulkApkService(
                new ApkInstaller(runner),
                new FakePackageManager(),
                new TestPaths(root));
            var packageSet = new BulkInstallPackageSet(
                [new(
                    "streamer",
                    "streamer (2 APK splits)",
                    [
                        new(basePath, "base.apk", 4, ApkContainerKind.Apk, true),
                        new(splitPath, "config.apk", 5, ApkContainerKind.Apk, false)
                    ])],
                []);

            var result = await service.InstallAsync("emulator-5554", packageSet);

            result.SucceededCount.Should().Be(1);
            runner.Calls.Should().ContainSingle();
            runner.Calls[0].Arguments.Should().ContainInOrder("install-multiple", "-r", basePath, splitPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static BulkApkService CreateService()
        => new(new ApkInstaller(new FakeAdbProcessRunner()), new FakePackageManager(), new TestPaths(CreateDirectory()));

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AndroidTVManager-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AddEntry(ZipArchive archive, string path)
    {
        using var stream = archive.CreateEntry(path).Open();
        stream.WriteByte(1);
    }

    private sealed class FakePackageManager : IPackageManager
    {
        public Task<IReadOnlyList<PackageInfo>> ListAsync(string serial, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PackageInfo>>([]);

        public Task<AdbCommandResult> LaunchAsync(string serial, string packageName, CancellationToken cancellationToken = default) => Result();
        public Task<AdbCommandResult> ForceStopAsync(string serial, string packageName, CancellationToken cancellationToken = default) => Result();
        public Task<AdbCommandResult> EnableAsync(string serial, string packageName, CancellationToken cancellationToken = default) => Result();
        public Task<AdbCommandResult> DisableAsync(string serial, string packageName, CancellationToken cancellationToken = default) => Result();
        public Task<AdbCommandResult> UninstallForUserAsync(string serial, string packageName, CancellationToken cancellationToken = default) => Result();
        public Task<AdbCommandResult> RestoreAsync(string serial, string packageName, CancellationToken cancellationToken = default) => Result();
        public Task<AdbCommandResult> FullUninstallAsync(string serial, string packageName, CancellationToken cancellationToken = default) => Result();
        public Task<AdbCommandResult> ClearDataAsync(string serial, string packageName, CancellationToken cancellationToken = default) => Result();
        public Task<AdbCommandResult> ClearCacheAsync(string serial, string packageName, CancellationToken cancellationToken = default) => Result();
        public Task<AdbCommandResult> GrantPermissionAsync(string serial, string packageName, string permission, CancellationToken cancellationToken = default) => Result();
        public Task<AdbCommandResult> RevokePermissionAsync(string serial, string packageName, string permission, CancellationToken cancellationToken = default) => Result();
        public Task<AdbCommandResult> OpenAppSettingsAsync(string serial, string packageName, CancellationToken cancellationToken = default) => Result();
        public Task<AdbCommandResult> PullApkAsync(string serial, string remotePath, string localPath, CancellationToken cancellationToken = default) => Result();

        private static Task<AdbCommandResult> Result()
            => Task.FromResult(new AdbCommandResult("adb.exe", [], 0, string.Empty, string.Empty, TimeSpan.Zero));
    }

    private sealed class TestPaths(string root) : ILocalAppDataPaths
    {
        public string Root { get; } = root;
        public string DatabasePath => Path.Combine(Root, "Data", "test.db");
        public string ToolsPath => Path.Combine(Root, "Tools");
        public string LogsPath => Path.Combine(Root, "Logs");
        public string ScriptsPath => Path.Combine(Root, "Scripts");
        public string SnapshotsPath => Path.Combine(Root, "Snapshots");
        public string ScreenshotsPath => Path.Combine(Root, "Screenshots");
        public string RecordingsPath => Path.Combine(Root, "Recordings");
        public string BackupsPath => Path.Combine(Root, "Backups");
        public string TempPath => Path.Combine(Root, "Temp");
        public void EnsureCreated() => Directory.CreateDirectory(Root);
    }
}
