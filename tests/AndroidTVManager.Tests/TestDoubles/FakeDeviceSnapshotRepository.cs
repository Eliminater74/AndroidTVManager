using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Tests.TestDoubles;

public sealed class FakeDeviceSnapshotRepository : IDeviceSnapshotRepository
{
    public DeviceInspectionResult? Latest { get; private set; }

    public Task<long> SaveAsync(DeviceInspectionResult inspection, CancellationToken cancellationToken = default)
    {
        Latest = inspection;
        return Task.FromResult(1L);
    }

    public Task<DeviceInspectionResult?> GetLatestAsync(string serial, CancellationToken cancellationToken = default)
        => Task.FromResult(Latest?.Serial == serial ? Latest : null);
}
