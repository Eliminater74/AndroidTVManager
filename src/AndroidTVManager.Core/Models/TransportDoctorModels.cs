namespace AndroidTVManager.Core.Models;

public sealed record TransportProbe(
    int Sequence,
    bool Succeeded,
    TimeSpan Duration,
    string? Output,
    string? Error);

public sealed record TransportDoctorResult(
    string Serial,
    string? Endpoint,
    ConnectionType ConnectionType,
    DeviceState State,
    DateTimeOffset CapturedUtc,
    IReadOnlyList<TransportProbe> Probes)
{
    public int SuccessfulProbes => Probes.Count(probe => probe.Succeeded);
    public int FailedProbes => Probes.Count - SuccessfulProbes;
    public TimeSpan? AverageLatency => Probes.Count == 0
        ? null
        : TimeSpan.FromTicks((long)Probes.Average(probe => probe.Duration.Ticks));
    public bool IsStable => Probes.Count > 0 && FailedProbes == 0;
}
