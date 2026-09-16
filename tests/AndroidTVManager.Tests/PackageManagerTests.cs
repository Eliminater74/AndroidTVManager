using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Adb;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class PackageManagerTests
{
    [Fact]
    public async Task Gate_denial_prevents_the_adb_mutation()
    {
        var runner = new FakeAdbProcessRunner();
        var manager = new PackageManager(runner, new RecordingGate(allowed: false));

        await FluentActions.Awaiting(() => manager.DisableAsync("tv-1", "com.example.app"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*blocked*");
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Expected_fingerprint_is_supplied_to_the_gate()
    {
        var runner = new FakeAdbProcessRunner();
        var gate = new RecordingGate(allowed: true);
        var manager = new PackageManager(runner, gate);

        await manager.DisableAsync("tv-1", "com.example.app", expectedBuildFingerprint: "build/fingerprint");

        gate.LastRequest.Should().NotBeNull();
        gate.LastRequest!.ExpectedBuildFingerprint.Should().Be("build/fingerprint");
        gate.LastRequest.Kind.Should().Be(PackageMutationKind.Disable);
        runner.Calls.Should().ContainSingle();
    }

    private sealed class RecordingGate(bool allowed) : IPackageSafetyGate
    {
        public PackageMutationRequest? LastRequest { get; private set; }

        public Task<PackageMutationDecision> EvaluateAsync(
            PackageMutationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new PackageMutationDecision(allowed, allowed ? null : "The package mutation was blocked."));
        }

        public async Task EnsureAllowedAsync(
            PackageMutationRequest request,
            CancellationToken cancellationToken = default)
        {
            var decision = await EvaluateAsync(request, cancellationToken);
            if (!decision.Allowed)
                throw new InvalidOperationException(decision.BlockReason ?? "The package mutation was blocked.");
        }
    }
}
