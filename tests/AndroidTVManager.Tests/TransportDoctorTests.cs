using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Adb;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class TransportDoctorTests
{
    [Fact]
    public async Task Runs_bounded_transport_probes_and_reports_latency()
    {
        var runner = new FakeAdbProcessRunner();
        runner.Responses["shell echo android-tv-manager-transport-check"]
            = new("adb.exe", [], 0, "android-tv-manager-transport-check\n", string.Empty,
                TimeSpan.FromMilliseconds(12));
        var service = new TransportDoctorService(runner);

        var result = await service.RunAsync(Device(), 25);

        result.Probes.Should().HaveCount(25);
        result.SuccessfulProbes.Should().Be(25);
        result.FailedProbes.Should().Be(0);
        result.IsStable.Should().BeTrue();
        result.AverageLatency.Should().Be(TimeSpan.FromMilliseconds(12));
    }

    [Fact]
    public async Task Failed_probe_is_reported_without_being_hidden_as_a_transport_success()
    {
        var runner = new FakeAdbProcessRunner();
        runner.Responses["shell echo android-tv-manager-transport-check"]
            = new("adb.exe", [], 1, string.Empty, "device offline", TimeSpan.FromMilliseconds(20));
        var service = new TransportDoctorService(runner);

        var result = await service.RunAsync(Device(), 10);

        result.SuccessfulProbes.Should().Be(0);
        result.FailedProbes.Should().Be(10);
        result.IsStable.Should().BeFalse();
        result.Probes.Should().AllSatisfy(probe => probe.Error.Should().Be("device offline"));
    }

    private static AndroidDevice Device()
        => new()
        {
            Serial = "tv-1",
            Endpoint = "192.168.1.20:5555",
            ConnectionType = ConnectionType.Network,
            State = DeviceState.Device
        };
}
