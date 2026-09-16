using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Packages;

public sealed class DebloatCleanupService : IDebloatCleanupService
{
    private static readonly string[] ProtectedHighlights =
    [
        "Launcher",
        "Casting",
        "Play services",
        "OTA / updates",
        "Input",
        "Accessibility",
        "Critical Android services"
    ];

    private readonly IDebloatPlanner _planner;

    public DebloatCleanupService(IDebloatPlanner planner)
    {
        _planner = planner;
    }

    public async Task<DebloatCleanupOverview> CreateOverviewAsync(
        string serial,
        AndroidDevice? targetDevice = null,
        CancellationToken cancellationToken = default)
    {
        var source = await _planner.CreatePlanAsync(serial, DebloatPreset.Aggressive, targetDevice, cancellationToken);
        var device = targetDevice ?? new AndroidDevice
        {
            Serial = serial,
            State = DeviceState.Device,
            ConnectionType = ConnectionType.Unknown,
            BuildFingerprint = source.BuildFingerprint
        };
        var detected = DescribeDevice(device, source.ReferenceSummary);
        var unknown = source.Items.Count(item => item.Assessment.Risk == PackageRiskLevel.Unknown);
        var profiles = new[]
        {
            BuildProfile(DebloatCleanupKind.Safe, source, detected, extraBeyond: null),
            BuildProfile(DebloatCleanupKind.Recommended, source, detected, extraBeyond: DebloatPreset.Simple),
            BuildProfile(DebloatCleanupKind.Deep, source, detected, extraBeyond: DebloatPreset.Medium)
        };

        return new(
            source.Serial,
            source.BuildFingerprint,
            device.DisplayLabel,
            detected.Title,
            detected.Detail,
            detected.HasModelProfile,
            source.Items.Count,
            unknown,
            ProtectedHighlights,
            profiles,
            source.ReferenceSummary?.ProfileMatches ?? [],
            source,
            source.CreatedUtc);
    }

    public DebloatPlan CreateProfilePlan(DebloatCleanupOverview overview, DebloatCleanupKind kind)
    {
        if (kind == DebloatCleanupKind.Custom)
            throw new ArgumentOutOfRangeException(nameof(kind), "Custom cleanup uses the detailed planner workflow.");
        var preset = PresetFor(kind);
        return overview.SourcePlan with
        {
            Preset = preset,
            Items = overview.SourcePlan.Items.Select(item => DebloatPresetSelection.Apply(item, preset)).ToArray()
        };
    }

    private static DebloatCleanupProfileState BuildProfile(
        DebloatCleanupKind kind,
        DebloatPlan source,
        DeviceDescription device,
        DebloatPreset? extraBeyond)
    {
        var preset = PresetFor(kind);
        var selected = source.Items.Where(item => DebloatPresetSelection.IsSelected(item.Package, item.Assessment, preset)).ToArray();
        var already = source.Items.Count(item => DebloatPresetSelection.IsAlreadyCleaned(item.Package, item.Assessment, preset));
        var additional = extraBeyond is { } beyond
            ? selected.Count(item => !DebloatPresetSelection.IsSelected(item.Package, item.Assessment, beyond)
                                     && !DebloatPresetSelection.IsAlreadyCleaned(item.Package, item.Assessment, beyond))
            : 0;
        var disable = selected.Count(item => item.Action == DebloatAction.Disable);
        var uninstall = selected.Count(item => item.Action == DebloatAction.UninstallForUser);
        var impacts = selected
            .SelectMany(item => item.Assessment.Impacts.Select(impact => impact.Description))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
        var copy = CopyFor(kind, device, selected.Length, additional);
        var alreadyClean = selected.Length == 0 && already > 0;
        var hasActions = selected.Length > 0;
        return new(
            kind,
            copy.Id,
            copy.DisplayName,
            copy.Description,
            preset,
            Reversibility(selected),
            impacts,
            selected.Length,
            already,
            additional,
            disable,
            uninstall,
            hasActions,
            alreadyClean,
            StatusText(kind, selected.Length, already, additional, device),
            ActionLabel(kind, hasActions, alreadyClean));
    }

    private static (string Id, string DisplayName, string Description) CopyFor(
        DebloatCleanupKind kind,
        DeviceDescription device,
        int actions,
        int additional)
        => kind switch
        {
            DebloatCleanupKind.Safe => (
                "safe",
                "Safe Cleanup",
                actions == 0
                    ? "No Safe Cleanup actions are available for this device."
                    : "Very conservative cleanup. Only high-confidence reversible candidates."),
            DebloatCleanupKind.Recommended => (
                "recommended",
                "Recommended Cleanup",
                device.HasModelProfile
                    ? $"Reviewed cleanup for {device.Title}."
                    : "Reviewed generic Android TV / Google TV cleanup. No model-specific profile applies."),
            DebloatCleanupKind.Deep => (
                "deep",
                "Deep Cleanup",
                additional > 0
                    ? $"Includes Recommended plus {additional} additional reviewed optional component(s)."
                    : "Additional reviewed optional components. May disable optional features."),
            _ => ("custom", "Custom", "Choose individual packages and actions.")
        };

    private static string StatusText(
        DebloatCleanupKind kind,
        int actions,
        int already,
        int additional,
        DeviceDescription device)
    {
        if (actions == 0 && already == 0)
        {
            return kind switch
            {
                DebloatCleanupKind.Safe => "No Safe Cleanup actions are available for this device.",
                DebloatCleanupKind.Recommended when !device.HasModelProfile =>
                    "0 reviewed cleanup actions. No model-specific profile applies to this device.",
                _ => "Nothing left to clean."
            };
        }
        if (actions == 0)
            return "Device already matches this cleanup profile.";
        if (kind == DebloatCleanupKind.Deep && additional > 0)
            return $"{actions} action(s) available · {additional} additional beyond Recommended · {already} already cleaned";
        return $"{actions} action(s) available · {already} already cleaned";
    }

    private static string ActionLabel(DebloatCleanupKind kind, bool hasActions, bool alreadyClean)
    {
        if (alreadyClean)
            return "Clean";
        if (!hasActions)
            return "Nothing to clean";
        return kind switch
        {
            DebloatCleanupKind.Safe => "Run Safe Cleanup",
            DebloatCleanupKind.Recommended => "Run Recommended Cleanup",
            DebloatCleanupKind.Deep => "Review / Run Deep Cleanup",
            _ => "Open Custom Cleanup"
        };
    }

    private static string Reversibility(IReadOnlyList<DebloatPlanItem> selected)
    {
        if (selected.Count == 0)
            return "No changes";
        if (selected.All(item => item.Action == DebloatAction.Disable))
            return "Fully reversible";
        if (selected.Any(item => item.Action == DebloatAction.UninstallForUser))
            return "Partially reversible";
        return "Reversible where Android permits it";
    }

    private static DebloatPreset PresetFor(DebloatCleanupKind kind)
        => kind switch
        {
            DebloatCleanupKind.Safe => DebloatPreset.Simple,
            DebloatCleanupKind.Recommended => DebloatPreset.Medium,
            DebloatCleanupKind.Deep => DebloatPreset.Aggressive,
            _ => DebloatPreset.Medium
        };

    private static DeviceDescription DescribeDevice(AndroidDevice device, PackageReferenceSummary? summary)
    {
        if (DeviceFamilies.IsShieldTv(device))
        {
            var code = FirstCode(device, "darcy", "foster", "mdarcy", "sif") ?? "Shield";
            return new($"NVIDIA Shield TV — {code}", true,
                "Uses the reviewed NVIDIA Shield TV package profile. Launcher, OTA, remote, and casting stay protected.");
        }
        if (DeviceFamilies.IsGoogleTvStreamer4K(device))
            return new("Google TV Streamer 4K — kirkwood", true,
                "Uses the kirkwood overlay. The overlay is Keep-heavy; Recommended may only include reviewed Google TV optionals.");
        if (DeviceFamilies.IsChromecastWithGoogleTv4K(device))
            return new("Chromecast with Google TV 4K — sabrina", true,
                "Uses the sabrina overlay. Chromecast receiver and casting stay protected.");
        if (DeviceFamilies.IsOnnGoogleTv4kBox(device))
            return new("onn. Google TV 4K Box — YOC", true,
                "Uses the YOC overlay. Unreviewed Walmart/onn packages stay manual.");
        if (DeviceFamilies.IsGoogleAtvEmulator(device)
            || ContainsIgnoreCase(device.Model, "sdk")
            || ContainsIgnoreCase(device.Product, "emulator")
            || device.Serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase))
            return new("Generic Android TV profile", false,
                "No model-specific profile applies to this emulator. Only reviewed generic Android TV / Google TV rules are used.");

        var matchedModel = summary?.ProfileMatches?.Any(profile =>
            profile.BaselineId.Contains("shield", StringComparison.OrdinalIgnoreCase)
            || profile.BaselineId.Contains("kirkwood", StringComparison.OrdinalIgnoreCase)
            || profile.BaselineId.Contains("sabrina", StringComparison.OrdinalIgnoreCase)
            || profile.BaselineId.Contains("yoc", StringComparison.OrdinalIgnoreCase)) == true;
        return matchedModel
            ? new(summary!.ProfileMatches!.First().BaselineName, true, "Matched from active reference profiles.")
            : new("Generic Android TV / Google TV", false,
                "No model-specific cleanup profile matched. Only reviewed generic rules will be used.");
    }

    private static string? FirstCode(AndroidDevice device, params string[] codes)
        => codes.FirstOrDefault(code =>
            EqualsIgnoreCase(device.Product, code)
            || EqualsIgnoreCase(device.DeviceName, code)
            || EqualsIgnoreCase(device.Board, code));

    private static bool EqualsIgnoreCase(string? left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsIgnoreCase(string? value, string token)
        => value?.Contains(token, StringComparison.OrdinalIgnoreCase) == true;

    private readonly record struct DeviceDescription(string Title, bool HasModelProfile, string Detail);
}
