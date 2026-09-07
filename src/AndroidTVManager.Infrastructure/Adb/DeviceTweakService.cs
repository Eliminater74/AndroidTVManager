using System.Globalization;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Core.Scripts;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class DeviceTweakService(IAdbProcessRunner runner, IScriptExecutionService scripts) : IDeviceTweakService
{
    private static readonly string[] Keys = ["window_animation_scale", "transition_animation_scale", "animator_duration_scale"];

    public async Task<AnimationSettings> ReadAsync(string serial, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        var features = await runner.RunForDeviceAsync(serial, ["shell", "pm", "list", "features"], cancellationToken: cancellationToken);
        var user = await runner.RunForDeviceAsync(serial, ["shell", "am", "get-current-user"], cancellationToken: cancellationToken);
        string? blocked = !features.IsSuccess || !features.StandardOutput.Contains("feature:", StringComparison.Ordinal)
            || !user.IsSuccess || user.StandardOutput.Trim() != "0"
            ? "Tuning requires readable device features and foreground User 0."
            : features.StandardOutput.Contains("android.hardware.type.automotive", StringComparison.Ordinal)
                ? "Global tuning is unavailable on Android Automotive. Use the vehicle manufacturer's settings."
                : null;
        var values = new Dictionary<string, string>();
        foreach (var key in Keys)
        {
            var result = await runner.RunForDeviceAsync(serial, ["shell", "settings", "get", "global", key], cancellationToken: cancellationToken);
            var value = result.StandardOutput.Trim();
            if (!result.IsSuccess || (value != "null" && (!double.TryParse(value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || number < 0)))
            {
                blocked ??= "One or more animation settings could not be read. No tuning changes are allowed.";
                value = "Unavailable";
            }
            values.Add(key, value);
        }
        return new(serial, values, blocked);
    }

    public async Task<ScriptExecutionResult> ApplyAnimationsAsync(AndroidDevice device, string scale, CancellationToken cancellationToken = default)
    {
        if (scale is not ("0" or "0.5" or "1"))
            throw new ArgumentOutOfRangeException(nameof(scale));
        var current = await ReadAsync(device.Serial, cancellationToken);
        if (current.BlockReason is not null)
            throw new InvalidOperationException(current.BlockReason);
        return await scripts.ExecuteAsync(new ScriptDefinition
        {
            SchemaVersion = 1,
            Name = $"Animation scale {scale}",
            Description = "Adjust Android UI animation timing; this does not increase video frame rate or processor speed.",
            RequireCapturedState = true,
            VerifySettingWrites = true,
            Actions = Keys.Select(key => new ScriptAction { Type = "setSetting", Value = $"global:{key}={scale}", Reversible = true }).ToList()
        }, device, cancellationToken);
    }

    public Task<ScriptUndoResult> UndoAsync(long executionId, string serial, CancellationToken cancellationToken = default)
        => scripts.UndoAsync(executionId, serial, cancellationToken);
}
