using AndroidTVManager.Core.Backup;
using FluentAssertions;
using System.Security.Cryptography;

namespace AndroidTVManager.Tests;

public sealed class BackupIntegrityVerifierTests
{
    [Fact]
    public async Task Matching_expected_and_actual_apk_sets_succeed()
    {
        using var backup = new Workspace();
        backup.WriteApk("com.example.single", "base.apk", "apk");
        backup.WriteApk("com.example.split", "base.apk", "apk");
        backup.WriteApk("com.example.split", "config.tv.apk", "apk");
        await backup.WriteChecksumsAsync();

        var result = await BackupIntegrityVerifier.VerifyApkSetAsync(backup.Root);

        result.IsValid.Should().BeTrue();
        result.ExpectedPackageCount.Should().Be(2);
        result.ExpectedFileCount.Should().Be(3);
        result.ActualFileCount.Should().Be(3);
    }

    [Fact]
    public async Task Missing_checksum_file_fails_closed()
    {
        using var backup = new Workspace();
        backup.WriteApk("com.example.single", "base.apk", "apk");

        var result = await BackupIntegrityVerifier.VerifyApkSetAsync(backup.Root);

        result.IsValid.Should().BeFalse();
        result.Messages.Should().Contain(message => message.Contains("SHA256SUMS.txt"));
    }

    [Fact]
    public async Task Deleted_split_listed_in_checksums_is_detected_before_install()
    {
        using var backup = new Workspace();
        backup.WriteApk("com.example.split", "base.apk", "apk");
        backup.WriteApk("com.example.split", "config.tv.apk", "apk");
        await backup.WriteChecksumsAsync();
        File.Delete(Path.Combine(backup.Root, "apks", "com.example.split", "config.tv.apk"));

        var result = await BackupIntegrityVerifier.VerifyApkSetAsync(backup.Root);

        result.IsValid.Should().BeFalse();
        result.ExpectedPackageCount.Should().Be(1);
        result.ExpectedFileCount.Should().Be(2);
        result.MissingFiles.Should().Contain("apks/com.example.split/config.tv.apk");
        result.Messages.Should().Contain(message => message.Contains("nothing was installed"));
    }

    [Fact]
    public async Task Extra_apk_without_a_checksum_is_rejected()
    {
        using var backup = new Workspace();
        backup.WriteApk("com.example.single", "base.apk", "apk");
        await backup.WriteChecksumsAsync();
        backup.WriteApk("com.example.extra", "base.apk", "apk");

        var result = await BackupIntegrityVerifier.VerifyApkSetAsync(backup.Root);

        result.IsValid.Should().BeFalse();
        result.UnexpectedFiles.Should().Contain("apks/com.example.extra/base.apk");
    }

    [Fact]
    public async Task Checksum_mismatch_fails_closed()
    {
        using var backup = new Workspace();
        backup.WriteApk("com.example.single", "base.apk", "apk");
        await backup.WriteChecksumsAsync();
        File.WriteAllText(Path.Combine(backup.Root, "apks", "com.example.single", "base.apk"), "changed");

        var result = await BackupIntegrityVerifier.VerifyApkSetAsync(backup.Root);

        result.IsValid.Should().BeFalse();
        result.MismatchedFiles.Should().Contain("apks/com.example.single/base.apk");
        result.Messages.Should().Contain(message => message.Contains("nothing was installed"));
    }

    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "AndroidTVManagerBackupIntegrity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "apks"));
        }

        public string Root { get; }

        public void WriteApk(string package, string fileName, string contents)
        {
            var directory = Path.Combine(Root, "apks", package);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), contents);
        }

        public async Task WriteChecksumsAsync()
        {
            var lines = new List<string>();
            foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "apks"), "*", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file))).ToLowerInvariant();
                var relative = Path.GetRelativePath(Root, file).Replace('\\', '/');
                lines.Add($"{hash}  {relative}");
            }
            await File.WriteAllLinesAsync(Path.Combine(Root, "SHA256SUMS.txt"), lines);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
