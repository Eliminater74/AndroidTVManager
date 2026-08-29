namespace AndroidTVManager.Core.Scripts;

public sealed record ScriptActionRecord(
    long Id,
    int ActionIndex,
    string ActionType,
    string? Target,
    string? PreviousState,
    string? RequestedState,
    string? ResultingState,
    string? Output,
    bool Success,
    bool Reversible,
    string? UndoStatus);

public sealed record ScriptExecutionRecord(
    long Id,
    long DeviceId,
    string Serial,
    string ScriptName,
    string? ScriptHash,
    DateTimeOffset StartedUtc,
    DateTimeOffset? EndedUtc,
    string Status,
    IReadOnlyList<ScriptActionRecord> Actions);

public interface IScriptExecutionStore
{
    Task<long> CreateAsync(
        string serial,
        string scriptName,
        string? scriptHash,
        CancellationToken cancellationToken = default);

    Task<long> AddActionAsync(
        long executionId,
        ScriptActionRecord action,
        CancellationToken cancellationToken = default);

    Task UpdateActionAsync(
        long actionId,
        bool success,
        bool reversible,
        string? resultingState,
        string? output,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(long executionId, string status, CancellationToken cancellationToken = default);
    Task<ScriptExecutionRecord?> GetAsync(long executionId, CancellationToken cancellationToken = default);
    Task SetUndoStatusAsync(long actionId, string status, CancellationToken cancellationToken = default);
    Task SetExecutionStatusAsync(long executionId, string status, CancellationToken cancellationToken = default);
}
