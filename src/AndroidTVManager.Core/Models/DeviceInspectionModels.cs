namespace AndroidTVManager.Core.Models;

public enum CapabilityState
{
    Supported,
    Unsupported,
    Partial,
    Unknown,
    Unavailable,
    PermissionDenied
}

public enum EvidenceConfidence
{
    Low,
    Medium,
    High,
    Verified
}

public sealed record CapabilityEvidence(
    string Source,
    string? ObservedValue,
    string Explanation,
    EvidenceConfidence Confidence = EvidenceConfidence.High);

public sealed record DeviceCapability(
    string Name,
    CapabilityState State,
    string Summary,
    IReadOnlyList<CapabilityEvidence> Evidence,
    bool IsInferred = false);

public enum InspectionSectionState
{
    NotStarted,
    Running,
    Completed,
    Partial,
    Failed,
    Canceled
}

public sealed record InspectionCommandEvidence(
    string Command,
    InspectionSectionState State,
    string? StandardOutput,
    string? StandardError,
    int? ExitCode,
    TimeSpan Duration,
    string? ErrorMessage = null);

public sealed record InspectionSection<T>(
    string Name,
    InspectionSectionState State,
    T? Value,
    IReadOnlyList<InspectionCommandEvidence> Evidence,
    string? Message = null);

public sealed record CpuInfo(
    string? Architecture,
    string? PrimaryAbi,
    IReadOnlyList<string> SupportedAbis,
    int? LogicalCoreCount,
    string? Implementer,
    string? Part,
    string? Hardware,
    string? BoardPlatform,
    string? DetectedSoC,
    string? InferredSoC,
    string? FrequencySummary,
    string? Governor)
{
    public string SupportedAbiSummary => string.Join(", ", SupportedAbis);
}

public sealed record MemoryInfo(
    long? TotalBytes,
    long? AvailableBytes,
    long? FreeBytes,
    long? CachedBytes,
    long? SwapTotalBytes,
    long? SwapFreeBytes,
    long? ZramTotalBytes);

public sealed record GraphicsInfo(
    string? Renderer,
    string? Vendor,
    string? OpenGlEsVersion,
    string? VulkanVersion,
    string? HardwareComposer,
    string? Driver);

public sealed record DisplayInfo(
    string? CurrentResolution,
    string? PhysicalResolution,
    int? Density,
    string? RefreshRate,
    IReadOnlyList<string> SupportedModes,
    IReadOnlyList<string> HdrCapabilities,
    string? ColorMode,
    string? Orientation);

public sealed record StorageVolume(
    string MountPoint,
    long? TotalBytes,
    long? UsedBytes,
    long? AvailableBytes,
    string? FileSystem);

public sealed record StorageInfo(IReadOnlyList<StorageVolume> Volumes);

public sealed record SecurityInfo(
    string? SelinuxState,
    string? VerifiedBootState,
    string? BootloaderDeviceState,
    string? FlashLockState,
    string? BuildType,
    string? BuildTags,
    CapabilityState RootAvailability,
    CapabilityState AdbRoot,
    IReadOnlyList<CapabilityEvidence> Evidence,
    string? OemUnlockAllowed = null);

public sealed record BootInfo(
    bool? IsAbDevice,
    bool? IsVirtualAb,
    bool? HasDynamicPartitions,
    string? CurrentSlot,
    string? SuperPartition,
    string? SystemAsRoot,
    IReadOnlyList<CapabilityEvidence> Evidence);

public enum GsiAssessment
{
    LikelySupported,
    PossiblySupported,
    NotDetected,
    Unknown
}

public sealed record GsiInfo(
    CapabilityState Treble,
    CapabilityState DynamicPartitions,
    CapabilityState VirtualAb,
    CapabilityState GsiTool,
    CapabilityState DsuService,
    GsiAssessment Assessment,
    IReadOnlyList<CapabilityEvidence> Evidence);

public sealed record NetworkInfo(
    IReadOnlyList<string> Addresses,
    IReadOnlyList<string> Interfaces,
    string? Hostname,
    string? Gateway,
    IReadOnlyList<string> DnsServers,
    string? LinkSummary,
    IReadOnlyList<string>? MacAddresses = null)
{
    public string AddressSummary => string.Join(", ", Addresses);
    public string InterfaceSummary => string.Join(", ", Interfaces);
    public string MacAddressSummary => string.Join(", ", MacAddresses ?? []);
    public string DnsSummary => string.Join(", ", DnsServers);
}

public sealed record RuntimeInfo(
    string? Uptime,
    string? BatterySummary,
    string? TemperatureSummary,
    string? RunningProcesses,
    string? RunningServices);

public sealed record PackageSummaryInfo(
    int? TotalPackages,
    string? PackageListSummary,
    IReadOnlyList<string>? PackageNames = null,
    int? InstalledCount = null,
    int? DisabledCount = null,
    int? EnabledCount = null,
    int? UserPackageCount = null,
    int? SystemPackageCount = null,
    int? UninstalledForUserCount = null,
    int? ActiveLauncherCount = null,
    int? AccessibilityServiceCount = null,
    int? DeviceOwnerCount = null);

public sealed record ServiceSummaryInfo(
    int? RunningServiceCount,
    string? ServiceListSummary,
    IReadOnlyList<ServiceInfo>? Entries = null);

public enum AdvancedFlowAvailability
{
    Available,
    NotDetected,
    Unknown
}

public enum AdvancedFlowState
{
    Enabled,
    Disabled,
    Unknown
}

public enum WaitingPeriodState
{
    NotApplicableToAdb,
    Pending,
    Completed,
    Unknown
}

public sealed record DeveloperVerificationInfo(
    bool? VerifierPresent,
    string? VerifierPackageVersion,
    CapabilityState AdbInstallAvailability,
    AdvancedFlowAvailability AdvancedFlowAvailability,
    AdvancedFlowState AdvancedFlowState,
    WaitingPeriodState WaitingPeriod,
    bool StateDetectable,
    IReadOnlyList<CapabilityEvidence> Evidence,
    DateTimeOffset LastCheckedUtc);

public sealed record DeviceInspectionResult(
    string Serial,
    DateTimeOffset CapturedUtc,
    InspectionSection<AndroidDevice> Overview,
    InspectionSection<CpuInfo> Cpu,
    InspectionSection<MemoryInfo> Memory,
    InspectionSection<GraphicsInfo> Graphics,
    InspectionSection<DisplayInfo> Display,
    InspectionSection<StorageInfo> Storage,
    InspectionSection<SecurityInfo> Security,
    InspectionSection<BootInfo> Boot,
    InspectionSection<GsiInfo> Gsi,
    InspectionSection<NetworkInfo> Network,
    InspectionSection<RuntimeInfo> Runtime,
    InspectionSection<IReadOnlyList<string>> Features,
    InspectionSection<PackageSummaryInfo> Packages,
    InspectionSection<ServiceSummaryInfo> Services,
    InspectionSection<DeveloperVerificationInfo> DeveloperVerification,
    IReadOnlyDictionary<string, string> RawProperties,
    IReadOnlyList<DeviceCapability> Capabilities,
    IReadOnlyList<InspectionCommandEvidence> Commands,
    InspectionSection<OemUnlockInfo>? OemUnlock = null,
    InspectionSection<RootInfo>? Root = null,
    InspectionSection<BluetoothInfo>? Bluetooth = null,
    InspectionSection<HdmiInfo>? Hdmi = null,
    InspectionSection<DrmInfo>? Drm = null);
