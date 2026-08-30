using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Diagnostics;

public sealed class DiagnosticBundleService : IDiagnosticBundleService
{
    private readonly ILocalAppDataPaths _paths;
    private readonly IAdbProcessRunner _runner;
    private readonly IDeviceInspectionService _inspection;
    private readonly IConfigurationExplorerService _configuration;
    private readonly IDisplayDiagnosticsService _display;
    private readonly ITransportDoctorService _transport;
    private readonly IDeviceLogcatService _logcat;

    public DiagnosticBundleService(
        ILocalAppDataPaths paths,
        IAdbProcessRunner runner,
        IDeviceInspectionService inspection,
        IConfigurationExplorerService configuration,
        IDisplayDiagnosticsService display,
        ITransportDoctorService transport,
        IDeviceLogcatService logcat)
    {
        _paths = paths;
        _runner = runner;
        _inspection = inspection;
        _configuration = configuration;
        _display = display;
        _transport = transport;
        _logcat = logcat;
    }

    public async Task<DiagnosticBundleResult> CreateAsync(
        DiagnosticBundleRequest request,
        CancellationToken cancellationToken = default)
    {
        var serial = request.Device.Serial.Trim();
        if (request.Device.State != DeviceState.Device || serial.Length == 0)
            throw new InvalidOperationException("The diagnostic bundle target is not connected and authorized.");

        _paths.EnsureCreated();
        var staging = Path.Combine(_paths.TempPath, "diagnostic-" + Guid.NewGuid().ToString("N"));
        var warnings = new List<string>();
        var included = new List<string>();
        Directory.CreateDirectory(staging);
        try
        {
            await WriteJsonAsync("device.json", request.Device, request.PrivacyMode, staging, included, cancellationToken);
            await WriteJsonAsync(
                "bundle-manifest.json",
                new
                {
                    formatVersion = 1,
                    applicationVersion = request.ApplicationVersion,
                    capturedUtc = DateTimeOffset.UtcNow,
                    privacyMode = request.PrivacyMode.ToString(),
                    serialIncluded = request.PrivacyMode == DiagnosticBundlePrivacyMode.LocalFull
                },
                request.PrivacyMode,
                staging,
                included,
                cancellationToken);

            await TryWriteAsync("inspection.json", async () =>
                await _inspection.InspectAsync(serial, cancellationToken: cancellationToken), staging, included,
                warnings, request.PrivacyMode, cancellationToken);
            await TryWriteAsync("configuration.json", async () =>
                await _configuration.InspectAsync(serial, request.Device.FriendlyName, cancellationToken: cancellationToken),
                staging, included, warnings, request.PrivacyMode, cancellationToken);
            await TryWriteAsync("display.json", async () =>
                await _display.CaptureAsync(serial, request.Device.FriendlyName, cancellationToken: cancellationToken),
                staging, included, warnings, request.PrivacyMode, cancellationToken);
            await TryWriteAsync("transport.json", async () =>
                await _transport.RunAsync(request.Device, cancellationToken: cancellationToken),
                staging, included, warnings, request.PrivacyMode, cancellationToken);
            await WriteCommandAsync("getprop.txt", serial, ["shell", "getprop"], staging, included, warnings, request.PrivacyMode, cancellationToken);
            await WriteCommandAsync("packages.txt", serial, ["shell", "pm", "list", "packages", "-f", "-u"], staging, included, warnings, request.PrivacyMode, cancellationToken);
            await WriteCommandAsync("network.txt", serial, ["shell", "ip", "addr", "show"], staging, included, warnings, request.PrivacyMode, cancellationToken);
            await WriteCommandAsync("display-dumpsys.txt", serial, ["shell", "dumpsys", "display"], staging, included, warnings, request.PrivacyMode, cancellationToken);
            await WriteCommandAsync("hdmi-dumpsys.txt", serial, ["shell", "dumpsys", "hdmi_control"], staging, included, warnings, request.PrivacyMode, cancellationToken);
            await WriteCommandAsync("codec-dumpsys.txt", serial, ["shell", "dumpsys", "media.codec"], staging, included, warnings, request.PrivacyMode, cancellationToken);
            await CaptureLogcatAsync(serial, request.LogcatLineLimit, staging, included, warnings, request.PrivacyMode, cancellationToken);
            await WriteChecksumsAsync(staging, included, cancellationToken);

            var destinationDirectory = Path.Combine(_paths.BackupsPath, "DiagnosticBundles");
            Directory.CreateDirectory(destinationDirectory);
            var fileName = $"{SafeName(request.Device.FriendlyName ?? request.Device.Model ?? "device")}-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
            var archivePath = Path.Combine(destinationDirectory, fileName);
            if (File.Exists(archivePath))
                File.Delete(archivePath);
            ZipFile.CreateFromDirectory(staging, archivePath, CompressionLevel.Fastest, includeBaseDirectory: false);
            return new(archivePath, included, warnings);
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task CaptureLogcatAsync(
        string serial,
        int lineLimit,
        string staging,
        ICollection<string> included,
        ICollection<string> warnings,
        DiagnosticBundlePrivacyMode privacyMode,
        CancellationToken cancellationToken)
    {
        IAdbProcessSession? session = null;
        var lines = new Queue<string>(Math.Clamp(lineLimit, 1, 5000));
        using var captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        captureCts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            session = await _logcat.StartAsync(serial, new LogcatOptions(MaxLines: lineLimit), captureCts.Token);
            await foreach (var line in session.ReadStandardOutputAsync(captureCts.Token))
            {
                lines.Enqueue(Redact(line, privacyMode));
                while (lines.Count > Math.Clamp(lineLimit, 1, 5000))
                    lines.Dequeue();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            warnings.Add($"logcat: {exception.Message}");
        }
        finally
        {
            if (session is not null)
                await session.DisposeAsync();
        }
        await File.WriteAllLinesAsync(Path.Combine(staging, "logcat.txt"), lines, cancellationToken);
        included.Add("logcat.txt");
    }

    private async Task WriteCommandAsync(
        string name,
        string serial,
        IReadOnlyList<string> arguments,
        string staging,
        ICollection<string> included,
        ICollection<string> warnings,
        DiagnosticBundlePrivacyMode privacyMode,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _runner.RunForDeviceAsync(serial, arguments, TimeSpan.FromMinutes(2), cancellationToken);
            var content = result.StandardOutput;
            if (!string.IsNullOrWhiteSpace(result.StandardError))
                content += $"{Environment.NewLine}[stderr]{Environment.NewLine}{result.StandardError}";
            if (!result.IsSuccess)
                warnings.Add($"{name}: ADB exited with {result.ExitCode}.");
            await File.WriteAllTextAsync(
                Path.Combine(staging, name),
                Redact(content, privacyMode),
                cancellationToken);
            included.Add(name);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            warnings.Add($"{name}: {exception.Message}");
        }
    }

    private static async Task TryWriteAsync<T>(
        string name,
        Func<Task<T>> operation,
        string staging,
        ICollection<string> included,
        ICollection<string> warnings,
        DiagnosticBundlePrivacyMode privacyMode,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await operation();
            await WriteJsonAsync(name, value, privacyMode, staging, included, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            warnings.Add($"{name}: {exception.Message}");
        }
    }

    private static async Task WriteJsonAsync<T>(
        string name,
        T value,
        DiagnosticBundlePrivacyMode privacyMode,
        string staging,
        ICollection<string> included,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(staging, name), Redact(json, privacyMode), cancellationToken);
        included.Add(name);
    }

    private static async Task WriteChecksumsAsync(
        string staging,
        ICollection<string> included,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        foreach (var file in Directory.EnumerateFiles(staging).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var hash = await HashAsync(file, cancellationToken);
            lines.Add($"{hash}  {Path.GetFileName(file)}");
        }
        await File.WriteAllLinesAsync(Path.Combine(staging, "SHA256SUMS.txt"), lines, cancellationToken);
        included.Add("SHA256SUMS.txt");
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static string Redact(string value, DiagnosticBundlePrivacyMode mode)
    {
        value = Regex.Replace(value, @"(?i)(pairing[-_ ]?code|password|token|secret|credential)(\s*[:=]\s*)\S+", "$1$2<redacted>");
        return mode == DiagnosticBundlePrivacyMode.LocalFull
            ? value
            : Regex.Replace(
                Regex.Replace(
                    Regex.Replace(value, @"(?i)\b[0-9a-f]{2}([: -][0-9a-f]{2}){5}\b", "<mac-redacted>"),
                    @"\b(?:\d{1,3}\.){3}\d{1,3}\b",
                    "<ip-redacted>"),
                @"(?i)(ssid|wifi|network)(\s*[:=]\s*)\S+",
                "$1$2<redacted>");
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character)).Trim();
        return string.IsNullOrWhiteSpace(name) ? "device" : name;
    }
}
