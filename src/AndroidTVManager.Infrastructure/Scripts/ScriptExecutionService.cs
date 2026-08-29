using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Core.Scripts;

namespace AndroidTVManager.Infrastructure.Scripts;

public sealed class ScriptExecutionService : IScriptExecutionService
{
    private readonly IAdbProcessRunner _runner;
    private readonly IScriptExecutionStore _store;

    public ScriptExecutionService(IAdbProcessRunner runner, IScriptExecutionStore store)
    {
        _runner = runner;
        _store = store;
    }

    public async Task<ScriptExecutionResult> ExecuteAsync(
        ScriptDefinition script,
        AndroidDevice target,
        CancellationToken cancellationToken = default)
    {
        var validation = ScriptDefinitionParser.Validate(script);
        if (!validation.IsValid)
            throw new InvalidOperationException(string.Join(" ", validation.Errors));
        if (!ScriptDefinitionParser.IsCompatible(script, target))
            throw new InvalidOperationException("The script does not declare compatibility with the selected device.");

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(script))));
        var executionId = await _store.CreateAsync(target.Serial, script.Name, hash, cancellationToken);
        var succeeded = 0;
        var failed = 0;
        var canUndo = false;

        try
        {
            foreach (var (action, index) in script.Actions.Select((action, index) => (action, index)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var previous = await CapturePreviousStateAsync(target.Serial, action, cancellationToken);
                var result = await ExecuteActionAsync(target.Serial, action, cancellationToken);
                var reversible = result.IsSuccess && action.Reversible && previous is not null
                    && IsUndoSupported(action.Type);
                var actionId = await _store.AddActionAsync(executionId, new ScriptActionRecord(
                    0,
                    index,
                    action.Type,
                    ActionTarget(action),
                    previous,
                    action.Value ?? action.Package,
                    result.IsSuccess ? "completed" : "unchanged",
                    RedactOutput(result.StandardOutput, result.StandardError),
                    result.IsSuccess,
                    reversible,
                    null), cancellationToken);

                if (result.IsSuccess)
                {
                    succeeded++;
                    canUndo |= reversible;
                }
                else
                {
                    failed++;
                    if (actionId < 0)
                        break;
                    await _store.CompleteAsync(executionId, "Failed", cancellationToken);
                    return new(executionId, "Failed", succeeded, failed, canUndo);
                }
            }

            var status = failed == 0 ? "Succeeded" : "PartiallyFailed";
            await _store.CompleteAsync(executionId, status, cancellationToken);
            return new(executionId, status, succeeded, failed, canUndo);
        }
        catch (OperationCanceledException)
        {
            await _store.CompleteAsync(executionId, "Canceled", CancellationToken.None);
            throw;
        }
        catch
        {
            await _store.CompleteAsync(executionId, failed > 0 ? "PartiallyFailed" : "Failed", CancellationToken.None);
            throw;
        }
    }

    public async Task<ScriptUndoResult> UndoAsync(
        long executionId,
        string serial,
        CancellationToken cancellationToken = default)
    {
        var execution = await _store.GetAsync(executionId, cancellationToken)
            ?? throw new InvalidOperationException("The script execution no longer exists.");
        if (!string.Equals(execution.Serial, serial, StringComparison.Ordinal))
            throw new InvalidOperationException("Undo target serial does not match the original execution target.");
        if (execution.Actions.Any(action => string.Equals(action.UndoStatus, "Succeeded", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This execution has already been undone.");

        var restored = 0;
        var failed = 0;
        foreach (var action in execution.Actions
                     .Where(action => action.Success && action.Reversible && action.PreviousState is not null)
                     .Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await UndoActionAsync(serial, action, cancellationToken);
            if (result.IsSuccess)
            {
                restored++;
                await _store.SetUndoStatusAsync(action.Id, "Succeeded", cancellationToken);
            }
            else
            {
                failed++;
                await _store.SetUndoStatusAsync(action.Id, $"Failed: {RedactOutput(result.StandardOutput, result.StandardError)}", cancellationToken);
            }
        }

        var status = failed == 0 ? "Undone" : restored > 0 ? "UndoPartiallyFailed" : "UndoFailed";
        await _store.SetExecutionStatusAsync(executionId, status, cancellationToken);
        return new(executionId, status, restored, failed);
    }

    private async Task<string?> CapturePreviousStateAsync(
        string serial,
        ScriptAction action,
        CancellationToken cancellationToken)
    {
        if (action.Type.Equals("disablePackage", StringComparison.OrdinalIgnoreCase)
            || action.Type.Equals("enablePackage", StringComparison.OrdinalIgnoreCase))
        {
            var disabled = await _runner.RunForDeviceAsync(serial, ["shell", "pm", "list", "packages", "-d"],
                TimeSpan.FromSeconds(30), cancellationToken);
            if (!disabled.IsSuccess)
                return null;
            return disabled.StandardOutput.Contains($"package:{action.Package}", StringComparison.OrdinalIgnoreCase)
                ? "disabled"
                : "enabled";
        }

        if (action.Type.Equals("uninstallUser", StringComparison.OrdinalIgnoreCase)
            || action.Type.Equals("restorePackage", StringComparison.OrdinalIgnoreCase))
        {
            var packages = await _runner.RunForDeviceAsync(serial, ["shell", "pm", "list", "packages", "-u"],
                TimeSpan.FromSeconds(30), cancellationToken);
            return packages.IsSuccess
                ? packages.StandardOutput.Contains($"package:{action.Package}", StringComparison.OrdinalIgnoreCase)
                    ? "installed"
                    : "missing"
                : null;
        }

        if (action.Type.Equals("setSetting", StringComparison.OrdinalIgnoreCase))
        {
            var setting = ParseSetting(action.Value);
            if (setting is null)
                return null;
            var result = await _runner.RunForDeviceAsync(serial,
                ["shell", "settings", "get", setting.Value.Namespace, setting.Value.Key],
                TimeSpan.FromSeconds(30), cancellationToken);
            return result.IsSuccess ? $"{setting.Value.Namespace}|{result.StandardOutput.Trim()}" : null;
        }

        return null;
    }

    private Task<AdbCommandResult> ExecuteActionAsync(
        string serial,
        ScriptAction action,
        CancellationToken cancellationToken)
    {
        var type = action.Type.ToLowerInvariant();
        return type switch
        {
            "disablepackage" => _runner.RunForDeviceAsync(serial, ["shell", "pm", "disable-user", "--user", "0", action.Package!], cancellationToken: cancellationToken),
            "enablepackage" => _runner.RunForDeviceAsync(serial, ["shell", "pm", "enable", action.Package!], cancellationToken: cancellationToken),
            "uninstalluser" => _runner.RunForDeviceAsync(serial, ["shell", "pm", "uninstall", "--user", "0", action.Package!], cancellationToken: cancellationToken),
            "restorepackage" => _runner.RunForDeviceAsync(serial, ["shell", "cmd", "package", "install-existing", action.Package!], cancellationToken: cancellationToken),
            "clear data" => _runner.RunForDeviceAsync(serial, ["shell", "pm", "clear", action.Package!], cancellationToken: cancellationToken),
            "cleardata" => _runner.RunForDeviceAsync(serial, ["shell", "pm", "clear", action.Package!], cancellationToken: cancellationToken),
            "launchpackage" => _runner.RunForDeviceAsync(serial, ["shell", "monkey", "-p", action.Package!, "1"], cancellationToken: cancellationToken),
            "forcestop" => _runner.RunForDeviceAsync(serial, ["shell", "am", "force-stop", action.Package!], cancellationToken: cancellationToken),
            "grantpermission" => _runner.RunForDeviceAsync(serial, ["shell", "pm", "grant", action.Package!, action.Value!], cancellationToken: cancellationToken),
            "revokepermission" => _runner.RunForDeviceAsync(serial, ["shell", "pm", "revoke", action.Package!, action.Value!], cancellationToken: cancellationToken),
            "setsetting" => ExecuteSettingAsync(serial, action, cancellationToken),
            "installapk" => _runner.RunForDeviceAsync(serial, ["install", action.Path!], TimeSpan.FromMinutes(5), cancellationToken),
            "pushfile" => _runner.RunForDeviceAsync(serial, ["push", action.Path!, action.Value!], TimeSpan.FromMinutes(5), cancellationToken),
            "pullfile" => _runner.RunForDeviceAsync(serial, ["pull", action.Path!, action.Value!], TimeSpan.FromMinutes(5), cancellationToken),
            "deletefile" => _runner.RunForDeviceAsync(serial, ["shell", "rm", action.Path!], cancellationToken: cancellationToken),
            "reboot" => _runner.RunForDeviceAsync(serial, string.IsNullOrWhiteSpace(action.Value) ? ["reboot"] : ["reboot", action.Value], cancellationToken: cancellationToken),
            "shell" => _runner.RunForDeviceAsync(serial, ["shell", action.Value ?? string.Empty], TimeSpan.FromMinutes(5), cancellationToken),
            _ => Task.FromResult(new AdbCommandResult("adb.exe", [], 2, string.Empty, $"Unsupported script action: {action.Type}", TimeSpan.Zero))
        };
    }

    private async Task<AdbCommandResult> UndoActionAsync(
        string serial,
        ScriptActionRecord action,
        CancellationToken cancellationToken)
    {
        return action.ActionType.ToLowerInvariant() switch
        {
            "disablepackage" when action.PreviousState == "enabled"
                => await _runner.RunForDeviceAsync(serial, ["shell", "pm", "enable", action.Target!], cancellationToken: cancellationToken),
            "enablepackage" when action.PreviousState == "disabled"
                => await _runner.RunForDeviceAsync(serial, ["shell", "pm", "disable-user", "--user", "0", action.Target!], cancellationToken: cancellationToken),
            "uninstalluser" when action.PreviousState == "installed"
                => await _runner.RunForDeviceAsync(serial, ["shell", "cmd", "package", "install-existing", action.Target!], cancellationToken: cancellationToken),
            "restorepackage" when action.PreviousState == "missing"
                => await _runner.RunForDeviceAsync(serial, ["shell", "pm", "uninstall", "--user", "0", action.Target!], cancellationToken: cancellationToken),
            "setsetting" => await UndoSettingAsync(serial, action, cancellationToken),
            _ => new AdbCommandResult("adb.exe", [], 2, string.Empty, "Previous state cannot be restored for this action.", TimeSpan.Zero)
        };
    }

    private async Task<AdbCommandResult> ExecuteSettingAsync(
        string serial,
        ScriptAction action,
        CancellationToken cancellationToken)
    {
        var setting = ParseSetting(action.Value);
        if (setting is null)
            return new AdbCommandResult("adb.exe", [], 2, string.Empty,
                "Setting must be namespace:key=value.", TimeSpan.Zero);
        return await _runner.RunForDeviceAsync(serial,
            ["shell", "settings", "put", setting.Value.Namespace, setting.Value.Key, setting.Value.Value],
            cancellationToken: cancellationToken);
    }

    private async Task<AdbCommandResult> UndoSettingAsync(
        string serial,
        ScriptActionRecord action,
        CancellationToken cancellationToken)
    {
        var separator = action.PreviousState!.IndexOf('|');
        if (separator <= 0)
            return new AdbCommandResult("adb.exe", [], 2, string.Empty, "Previous setting state is invalid.", TimeSpan.Zero);
        var ns = action.PreviousState[..separator];
        var previous = action.PreviousState[(separator + 1)..];
        return previous.Equals("null", StringComparison.OrdinalIgnoreCase)
            ? await _runner.RunForDeviceAsync(serial, ["shell", "settings", "delete", ns, action.Target!], cancellationToken: cancellationToken)
            : await _runner.RunForDeviceAsync(serial, ["shell", "settings", "put", ns, action.Target!, previous], cancellationToken: cancellationToken);
    }

    private static (string Namespace, string Key, string Value)? ParseSetting(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var equals = value.IndexOf('=');
        var colon = value.IndexOf(':');
        return colon > 0 && equals > colon
            ? (value[..colon], value[(colon + 1)..equals], value[(equals + 1)..])
            : null;
    }

    private static bool IsUndoSupported(string actionType)
        => actionType.Equals("disablePackage", StringComparison.OrdinalIgnoreCase)
           || actionType.Equals("enablePackage", StringComparison.OrdinalIgnoreCase)
           || actionType.Equals("uninstallUser", StringComparison.OrdinalIgnoreCase)
           || actionType.Equals("restorePackage", StringComparison.OrdinalIgnoreCase)
           || actionType.Equals("setSetting", StringComparison.OrdinalIgnoreCase);

    private static string? ActionTarget(ScriptAction action)
    {
        if (action.Type.Equals("setSetting", StringComparison.OrdinalIgnoreCase)
            && ParseSetting(action.Value) is { } setting)
            return setting.Key;
        return action.Package ?? action.Path ?? action.Value;
    }

    private static string RedactOutput(string stdout, string stderr)
        => $"{stdout}\n{stderr}".Replace("\r", string.Empty).Trim();
}
