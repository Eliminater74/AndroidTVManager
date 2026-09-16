using System.Collections.ObjectModel;
using AndroidTVManager.App.Services;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Core.Recovery;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace AndroidTVManager.App.ViewModels;

public sealed partial class RecoveryPageViewModel(IRecoveryService recovery, IConfirmationService confirmation) : PageViewModel("Recovery / Sideload")
{
    private CancellationTokenSource? _operationSource;
    private RecoveryFile? _image;
    private RecoveryFile? _zip;
    public ObservableCollection<RecoveryTarget> Targets { get; } = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BootloaderCommand), nameof(RecoveryCommand), nameof(RebootAndroidCommand), nameof(FlashRecoveryCommand), nameof(ChooseAndSideloadCommand), nameof(SideloadCommand))]
    private RecoveryTarget? _selectedTarget;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(ChooseImageCommand), nameof(BootloaderCommand), nameof(RecoveryCommand), nameof(RebootAndroidCommand), nameof(FlashRecoveryCommand), nameof(ChooseAndSideloadCommand), nameof(SideloadCommand), nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isFlashing;
    [ObservableProperty] private string _status = "Connect by USB, refresh, and explicitly select the device. This page keeps its own recovery target across mode changes.";
    [ObservableProperty] private string _imageSummary = "No recovery image selected.";
    [ObservableProperty] private string _zipSummary = "No update ZIP selected.";
    [ObservableProperty] private string _output = "";
    public bool IsIdle => !IsBusy;
    private bool CanSelectTarget() => !IsBusy && SelectedTarget is not null;
    private bool CanReboot() => CanSelectTarget() && SelectedTarget!.Mode is RecoveryMode.Android or RecoveryMode.Recovery;
    private bool CanFlash() => CanSelectTarget() && SelectedTarget!.Mode == RecoveryMode.Fastboot && _image is not null;
    private bool CanSideload() => CanSelectTarget() && SelectedTarget!.Mode is RecoveryMode.Android or RecoveryMode.Recovery or RecoveryMode.Sideload;
    private bool CanSendSelectedZip() => CanSideload() && _zip is not null;
    private bool CanCancel() => IsBusy && !IsFlashing;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task RefreshAsync() => await RunAsync(async token =>
    {
        var selectedSerial = SelectedTarget?.Serial;
        var targets = await recovery.DiscoverAsync(token);
        Targets.Clear();
        foreach (var target in targets) Targets.Add(target);
        SelectedTarget = targets.Count(t => t.Serial == selectedSerial) == 1
            ? targets.Single(t => t.Serial == selectedSerial) : null;
        Status = targets.Count == 0 ? "No ADB/Fastboot device found. Check USB debugging, recovery mode and Windows drivers." : "Select the correct serial and mode below. No device operation has run.";
    });

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ChooseImageAsync()
    {
        var picker = new OpenFileDialog { Title = "Choose Pixel C recovery image", Filter = "Android recovery image (*.img)|*.img", CheckFileExists = true };
        if (picker.ShowDialog() != true) return;
        await RunAsync(async token =>
        {
            _image = null;
            ImageSummary = "Checking image and calculating SHA-256…";
            _image = await recovery.InspectFileAsync(picker.FileName, RecoveryFileKind.RecoveryImage, token);
            ImageSummary = Describe(_image);
            Status = "Image format checked. Verify its source and Pixel C compatibility; a hash alone does not establish authenticity.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanReboot))]
    private Task BootloaderAsync() => RebootAsync("bootloader", "Reboot to bootloader");
    [RelayCommand(CanExecute = nameof(CanReboot))]
    private Task RecoveryAsync() => RebootAsync("recovery", "Reboot to recovery");
    [RelayCommand(CanExecute = nameof(CanReboot))]
    private Task RebootAndroidAsync() => RebootAsync("", "Reboot Android after confirming installation and any add-ons on the device");

    private async Task RebootAsync(string mode, string title)
    {
        var target = SelectedTarget;
        if (target is null || !confirmation.Confirm(title, $"{title} on {target.Serial}?")) return;
        await RunAsync(async token =>
        {
            await recovery.RebootAsync(target, mode, token);
            Status = $"Reboot requested for {target.Serial}. Refresh after the device changes mode. If entering recovery from Fastboot, use the device menu.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanFlash))]
    private async Task FlashRecoveryAsync()
    {
        var target = SelectedTarget;
        var image = _image;
        if (target is null || image is null || !confirmation.Confirm("Flash Pixel C recovery partition",
                $"Target: {target.Serial}\nImage: {image.FileName}\nSHA-256: {image.Sha256}\n\nThis replaces the recovery partition and cannot be undone here. Confirm this image is intended for your Pixel C. The bootloader must already be unlocked. Keep USB connected until completion.")) return;
        IsFlashing = true;
        try
        {
            await RunAsync(async token => ShowResult(await recovery.FlashPixelCRecoveryAsync(target, image, Progress(), token)));
        }
        finally { IsFlashing = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSideload))]
    private async Task ChooseAndSideloadAsync()
    {
        var target = SelectedTarget;
        if (target is null) return;
        var picker = new OpenFileDialog { Title = "Choose LineageOS or add-on ZIP to sideload", Filter = "Android update ZIP (*.zip)|*.zip", CheckFileExists = true };
        if (picker.ShowDialog() != true) return;
        await RunAsync(async token =>
        {
            _zip = null;
            ZipSummary = "Checking ZIP and calculating SHA-256…";
            _zip = await recovery.InspectFileAsync(picker.FileName, RecoveryFileKind.SideloadZip, token);
            ZipSummary = Describe(_zip);
            await SendZipAsync(target, _zip, token);
        });
    }

    [RelayCommand(CanExecute = nameof(CanSendSelectedZip))]
    private async Task SideloadAsync()
    {
        var target = SelectedTarget;
        var zip = _zip;
        if (target is null || zip is null) return;
        await RunAsync(token => SendZipAsync(target, zip, token));
    }

    private async Task SendZipAsync(RecoveryTarget target, RecoveryFile zip, CancellationToken token)
    {
        if (!confirmation.Confirm("Sideload selected update",
                $"Target: {target.Serial}\nPackage: {zip.FileName}\nSHA-256: {zip.Sha256}\n{zip.ZipDeclaration?.Evidence ?? "The ZIP did not declare a target device."}\n\nConfirm this package matches your device and build. Compatibility is not assumed from the file name. The app may reboot Android to recovery, then wait for Apply Update → Apply from ADB. It will not wipe, unlock or reboot Android after installation."))
        { Status = "Sideload canceled before sending the package."; return; }
        ShowResult(await recovery.SideloadAsync(target, zip, Progress(), token));
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (confirmation.Confirm("Stop waiting or transfer", "Stop the PC operation? Recovery may already be applying an update. Inspect the device before retrying; stopping the PC is not a rollback."))
            _operationSource?.Cancel();
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        Output = "";
        using var source = new CancellationTokenSource();
        _operationSource = source;
        try { await action(source.Token); }
        catch (OperationCanceledException) { Status = "PC operation stopped. Verify the device's state before continuing; no rollback is implied."; }
        catch (Exception exception) { Status = exception.Message; }
        finally { _operationSource = null; IsBusy = false; }
    }

    private IProgress<string> Progress()
    {
        var source = _operationSource;
        return new Progress<string>(message =>
        {
            if (IsBusy && ReferenceEquals(_operationSource, source)) Status = message;
        });
    }
    private void ShowResult(RecoveryOperationResult result) { Status = result.Message; Output = result.Output; }
    private static string Describe(RecoveryFile file)
    {
        var summary = $"{file.FileName} · {file.Length:N0} bytes\nSHA-256: {file.Sha256}";
        if (file.ZipDeclaration is null)
            return summary;
        var compatibility = RecoveryZipMetadataParser.Evaluate(file.ZipDeclaration, null);
        return $"{summary}\n{file.ZipDeclaration.Evidence}\n{compatibility.Reasons[0]}";
    }
}
