using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Tests.TestDoubles;

public sealed class FakeAdbProcessRunner : IAdbProcessRunner
{
    public Dictionary<string, AdbCommandResult> Responses { get; } = new(StringComparer.Ordinal);
    public List<(string Serial, IReadOnlyList<string> Arguments)> Calls { get; } = [];

    public Task<AdbCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Response(string.Empty, arguments));

    public Task<AdbCommandResult> RunForDeviceAsync(
        string serial,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((serial, arguments));
        return Task.FromResult(Response(serial, arguments));
    }

    private AdbCommandResult Response(string serial, IReadOnlyList<string> arguments)
    {
        var key = string.Join(" ", arguments);
        return Responses.GetValueOrDefault(key)
            ?? new AdbCommandResult("adb.exe", arguments, 0, string.Empty, string.Empty, TimeSpan.Zero);
    }
}
