namespace AndroidTVManager.Core.Models;

public static class DebloatPresetSelection
{
    public static bool IsRiskAllowed(PackageRiskLevel risk, DebloatPreset preset)
        => risk switch
        {
            PackageRiskLevel.Safe => true,
            PackageRiskLevel.Caution => preset is DebloatPreset.Medium or DebloatPreset.Aggressive,
            PackageRiskLevel.HighRisk => preset == DebloatPreset.Aggressive,
            _ => false
        };

    public static bool IsEligibleCandidate(
        PackageInventoryEntry package,
        PackageAssessment assessment,
        DebloatPreset preset)
    {
        if (assessment.Override is PackageOverride.AlwaysKeep or PackageOverride.NeverSuggest)
            return false;
        if (PackageAssessmentReferenceEnricher.IsSafetyLocked(assessment))
            return false;
        if (assessment.Override == PackageOverride.UserApproved)
            return true;
        return PackageAssessmentReferenceEnricher.IsAutoDebloatAction(assessment)
            && IsRiskAllowed(assessment.Risk, preset);
    }

    public static bool IsSelected(
        PackageInventoryEntry package,
        PackageAssessment assessment,
        DebloatPreset preset)
        => IsEligibleCandidate(package, assessment, preset)
            && package.IsInstalled
            && package.IsEnabled;

    public static bool IsAlreadyCleaned(
        PackageInventoryEntry package,
        PackageAssessment assessment,
        DebloatPreset preset)
        => IsEligibleCandidate(package, assessment, preset)
            && package.IsInstalled
            && !package.IsEnabled;

    public static DebloatPlanItem Apply(DebloatPlanItem item, DebloatPreset preset)
    {
        var selected = IsSelected(item.Package, item.Assessment, preset);
        return item with
        {
            Selected = selected,
            SelectionBlockReason = selected
                ? null
                : BlockReason(item.Package, item.Assessment, preset, selected: false)
        };
    }

    public static string? BlockReason(
        PackageInventoryEntry package,
        PackageAssessment assessment,
        DebloatPreset preset,
        bool selected)
    {
        if (selected)
            return null;
        if (PackageAssessmentReferenceEnricher.IsSafetyLocked(assessment)
            || assessment.Override is PackageOverride.AlwaysKeep or PackageOverride.NeverSuggest)
            return "Locked: critical package, Keep rule, or active device role.";
        if (!package.IsInstalled)
            return "Package is not currently installed for the user.";
        if (!package.IsEnabled)
            return "Package is already disabled.";
        if (assessment.Risk == PackageRiskLevel.Unknown)
            return "Not auto-selected: unknown package. You may select it manually after review.";
        if (!PackageAssessmentReferenceEnricher.IsAutoDebloatAction(assessment)
            && assessment.Override != PackageOverride.UserApproved)
            return $"Not auto-selected: reviewed action is {assessment.RecommendedAction}.";
        return $"Not included in {preset} preset.";
    }
}
