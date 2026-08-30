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

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
        => OnPropertyChanged(nameof(SelectionLabel));

    public void UpdatePackage(PackageInventoryEntry package)
    {
        Item = Item with { Package = package };
        OnPropertyChanged(nameof(Item));
        OnPropertyChanged(nameof(Package));
    }

    public DebloatPlanItem ToModel()
        => Item with
        {
            Selected = IsSelected,
            SelectionBlockReason = IsSelected ? null : Item.SelectionBlockReason
        };
}
