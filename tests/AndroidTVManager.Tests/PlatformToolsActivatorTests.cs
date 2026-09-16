using AndroidTVManager.Infrastructure.Adb;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class PlatformToolsActivatorTests
{
    [Fact]
    public async Task Successful_activation_replaces_the_previous_install_and_deletes_the_backup()
    {
        using var workspace = new Workspace();
        workspace.WritePackage(workspace.Incoming, "1.0.42");
        workspace.WritePackage(workspace.Active, "1.0.41");

        var version = await new PlatformToolsActivator().ActivateAsync(
            workspace.Incoming,
            workspace.Active,
            Verify);

        version.Should().Be("1.0.42");
        File.ReadAllText(Path.Combine(workspace.Active, "marker.txt")).Should().Be("1.0.42");
        Directory.Exists(workspace.Incoming).Should().BeFalse();
        Directory.Exists(workspace.Previous).Should().BeFalse();
        File.Exists(Path.Combine(workspace.Active, "adb.exe")).Should().BeTrue();
        File.Exists(Path.Combine(workspace.Active, "fastboot.exe")).Should().BeTrue();
    }

    [Fact]
    public async Task Failed_verification_after_the_new_package_is_active_restores_the_previous_install()
    {
        using var workspace = new Workspace();
        workspace.WritePackage(workspace.Incoming, "broken");
        workspace.WritePackage(workspace.Active, "1.0.41");

        await FluentActions.Awaiting(() => new PlatformToolsActivator().ActivateAsync(
                workspace.Incoming,
                workspace.Active,
                (_, _) => throw new InvalidDataException("adb version failed.")))
            .Should().ThrowAsync<InvalidDataException>();

        File.ReadAllText(Path.Combine(workspace.Active, "marker.txt")).Should().Be("1.0.41");
        Directory.Exists(workspace.Previous).Should().BeFalse();
        Directory.GetDirectories(workspace.Root, "PlatformTools.failed-*").Should().BeEmpty();
    }

    [Fact]
    public async Task Failure_moving_the_new_package_into_place_restores_the_previous_install()
    {
        using var workspace = new Workspace();
        workspace.WritePackage(workspace.Incoming, "1.0.42");
        workspace.WritePackage(workspace.Active, "1.0.41");
        var ops = new FaultingDirectoryOperations(new FileSystemDirectoryOperations(), failOnMove: 2);

        await FluentActions.Awaiting(() => new PlatformToolsActivator(ops).ActivateAsync(
                workspace.Incoming,
                workspace.Active,
                Verify))
            .Should().ThrowAsync<IOException>();

        File.ReadAllText(Path.Combine(workspace.Active, "marker.txt")).Should().Be("1.0.41");
        Directory.Exists(workspace.Previous).Should().BeFalse();
    }

    [Fact]
    public async Task Failure_moving_the_old_install_aside_leaves_it_in_place()
    {
        using var workspace = new Workspace();
        workspace.WritePackage(workspace.Incoming, "1.0.42");
        workspace.WritePackage(workspace.Active, "1.0.41");
        var ops = new FaultingDirectoryOperations(new FileSystemDirectoryOperations(), failOnMove: 1);

        await FluentActions.Awaiting(() => new PlatformToolsActivator(ops).ActivateAsync(
                workspace.Incoming,
                workspace.Active,
                Verify))
            .Should().ThrowAsync<IOException>();

        File.ReadAllText(Path.Combine(workspace.Active, "marker.txt")).Should().Be("1.0.41");
        Directory.Exists(workspace.Incoming).Should().BeTrue();
        Directory.Exists(workspace.Previous).Should().BeFalse();
    }

    [Fact]
    public async Task First_install_cleans_up_when_verification_fails()
    {
        using var workspace = new Workspace();
        workspace.WritePackage(workspace.Incoming, "broken");

        await FluentActions.Awaiting(() => new PlatformToolsActivator().ActivateAsync(
                workspace.Incoming,
                workspace.Active,
                (_, _) => throw new InvalidDataException("adb version failed.")))
            .Should().ThrowAsync<InvalidDataException>();

        Directory.Exists(workspace.Active).Should().BeFalse();
        Directory.Exists(workspace.Previous).Should().BeFalse();
    }

    [Fact]
    public async Task Missing_fastboot_in_the_incoming_package_does_not_displace_the_current_install()
    {
        using var workspace = new Workspace();
        workspace.WritePackage(workspace.Incoming, "1.0.42", includeFastboot: false);
        workspace.WritePackage(workspace.Active, "1.0.41");

        await FluentActions.Awaiting(() => new PlatformToolsActivator().ActivateAsync(
                workspace.Incoming,
                workspace.Active,
                Verify))
            .Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*fastboot.exe*");

        File.ReadAllText(Path.Combine(workspace.Active, "marker.txt")).Should().Be("1.0.41");
    }

    private static Task<string> Verify(string activePath, CancellationToken cancellationToken)
        => Task.FromResult(File.ReadAllText(Path.Combine(activePath, "marker.txt")).Trim());

    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "AndroidTVManagerPlatformToolsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Active = Path.Combine(Root, "PlatformTools");
            Incoming = Path.Combine(Root, "incoming");
        }

        public string Root { get; }
        public string Active { get; }
        public string Incoming { get; }
        public string Previous => Active + PlatformToolsActivator.PreviousSuffix;

        public void WritePackage(string path, string marker, bool includeFastboot = true)
        {
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "adb.exe"), "adb");
            if (includeFastboot)
                File.WriteAllText(Path.Combine(path, "fastboot.exe"), "fastboot");
            File.WriteAllText(Path.Combine(path, "marker.txt"), marker);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class FaultingDirectoryOperations : IDirectoryOperations
    {
        private readonly IDirectoryOperations _inner;
        private readonly int _failOnMove;
        private int _moves;

        public FaultingDirectoryOperations(IDirectoryOperations inner, int failOnMove)
        {
            _inner = inner;
            _failOnMove = failOnMove;
        }

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public bool FileExists(string path) => _inner.FileExists(path);
        public void DeleteDirectory(string path) => _inner.DeleteDirectory(path);

        public void MoveDirectory(string source, string destination)
        {
            _moves++;
            if (_moves == _failOnMove)
                throw new IOException($"Injected failure moving '{source}'.");
            _inner.MoveDirectory(source, destination);
        }
    }
}
