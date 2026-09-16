using AndroidTVManager.Core.Recovery;

namespace AndroidTVManager.Core.Models;

public enum RecoveryMode { Android, Recovery, Sideload, Fastboot, Unauthorized, Offline }
public enum RecoveryFileKind { RecoveryImage, SideloadZip }
public sealed record RecoveryTarget(string Serial, RecoveryMode Mode)
{
    public string DisplayName => $"{Serial} — {Mode}";
}
public sealed record RecoveryFile(
    string Path,
    string FileName,
    RecoveryFileKind Kind,
    long Length,
    string Sha256,
    RecoveryZipDeclaration? ZipDeclaration = null);
public sealed record RecoveryOperationResult(bool CommandSucceeded, string Message, string Output);

public interface IFastbootProcessRunner
{
    Task<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default);
}

public interface IRecoveryService
{
    Task<IReadOnlyList<RecoveryTarget>> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<RecoveryFile> InspectFileAsync(string path, RecoveryFileKind kind, CancellationToken cancellationToken = default);
    Task RebootAsync(RecoveryTarget target, string mode, CancellationToken cancellationToken = default);
    Task<RecoveryOperationResult> FlashPixelCRecoveryAsync(RecoveryTarget target, RecoveryFile image, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<RecoveryOperationResult> SideloadAsync(RecoveryTarget target, RecoveryFile zip, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

public static class RecoveryTransportParser
{
    public static IReadOnlyList<RecoveryTarget> Parse(string output, bool fastboot = false)
    {
        var result = new List<RecoveryTarget>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2) continue;
            RecoveryMode? mode = fields[1] switch
            {
                "device" when !fastboot => RecoveryMode.Android,
                "recovery" when !fastboot => RecoveryMode.Recovery,
                "sideload" when !fastboot => RecoveryMode.Sideload,
                "unauthorized" when !fastboot => RecoveryMode.Unauthorized,
                "offline" when !fastboot => RecoveryMode.Offline,
                "fastboot" when fastboot => RecoveryMode.Fastboot,
                _ => null
            };
            if (mode is { } value) result.Add(new(fields[0], value));
        }
        return result;
    }
}
