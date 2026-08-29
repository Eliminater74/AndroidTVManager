using AndroidTVManager.Core.Abstractions;

namespace AndroidTVManager.Tests.TestDoubles;

public sealed class FakeAppLogger : IAppLogger
{
    public List<string> Entries { get; } = [];

    public void Information(string source, string message) => Entries.Add($"INFO:{source}:{message}");
    public void Warning(string source, string message) => Entries.Add($"WARN:{source}:{message}");
    public void Error(string source, string message, Exception? exception = null)
        => Entries.Add($"ERROR:{source}:{message}");
}
