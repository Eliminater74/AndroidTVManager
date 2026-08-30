using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Adb;
using AndroidTVManager.Infrastructure.Storage;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class ConfigurationExplorerServiceTests
{
    [Fact]
    public async Task Collects_runtime_and_partition_evidence_for_the_selected_serial()
    {
        var runner = new FakeAdbProcessRunner();
        runner.Responses["shell getprop"] = Result("""
            [ro.product.manufacturer]: [Philips]
            [ro.product.model]: [Runtime TV]
            [ro.build.version.release]: [14]
            [ro.build.version.sdk]: [34]
            [ro.build.fingerprint]: [philips/fingerprint]
            """);
        runner.Responses[FileCommand("/system/build.prop")] = Result("ro.product.model=System TV\n");
        runner.Responses[FileCommand("/vendor/build.prop")] = Result("ro.build.version.security_patch=2026-06-05\n");
        runner.Responses[FileCommand("/product/build.prop")] = Result(ConfigurationPropertyParser.UnavailableMarker);

        var service = new ConfigurationExplorerService(
            runner,
            new InMemoryConfigurationSnapshotStore(),
            new FakeAppLogger());

        var snapshot = await service.InspectAsync("usb-device-1");

        snapshot.Serial.Should().Be("usb-device-1");
        snapshot.Manufacturer.Should().Be("Philips");
        snapshot.Model.Should().Be("Runtime TV");
        snapshot.ApiLevel.Should().Be(34);
        snapshot.Properties.Should().Contain(property =>
            property.Name == "ro.product.model"
            && property.Status == ConfigurationValueStatus.Conflict);
        snapshot.Properties.Should().Contain(property =>
            property.Name == "ro.build.version.security_patch"
            && property.Status == ConfigurationValueStatus.FileOnly);
        snapshot.Properties.Single(property => property.Name == "ro.product.model")
            .StaticValues.Should().Contain(value =>
                value.Source == ConfigurationSource.Product
                && !value.IsAvailable
                && value.Error == "File is missing or not readable.");
        snapshot.Sections.Should().Contain(section => section.State == InspectionSectionState.Partial);
        snapshot.Commands.Should().HaveCount(6);
        runner.Calls.Should().OnlyContain(call => call.Serial == "usb-device-1");
        runner.Calls.Select(call => string.Join(" ", call.Arguments))
            .Should().NotContain(command => command.Contains("reboot", StringComparison.OrdinalIgnoreCase)
                || command.Contains("root", StringComparison.OrdinalIgnoreCase)
                || command.Contains("setprop", StringComparison.OrdinalIgnoreCase)
                || command.Contains("pm ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cancellation_prevents_configuration_collection()
    {
        var runner = new FakeAdbProcessRunner();
        var service = new ConfigurationExplorerService(
            runner,
            new InMemoryConfigurationSnapshotStore(),
            new FakeAppLogger());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.InspectAsync("tv-1", cancellationToken: cancellation.Token));
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task File_store_retains_recent_snapshots_by_serial()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AndroidTVManager-config-{Guid.NewGuid():N}");
        try
        {
            var paths = new TestPaths(root);
            var store = new ConfigurationSnapshotStore(paths, new FakeAppLogger());
            var first = Snapshot(DateTimeOffset.UtcNow.AddMinutes(-1));
            var second = Snapshot(DateTimeOffset.UtcNow);

            await store.SaveAsync(first);
            await store.SaveAsync(second);

            var recent = await store.GetRecentAsync("tv-1");
            recent.Should().HaveCount(2);
            recent[0].CapturedUtc.Should().Be(second.CapturedUtc);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string FileCommand(string path)
        => $"shell sh -c if [ -r '{path}' ]; then cat '{path}'; else echo '{ConfigurationPropertyParser.UnavailableMarker}'; fi";

    private static AdbCommandResult Result(string output)
        => new("adb.exe", [], 0, output, string.Empty, TimeSpan.Zero);

    private static ConfigurationSnapshot Snapshot(DateTimeOffset captured)
        => new(
            "tv-1",
            captured,
            "Living Room TV",
            "Philips",
            "TV",
            "fingerprint",
            "14",
            34,
            "2026-06-05",
            [],
            []);

    private sealed class InMemoryConfigurationSnapshotStore : IConfigurationSnapshotStore
    {
        private readonly List<ConfigurationSnapshot> _snapshots = [];

        public Task SaveAsync(ConfigurationSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _snapshots.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task<ConfigurationSnapshot?> GetLatestAsync(
            string serial,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshots.Where(snapshot => snapshot.Serial == serial)
                .OrderByDescending(snapshot => snapshot.CapturedUtc)
                .FirstOrDefault());

        public Task<IReadOnlyList<ConfigurationSnapshot>> GetRecentAsync(
            string serial,
            int limit = 10,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ConfigurationSnapshot>>(
                _snapshots.Where(snapshot => snapshot.Serial == serial)
                    .OrderByDescending(snapshot => snapshot.CapturedUtc)
                    .Take(limit)
                    .ToArray());
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

        public void EnsureCreated()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(SnapshotsPath);
        }
    }
}
