using System.Text.RegularExpressions;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class CodecInspectionService : ICodecInspectionService
{
    private static readonly Regex CodecRegex = new(
        @"(?<name>(?:c2|OMX)\.[A-Za-z0-9._-]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly IAdbProcessRunner _runner;

    public CodecInspectionService(IAdbProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<CodecInspectionResult> InspectAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunForDeviceAsync(
            serial.Trim(),
            ["shell", "dumpsys", "media.codec"],
            TimeSpan.FromMinutes(2),
            cancellationToken);
        var raw = result.IsSuccess ? result.StandardOutput : result.StandardError;
        var codecs = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => (Line: line, Match: CodecRegex.Match(line)))
            .Where(item => item.Match.Success)
            .Select(item => new CodecCapability(
                item.Match.Groups["name"].Value,
                item.Line.Contains("encoder", StringComparison.OrdinalIgnoreCase) ? "Encoder" : "Decoder",
                item.Line.Trim()))
            .DistinctBy(codec => $"{codec.Type}:{codec.Name}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(codec => codec.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new(codecs, raw, DateTimeOffset.UtcNow);
    }
}
