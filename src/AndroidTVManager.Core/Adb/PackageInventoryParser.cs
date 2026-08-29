using System.Text.RegularExpressions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Core.Adb;

public static class PackageInventoryParser
{
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
}
