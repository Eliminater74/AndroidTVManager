using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Infrastructure.Logging;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class LoggingTests
{
    [Fact]
    public async Task File_logger_publishes_live_entries_and_can_clear_the_log()
    {
        var root = Path.Combine(Path.GetTempPath(), "AndroidTVManagerTests", Guid.NewGuid().ToString("N"));
        var paths = new TestPaths(root);

        try
        {
            using var logger = new FileLogger(paths);
            var liveEntry = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            logger.EntryWritten += (_, entry) => liveEntry.TrySetResult(entry);

            logger.Information("Test", "live entry");

            var completed = await Task.WhenAny(liveEntry.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            completed.Should().Be(liveEntry.Task);
            var entry = await liveEntry.Task;
            entry.Should().Contain("[Test] live entry");

            IReadOnlyList<string> lines = [];
            for (var attempt = 0; attempt < 20 && lines.Count == 0; attempt++)
            {
                lines = await logger.ReadCurrentAsync();
                if (lines.Count == 0)
                    await Task.Delay(25);
            }

            lines.Should().ContainSingle(line => line.Contains("[Test] live entry"));
            await logger.ClearAsync();
            (await logger.ReadCurrentAsync()).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
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
        public string TempPath => Path.Combine(Root, "Temp");

        public void EnsureCreated()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            Directory.CreateDirectory(LogsPath);
        }
    }
}
