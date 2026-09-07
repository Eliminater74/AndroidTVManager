using AndroidTVManager.Core.Scripts;
using AndroidTVManager.Infrastructure.Scripts;
using AndroidTVManager.Tests.TestDoubles;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class ScriptPackageStateTests
{
    [Theory]
    [InlineData("disablePackage", "package:com.example.app.extra", "enabled")]
    [InlineData("enablePackage", "package:com.example.app", "disabled")]
    [InlineData("uninstallUser", "package:com.example.app.extra", "missing")]
    [InlineData("restorePackage", "package:com.example.app", "installed")]
    public async Task Journal_matches_exact_package_identity_in_user_zero(string type, string output, string expected)
    {
        var runner = new FakeAdbProcessRunner();
        runner.Responses["shell pm list packages -d --user 0"] = new("adb.exe", [], 0, output, "", TimeSpan.Zero);
        runner.Responses["shell pm list packages --user 0"] = new("adb.exe", [], 0, output, "", TimeSpan.Zero);
        var store = new CapturingStore();
        await new ScriptExecutionService(runner, store).ExecuteAsync(new()
        {
            SchemaVersion = 1, Name = "Package state test",
            Actions = [new() { Type = type, Package = "com.example.app", Reversible = true }]
        }, new() { Serial = "tablet" });
        store.Previous.Should().Be(expected);
        runner.Calls.Should().OnlyContain(call => call.Serial == "tablet"
            && string.Join(" ", call.Arguments).Contains("--user 0"));
    }

    private sealed class CapturingStore : IScriptExecutionStore
    {
        public string? Previous { get; private set; }
        public Task<long> CreateAsync(string serial, string scriptName, string? scriptHash, CancellationToken cancellationToken = default) => Task.FromResult(1L);
        public Task<long> AddActionAsync(long executionId, ScriptActionRecord action, CancellationToken cancellationToken = default)
        { Previous = action.PreviousState; return Task.FromResult(1L); }
        public Task UpdateActionAsync(long actionId, bool success, bool reversible, string? resultingState, string? output, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CompleteAsync(long executionId, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ScriptExecutionRecord?> GetAsync(long executionId, CancellationToken cancellationToken = default) => Task.FromResult<ScriptExecutionRecord?>(null);
        public Task SetUndoStatusAsync(long actionId, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetExecutionStatusAsync(long executionId, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
