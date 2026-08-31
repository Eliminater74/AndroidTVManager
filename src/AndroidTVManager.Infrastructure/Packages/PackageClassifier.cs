using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Packages;

public sealed class PackageClassifier : IPackageClassifier
{
    public const string RulesetVersion = "vendor-tv-sourced-2026-08-30-v2";
    private readonly IReadOnlyList<PackageKnowledgeRule> _rules;
    private readonly IReadOnlyDictionary<string, PackageKnowledgeSource> _sources;

    public PackageClassifier()
    {
        _rules = PackageKnowledgeLoader.Load();
        _sources = PackageKnowledgeLoader.LoadSources()
            .ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
        var unknownSourceIds = _rules
            .SelectMany(rule => rule.SourceIds ?? [])
            .Where(sourceId => !_sources.ContainsKey(sourceId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknownSourceIds.Length > 0)
            throw new InvalidDataException($"Package knowledge references missing source(s): {string.Join(", ", unknownSourceIds)}.");
    }

    public PackageAssessment Classify(
        PackageInventoryEntry package,
        PackageClassificationContext context)
    {
        var reasons = new List<string>();
        var activeRoles = GetActiveRoles(package, context);
        if (activeRoles.Length > 0)
        {
            reasons.Add($"This package currently holds active device role(s): {string.Join(", ", activeRoles)}.");
            return Assessment(package, PackageRiskLevel.Critical, PackageConfidence.Verified,
                "Active system role", "Keep", reasons, true);
        }

        var rule = _rules
            .Where(candidate => Matches(candidate, package, context.Device))
            .OrderByDescending(Specificity)
            .FirstOrDefault();
        if (rule is null)
        {
            reasons.Add(package.IsSystem
                ? "System package is not covered by a verified starter rule."
                : "No device-specific knowledge rule matches this package.");
            return Assessment(package, PackageRiskLevel.Unknown, PackageConfidence.Low,
                "Unknown", "Review manually", reasons, false);
        }

        reasons.Add($"Matched {Specificity(rule)}-level knowledge rule.");
        if (package.IsUpdatedSystem)
            reasons.Add("Package is an updated system application.");
        if (!string.IsNullOrWhiteSpace(rule.ObservedModels))
            reasons.Add($"Observed models/families: {rule.ObservedModels}.");
        if (!string.IsNullOrWhiteSpace(rule.EvidenceNotes))
            reasons.Add($"Evidence note: {rule.EvidenceNotes}");
        reasons.Add(rule.HardwareVerified
            ? "Hardware behavior verified by Android TV Manager."
            : "Community/source evidence only; Android TV Manager has not hardware-verified this behavior.");
        foreach (var sourceId in rule.SourceIds ?? [])
        {
            if (_sources.TryGetValue(sourceId, out var source))
                reasons.Add($"Source evidence [{source.Id}]: {source.Title} "
                    + $"({source.SourceConfidence}, {source.SourceType}) — {source.Url}");
            else
                reasons.Add($"Source evidence reference '{sourceId}' is unavailable.");
        }
        return Assessment(package, rule.Risk, rule.Confidence, rule.Category,
            rule.RecommendedAction, reasons, false, rule.Description, rule.Impacts);
    }

    private static PackageAssessment Assessment(
        PackageInventoryEntry package,
        PackageRiskLevel risk,
        PackageConfidence confidence,
        string category,
        string recommendedAction,
        IReadOnlyList<string> reasons,
        bool protectedPackage,
        string? description = null,
        IReadOnlyList<PackageImpact>? impacts = null)
        => new(package.PackageName, risk, confidence, category,
            description ?? "No trusted description is available.",
            recommendedAction,
            reasons,
            impacts ?? [],
            protectedPackage,
            RulesetVersion);

    private static bool Matches(
        PackageKnowledgeRule rule,
        PackageInventoryEntry package,
        AndroidDevice device)
        => (string.Equals(rule.Package, package.PackageName, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(rule.PackagePrefix)
                && package.PackageName.StartsWith(rule.PackagePrefix, StringComparison.OrdinalIgnoreCase)))
            && (string.IsNullOrWhiteSpace(rule.Manufacturer)
                || string.Equals(rule.Manufacturer, device.Manufacturer, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(rule.Product)
                || string.Equals(rule.Product, device.Product, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(rule.ModelContains)
                || (device.Model?.Contains(rule.ModelContains, StringComparison.OrdinalIgnoreCase) ?? false))
            && (!rule.MinApi.HasValue || (device.ApiLevel ?? 0) >= rule.MinApi)
            && (!rule.MaxApi.HasValue || (device.ApiLevel ?? int.MaxValue) <= rule.MaxApi);

    private static string[] GetActiveRoles(
        PackageInventoryEntry package,
        PackageClassificationContext context)
    {
        var roles = new List<string>();
        if (package.IsActiveLauncher
            || string.Equals(package.PackageName, context.ActiveLauncherPackage, StringComparison.OrdinalIgnoreCase))
            roles.Add("active launcher");
        if (package.IsDefaultInputMethod || context.DefaultInputMethodPackages.Contains(package.PackageName))
            roles.Add("default input method");
        if (package.IsEnabledAccessibilityService || context.EnabledAccessibilityPackages.Contains(package.PackageName))
            roles.Add("enabled accessibility service");
        if (package.IsDeviceOwner || context.DeviceOwnerPackages.Contains(package.PackageName))
            roles.Add("device owner");
        return roles.ToArray();
    }

    private static int Specificity(PackageKnowledgeRule rule)
        => (string.IsNullOrWhiteSpace(rule.PackagePrefix) ? 16 : 0)
           + (rule.Manufacturer is null ? 0 : 8)
           + (rule.Product is null ? 0 : 4)
           + (rule.ModelContains is null ? 0 : 2)
           + (rule.MinApi.HasValue || rule.MaxApi.HasValue ? 1 : 0);
}

public static class PackageKnowledgeLoader
{
    public static IReadOnlyList<PackageKnowledgeRule> Load()
    {
        var resource = typeof(PackageKnowledgeLoader).Assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("Data.package-knowledge.json", StringComparison.OrdinalIgnoreCase));
        if (resource is null)
            return [];
        using var stream = typeof(PackageKnowledgeLoader).Assembly.GetManifestResourceStream(resource);
        return stream is null
            ? []
            : JsonSerializer.Deserialize<List<PackageKnowledgeRule>>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            }) ?? [];
    }

    public static IReadOnlyList<PackageKnowledgeSource> LoadSources()
    {
        var resource = typeof(PackageKnowledgeLoader).Assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("Data.package-knowledge-sources.json", StringComparison.OrdinalIgnoreCase));
        if (resource is null)
            return [];
        using var stream = typeof(PackageKnowledgeLoader).Assembly.GetManifestResourceStream(resource);
        if (stream is null)
            throw new InvalidDataException("Package knowledge source catalog could not be opened.");
        var sources = JsonSerializer.Deserialize<List<PackageKnowledgeSource>>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            }) ?? throw new JsonException("Package knowledge source catalog is empty.");
        if (sources.Count == 0 || sources.Any(source =>
                string.IsNullOrWhiteSpace(source.Id)
                || string.IsNullOrWhiteSpace(source.Title)
                || string.IsNullOrWhiteSpace(source.Url)
                || string.IsNullOrWhiteSpace(source.SourceType)
                || string.IsNullOrWhiteSpace(source.Attribution)))
            throw new JsonException("Package knowledge source catalog contains an incomplete source entry.");
        return sources
            .Select(source => source.SourceConfidence == PackageSourceConfidence.Unknown
                ? source with { SourceConfidence = InferSourceConfidence(source.SourceType) }
                : source)
            .ToArray();
    }

    private static PackageSourceConfidence InferSourceConfidence(string sourceType)
        => sourceType switch
        {
            "community-package-dump" or "community-hardware-research"
                => PackageSourceConfidence.RealHardwareDump,
            "community-tested-guide" or "community-regression-report"
                => PackageSourceConfidence.TestedDeviceReport,
            "community-tool" or "community-knowledge-base" or "community-package-research"
                => PackageSourceConfidence.MultiSourceCommunityEvidence,
            "manufacturer-product-reference" or "device-compatibility-reference"
                => PackageSourceConfidence.GenericManufacturerEvidence,
            _ when sourceType.Contains("anecdotal", StringComparison.OrdinalIgnoreCase)
                => PackageSourceConfidence.SingleAnecdotalReport,
            _ => PackageSourceConfidence.Unknown
        };
}
