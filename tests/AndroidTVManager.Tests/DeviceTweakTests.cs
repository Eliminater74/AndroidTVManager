using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Core.Scripts;
using AndroidTVManager.Infrastructure.Adb;
using AndroidTVManager.Infrastructure.Scripts;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DeviceTweakTests
{
    [Fact]
    public async Task Animation_tuning_verifies_three_values_and_restores_exact_previous_values()
    {
        var runner = new StatefulRunner();
        var previous = new Dictionary<string, string>(runner.Values);
        var service = Create(runner);
        var result = await service.ApplyAnimationsAsync(new() { Serial = "shield" }, "0.5");
        result.Status.Should().Be("Succeeded");
        result.SuccessfulActions.Should().Be(3);
        runner.Values.Values.Should().OnlyContain(value => value == "0.5");
        var undo = await service.UndoAsync(result.ExecutionId, "shield");
        undo.FailedActions.Should().Be(0);
        runner.Values.Should().BeEquivalentTo(previous);
        runner.Serials.Should().OnlyContain(serial => serial == "shield");
    }

    [Theory]
    [InlineData("10", false, false)]
    [InlineData("0", true, false)]
    [InlineData("0", false, true)]
    public async Task Unsupported_context_or_unreadable_setting_never_writes(string user, bool automotive, bool failRead)
    {
        var runner = new StatefulRunner { User = user, Automotive = automotive, FailRead = failRead };
        await FluentActions.Awaiting(() => Create(runner).ApplyAnimationsAsync(new() { Serial = "device" }, "0.5"))
            .Should().ThrowAsync<InvalidOperationException>();
        runner.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Ignored_write_is_not_reported_as_verified_and_can_be_undone()
    {
        var runner = new StatefulRunner { IgnoreWrites = true };
        var result = await Create(runner).ApplyAnimationsAsync(new() { Serial = "shield" }, "0.5");
        result.Status.Should().Be("VerificationFailed");
        result.SuccessfulActions.Should().Be(0);
        result.CanUndo.Should().BeTrue();
        runner.Writes.Should().Be(1);
    }

    [Fact]
    public async Task Capture_failure_after_preflight_prevents_the_first_write()
    {
        var runner = new StatefulRunner { FailReadAfter = 3 };
        await FluentActions.Awaiting(() => Create(runner).ApplyAnimationsAsync(new() { Serial = "shield" }, "0.5"))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("Previous state could not be captured*");
        runner.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Undo_rejects_a_different_device()
    {
        var service = Create(new StatefulRunner());
        var result = await service.ApplyAnimationsAsync(new() { Serial = "shield" }, "0");
        await FluentActions.Awaiting(() => service.UndoAsync(result.ExecutionId, "tablet"))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("Undo target serial*");
    }

    [Fact]
    public async Task Partial_undo_can_retry_only_the_failed_action()
    {
        var runner = new StatefulRunner();
        var original = new Dictionary<string, string>(runner.Values);
        var service = Create(runner);
        var result = await service.ApplyAnimationsAsync(new() { Serial = "shield" }, "0.5");
        runner.FailWriteKey = "animator_duration_scale";
        (await service.UndoAsync(result.ExecutionId, "shield")).FailedActions.Should().Be(1);
        var writes = runner.Writes;
        runner.FailWriteKey = null;
        (await service.UndoAsync(result.ExecutionId, "shield")).RestoredActions.Should().Be(1);
        runner.Writes.Should().Be(writes + 1);
        runner.Values.Should().BeEquivalentTo(original);
    }

    private static DeviceTweakService Create(StatefulRunner runner)
        => new(runner, new ScriptExecutionService(runner, new MemoryStore()));

    private sealed class StatefulRunner : IAdbProcessRunner
    {
        public Dictionary<string, string> Values { get; } = new()
        {
            ["window_animation_scale"] = "1.5", ["transition_animation_scale"] = "null", ["animator_duration_scale"] = "0"
        };
        public List<string> Serials { get; } = [];
        public string User { get; init; } = "0";
        public bool Automotive { get; init; }
        public bool FailRead { get; init; }
        public int FailReadAfter { get; init; } = int.MaxValue;
        public bool IgnoreWrites { get; init; }
        public string? FailWriteKey { get; set; }
        public int Writes { get; private set; }
        private int _reads;
        public Task<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("A device serial is required.");
        public Task<AdbCommandResult> RunForDeviceAsync(string serial, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            Serials.Add(serial);
            var output = "";
            var exit = 0;
            if (arguments.Contains("features")) output = Automotive ? "feature:android.hardware.type.automotive" : "feature:android.hardware.type.television";
            else if (arguments.Contains("get-current-user")) output = User;
            else if (arguments[1] == "settings")
            {
                var key = arguments[4];
                if (arguments[2] == "get")
                {
                    if (FailRead || ++_reads > FailReadAfter) exit = 1;
                    else output = Values[key];
                }
                else
                {
                    Writes++;
                    if (FailWriteKey == key) exit = 1;
                    else if (!IgnoreWrites) Values[key] = arguments[2] == "delete" ? "null" : arguments[5];
                }
            }
            return Task.FromResult(new AdbCommandResult("adb.exe", arguments, exit, output, exit == 0 ? "" : "denied", TimeSpan.Zero));
        }
    }

    private sealed class MemoryStore : IScriptExecutionStore
    {
        private string _serial = "";
        private readonly List<ScriptActionRecord> _actions = [];
        public Task<long> CreateAsync(string serial, string scriptName, string? scriptHash, CancellationToken cancellationToken = default)
        { _serial = serial; return Task.FromResult(1L); }
        public Task<long> AddActionAsync(long executionId, ScriptActionRecord action, CancellationToken cancellationToken = default)
        { _actions.Add(action with { Id = _actions.Count + 1 }); return Task.FromResult((long)_actions.Count); }
        public Task UpdateActionAsync(long actionId, bool success, bool reversible, string? resultingState, string? output, CancellationToken cancellationToken = default)
        { var index = (int)actionId - 1; _actions[index] = _actions[index] with { Success = success, Reversible = reversible, ResultingState = resultingState }; return Task.CompletedTask; }
        public Task CompleteAsync(long executionId, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ScriptExecutionRecord?> GetAsync(long executionId, CancellationToken cancellationToken = default)
            => Task.FromResult<ScriptExecutionRecord?>(new(1, 1, _serial, "test", null, DateTimeOffset.UtcNow, null, "Succeeded", _actions));
        public Task SetUndoStatusAsync(long actionId, string status, CancellationToken cancellationToken = default)
        { var index = (int)actionId - 1; _actions[index] = _actions[index] with { UndoStatus = status }; return Task.CompletedTask; }
        public Task SetExecutionStatusAsync(long executionId, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
