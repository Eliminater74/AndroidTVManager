namespace AndroidTVManager.Core.Models;

public enum PackageRiskLevel
{
    Safe,
    Caution,
    HighRisk,
    Critical,
    Unknown
}

public enum PackageConfidence
{
    Low,
    Medium,
    High,
    Verified
}

public enum PackageSourceConfidence
{
    Unknown,
    RealHardwareDump,
    TestedDeviceReport,
    MultiSourceCommunityEvidence,
    SingleAnecdotalReport,
    GenericManufacturerEvidence
}

public enum PackageOverride
{
    None,
    AlwaysKeep,
    NeverSuggest,
    UserApproved
}

public sealed record PackageInventoryEntry(
    string PackageName,
    string? Label,
    string? VersionName,
    long? VersionCode,
    string? UserId,
    bool IsSystem,
    bool IsUpdatedSystem,
    bool IsEnabled,
    bool IsInstalled,
    bool IsUninstalledForUser,
    IReadOnlyList<string> ApkPaths,
    DateTimeOffset CapturedUtc,
    string Serial,
    string? AndroidVersion,
    string? BuildFingerprint,
    bool IsActiveLauncher = false,
    bool IsDefaultInputMethod = false,
    bool IsEnabledAccessibilityService = false,
    bool IsDeviceOwner = false,
    string? IconPath = null);

public sealed record PackageImpact(
    string Area,
    string Description,
    bool IsKnownDependency = false);

public sealed record PackageAssessment(
    string PackageName,
    PackageRiskLevel Risk,
    PackageConfidence Confidence,
    string Category,
    string Description,
    string RecommendedAction,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<PackageImpact> Impacts,
    bool IsProtected,
    string RulesetVersion,
    PackageOverride Override = PackageOverride.None);

public sealed record PackageKnowledgeRule(
    string Package,
    PackageRiskLevel Risk,
    PackageConfidence Confidence,
    string Category,
    string Description,
    string RecommendedAction,
    IReadOnlyList<PackageImpact> Impacts,
    string? Manufacturer = null,
    string? Product = null,
    string? ModelContains = null,
    int? MinApi = null,
    int? MaxApi = null,
    IReadOnlyList<string>? SourceIds = null,
    string? ObservedModels = null,
    string? EvidenceNotes = null,
    bool HardwareVerified = false,
    string? PackagePrefix = null);

public sealed record PackageKnowledgeSource(
    string Id,
    string Title,
    string Url,
    string SourceType,
    string RetrievedUtc,
    string Attribution,
    PackageSourceConfidence SourceConfidence = PackageSourceConfidence.Unknown);

public enum DebloatPreset
{
    Simple,
    Medium,
    Aggressive
}

public enum DebloatAction
{
    None,
    Disable,
    UninstallForUser
}

public sealed record DebloatPlanItem(
    PackageInventoryEntry Package,
    PackageAssessment Assessment,
    DebloatAction Action,
    bool Selected,
    string? SelectionBlockReason);

public sealed record DebloatPlan(
    string Serial,
    string? BuildFingerprint,
    DateTimeOffset CreatedUtc,
    DebloatPreset Preset,
    string? BaselineSnapshotHash,
    IReadOnlyList<DebloatPlanItem> Items,
    IReadOnlyList<string> Warnings);
