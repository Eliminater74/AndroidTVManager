using AndroidTVManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AndroidTVManager.App.ViewModels;

public sealed partial class DebloatPlanItemViewModel : ObservableObject
{
    public DebloatPlanItemViewModel(DebloatPlanItem item)
    {
        Item = item;
        _isSelected = item.Selected;
    }

    public DebloatPlanItem Item { get; private set; }
    public PackageInventoryEntry Package => Item.Package;
    public PackageAssessment Assessment => Item.Assessment;
    public PackageReferenceAnalysisItem? Reference => Item.Reference;
    public bool CanSelect => !Assessment.IsProtected
        && Assessment.Risk != PackageRiskLevel.Critical
        && Package.IsInstalled;
    public bool RequiresManualReview => Assessment.Risk == PackageRiskLevel.Unknown;
    public string SelectionLabel => CanSelect
        ? RequiresManualReview
            ? "Unknown — review before selecting"
            : IsSelected
                ? "Selected"
                : "Not selected"
        : Assessment.IsProtected || Assessment.Risk == PackageRiskLevel.Critical
            ? "Locked for safety"
            : "Not installed for User 0";
    public string ImpactSummary => Assessment.Impacts.Count == 0
        ? "No documented impact is available."
        : string.Join(" · ", Assessment.Impacts.Select(impact => $"{impact.Area}: {impact.Description}"));
    public string ProfileSummary
    {
        get
        {
            if (Reference is null || Reference.Matches.Count == 0)
                return "Profile: no reference match";

            var role = string.IsNullOrWhiteSpace(Reference.Role) ? "role unknown" : Reference.Role;
            return $"Profile: {FormatOrigin(Reference.Origin)} · {role} · {Reference.Matches.Count} match(es)";
        }
    }

    public string EvidenceSummary
    {
        get
        {
            if (Reference is null || Reference.Matches.Count == 0)
                return "No reference evidence on this device profile.";

            var observed = Reference.ObservedOn.Count == 0
                ? "No observed-device list"
                : $"Observed on {string.Join(", ", Reference.ObservedOn)}";
            var dependencies = Reference.Matches
                .SelectMany(match => match.Dependencies.Concat(match.NeededBy))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return dependencies.Length == 0
                ? observed
                : $"{observed} · Related: {string.Join(", ", dependencies)}";
        }
    }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
        => OnPropertyChanged(nameof(SelectionLabel));

    public void UpdatePackage(PackageInventoryEntry package)
    {
        Item = Item with { Package = package };
        OnPropertyChanged(nameof(Item));
        OnPropertyChanged(nameof(Package));
        OnPropertyChanged(nameof(Reference));
        OnPropertyChanged(nameof(ProfileSummary));
        OnPropertyChanged(nameof(EvidenceSummary));
    }

    public DebloatPlanItem ToModel()
        => Item with
        {
            Selected = IsSelected,
            SelectionBlockReason = IsSelected ? null : Item.SelectionBlockReason
        };

    private static string FormatOrigin(PackageOrigin origin)
        => origin switch
        {
            PackageOrigin.AospTvCore => "AOSP TV",
            PackageOrigin.GoogleTvGms => "Google TV",
            PackageOrigin.SocPlatform => "SoC",
            PackageOrigin.RegionalOperator => "Regional",
            PackageOrigin.ThirdParty => "Third-party",
            PackageOrigin.Oem => "OEM",
            _ => "Unknown"
        };
}
