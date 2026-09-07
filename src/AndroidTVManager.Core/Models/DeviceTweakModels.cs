using AndroidTVManager.Core.Abstractions;

namespace AndroidTVManager.Core.Models;

public sealed record AnimationSettings(string Serial, IReadOnlyDictionary<string, string> Values, string? BlockReason);

public interface IDeviceTweakService
{
    Task<AnimationSettings> ReadAsync(string serial, CancellationToken cancellationToken = default);
    Task<ScriptExecutionResult> ApplyAnimationsAsync(AndroidDevice device, string scale, CancellationToken cancellationToken = default);
    Task<ScriptUndoResult> UndoAsync(long executionId, string serial, CancellationToken cancellationToken = default);
}
