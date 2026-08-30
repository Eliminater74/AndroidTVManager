using AndroidTVManager.Core.Models;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Infrastructure.Database;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DeploymentProfileRepositoryTests
{
    [Fact]
    public async Task Persists_profile_assets_steps_and_execution_history()
    {
        var root = Path.Combine(Path.GetTempPath(), "AndroidTVManager-profile-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var database = new SqliteDatabase(new TestPaths(root));
            var repository = new DeploymentProfileRepository(database);
            var profile = new DeploymentProfile(
                0,
                "Streamer setup",
                "Repeatable setup",
                "Google",
                "google",
                "Google TV Streamer",
                "kirkwood",
                "kirkwood",
                34,
                null,
                "arm64-v8a",
                true,
                null,
                "google/kirkwood",
                1,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                [
                    new(0, 0, DeploymentStepKind.InstallApk, "Install app", AssetIds: [1]),
                    new(0, 1, DeploymentStepKind.DisablePackage, "Disable recommendations", PackageName: "com.example.recommendations")
                ],
                [
                    new(0, 0, "abc123", "base.apk", "abc123-base.apk", 100, ApkContainerKind.Apk,
                        "com.example.app", "1.0", 1, DateTimeOffset.UtcNow)
                ]);

            var id = await repository.UpsertAsync(profile);
            var loaded = await repository.GetAsync(id);

            loaded.Should().NotBeNull();
            loaded!.Name.Should().Be("Streamer setup");
            loaded.Steps.Should().HaveCount(2);
            loaded.Steps[0].AssetIds.Should().ContainSingle().Which.Should().Be(1);
            loaded.Assets.Should().ContainSingle();
            loaded.Assets![0].Sha256.Should().Be("abc123");

            var executionId = await repository.StartExecutionAsync(id, loaded.Name, "ABC123");
            await repository.RecordExecutionStepAsync(executionId,
                new(0, executionId, loaded.Steps[0].Id, 0, "Succeeded", "installed", false, null));
            await repository.CompleteExecutionAsync(executionId, "Succeeded");
            var executions = await repository.GetExecutionsAsync(id);

            executions.Should().ContainSingle();
            executions[0].Status.Should().Be("Succeeded");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    try
                    {
                        Directory.Delete(root, recursive: true);
                        break;
                    }
                    catch (IOException) when (attempt < 19)
                    {
                        await Task.Delay(25);
                    }
                }
            }
        }
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
        public void EnsureCreated() => Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
    }
}
