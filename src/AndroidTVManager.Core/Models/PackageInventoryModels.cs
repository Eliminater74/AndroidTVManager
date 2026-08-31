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
    OfficialAosp,
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

public enum PackageOrigin
{
    Unknown,
    AospTvCore,
    GoogleTvGms,
    Oem,
    SocPlatform,
    RegionalOperator,
    ThirdParty
}

public sealed record PackageReferenceBaseline(
    string Id,
    string Name,
    PackageOrigin Origin,
    string Generation,
    string? AndroidVersion = null,
    string? Manufacturer = null,
    string? DeviceFamily = null,
    string? PlatformFamily = null,
    IReadOnlyList<string>? SourceIds = null);

public sealed record PackageReferenceEntry(
    string? PackageName,
    string? DisplayName,
    PackageOrigin Origin,
    string? Generation,
    string? Manufacturer = null,
    string? DeviceFamily = null,
    string? PlatformFamily = null,
    string? ModuleName = null,
    IReadOnlyList<string>? Partitions = null,
    string? FirstReferenceVersion = null,
    string? LastReferenceVersion = null,
    IReadOnlyList<string>? ObservedOn = null,
    IReadOnlyList<string>? EvidenceSourceIds = null,
    PackageSourceConfidence SourceConfidence = PackageSourceConfidence.Unknown,
    PackageConfidence Confidence = PackageConfidence.Low,
    string? Role = null,
    IReadOnlyList<PackageImpact>? FeatureImpacts = null,
    IReadOnlyList<string>? Dependencies = null,
    IReadOnlyList<string>? NeededBy = null,
    bool ActiveRoleProtection = false,
    PackageRiskLevel? Risk = null,
    string? RecommendedAction = null,
    string? ReversibleMethod = null,
    string? Notes = null,
    string? PackagePrefix = null);

public sealed record PackageReferenceCatalogEntry(
    PackageReferenceBaseline Baseline,
    PackageReferenceEntry Package);

public sealed record PackageReferenceBaselineDocument(
    PackageReferenceBaseline Baseline,
    IReadOnlyList<PackageReferenceEntry> Packages);

public sealed record PackageReferenceMatch(
    string BaselineId,
    string BaselineName,
    PackageOrigin Origin,
    string? Generation,
    string? Role,
    PackageSourceConfidence SourceConfidence,
    PackageConfidence Confidence,
    IReadOnlyList<string> ObservedOn,
    IReadOnlyList<string> EvidenceSourceIds,
    IReadOnlyList<PackageImpact> FeatureImpacts,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> NeededBy,
    bool ActiveRoleProtection,
    PackageRiskLevel? Risk,
    string? RecommendedAction,
    string? ReversibleMethod,
    string? Notes);

public sealed record PackageReferenceAnalysisItem(
    string PackageName,
    PackageOrigin Origin,
    IReadOnlyList<PackageReferenceMatch> Matches,
    IReadOnlyList<string> ObservedOn,
    string? Role);

public sealed record PackageOriginCount(
    PackageOrigin Origin,
    int Count);

public sealed record PackageReferenceSummary(
    int TotalPackages,
    IReadOnlyList<PackageOriginCount> OriginCounts,
    int BaselineMatches,
    int UnknownPackages);

public sealed record PackageReferenceAnalysis(
    string Serial,
    DateTimeOffset AnalyzedUtc,
    IReadOnlyList<PackageReferenceAnalysisItem> Packages,
    PackageReferenceSummary Summary);

public sealed record ReferenceDeviceIdentity(
    string? Manufacturer,
    string? Brand,
    string? Model,
    string? Device,
    string? Product,
    string? AndroidVersion,
    int? ApiLevel,
    string? SecurityPatch,
    string? BuildId,
    string? BuildType,
    string? BuildFingerprint,
    string? PlatformFamily = null);

public sealed record ReferencePackageDumpPackage(
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
    bool IsActiveLauncher,
    bool IsDefaultInputMethod,
    bool IsEnabledAccessibilityService,
    bool IsDeviceOwner);

public sealed record ReferencePackageDump(
    int SchemaVersion,
    string ExportKind,
    DateTimeOffset CapturedUtc,
    ReferenceDeviceIdentity Device,
    IReadOnlyList<ReferencePackageDumpPackage> Packages);

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
