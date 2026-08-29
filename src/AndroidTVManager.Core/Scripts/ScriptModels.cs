using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Core.Scripts;

public sealed class ScriptDefinition
{
    public int SchemaVersion { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<SupportedDevice> SupportedDevices { get; init; } = [];
    public List<ScriptAction> Actions { get; init; } = [];
}

public sealed class SupportedDevice
{
    public string? Manufacturer { get; init; }
    public string? ModelContains { get; init; }
}

public sealed class ScriptAction
{
    public string Type { get; init; } = string.Empty;
    public string? Package { get; init; }
    public string? Path { get; init; }
    public string? Value { get; init; }
    public bool Reversible { get; init; }
    public bool IsAdvanced => Type.Equals("shell", StringComparison.OrdinalIgnoreCase);
}

public sealed record ScriptValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public static class ScriptDefinitionParser
{
    private static readonly HashSet<string> ActionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "disablePackage", "enablePackage", "uninstallUser", "restorePackage",
        "installApk", "clearData", "launchPackage", "forceStop",
        "grantPermission", "revokePermission", "setSetting", "pushFile",
        "pullFile", "deleteFile", "reboot", "shell"
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static ScriptDefinition Parse(string json)
    {
        var definition = JsonSerializer.Deserialize<ScriptDefinition>(json, Options)
            ?? throw new JsonException("The script is empty.");
        var validation = Validate(definition);
        if (!validation.IsValid)
            throw new JsonException(string.Join(" ", validation.Errors));
        return definition;
    }

    public static ScriptValidationResult Validate(ScriptDefinition? script)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (script is null)
            return new(false, ["Script content is missing."], []);
        if (script.SchemaVersion != 1)
            errors.Add($"Unsupported script schema version: {script.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(script.Name))
            errors.Add("A script name is required.");
        if (script.Actions.Count == 0)
            errors.Add("A script must contain at least one action.");

        for (var i = 0; i < script.Actions.Count; i++)
        {
            var action = script.Actions[i];
            if (!ActionTypes.Contains(action.Type))
                errors.Add($"Action {i + 1} has an unknown type: {action.Type}.");
            if (RequiresPackage(action.Type)
                && string.IsNullOrWhiteSpace(action.Package))
                errors.Add($"Action {i + 1} requires a package.");
            if (RequiresPath(action.Type) && string.IsNullOrWhiteSpace(action.Path))
                errors.Add($"Action {i + 1} requires a path.");
            if (action.Type.Equals("setSetting", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(action.Value))
                errors.Add($"Action {i + 1} requires a namespace:key=value setting value.");
            if (action.Type.Equals("shell", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(action.Value))
                errors.Add($"Action {i + 1} requires a shell command.");
            if ((action.Type.Equals("grantPermission", StringComparison.OrdinalIgnoreCase)
                 || action.Type.Equals("revokePermission", StringComparison.OrdinalIgnoreCase))
                && string.IsNullOrWhiteSpace(action.Value))
                errors.Add($"Action {i + 1} requires a permission value.");
            if ((action.Type.Equals("pushFile", StringComparison.OrdinalIgnoreCase)
                 || action.Type.Equals("pullFile", StringComparison.OrdinalIgnoreCase))
                && string.IsNullOrWhiteSpace(action.Value))
                errors.Add($"Action {i + 1} requires a destination path.");
            if (action.IsAdvanced)
                warnings.Add($"Action {i + 1} is an advanced raw shell action.");
            if (action.Type.Equals("clearData", StringComparison.OrdinalIgnoreCase)
                || action.Type.Equals("uninstallUser", StringComparison.OrdinalIgnoreCase))
                warnings.Add($"Action {i + 1} is destructive and should be reviewed before execution.");
        }

        return new(errors.Count == 0, errors, warnings);
    }

    private static bool RequiresPackage(string type)
        => type.Equals("disablePackage", StringComparison.OrdinalIgnoreCase)
           || type.Equals("enablePackage", StringComparison.OrdinalIgnoreCase)
           || type.Equals("uninstallUser", StringComparison.OrdinalIgnoreCase)
           || type.Equals("restorePackage", StringComparison.OrdinalIgnoreCase)
           || type.Equals("clearData", StringComparison.OrdinalIgnoreCase)
           || type.Equals("launchPackage", StringComparison.OrdinalIgnoreCase)
           || type.Equals("forceStop", StringComparison.OrdinalIgnoreCase)
           || type.Equals("grantPermission", StringComparison.OrdinalIgnoreCase)
           || type.Equals("revokePermission", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresPath(string type)
        => type.Equals("installApk", StringComparison.OrdinalIgnoreCase)
           || type.Equals("pushFile", StringComparison.OrdinalIgnoreCase)
           || type.Equals("pullFile", StringComparison.OrdinalIgnoreCase)
           || type.Equals("deleteFile", StringComparison.OrdinalIgnoreCase);

    public static bool IsCompatible(ScriptDefinition script, AndroidDevice device)
    {
        if (script.SupportedDevices.Count == 0)
            return true;

        return script.SupportedDevices.Any(rule =>
            (string.IsNullOrWhiteSpace(rule.Manufacturer)
             || string.Equals(rule.Manufacturer, device.Manufacturer, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(rule.ModelContains)
                || (device.Model?.Contains(rule.ModelContains, StringComparison.OrdinalIgnoreCase) ?? false)));
    }
}
