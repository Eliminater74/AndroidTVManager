using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class AdbCommandService : IAdbCommandService
{
    private readonly IAdbProcessRunner _runner;

    public AdbCommandService(IAdbProcessRunner runner)
    {
        _runner = runner;
    }

    public Task<AdbCommandResult> ExecuteAsync(
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("A device serial is required.", nameof(serial));
        if (arguments.Count == 0)
            throw new ArgumentException("At least one ADB argument is required.", nameof(arguments));
        return _runner.RunForDeviceAsync(serial.Trim(), arguments, timeout, cancellationToken);
    }
}
