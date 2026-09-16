using System.IO.Compression;
using System.Text;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Adb;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class BulkApkServiceTests
{
    [Fact]
    public async Task Prepares_a_single_apk_as_one_application()
    {
        var root = CreateDirectory();
        try
        {
            var apk = Path.Combine(root, "Plex.apk");
            await File.WriteAllTextAsync(apk, "apk");

            var packageSet = await CreateService().PrepareAsync([apk]);

            packageSet.Groups.Should().ContainSingle();
            packageSet.Groups[0].IsSplit.Should().BeFalse();
            packageSet.Groups[0].ContainerLabel.Should().Be("APK");
            packageSet.Groups[0].Artifacts.Should().ContainSingle(item => item.FileName == "Plex.apk");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Multiple_unrelated_apks_remain_independent_installs()
    {
        var root = CreateDirectory();
        try
        {
            var first = Path.Combine(root, "One.apk");
            var second = Path.Combine(root, "Two.apk");
            await File.WriteAllTextAsync(first, "one");
            await File.WriteAllTextAsync(second, "two");

            var packageSet = await CreateService().PrepareAsync([first, second]);

            packageSet.Groups.Should().HaveCount(2);
            packageSet.Groups.Should().OnlyContain(group => !group.IsSplit);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Base_and_split_apks_group_together()
    {
        var root = CreateDirectory();
        try
        {
            var basePath = Path.Combine(root, "base.apk");
            var splitPath = Path.Combine(root, "split_config.arm64_v8a.apk");
            var localePath = Path.Combine(root, "split_config.en.apk");
            await File.WriteAllTextAsync(basePath, "base");
            await File.WriteAllTextAsync(splitPath, "abi");
            await File.WriteAllTextAsync(localePath, "en");

            var packageSet = await CreateService().PrepareAsync([basePath, splitPath, localePath]);

            packageSet.Groups.Should().ContainSingle();
            packageSet.Groups[0].IsSplit.Should().BeTrue();
            packageSet.Groups[0].ContainerLabel.Should().Be("Split APK");
            packageSet.Groups[0].BaseApkName.Should().Be("base.apk");
            packageSet.Groups[0].Splits.Should().Contain(new[] { "arm64-v8a", "en" });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Folder_of_base_and_splits_groups_together()
    {
        var root = CreateDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "base.apk"), "base");
            await File.WriteAllTextAsync(Path.Combine(root, "split_config.en.apk"), "en");

            var packageSet = await CreateService().PrepareAsync([root]);

            packageSet.Groups.Should().ContainSingle();
            packageSet.Groups[0].IsSplit.Should().BeTrue();
            packageSet.Groups[0].BaseApkName.Should().Be("base.apk");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
            packageSet.Groups[0].ContainerKind.Should().Be(ApkContainerKind.Apks);
            packageSet.Groups[0].Artifacts.Should().HaveCount(2);
            service.Cleanup(packageSet);
            Directory.Exists(packageSet.TemporaryDirectories[0]).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Recognizes_apkm_and_xapk_archives()
    {
        var root = CreateDirectory();
        try
        {
            var apkm = CreateArchive(root, "app.apkm", "base.apk", "split_config.en.apk");
            var xapk = CreateXapk(root, "app.xapk", "com.example.app", includeObb: false);

            var service = CreateService();
            var apkmSet = await service.PrepareAsync([apkm]);
            var xapkSet = await service.PrepareAsync([xapk]);

            apkmSet.Groups[0].ContainerKind.Should().Be(ApkContainerKind.Apkm);
            apkmSet.Groups[0].IsSplit.Should().BeTrue();
            xapkSet.Groups[0].ContainerKind.Should().Be(ApkContainerKind.Xapk);
            xapkSet.Groups[0].PackageName.Should().Be("com.example.app");
            service.Cleanup(apkmSet);
            service.Cleanup(xapkSet);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Parses_xapk_obb_paths_and_ignores_android_data()
    {
        var root = CreateDirectory();
        try
        {
            var archivePath = Path.Combine(root, "game.xapk");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                AddText(archive, "manifest.json", """{"package_name":"com.example.game","native_code":["arm64-v8a"]}""");
                AddEntry(archive, "base.apk");
                AddEntry(archive, "split_config.arm64_v8a.apk");
                AddEntry(archive, "Android/obb/com.example.game/main.123.com.example.game.obb");
                AddEntry(archive, "Android/data/com.example.game/files/secret.bin");
            }

            var packageSet = await CreateService().PrepareAsync([archivePath]);
            var group = packageSet.Groups.Should().ContainSingle().Subject;

            group.PackageName.Should().Be("com.example.game");
            group.IsSplit.Should().BeTrue();
            group.Payloads.Should().ContainSingle(payload =>
                payload.RemoteDirectory == "/sdcard/Android/obb/com.example.game"
                && payload.FileName == "main.123.com.example.game.obb");
            group.Payloads.Should().NotContain(payload =>
                payload.RemotePath.Contains("/Android/data/", StringComparison.OrdinalIgnoreCase));
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
    public async Task Malformed_archive_fails_cleanly()
    {
        var root = CreateDirectory();
        try
        {
            var archivePath = Path.Combine(root, "broken.xapk");
            await File.WriteAllTextAsync(archivePath, "not a zip");

            var action = () => CreateService().PrepareAsync([archivePath]);

            await action.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_base_apk_in_a_split_set_fails_before_install()
    {
        var root = CreateDirectory();
        try
        {
            var split = Path.Combine(root, "split_config.arm64_v8a.apk");
            var locale = Path.Combine(root, "split_config.en.apk");
            await File.WriteAllTextAsync(split, "abi");
            await File.WriteAllTextAsync(locale, "en");

            var action = () => CreateService().PrepareAsync([split, locale]);

            await action.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*could not identify a base APK*");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Apks_with_multiple_standalone_variants_fails_closed()
    {
        var root = CreateDirectory();
        try
        {
            var archivePath = Path.Combine(root, "multi.apks");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                AddEntry(archive, "standalones/standalone-x86_64.apk");
                AddEntry(archive, "standalones/standalone-armeabi_v7a.apk");
            }

            var action = () => CreateService().PrepareAsync([archivePath]);

            await action.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*multiple device-targeted variants*");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Definitive_abi_mismatch_blocks_prepare()
    {
        var root = CreateDirectory();
        try
        {
            var archivePath = CreateXapk(root, "x86.xapk", "com.example.x86", includeObb: false, abi: "x86_64");

            var action = () => CreateService().PrepareAsync([archivePath], ["arm64-v8a"]);

            await action.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*x86_64*arm64-v8a*");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Unknown_abi_does_not_fabricate_incompatibility()
    {
        var root = CreateDirectory();
        try
        {
            var apk = Path.Combine(root, "app.apk");
            await File.WriteAllTextAsync(apk, "apk");

            var packageSet = await CreateService().PrepareAsync([apk], ["arm64-v8a"]);

            packageSet.Groups.Should().ContainSingle();
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
            var service = CreateService(runner);
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
            result.Items[0].Verification.Should().Be(BulkInstallVerificationState.IdentityUnavailable);
            result.Items[0].Message.Should().Contain("package identity was unavailable");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Successful_install_verifies_known_package_identity()
    {
        var root = CreateDirectory();
        try
        {
            var apk = Path.Combine(root, "app.apk");
            await File.WriteAllTextAsync(apk, "apk");
            var packages = new FakePackageManager();
            packages.Packages.Add(new("com.example.app", true, false, false));
            var service = CreateService(packageManager: packages);
            var packageSet = new BulkInstallPackageSet(
                [new("app", "app.apk",
                    [new(apk, "app.apk", 3, ApkContainerKind.Apk, true, "com.example.app")],
                    "com.example.app")],
                []);

            var result = await service.InstallAsync("tv-1", packageSet);

            result.Items[0].Status.Should().Be(BulkInstallItemStatus.Succeeded);
            result.Items[0].Verification.Should().Be(BulkInstallVerificationState.Verified);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Adb_success_without_package_readback_is_not_verified_success()
    {
        var root = CreateDirectory();
        try
        {
            var apk = Path.Combine(root, "app.apk");
            await File.WriteAllTextAsync(apk, "apk");
            var service = CreateService();
            var packageSet = new BulkInstallPackageSet(
                [new("app", "app.apk",
                    [new(apk, "app.apk", 3, ApkContainerKind.Apk, true, "com.missing.app")],
                    "com.missing.app")],
                []);

            var result = await service.InstallAsync("tv-1", packageSet);

            result.Items[0].Status.Should().Be(BulkInstallItemStatus.Failed);
            result.Items[0].Verification.Should().Be(BulkInstallVerificationState.Missing);
            result.Items[0].Message.Should().Contain("package verification failed");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Adb_install_failure_surfaces_a_useful_message()
    {
        var root = CreateDirectory();
        try
        {
            var apk = Path.Combine(root, "app.apk");
            await File.WriteAllTextAsync(apk, "apk");
            var runner = new FakeAdbProcessRunner();
            runner.Responses["install -r " + apk] = new(
                "adb.exe", ["install", "-r", apk], 1, string.Empty,
                "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: signatures do not match]",
                TimeSpan.Zero);
            var service = CreateService(runner);
            var packageSet = new BulkInstallPackageSet(
                [new("app", "app.apk", [new(apk, "app.apk", 3, ApkContainerKind.Apk, true)])],
                []);

            var result = await service.InstallAsync("tv-1", packageSet);

            result.Items[0].Status.Should().Be(BulkInstallItemStatus.Failed);
            result.Items[0].Message.Should().Contain("signing certificate does not match");
            result.Items[0].Message.Should().Contain("INSTALL_FAILED_UPDATE_INCOMPATIBLE");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Obb_copy_failure_is_partial_success()
    {
        var root = CreateDirectory();
        try
        {
            var archivePath = CreateXapk(root, "game.xapk", "com.example.game", includeObb: true);
            var files = new FakeDeviceFileService { FailPush = true };
            var packages = new FakePackageManager();
            packages.Packages.Add(new("com.example.game", true, false, false));
            var service = CreateService(files: files, packageManager: packages);
            var packageSet = await service.PrepareAsync([archivePath]);

            var result = await service.InstallAsync("tv-1", packageSet);

            result.PartialSuccessCount.Should().Be(1);
            result.Items[0].Status.Should().Be(BulkInstallItemStatus.PartialSuccess);
            result.Items[0].Message.Should().Contain("additional XAPK data could not be copied");
            files.Pushes.Should().ContainSingle(push =>
                push.Remote.StartsWith("/sdcard/Android/obb/com.example.game/", StringComparison.OrdinalIgnoreCase));
            files.Pushes.Should().NotContain(push =>
                push.Remote.Contains("/Android/data/", StringComparison.OrdinalIgnoreCase));
            Directory.Exists(packageSet.TemporaryDirectories[0]).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cleans_temporary_files_on_canceled_install()
    {
        var root = CreateDirectory();
        try
        {
            var archivePath = CreateArchive(root, "app.apkm", "base.apk", "split_config.en.apk");
            var service = CreateService();
            var packageSet = await service.PrepareAsync([archivePath]);
            packageSet.TemporaryDirectories.Should().Contain(path => Directory.Exists(path));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();

            var result = await service.InstallAsync("tv-1", packageSet, cancellationToken: canceled.Token);

            result.WasCanceled.Should().BeTrue();
            result.ReconciliationMessage.Should().NotContain("nothing changed");
            Directory.Exists(packageSet.TemporaryDirectories[0]).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cleans_temporary_files_when_prepare_fails()
    {
        var root = CreateDirectory();
        try
        {
            var archivePath = Path.Combine(root, "unsafe.xapk");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
                AddEntry(archive, "../escape.apk");

            var temps = Directory.GetDirectories(Path.GetTempPath(), "AndroidTVManager-apk-*");
            var action = () => CreateService().PrepareAsync([archivePath]);
            await action.Should().ThrowAsync<InvalidDataException>();
            var remaining = Directory.GetDirectories(Path.GetTempPath(), "AndroidTVManager-apk-*")
                .Except(temps, StringComparer.OrdinalIgnoreCase);
            remaining.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_target_is_rejected_by_install()
    {
        var service = CreateService();
        var packageSet = new BulkInstallPackageSet([], []);

        var action = () => service.InstallAsync(" ", packageSet);

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("serial");
    }

    [Fact]
    public void Explains_signing_mismatch_without_suggesting_uninstall()
    {
        var result = new AdbCommandResult(
            "adb.exe",
            ["install", "-r", "app.apk"],
            1,
            string.Empty,
            "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE]",
            TimeSpan.Zero);

        var message = AdbInstallErrorFormatter.Explain(result);

        message.Should().Contain("signing certificate does not match");
        message.Should().Contain("will not uninstall");
        message.Should().Contain("INSTALL_FAILED_UPDATE_INCOMPATIBLE");
    }

    private static BulkApkService CreateService(
        FakeAdbProcessRunner? runner = null,
        FakePackageManager? packageManager = null,
        FakeDeviceFileService? files = null)
        => new(
            new ApkInstaller(runner ?? new FakeAdbProcessRunner()),
            packageManager ?? new FakePackageManager(),
            files ?? new FakeDeviceFileService(),
            new TestPaths(CreateDirectory()));

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AndroidTVManager-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateArchive(string root, string name, params string[] entries)
    {
        var path = Path.Combine(root, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
            AddEntry(archive, entry);
        return path;
    }

    private static string CreateXapk(
        string root,
        string name,
        string packageName,
        bool includeObb,
        string abi = "arm64-v8a")
    {
        var path = Path.Combine(root, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddText(archive, "manifest.json",
            "{\"package_name\":\"" + packageName + "\",\"native_code\":[\"" + abi + "\"]}");
        AddEntry(archive, "base.apk");
        AddEntry(archive, $"split_config.{abi.Replace('-', '_')}.apk");
        if (includeObb)
            AddEntry(archive, $"Android/obb/{packageName}/main.1.{packageName}.obb");
        return path;
    }

    private static void AddEntry(ZipArchive archive, string path)
    {
        using var stream = archive.CreateEntry(path).Open();
        stream.WriteByte(1);
    }

    private static void AddText(ZipArchive archive, string path, string content)
    {
        using var stream = archive.CreateEntry(path).Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private sealed class FakePackageManager : IPackageManager
    {
        public List<PackageInfo> Packages { get; } = [];

        public Task<IReadOnlyList<PackageInfo>> ListAsync(string serial, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PackageInfo>>(Packages);

        public Task<AdbCommandResult> LaunchAsync(string serial, string packageName, CancellationToken cancellationToken = default) => Result();
        public Task<AdbCommandResult> ForceStopAsync(string serial, string packageName, CancellationToken cancellationToken = default) => Result();
        public Task<AdbCommandResult> EnableAsync(string serial, string packageName, CancellationToken cancellationToken = default, string? expectedBuildFingerprint = null) => Result();
        public Task<AdbCommandResult> DisableAsync(string serial, string packageName, CancellationToken cancellationToken = default, string? expectedBuildFingerprint = null) => Result();
        public Task<AdbCommandResult> UninstallForUserAsync(string serial, string packageName, CancellationToken cancellationToken = default, string? expectedBuildFingerprint = null) => Result();
        public Task<AdbCommandResult> RestoreAsync(string serial, string packageName, CancellationToken cancellationToken = default, string? expectedBuildFingerprint = null) => Result();
        public Task<AdbCommandResult> FullUninstallAsync(string serial, string packageName, CancellationToken cancellationToken = default, string? expectedBuildFingerprint = null) => Result();
        public Task<AdbCommandResult> ClearDataAsync(string serial, string packageName, CancellationToken cancellationToken = default, string? expectedBuildFingerprint = null) => Result();
        public Task<AdbCommandResult> ClearCacheAsync(string serial, string packageName, CancellationToken cancellationToken = default, string? expectedBuildFingerprint = null) => Result();
        public Task<AdbCommandResult> GrantPermissionAsync(string serial, string packageName, string permission, CancellationToken cancellationToken = default, string? expectedBuildFingerprint = null) => Result();
        public Task<AdbCommandResult> RevokePermissionAsync(string serial, string packageName, string permission, CancellationToken cancellationToken = default, string? expectedBuildFingerprint = null) => Result();
        public Task<AdbCommandResult> OpenAppSettingsAsync(string serial, string packageName, CancellationToken cancellationToken = default) => Result();
        public Task<AdbCommandResult> PullApkAsync(string serial, string remotePath, string localPath, CancellationToken cancellationToken = default) => Result();

        private static Task<AdbCommandResult> Result()
            => Task.FromResult(new AdbCommandResult("adb.exe", [], 0, string.Empty, string.Empty, TimeSpan.Zero));
    }

    private sealed class FakeDeviceFileService : IDeviceFileService
    {
        public bool FailPush { get; set; }
        public List<(string Serial, string Local, string Remote)> Pushes { get; } = [];

        public Task<IReadOnlyList<DeviceFileEntry>> ListAsync(
            string serial,
            string remoteDirectory,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DeviceFileEntry>>([]);

        public Task<AdbCommandResult> PushAsync(
            string serial,
            string localPath,
            string remotePath,
            CancellationToken cancellationToken = default)
        {
            Pushes.Add((serial, localPath, remotePath));
            return Task.FromResult(new AdbCommandResult(
                "adb.exe",
                ["push", localPath, remotePath],
                FailPush ? 1 : 0,
                string.Empty,
                FailPush ? "Permission denied" : string.Empty,
                TimeSpan.Zero));
        }

        public Task<AdbCommandResult> PullAsync(
            string serial,
            string remotePath,
            string localPath,
            CancellationToken cancellationToken = default)
            => Result();

        public Task<AdbCommandResult> CreateDirectoryAsync(
            string serial,
            string remotePath,
            CancellationToken cancellationToken = default)
            => Result();

        public Task<AdbCommandResult> DeleteAsync(
            string serial,
            string remotePath,
            CancellationToken cancellationToken = default)
            => Result();

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
