using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Core.Scripts;

namespace AndroidTVManager.Infrastructure.Packages;

public sealed class DeploymentProfileService : IDeploymentProfileService
{
    private readonly IDeploymentProfileRepository _repository;
    private readonly IDeploymentProfileStorage _storage;
    private readonly IApkInstaller _installer;
    private readonly IPackageManager _packageManager;
    private readonly IScriptExecutionService _scripts;

    public DeploymentProfileService(
        IDeploymentProfileRepository repository,
        IDeploymentProfileStorage storage,
        IApkInstaller installer,
        IPackageManager packageManager,
        IScriptExecutionService scripts)
    {
        _repository = repository;
        _storage = storage;
        _installer = installer;
        _packageManager = packageManager;
        _scripts = scripts;
    }

    public DeploymentCompatibility CheckCompatibility(
        DeploymentProfile profile,
        AndroidDevice device)
    {
        var reasons = new List<string>();
        var unknown = false;
        Check(profile.Manufacturer, device.Manufacturer, "manufacturer", reasons, ref unknown);
        Check(profile.Brand, device.Brand, "brand", reasons, ref unknown);
        Check(profile.Model, device.Model, "model", reasons, ref unknown);
        Check(profile.Product, device.Product, "product", reasons, ref unknown);
        Check(profile.Device, device.DeviceName, "device codename", reasons, ref unknown);
        Check(profile.BuildFingerprintPrefix, device.BuildFingerprint, "build fingerprint", reasons, ref unknown);
        if (profile.MinimumApiLevel is { } minimum)
        {
            if (device.ApiLevel is null)
                unknown = true;
            else if (device.ApiLevel < minimum)
                reasons.Add($"API level {device.ApiLevel} is below the minimum {minimum}.");
        }
        if (profile.MaximumApiLevel is { } maximum)
        {
            if (device.ApiLevel is null)
                unknown = true;
            else if (device.ApiLevel > maximum)
                reasons.Add($"API level {device.ApiLevel} is above the maximum {maximum}.");
        }
        if (!string.IsNullOrWhiteSpace(profile.Abi))
        {
            unknown = true;
            reasons.Add($"ABI requirement '{profile.Abi}' cannot be verified from the current device metadata.");
        }
        if (profile.RequiresAndroidTv is not null || profile.RequiresGoogleTv is not null)
        {
            unknown = true;
            reasons.Add("TV platform feature requirements need an inspection result before deployment.");
        }
        if (reasons.Any(reason => reason.Contains("below", StringComparison.OrdinalIgnoreCase)
                                 || reason.Contains("above", StringComparison.OrdinalIgnoreCase)
                                 || reason.Contains("does not match", StringComparison.OrdinalIgnoreCase)))
            return new(DeploymentCompatibilityState.Incompatible, reasons);
        return new(
            unknown ? DeploymentCompatibilityState.Warning : DeploymentCompatibilityState.Compatible,
            reasons);
    }

    public async Task<DeploymentProfileDeploymentResult> DeployAsync(
        DeploymentProfile profile,
        AndroidDevice device,
        IProgress<DeploymentProfileStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var serial = device.Serial.Trim();
        if (device.State != DeviceState.Device || string.IsNullOrWhiteSpace(serial))
            throw new InvalidOperationException("The deployment target is not connected and authorized.");
        var compatibility = CheckCompatibility(profile, device);
        if (compatibility.State == DeploymentCompatibilityState.Incompatible)
            throw new InvalidOperationException(string.Join(" ", compatibility.Reasons));

        var executionId = await _repository.StartExecutionAsync(profile.Id, profile.Name, serial, cancellationToken);
        var results = new List<DeploymentProfileStepResult>();
        try
        {
            foreach (var step in profile.Steps.OrderBy(step => step.SortOrder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await ExecuteStepAsync(profile, step, device, serial, cancellationToken);
                results.Add(result);
                progress?.Report(result);
                await _repository.RecordExecutionStepAsync(
                    executionId,
                    new(
                        0,
                        executionId,
                        step.Id,
                        step.SortOrder,
                        result.Status,
                        result.CommandResult?.StandardOutput ?? result.ErrorMessage,
                        step.Kind is DeploymentStepKind.DisablePackage or DeploymentStepKind.EnablePackage,
                        null),
                    cancellationToken);
                if (result.Status == "Failed" && !step.IsOptional)
                    break;
            }
            var status = results.Any(result => result.Status == "Failed")
                ? results.Any(result => result.Status == "Succeeded") ? "Partial" : "Failed"
                : "Succeeded";
            await _repository.CompleteExecutionAsync(executionId, status, null, cancellationToken);
            return new(
                new(executionId, profile.Id, profile.Name, serial, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, status, null),
                results,
                false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _repository.CompleteExecutionAsync(executionId, "Canceled", "Canceled by user.", CancellationToken.None);
            return new(
                new(executionId, profile.Id, profile.Name, serial, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Canceled", "Canceled by user."),
                results,
                true);
        }
        catch (Exception exception)
        {
            await _repository.CompleteExecutionAsync(executionId, "Failed", exception.Message, CancellationToken.None);
            throw;
        }
    }

    private async Task<DeploymentProfileStepResult> ExecuteStepAsync(
        DeploymentProfile profile,
        DeploymentProfileStep step,
        AndroidDevice device,
        string serial,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = step.Kind switch
            {
                DeploymentStepKind.InstallApk => await InstallStepAsync(profile, step, serial, cancellationToken),
                DeploymentStepKind.DisablePackage => await _packageManager.DisableAsync(
                    serial, RequirePackage(step), cancellationToken),
                DeploymentStepKind.EnablePackage => await _packageManager.EnableAsync(
                    serial, RequirePackage(step), cancellationToken),
                DeploymentStepKind.RunScript => await RunScriptAsync(step, device, cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported deployment step: {step.Kind}.")
            };
            return new(step, result.IsSuccess ? "Succeeded" : "Failed", result,
                result.IsSuccess ? null : FirstLine(result.StandardError));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(step, "Failed", null, exception.Message);
        }
    }

    private async Task<AdbCommandResult> InstallStepAsync(
        DeploymentProfile profile,
        DeploymentProfileStep step,
        string serial,
        CancellationToken cancellationToken)
    {
        var paths = (step.AssetIds ?? [])
            .Select(id => profile.Assets?.FirstOrDefault(asset => asset.Id == id))
            .Where(asset => asset is not null)
            .Select(asset => _storage.GetPackagePath(profile.Id, asset!.StoredFileName))
            .ToArray();
        if (paths.Length == 0 && !string.IsNullOrWhiteSpace(step.RelativePath))
            paths = [_storage.GetPackagePath(profile.Id, step.RelativePath)];
        if (paths.Length == 0 || paths.Any(path => !File.Exists(path)))
            throw new FileNotFoundException($"No stored APK asset was found for '{step.DisplayName}'.");
        return paths.Length == 1
            ? await _installer.InstallAsync(serial, paths[0], cancellationToken: cancellationToken)
            : await _installer.InstallMultipleAsync(serial, paths, cancellationToken: cancellationToken);
    }

    private async Task<AdbCommandResult> RunScriptAsync(
        DeploymentProfileStep step,
        AndroidDevice device,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(step.ScriptJson))
            throw new InvalidOperationException($"Script step '{step.DisplayName}' has no script content.");
        var script = ScriptDefinitionParser.Parse(step.ScriptJson);
        var result = await _scripts.ExecuteAsync(script, device, cancellationToken);
        return new(
            "deployment-script",
            [],
            result.FailedActions == 0 ? 0 : 1,
            $"{result.SuccessfulActions} action(s) succeeded; {result.FailedActions} failed.",
            string.Empty,
            TimeSpan.Zero);
    }

    private static string RequirePackage(DeploymentProfileStep step)
        => string.IsNullOrWhiteSpace(step.PackageName)
            ? throw new InvalidOperationException($"Package step '{step.DisplayName}' has no package name.")
            : step.PackageName;

    private static void Check(
        string? expected,
        string? actual,
        string label,
        ICollection<string> reasons,
        ref bool unknown)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return;
        if (string.IsNullOrWhiteSpace(actual))
        {
            unknown = true;
            reasons.Add($"The device did not report a {label}.");
        }
        else if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            reasons.Add($"Device {label} '{actual}' does not match '{expected}'.");
    }

    private static string FirstLine(string value)
        => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "ADB command failed.";
}
