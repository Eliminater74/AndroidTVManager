using AndroidTVManager.Core.Models;
using AndroidTVManager.App.Services;
using AndroidTVManager.Core.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVManager.App.ViewModels;

public sealed partial class TweaksPageViewModel(IDeviceTweakService tweaks, IConfirmationService confirmation) : PageViewModel("Tweaks")
{
    private (long Id, string Serial)? _lastExecution;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadCommand), nameof(ApplyCommand), nameof(UndoCommand))]
    private AndroidDevice? _selectedDevice;
    [ObservableProperty] private string _selectedScale = "0.5";
    [ObservableProperty] private string _status = "Select a connected device in the header, then read its settings.";
    [ObservableProperty] private string _currentValues = "Not read yet.";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadCommand), nameof(ApplyCommand), nameof(UndoCommand))]
    private bool _isBusy;
    private bool CanUseDevice() => !IsBusy && SelectedDevice?.State == DeviceState.Device;
    private bool CanUndo() => CanUseDevice() && _lastExecution?.Serial == SelectedDevice?.Serial;
    public IReadOnlyList<string> Scales { get; } = ["0", "0.5", "1"];
    public string ShieldGuidance => DeviceFamilies.IsShieldTv(SelectedDevice ?? new())
        ? "SHIELD TV detected. Review the model-specific settings below on your television."
        : "Shield guidance applies to NVIDIA Shield TV models; availability depends on model and firmware.";

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
    {
        CurrentValues = "Not read yet.";
        OnPropertyChanged(nameof(ShieldGuidance));
    }

    [RelayCommand(CanExecute = nameof(CanUseDevice))]
    private async Task ReadAsync()
    {
        var target = SelectedDevice;
        if (IsBusy || target?.State != DeviceState.Device) return;
        IsBusy = true;
        try
        {
            var snapshot = await tweaks.ReadAsync(target.Serial);
            if (SelectedDevice?.Serial == target.Serial)
                CurrentValues = string.Join("\n", snapshot.Values.Select(pair => $"{pair.Key}: {pair.Value}"));
            Status = snapshot.BlockReason ?? $"Read {target.Serial}. 0 = off, 0.5 = faster transitions, 1 = normal duration. null = Android default.";
        }
        catch (Exception exception) { Status = exception.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanUseDevice))]
    private async Task ApplyAsync()
    {
        var target = SelectedDevice;
        var scale = SelectedScale;
        if (IsBusy || target?.State != DeviceState.Device) return;
        IsBusy = true;
        try
        {
            if (!confirmation.Confirm("Apply animation timing",
                    $"Set all three Android animation scales to {scale} on {target.Serial}?\n\nThis changes transition timing for all users. Previous values are journaled for undo. It does not improve video frame rate.")) return;
            var result = await tweaks.ApplyAnimationsAsync(target, scale);
            if (result.CanUndo) _lastExecution = (result.ExecutionId, target.Serial);
            Status = $"{target.Serial}: {result.Status}. Journal #{result.ExecutionId}; {result.SuccessfulActions} verified, {result.FailedActions} failed. Undo is also available in Scripts history.";
            if (SelectedDevice?.Serial == target.Serial) CurrentValues = "Read settings to refresh values.";
        }
        catch (Exception exception) { Status = exception.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (IsBusy || _lastExecution is not { } execution) return;
        if (SelectedDevice?.Serial != execution.Serial || SelectedDevice.State != DeviceState.Device)
        {
            Status = $"Select original device {execution.Serial} to undo journal #{execution.Id}.";
            return;
        }
        IsBusy = true;
        try
        {
            if (!confirmation.Confirm("Restore previous animation values", $"Restore journal #{execution.Id} on {execution.Serial}?")) return;
            var result = await tweaks.UndoAsync(execution.Id, execution.Serial);
            Status = $"{execution.Serial}: {result.Status}. Read settings to refresh values.";
            if (result.FailedActions == 0) _lastExecution = null;
        }
        catch (Exception exception) { Status = exception.Message; }
        finally { IsBusy = false; }
    }
}
