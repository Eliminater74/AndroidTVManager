using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Packages;

public sealed class PackageReferenceCatalog : IPackageReferenceCatalog
{
    private readonly IReadOnlyList<PackageReferenceBaselineDocument> _documents;

    public PackageReferenceCatalog()
    {
        var sources = PackageKnowledgeLoader.LoadSources()
            .Select(source => source.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _documents = LoadDocuments();
        var missingSourceIds = _documents
            .SelectMany(document => document.Baseline.SourceIds ?? [])
            .Concat(_documents.SelectMany(document =>
                document.Packages.SelectMany(package => package.EvidenceSourceIds ?? [])))
            .Where(sourceId => !sources.Contains(sourceId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingSourceIds.Length > 0)
            throw new InvalidDataException(
                $"Reference baseline catalog references missing source(s): {string.Join(", ", missingSourceIds)}.");
    }

    public IReadOnlyList<PackageReferenceProfileMatch> GetProfiles()
        => _documents
            .Select(document => ToProfile(document, matchedPackages: 0))
            .OrderBy(profile => profile.Origin)
            .ThenBy(profile => profile.BaselineName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public Task<PackageReferenceAnalysis> AnalyzeAsync(
        AndroidDevice device,
        IReadOnlyList<PackageInventoryEntry> packages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(packages);

        var applicableDocuments = _documents
            .Where(document => IsApplicable(document.Baseline, device))
            .ToArray();
        var applicableEntries = applicableDocuments
            .SelectMany(document => document.Packages.Select(package =>
                new PackageReferenceCatalogEntry(document.Baseline, package)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Package.PackageName)
                || !string.IsNullOrWhiteSpace(entry.Package.PackagePrefix))
            .ToArray();
        var results = new List<PackageReferenceAnalysisItem>(packages.Count);
        foreach (var package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = applicableEntries
                .Where(entry => Matches(entry.Package, package.PackageName))
                .OrderByDescending(entry => Specificity(entry.Package))
                .ThenBy(entry => entry.Baseline.Id, StringComparer.OrdinalIgnoreCase)
                .Select(ToMatch)
                .ToArray();
            results.Add(new PackageReferenceAnalysisItem(
                package.PackageName,
                matches.FirstOrDefault()?.Origin ?? PackageOrigin.Unknown,
                matches,
                matches.SelectMany(match => match.ObservedOn)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                matches.Select(match => match.Role)
                    .FirstOrDefault(role => !string.IsNullOrWhiteSpace(role))));
        }

        var summary = new PackageReferenceSummary(
            results.Count,
            results.GroupBy(result => result.Origin)
                .Select(group => new PackageOriginCount(group.Key, group.Count()))
                .OrderBy(count => count.Origin)
                .ToArray(),
            results.Sum(result => result.Matches.Count),
            results.Count(result => result.Origin == PackageOrigin.Unknown),
            BuildProfileMatches(applicableDocuments, results));
        return Task.FromResult(new PackageReferenceAnalysis(
            device.Serial,
            DateTimeOffset.UtcNow,
            results,
            summary));
    }

    private static IReadOnlyList<PackageReferenceBaselineDocument> LoadDocuments()
    {
        var resourceName = typeof(PackageReferenceCatalog).Assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(
                "Data.reference-baselines.json",
                StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            throw new InvalidDataException("Reference baseline catalog resource could not be found.");
        using var stream = typeof(PackageReferenceCatalog).Assembly
            .GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidDataException("Reference baseline catalog resource could not be opened.");
        var documents = JsonSerializer.Deserialize<List<PackageReferenceBaselineDocument>>(
            stream,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() }
            });
        if (documents is null || documents.Count == 0 || documents.Any(document =>
                string.IsNullOrWhiteSpace(document.Baseline.Id)
                || string.IsNullOrWhiteSpace(document.Baseline.Name)
                || string.IsNullOrWhiteSpace(document.Baseline.Generation)
                || document.Packages is null))
            throw new JsonException("Reference baseline catalog contains an incomplete baseline.");
        return documents;
    }

    private static bool IsApplicable(PackageReferenceBaseline baseline, AndroidDevice device)
    {
        if (!string.IsNullOrWhiteSpace(baseline.Manufacturer)
            && !string.Equals(baseline.Manufacturer, device.Manufacturer,
                StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(baseline.DeviceFamily)
            && !ContainsDeviceValue(device, baseline.DeviceFamily)
            && !string.Equals(baseline.Manufacturer, device.Manufacturer,
                StringComparison.OrdinalIgnoreCase))
            return false;
        if (baseline.MinApiLevel.HasValue
            && (!device.ApiLevel.HasValue || device.ApiLevel < baseline.MinApiLevel))
            return false;
        if (baseline.MaxApiLevel.HasValue
            && (!device.ApiLevel.HasValue || device.ApiLevel > baseline.MaxApiLevel))
            return false;
        if (!baseline.MinApiLevel.HasValue
            && !baseline.MaxApiLevel.HasValue
            && !string.IsNullOrWhiteSpace(baseline.AndroidVersion)
            && TryGetMajorVersion(device.AndroidVersion) is { } deviceVersion
            && TryGetMajorVersion(baseline.AndroidVersion) is { } baselineVersion
            && deviceVersion != baselineVersion)
            return false;
        return true;
    }

    private static bool ContainsDeviceValue(AndroidDevice device, string value)
        => new[] { device.Model, device.FriendlyName, device.ReportedName, device.DeviceName }
            .Any(candidate => candidate?.Contains(value, StringComparison.OrdinalIgnoreCase) == true);

    private static int? TryGetMajorVersion(string? version)
        => int.TryParse(version?.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
            out var major)
            ? major
            : null;

    private static bool Matches(PackageReferenceEntry entry, string packageName)
        => string.Equals(entry.PackageName, packageName, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(entry.PackagePrefix)
                && packageName.StartsWith(entry.PackagePrefix, StringComparison.OrdinalIgnoreCase));

    private static int Specificity(PackageReferenceEntry entry)
        => (entry.PackageName is null ? 0 : 2) + (entry.PackagePrefix is null ? 0 : 1);

    private static PackageReferenceMatch ToMatch(PackageReferenceCatalogEntry entry)
    {
        var package = entry.Package;
        return new PackageReferenceMatch(
            entry.Baseline.Id,
            entry.Baseline.Name,
            package.Origin,
            package.Generation ?? entry.Baseline.Generation,
            package.Role,
            package.SourceConfidence,
            package.Confidence,
            package.ObservedOn ?? [],
            package.EvidenceSourceIds ?? entry.Baseline.SourceIds ?? [],
            package.FeatureImpacts ?? [],
            package.Dependencies ?? [],
            package.NeededBy ?? [],
            package.ActiveRoleProtection,
            package.Risk,
            package.RecommendedAction,
            package.ReversibleMethod,
            package.Notes);
    }

    private static IReadOnlyList<PackageReferenceProfileMatch> BuildProfileMatches(
        IReadOnlyList<PackageReferenceBaselineDocument> applicableDocuments,
        IReadOnlyList<PackageReferenceAnalysisItem> results)
    {
        var matchedCounts = results
            .SelectMany(result => result.Matches.Select(match => new
            {
                match.BaselineId,
                result.PackageName
            }))
            .GroupBy(match => match.BaselineId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(match => match.PackageName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                StringComparer.OrdinalIgnoreCase);

        return applicableDocuments
            .Select(document => ToProfile(
                document,
                matchedCounts.GetValueOrDefault(document.Baseline.Id)))
            .Where(profile => profile.MatchedPackages > 0)
            .OrderByDescending(profile => profile.MatchedPackages)
            .ThenBy(profile => profile.BaselineName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PackageReferenceProfileMatch ToProfile(
        PackageReferenceBaselineDocument document,
        int matchedPackages)
        => new(
            document.Baseline.Id,
            document.Baseline.Name,
            document.Baseline.Origin,
            document.Baseline.Generation,
            document.Packages.Count,
            matchedPackages,
            document.Baseline.SourceIds ?? []);
}
