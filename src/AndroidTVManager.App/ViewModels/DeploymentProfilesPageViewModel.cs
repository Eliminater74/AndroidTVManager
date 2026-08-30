using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using AndroidTVManager.App.Services;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVManager.App.ViewModels;

public sealed partial class DeploymentProfilesPageViewModel : PageViewModel
{
    private readonly IDeploymentProfileRepository _repository;
    private readonly IDeploymentProfileStorage _storage;
    private readonly IDeploymentProfileService _deployment;
    private readonly IBulkApkService _bulkApk;
    private readonly IConfirmationService _confirmation;
    private readonly Action<AndroidDevice?> _selectTarget;
    private BulkInstallPackageSet? _pendingPackageSet;
    private CancellationTokenSource? _operationSource;

    public DeploymentProfilesPageViewModel(
        IDeploymentProfileRepository repository,
        IDeploymentProfileStorage storage,
        IDeploymentProfileService deployment,
        IBulkApkService bulkApk,
        IConfirmationService confirmation,
        ObservableCollection<AndroidDevice> devices,
        Action<AndroidDevice?> selectTarget) : base("Deployment Profiles")
    {
        _repository = repository;
        _storage = storage;
        _deployment = deployment;
        _bulkApk = bulkApk;
        _confirmation = confirmation;
        _selectTarget = selectTarget;
        Devices = devices;
        _ = LoadAsync();
    }

    public ObservableCollection<AndroidDevice> Devices { get; }
    public ObservableCollection<DeploymentProfile> Profiles { get; } = [];
    public ObservableCollection<DeploymentProfileStep> Steps { get; } = [];
    public ObservableCollection<DeploymentProfileExecution> Executions { get; } = [];
    public ObservableCollection<ApkInstallGroup> PendingPackages { get; } = [];

    [ObservableProperty]
    private DeploymentProfile? _selectedProfile;

    [ObservableProperty]
    private AndroidDevice? _selectedDevice;

    [ObservableProperty]
    private DeploymentProfileStep? _selectedStep;

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _packageName = string.Empty;

    [ObservableProperty]
    private string _status = "Create a profile to prepare a repeatable device setup.";

    [ObservableProperty]
    private bool _isBusy;

    public DeploymentCompatibility? Compatibility =>
        SelectedProfile is null || SelectedDevice is null
            ? null
            : _deployment.CheckCompatibility(SelectedProfile, SelectedDevice);

    partial void OnSelectedProfileChanged(DeploymentProfile? value)
    {
        Steps.Clear();
        Executions.Clear();
        if (value is null)
        {
            ProfileName = string.Empty;
            Description = string.Empty;
            OnPropertyChanged(nameof(Compatibility));
            return;
        }
        ProfileName = value.Name;
        Description = value.Description ?? string.Empty;
        foreach (var step in value.Steps.OrderBy(step => step.SortOrder))
            Steps.Add(step);
        _ = LoadExecutionsAsync(value.Id);
        OnPropertyChanged(nameof(Compatibility));
    }

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
    {
        _selectTarget(value);
        OnPropertyChanged(nameof(Compatibility));
    }

    [RelayCommand]
    private void NewProfile()
    {
        SelectedProfile = null;
        ProfileName = string.Empty;
        Description = string.Empty;
        Steps.Clear();
        Status = "Enter a profile name, import packages, then save the profile.";
    }

    [RelayCommand]
    private async Task ImportPackagesAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Android packages (*.apk;*.apks;*.xapk;*.apkm)|*.apk;*.apks;*.xapk;*.apkm|All files (*.*)|*.*",
            Multiselect = true,
            Title = "Add APKs or split-package archives to the profile"
        };
        if (dialog.ShowDialog() != true)
            return;
        await PreparePackagesAsync(dialog.FileNames);
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select a folder containing APKs or split-package archives.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;
        await PreparePackagesAsync([dialog.SelectedPath]);
    }

    private async Task PreparePackagesAsync(IReadOnlyList<string> paths)
    {
        try
        {
            if (_pendingPackageSet is not null)
                _bulkApk.Cleanup(_pendingPackageSet);
            _pendingPackageSet = await _bulkApk.PrepareAsync(paths);
            PendingPackages.Clear();
            foreach (var group in _pendingPackageSet.Groups)
                PendingPackages.Add(group);
            Status = $"{PendingPackages.Count} install group(s) ready to add to the profile.";
        }
        catch (Exception exception)
        {
            Status = $"Package import failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private void AddDisableStep()
    {
        AddPackageStep(DeploymentStepKind.DisablePackage, "Disable");
    }

    [RelayCommand]
    private void AddEnableStep()
    {
        AddPackageStep(DeploymentStepKind.EnablePackage, "Enable");
    }

    [RelayCommand]
    private void RemoveStep()
    {
        if (SelectedStep is not null)
            Steps.Remove(SelectedStep);
    }

    [RelayCommand]
    private void MoveStepUp()
    {
        if (SelectedStep is { } step && Steps.IndexOf(step) > 0)
        {
            var index = Steps.IndexOf(step);
            Steps.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveStepDown()
    {
        if (SelectedStep is { } step && Steps.IndexOf(step) < Steps.Count - 1)
        {
            var index = Steps.IndexOf(step);
            Steps.Move(index, index + 1);
        }
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            Status = "A profile name is required.";
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var existing = SelectedProfile;
        var profile = new DeploymentProfile(
            existing?.Id ?? 0,
            ProfileName.Trim(),
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            1,
            existing?.CreatedUtc ?? now,
            now,
            Steps.Select((step, index) => step with { SortOrder = index }).ToArray(),
            existing?.Assets);

        IsBusy = true;
        try
        {
            var id = await _repository.UpsertAsync(profile);
            var assets = (existing?.Assets ?? []).ToList();
            var newSteps = new List<DeploymentProfileStep>(profile.Steps);
            if (_pendingPackageSet is not null)
            {
                var imported = await ImportAssetsAsync(id, _pendingPackageSet, assets);
                assets = imported.Assets;
                await _repository.UpsertAsync(profile with { Id = id, Assets = assets, Steps = [] });
                var persistedAssets = (await _repository.GetAsync(id))?.Assets ?? [];
                foreach (var group in _pendingPackageSet.Groups)
                {
                    var ids = group.Artifacts
                        .Select(artifact => persistedAssets
                            .First(asset => string.Equals(
                                asset.Sha256,
                                imported.ByPath[artifact.Path].Sha256,
                                StringComparison.OrdinalIgnoreCase)).Id)
                        .ToArray();
                    newSteps.Add(new(
                        0,
                        newSteps.Count,
                        DeploymentStepKind.InstallApk,
                        group.DisplayName,
                        AssetIds: ids));
                }
                _bulkApk.Cleanup(_pendingPackageSet);
                _pendingPackageSet = null;
                PendingPackages.Clear();
            }

            profile = profile with
            {
                Id = id,
                Steps = newSteps.Select((step, index) => step with { SortOrder = index }).ToArray(),
                Assets = assets
            };
            await _repository.UpsertAsync(profile);
            await LoadProfilesAsync(id);
            Status = $"Saved profile '{profile.Name}' with {profile.Steps.Count} step(s).";
        }
        catch (Exception exception)
        {
            Status = $"Profile save failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is not { } profile)
            return;
        if (!_confirmation.Confirm(
                "Delete deployment profile",
                $"Delete '{profile.Name}' and its copied APK assets? Device history and previous executions will not be changed."))
            return;
        await _repository.DeleteAsync(profile.Id);
        await _storage.DeleteProfileFilesAsync(profile.Id);
        Profiles.Remove(profile);
        SelectedProfile = null;
        Status = "Deployment profile deleted.";
    }

    [RelayCommand]
    private async Task DeployAsync()
    {
        if (SelectedProfile is null || SelectedDevice is null)
        {
            Status = "Select a saved profile and a connected device.";
            return;
        }
        var compatibility = Compatibility!;
        if (compatibility.State == DeploymentCompatibilityState.Incompatible
            || (compatibility.State == DeploymentCompatibilityState.Warning
                && !_confirmation.Confirm(
                    "Deployment compatibility warning",
                    string.Join(Environment.NewLine, compatibility.Reasons))))
            return;
        if (!_confirmation.Confirm(
                $"Deploy {SelectedProfile.Name}",
                $"Deploy to {SelectedDevice.FriendlyName ?? SelectedDevice.Serial}?\n\n"
                + "Deployment is sequential and may be partial if a step fails. Existing installed apps will not be automatically uninstalled."))
            return;

        var target = SelectedDevice;
        IsBusy = true;
        _operationSource = new CancellationTokenSource();
        try
        {
            var progress = new Progress<DeploymentProfileStepResult>(result =>
                Status = $"{result.Status}: {result.Step.DisplayName}");
            var result = await _deployment.DeployAsync(
                SelectedProfile,
                target,
                progress,
                _operationSource.Token);
            Status = $"Deployment {result.Execution.Status.ToLowerInvariant()}: "
                + $"{result.Steps.Count(step => step.Status == "Succeeded")} succeeded, "
                + $"{result.Steps.Count(step => step.Status == "Failed")} failed.";
            await LoadExecutionsAsync(SelectedProfile.Id);
        }
        catch (OperationCanceledException)
        {
            Status = "Deployment canceled; completed device changes were not automatically undone.";
        }
        catch (Exception exception)
        {
            Status = $"Deployment failed: {exception.Message}";
        }
        finally
        {
            _operationSource.Dispose();
            _operationSource = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
        => _operationSource?.Cancel();

    private void AddPackageStep(DeploymentStepKind kind, string verb)
    {
        if (string.IsNullOrWhiteSpace(PackageName))
        {
            Status = "Enter a package name first.";
            return;
        }
        Steps.Add(new(
            0,
            Steps.Count,
            kind,
            $"{verb} {PackageName.Trim()}",
            PackageName: PackageName.Trim()));
        PackageName = string.Empty;
    }

    private async Task<(List<DeploymentProfileAsset> Assets, Dictionary<string, DeploymentProfileAsset> ByPath)> ImportAssetsAsync(
        long profileId,
        BulkInstallPackageSet packageSet,
        List<DeploymentProfileAsset> existing)
    {
        var assets = existing.ToList();
        var byPath = new Dictionary<string, DeploymentProfileAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in packageSet.Groups.SelectMany(group => group.Artifacts))
        {
            var hash = await HashAsync(artifact.Path);
            var known = assets.FirstOrDefault(asset =>
                string.Equals(asset.Sha256, hash, StringComparison.OrdinalIgnoreCase));
            if (known is null)
            {
                var storedName = $"{hash[..12]}-{SafeFileName(artifact.FileName)}";
                var stored = await _storage.CopyPackageAsync(profileId, artifact.Path, storedName);
                known = new(
                    0,
                    profileId,
                    hash,
                    artifact.FileName,
                    stored,
                    artifact.SizeBytes,
                    artifact.ContainerKind,
                    artifact.PackageName,
                    artifact.VersionName,
                    artifact.VersionCode,
                    DateTimeOffset.UtcNow);
                assets.Add(known);
            }
            byPath[artifact.Path] = known;
        }
        return (assets, byPath);
    }

    private async Task LoadAsync()
    {
        await LoadProfilesAsync();
        SelectedDevice = Devices.FirstOrDefault(device => device.State == DeviceState.Device);
    }

    private async Task LoadProfilesAsync(long? selectId = null)
    {
        var profiles = await _repository.GetAllAsync();
        Profiles.Clear();
        foreach (var profile in profiles)
            Profiles.Add(profile);
        SelectedProfile = selectId is { } id
            ? Profiles.FirstOrDefault(profile => profile.Id == id)
            : Profiles.FirstOrDefault();
    }

    private async Task LoadExecutionsAsync(long profileId)
    {
        var executions = await _repository.GetExecutionsAsync(profileId);
        Executions.Clear();
        foreach (var execution in executions)
            Executions.Add(execution);
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character));
    }
}
