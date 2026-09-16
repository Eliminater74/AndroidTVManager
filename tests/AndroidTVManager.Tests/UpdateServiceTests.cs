using System.Net;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Updates;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task Deletes_installer_when_checksum_is_missing()
    {
        using var workspace = new Workspace();
        var payload = "installer-bytes"u8.ToArray();
        var service = CreateService(workspace, new StubHandler(_ => Bytes(payload)));

        var result = await service.DownloadAndInstallAsync(Release(installerSha256: null));

        result.Started.Should().BeFalse();
        result.InstallerPath.Should().BeNull();
        result.Message.Should().Contain("checksum");
        Directory.EnumerateFiles(workspace.Paths.TempPath, "*.exe").Should().BeEmpty();
    }

    [Fact]
    public async Task Deletes_installer_when_checksum_does_not_match()
    {
        using var workspace = new Workspace();
        var payload = "installer-bytes"u8.ToArray();
        var service = CreateService(workspace, new StubHandler(_ => Bytes(payload)));

        var result = await service.DownloadAndInstallAsync(Release(installerSha256: new string('0', 64)));

        result.Started.Should().BeFalse();
        result.InstallerPath.Should().BeNull();
        result.Message.Should().Contain("checksum");
        Directory.EnumerateFiles(workspace.Paths.TempPath, "*.exe").Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_installer_when_content_length_exceeds_the_limit()
    {
        using var workspace = new Workspace();
        var service = CreateService(workspace, new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("tiny"u8.ToArray())
            };
            response.Content.Headers.ContentLength = UpdateService.MaximumInstallerBytes + 1;
            return response;
        }));

        var result = await service.DownloadAndInstallAsync(Release(installerSha256: new string('a', 64)));

        result.Started.Should().BeFalse();
        result.Message.Should().Contain("exceeds");
        Directory.EnumerateFiles(workspace.Paths.TempPath, "*.exe").Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_installer_when_the_stream_grows_past_the_limit()
    {
        using var workspace = new Workspace();
        var oversized = new byte[2048];
        var service = new UpdateService(
            workspace.Paths,
            new NoopLogger(),
            new HttpClient(new StubHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(oversized)
                };
                response.Content.Headers.ContentLength = null;
                return response;
            })),
            maximumInstallerBytes: 1024);

        var result = await service.DownloadAndInstallAsync(Release(installerSha256: new string('a', 64)));

        result.Started.Should().BeFalse();
        result.Message.Should().Contain("exceeded");
        Directory.EnumerateFiles(workspace.Paths.TempPath, "*.exe").Should().BeEmpty();
    }

    private static UpdateService CreateService(Workspace workspace, HttpMessageHandler handler)
        => new(workspace.Paths, new NoopLogger(), new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.invalid/")
        });

    private static HttpResponseMessage Bytes(byte[] payload)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };

    private static UpdateRelease Release(string? installerSha256)
        => new(
            "v1.0.0-B15",
            "1.0.0-B15",
            "Android TV Manager 1.0.0-B15",
            "notes",
            "https://example.invalid/release",
            "https://example.invalid/AndroidTVManager-1.0.0-B15-Setup.exe",
            installerSha256,
            null,
            DateTimeOffset.UtcNow);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            Paths = new TestPaths(Path.Combine(Path.GetTempPath(), "AndroidTVManagerUpdateTests", Guid.NewGuid().ToString("N")));
            Paths.EnsureCreated();
        }

        public TestPaths Paths { get; }

        public void Dispose()
        {
            if (Directory.Exists(Paths.Root))
                Directory.Delete(Paths.Root, recursive: true);
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
        public void EnsureCreated()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            Directory.CreateDirectory(TempPath);
        }
    }

    private sealed class NoopLogger : IAppLogger
    {
        public void Information(string source, string message) { }
        public void Warning(string source, string message) { }
        public void Error(string source, string message, Exception? exception = null) { }
    }
}
