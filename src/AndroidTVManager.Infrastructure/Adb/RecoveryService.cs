using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class RecoveryService(IAdbProcessRunner adb, IFastbootProcessRunner fastboot) : IRecoveryService
{
    private readonly SemaphoreSlim _operation = new(1, 1);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    public async Task<IReadOnlyList<RecoveryTarget>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var a = await adb.RunAsync(["devices", "-l"], ProbeTimeout, cancellationToken);
        var f = await fastboot.RunAsync(["devices"], ProbeTimeout, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!a.IsSuccess && !f.IsSuccess) throw new InvalidOperationException("Neither ADB nor Fastboot could list devices. Check Platform-Tools and USB drivers.");
        return (a.IsSuccess ? RecoveryTransportParser.Parse(a.StandardOutput) : [])
            .Concat(f.IsSuccess ? RecoveryTransportParser.Parse(f.StandardOutput, true) : []).ToArray();
    }

    public Task<RecoveryFile> InspectFileAsync(string path, RecoveryFileKind kind, CancellationToken cancellationToken = default)
        => Task.Run(async () =>
    {
        await using var stream = Open(path);
        return await InspectAsync(stream, path, kind, cancellationToken).ConfigureAwait(false);
    }, cancellationToken);

    private static FileStream Open(string path) => new(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read,
        81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task<RecoveryFile> InspectAsync(FileStream stream, string path, RecoveryFileKind kind, CancellationToken token)
    {
        var extension = kind == RecoveryFileKind.RecoveryImage ? ".img" : ".zip";
        if (!Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase) || stream.Length == 0)
            throw new InvalidDataException($"Select a nonempty {extension} file.");
        var header = new byte[8];
        if (await stream.ReadAsync(header, token).ConfigureAwait(false) < 8) throw new InvalidDataException("File is too small.");
        if (kind == RecoveryFileKind.RecoveryImage && Encoding.ASCII.GetString(header) != "ANDROID!")
            throw new InvalidDataException("Pixel C recovery must use the Android boot-image format. This file was not recognized.");
        stream.Position = 0;
        if (kind == RecoveryFileKind.SideloadZip)
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (!zip.Entries.Any(entry => entry.FullName is "META-INF/com/google/android/update-binary" or "META-INF/com/android/metadata" or "payload.bin"))
                throw new InvalidDataException("ZIP does not contain recognized Android update metadata or an installer. Select the ROM/add-on ZIP, not an APK or download page.");
        }
        stream.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
        return new(Path.GetFullPath(path), Path.GetFileName(path), kind, stream.Length, hash);
    }

    private async Task<RecoveryTarget> RequireTargetAsync(string serial, CancellationToken token)
    {
        var matches = (await DiscoverAsync(token)).Where(device => device.Serial == serial).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("The selected serial is missing or ambiguous. Refresh and select it explicitly; no other device will be used.");
        return matches[0];
    }

    public async Task RebootAsync(RecoveryTarget target, string mode, CancellationToken cancellationToken = default)
    {
        if (mode is not ("" or "recovery" or "bootloader")) throw new ArgumentException("Unsupported reboot mode.");
        await _operation.WaitAsync(cancellationToken);
        try
        {
            var current = await RequireTargetAsync(target.Serial, cancellationToken);
            if (current.Mode is not (RecoveryMode.Android or RecoveryMode.Recovery))
                throw new InvalidOperationException("Use the device's recovery/bootloader menu to change mode, then refresh. An ADB shell is not available in this mode.");
            EnsureSuccess(await adb.RunForDeviceAsync(target.Serial, mode.Length == 0 ? ["reboot"] : ["reboot", mode],
                ProbeTimeout, cancellationToken), cancellationToken);
        }
        finally { _operation.Release(); }
    }

    public async Task<RecoveryOperationResult> FlashPixelCRecoveryAsync(RecoveryTarget target, RecoveryFile image, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (image.Kind != RecoveryFileKind.RecoveryImage) throw new ArgumentException("Choose a recovery image.");
        await _operation.WaitAsync(cancellationToken);
        try
        {
            await using var locked = await Task.Run(() => Open(image.Path), cancellationToken).ConfigureAwait(false);
            await VerifyFileAsync(locked, image, cancellationToken);
            if ((await RequireTargetAsync(target.Serial, cancellationToken)).Mode != RecoveryMode.Fastboot)
                throw new InvalidOperationException("Reboot to bootloader first, then select the Fastboot serial explicitly.");
            progress?.Report("Checking Pixel C product, unlock state and recovery partition…");
            var product = await ReadVariableAsync(target.Serial, "product", cancellationToken);
            var unlocked = await ReadVariableAsync(target.Serial, "unlocked", cancellationToken);
            var size = await ReadVariableAsync(target.Serial, "partition-size:recovery", cancellationToken);
            if (product != "dragon") throw new InvalidOperationException("Recovery flashing is limited to a verified Pixel C (Fastboot product dragon). No partition was written.");
            if (unlocked is not ("yes" or "true")) throw new InvalidOperationException("The bootloader is not confirmed unlocked. This tool does not unlock it.");
            if (!ulong.TryParse(size.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? size[2..] : size,
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var partitionSize)
                || partitionSize == 0 || (ulong)image.Length > partitionSize)
                throw new InvalidOperationException("Recovery partition size is unavailable or the selected image is too large.");
            progress?.Report($"Flashing recovery on {target.Serial}. Keep USB connected; interruption can leave recovery unusable.");
            var result = await fastboot.RunAsync(["-s", target.Serial, "flash", "recovery", image.Path], TimeSpan.FromMinutes(5), cancellationToken);
            EnsureSuccess(result, cancellationToken, image.Path);
            return new(true, "Fastboot reported recovery flashed. Use the Pixel C bootloader menu to enter Recovery; do not boot Android first. Verify recovery on the tablet.", Sanitize(result, image.Path));
        }
        finally { _operation.Release(); }
    }

    public async Task<RecoveryOperationResult> SideloadAsync(RecoveryTarget target, RecoveryFile zip, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (zip.Kind != RecoveryFileKind.SideloadZip) throw new ArgumentException("Choose a sideload ZIP.");
        await _operation.WaitAsync(cancellationToken);
        try
        {
            await using var locked = await Task.Run(() => Open(zip.Path), cancellationToken).ConfigureAwait(false);
            await VerifyFileAsync(locked, zip, cancellationToken);
            var current = await RequireTargetAsync(target.Serial, cancellationToken);
            if (current.Mode == RecoveryMode.Android)
            {
                progress?.Report("Rebooting the selected device to recovery…");
                EnsureSuccess(await adb.RunForDeviceAsync(target.Serial, ["reboot", "recovery"], ProbeTimeout, cancellationToken), cancellationToken);
            }
            else if (current.Mode is not (RecoveryMode.Recovery or RecoveryMode.Sideload))
                throw new InvalidOperationException("Enter Recovery on the selected device before starting sideload.");
            progress?.Report("Waiting up to 5 minutes: in Lineage Recovery choose Apply Update → Apply from ADB. Perform only the wipes required by your build's guide before this step.");
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            wait.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                while (true)
                {
                    var result = await adb.RunAsync(["devices", "-l"], ProbeTimeout, wait.Token);
                    var found = result.IsSuccess ? RecoveryTransportParser.Parse(result.StandardOutput).Where(device => device.Serial == target.Serial).ToArray() : [];
                    if (found.Length == 1 && found[0].Mode == RecoveryMode.Sideload) break;
                    await Task.Delay(TimeSpan.FromSeconds(2), wait.Token);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new TimeoutException("The original serial did not enter sideload mode. If its serial changed, refresh and select it explicitly."); }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Sending {zip.FileName} to {target.Serial}. Recovery verifies and installs the package; read its screen for the final result.");
            var transfer = await adb.RunForDeviceAsync(target.Serial, ["sideload", zip.Path], TimeSpan.FromMinutes(30), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new(transfer.IsSuccess,
                transfer.WasTimedOut ? "Sideload timed out. Inspect recovery before retrying; installation state is unknown."
                    : transfer.IsSuccess ? "ADB transfer finished. Confirm installation succeeded on the tablet. Install any required add-ons before rebooting Android."
                    : "ADB reported a transfer error. Inspect recovery: some recoveries finish near 47% even when ADB reports an error. Installation success is not assumed.",
                Sanitize(transfer, zip.Path));
        }
        finally { _operation.Release(); }
    }

    private async Task<string> ReadVariableAsync(string serial, string variable, CancellationToken token)
    {
        var result = await fastboot.RunAsync(["-s", serial, "getvar", variable], ProbeTimeout, token);
        EnsureSuccess(result, token);
        var marker = variable + ":";
        return (result.StandardOutput + "\n" + result.StandardError).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Replace("(bootloader) ", "", StringComparison.Ordinal))
            .Where(line => line.StartsWith(marker, StringComparison.Ordinal))
            .Select(line => line[marker.Length..].Trim()).SingleOrDefault() ?? "";
    }

    private static async Task VerifyFileAsync(FileStream stream, RecoveryFile expected, CancellationToken token)
    {
        var current = await InspectAsync(stream, expected.Path, expected.Kind, token);
        if (current.Sha256 != expected.Sha256 || current.Length != expected.Length)
            throw new InvalidOperationException("The selected file changed. Choose it again and review its SHA-256 before continuing.");
    }

    private static string Sanitize(AdbCommandResult result, string path)
        => (result.StandardOutput + "\n" + result.StandardError).Replace(path, "<selected-file>", StringComparison.OrdinalIgnoreCase).Trim();

    private static void EnsureSuccess(AdbCommandResult result, CancellationToken token, string path = "<none>")
    {
        token.ThrowIfCancellationRequested();
        if (!result.IsSuccess) throw new InvalidOperationException(result.WasTimedOut ? "Device command timed out; inspect the device before retrying." : "Device command failed: " + Sanitize(result, path));
    }
}
