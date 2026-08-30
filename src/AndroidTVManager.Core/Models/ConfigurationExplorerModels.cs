namespace AndroidTVManager.Core.Models;

public enum ConfigurationSource
{
    Runtime,
    System,
    Vendor,
    Product,
    SystemExt,
    Odm
}

public enum ConfigurationValueStatus
{
    Match,
    Conflict,
    RuntimeOnly,
    FileOnly,
    Unavailable
}

public sealed record ConfigurationValueSource(
    ConfigurationSource Source,
    string? Value,
    bool IsAvailable = true,
    string? Error = null)
{
    public string SourceName => Source switch
    {
        ConfigurationSource.SystemExt => "System Ext",
        _ => Source.ToString()
    };

    public string DisplayValue => Value ?? Error ?? "Unavailable";
}

public sealed record ConfigurationProperty(
    string Name,
    string Category,
    string DisplayName,
    string? RuntimeValue,
    IReadOnlyList<ConfigurationValueSource> StaticValues,
    ConfigurationValueStatus Status,
    bool IsRedacted = false)
{
    public string DisplayValue => IsRedacted ? "[redacted]" : RuntimeValue
        ?? StaticValues.FirstOrDefault(value => value.IsAvailable)?.Value
        ?? "Unavailable";

    public string SourceSummary
        => string.Join(", ", StaticValues
            .Where(value => value.Value is not null || !value.IsAvailable)
            .Select(value => value.IsAvailable
                ? value.SourceName
                : $"{value.SourceName} unavailable")
            .Prepend(RuntimeValue is null ? null : "Runtime")
            .Where(value => value is not null));

    public IReadOnlyList<ConfigurationValueSource> DisplaySources
        => StaticValues.Where(value => value.Value is not null || !value.IsAvailable).ToArray();

    public string StatusLabel => Status switch
    {
        ConfigurationValueStatus.RuntimeOnly => "Runtime only",
        ConfigurationValueStatus.FileOnly => "File only",
        _ => Status.ToString()
    };

    public string RedactionLabel => IsRedacted ? "Value redacted for security" : string.Empty;
}

public sealed record ConfigurationSection(
    string Name,
    IReadOnlyList<ConfigurationProperty> Properties,
    InspectionSectionState State,
    IReadOnlyList<InspectionCommandEvidence> Evidence,
    string? Message = null);

public sealed record ConfigurationSnapshot(
    string Serial,
    DateTimeOffset CapturedUtc,
    string? FriendlyDeviceName,
    string? Manufacturer,
    string? Model,
    string? BuildFingerprint,
    string? AndroidVersion,
    int? ApiLevel,
    string? SecurityPatch,
    IReadOnlyList<ConfigurationSection> Sections,
    IReadOnlyList<InspectionCommandEvidence> Commands)
{
    public IReadOnlyList<ConfigurationProperty> Properties
        => Sections.SelectMany(section => section.Properties).ToArray();
}

public sealed record ConfigurationPropertyChange(
    string Name,
    string Category,
    string? PreviousValue,
    string? CurrentValue,
    ConfigurationValueStatus PreviousStatus,
    ConfigurationValueStatus CurrentStatus,
    IReadOnlyList<ConfigurationValueSource>? PreviousSources = null,
    IReadOnlyList<ConfigurationValueSource>? CurrentSources = null)
{
    public bool RuntimeChanged
    {
        get
        {
            var previous = PreviousSources?.FirstOrDefault(source => source.Source == ConfigurationSource.Runtime)?.Value;
            var current = CurrentSources?.FirstOrDefault(source => source.Source == ConfigurationSource.Runtime)?.Value;
            return !string.Equals(previous, current, StringComparison.Ordinal);
        }
    }

    public IReadOnlyList<ConfigurationSource> ChangedSources
        => Enum.GetValues<ConfigurationSource>()
            .Where(source => ValueFor(PreviousSources, source) != ValueFor(CurrentSources, source))
            .ToArray();

    public string ChangeSummary
        => PreviousValue is not null && CurrentValue is null
            ? "Property disappeared"
            : PreviousValue is null && CurrentValue is not null
                ? "Property appeared"
                : RuntimeChanged
                    ? "Runtime value changed"
                    : ChangedSources.Count == 1
                        ? $"{ChangedSources[0]} file changed"
                        : "Configuration source changed";

    private static string? ValueFor(
        IReadOnlyList<ConfigurationValueSource>? values,
        ConfigurationSource source)
        => values?.FirstOrDefault(value => value.Source == source)?.Value;
}

public sealed record ConfigurationSnapshotDiff(
    string Serial,
    DateTimeOffset PreviousCapturedUtc,
    DateTimeOffset CurrentCapturedUtc,
    IReadOnlyList<ConfigurationPropertyChange> Changes)
{
    public int ChangedCount => Changes.Count;
    public int ConflictCount => Changes.Count(change =>
        change.CurrentStatus == ConfigurationValueStatus.Conflict);
}

public sealed record ConfigurationInspectionProgress(
    string Category,
    int CompletedCategories,
    int TotalCategories,
    InspectionSectionState State);

public static class ConfigurationSnapshotComparer
{
    public static ConfigurationSnapshotDiff Compare(
        ConfigurationSnapshot previous,
        ConfigurationSnapshot current)
    {
        var oldValues = previous.Properties.ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);
        var newValues = current.Properties.ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);
        var changes = oldValues.Keys
            .Union(newValues.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(name =>
            {
                oldValues.TryGetValue(name, out var oldProperty);
                newValues.TryGetValue(name, out var newProperty);
                return newProperty?.DisplayValue != oldProperty?.DisplayValue
                    || newProperty?.Status != oldProperty?.Status
                    || SourcesDiffer(oldProperty, newProperty)
                    ? new ConfigurationPropertyChange(
                        newProperty?.Name ?? oldProperty!.Name,
                        newProperty?.Category ?? oldProperty!.Category,
                        oldProperty?.DisplayValue,
                        newProperty?.DisplayValue,
                        oldProperty?.Status ?? ConfigurationValueStatus.Unavailable,
                        newProperty?.Status ?? ConfigurationValueStatus.Unavailable,
                        oldProperty is null
                            ? null
                            : oldProperty.StaticValues
                                .Prepend(new ConfigurationValueSource(
                                    ConfigurationSource.Runtime,
                                    oldProperty.RuntimeValue))
                                .ToArray(),
                        newProperty is null
                            ? null
                            : newProperty.StaticValues
                                .Prepend(new ConfigurationValueSource(
                                    ConfigurationSource.Runtime,
                                    newProperty.RuntimeValue))
                                .ToArray())
                    : null;
            })
            .Where(change => change is not null)
            .Cast<ConfigurationPropertyChange>()
            .OrderBy(change => change.Category)
            .ThenBy(change => change.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new(
            current.Serial,
            previous.CapturedUtc,
            current.CapturedUtc,
            changes);
    }

    private static bool SourcesDiffer(
        ConfigurationProperty? previous,
        ConfigurationProperty? current)
    {
        var oldSources = previous?.StaticValues
            .Prepend(new ConfigurationValueSource(ConfigurationSource.Runtime, previous.RuntimeValue))
            ?? [];
        var newSources = current?.StaticValues
            .Prepend(new ConfigurationValueSource(ConfigurationSource.Runtime, current.RuntimeValue))
            ?? [];
        return oldSources.Count() != newSources.Count()
            || oldSources.Zip(newSources).Any(pair =>
                pair.First.Source != pair.Second.Source
                || pair.First.Value != pair.Second.Value
                || pair.First.IsAvailable != pair.Second.IsAvailable
                || pair.First.Error != pair.Second.Error);
    }
}
