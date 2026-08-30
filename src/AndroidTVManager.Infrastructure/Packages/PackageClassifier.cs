using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Packages;

public sealed class PackageClassifier : IPackageClassifier
{
    public const string RulesetVersion = "vendor-tv-2026-08-30";
    private readonly IReadOnlyList<PackageKnowledgeRule> _rules;

    public PackageClassifier()
    {
        _rules = PackageKnowledgeLoader.Load();
    }

    public PackageAssessment Classify(
        PackageInventoryEntry package,
        PackageClassificationContext context)
    {
        var reasons = new List<string>();
        var isRoleProtected = package.IsActiveLauncher
            || string.Equals(package.PackageName, context.ActiveLauncherPackage, StringComparison.OrdinalIgnoreCase)
            || package.IsDefaultInputMethod
            || context.DefaultInputMethodPackages.Contains(package.PackageName)
            || package.IsEnabledAccessibilityService
            || context.EnabledAccessibilityPackages.Contains(package.PackageName)
            || package.IsDeviceOwner
            || context.DeviceOwnerPackages.Contains(package.PackageName);
        if (isRoleProtected)
        {
            reasons.Add("This package currently holds an active device role.");
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
        => string.Equals(rule.Package, package.PackageName, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(rule.Manufacturer)
                || string.Equals(rule.Manufacturer, device.Manufacturer, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(rule.Product)
                || string.Equals(rule.Product, device.Product, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(rule.ModelContains)
                || (device.Model?.Contains(rule.ModelContains, StringComparison.OrdinalIgnoreCase) ?? false))
            && (!rule.MinApi.HasValue || (device.ApiLevel ?? 0) >= rule.MinApi)
            && (!rule.MaxApi.HasValue || (device.ApiLevel ?? int.MaxValue) <= rule.MaxApi);

    private static int Specificity(PackageKnowledgeRule rule)
        => (rule.Manufacturer is null ? 0 : 8)
           + (rule.Product is null ? 0 : 4)
           + (rule.ModelContains is null ? 0 : 2)
           + (rule.MinApi.HasValue || rule.MaxApi.HasValue ? 1 : 0);
}

internal static class PackageKnowledgeLoader
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
}
