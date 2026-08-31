using System.Text.RegularExpressions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Core.Adb;

public static class PackageInventoryParser
{
    private static readonly Regex PackageNamePattern = new(
        @"^[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+$",
        RegexOptions.Compiled);

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParsePackagePaths(string output)
    {
        var packages = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var value = line.Trim();
            if (!value.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
                continue;
            var separator = value.LastIndexOf('=');
            if (separator <= "package:".Length || separator == value.Length - 1)
                continue;
            var path = value["package:".Length..separator];
            var package = value[(separator + 1)..].Split(' ', 2)[0].Trim();
            if (package.Length == 0)
                continue;
            if (!packages.TryGetValue(package, out var paths))
                packages[package] = paths = [];
            paths.Add(path);
        }
        return packages.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlySet<string> ParsePackageNames(string output)
        => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
            .Select(line => line["package:".Length..].Split(' ', 2)[0].Trim())
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static PackageDetails ParseDetails(string packageName, string output)
    {
        var versionName = Match(output, @"versionName[=:](?<value>[^\s,]+)");
        var versionCode = long.TryParse(Match(output, @"versionCode=(?<value>\d+)"), out var code) ? (long?)code : null;
        var uid = Match(output, @"userId=(?<value>\d+)");
        var paths = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
            .Select(line => line["path:".Length..].Trim())
            .Where(path => path.Length > 0)
            .ToArray();
        var installed = !Regex.IsMatch(output, @"installed=false", RegexOptions.IgnoreCase);
        var enabled = !Regex.IsMatch(output, @"enabled=false|hidden=true", RegexOptions.IgnoreCase);
        return new(packageName, versionName, versionCode, uid, installed, enabled, paths);
    }

    public static string? ParseResolvedActivityPackage(string output)
        => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => ExtractPackageFromComponent(line.Trim()))
            .LastOrDefault(package => package is not null);

    public static IReadOnlySet<string> ParseSettingComponentPackages(string output)
        => output.Split(['\r', '\n', ':'], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => ExtractPackageFromComponent(value.Trim()))
            .Where(package => package is not null)
            .Select(package => package!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> ParseDeviceOwnerPackages(string output)
    {
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();
        var ownerBlockLinesRemaining = 0;

        foreach (var line in lines)
        {
            var ownerLine = IsOwnerLine(line) && !IsEmptyOwnerLine(line);
            if (ownerLine)
                ownerBlockLinesRemaining = 6;

            if (!ownerLine && ownerBlockLinesRemaining <= 0)
                continue;

            AddOwnerPackages(line, packages);
            if (!ownerLine)
                ownerBlockLinesRemaining--;
        }

        return packages;
    }

    public sealed record PackageDetails(
        string PackageName,
        string? VersionName,
        long? VersionCode,
        string? UserId,
        bool IsInstalled,
        bool IsEnabled,
        IReadOnlyList<string> ApkPaths);

    private static string? Match(string output, string pattern)
        => Regex.Match(output, pattern, RegexOptions.IgnoreCase).Groups["value"].Value is { Length: > 0 } value
            ? value.Trim()
            : null;

    private static bool IsOwnerLine(string line)
        => Regex.IsMatch(
            line,
            @"\b(mDeviceOwner|mProfileOwner|Device Owner|Profile Owner|OwnerInfo)\b",
            RegexOptions.IgnoreCase);

    private static bool IsEmptyOwnerLine(string line)
        => Regex.IsMatch(
            line,
            @"\b(?:Device Owner|Profile Owner|mDeviceOwner|mProfileOwner)\b\s*[:=]\s*(?:null|none|no owner|not set)\b",
            RegexOptions.IgnoreCase);

    private static void AddOwnerPackages(string line, HashSet<string> packages)
    {
        foreach (Match match in Regex.Matches(
                     line,
                     @"ComponentInfo\{(?<component>[^}]+)\}",
                     RegexOptions.IgnoreCase))
        {
            if (ExtractPackageFromComponent(match.Groups["component"].Value) is { } package)
                packages.Add(package);
        }

        foreach (Match match in Regex.Matches(
                     line,
                     @"\b(?:package|packageName|ownerPackage)\s*[:=]\s*(?<package>[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+)",
                     RegexOptions.IgnoreCase))
        {
            var package = match.Groups["package"].Value;
            if (IsPackageName(package))
                packages.Add(package);
        }

        foreach (Match match in Regex.Matches(
                     line,
                     @"\badmin\s*[:=]\s*(?<component>[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+/[^,\s}]+)",
                     RegexOptions.IgnoreCase))
        {
            if (ExtractPackageFromComponent(match.Groups["component"].Value) is { } package)
                packages.Add(package);
        }
    }

    private static string? ExtractPackageFromComponent(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var component = value.Trim();
        var componentInfo = Regex.Match(
            component,
            @"ComponentInfo\{(?<component>[^}]+)\}",
            RegexOptions.IgnoreCase);
        if (componentInfo.Success)
            component = componentInfo.Groups["component"].Value;

        var separator = component.IndexOf('/');
        if (separator <= 0)
            return IsPackageName(component) ? component : null;

        var package = component[..separator].Trim();
        return IsPackageName(package) ? package : null;
    }

    private static bool IsPackageName(string value)
        => PackageNamePattern.IsMatch(value);
}
