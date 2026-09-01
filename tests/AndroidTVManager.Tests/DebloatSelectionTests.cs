using AndroidTVManager.App.ViewModels;
using AndroidTVManager.Core.Models;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DebloatSelectionTests
{
    [Fact]
    public void Unknown_private_package_can_be_manually_selected_but_is_not_preselected()
    {
        var package = Package("com.purefusioniptv");
        var assessment = new PackageAssessment(
            package.PackageName,
            PackageRiskLevel.Unknown,
            PackageConfidence.Low,
            "Unknown",
            "No trusted rule matches this package.",
            "Review manually",
            ["No device-specific knowledge rule matches this package."],
            [],
            false,
            "test");
        var item = new DebloatPlanItem(
            package,
            assessment,
            DebloatAction.Disable,
            false,
            "Not auto-selected: unknown package. You may select it manually after review.");
        var viewModel = new DebloatPlanItemViewModel(item);

        viewModel.CanSelect.Should().BeTrue();
        viewModel.IsSelected.Should().BeFalse();
        viewModel.RequiresManualReview.Should().BeTrue();

        viewModel.IsSelected = true;

        viewModel.ToModel().Selected.Should().BeTrue();
        viewModel.ToModel().SelectionBlockReason.Should().BeNull();
    }

    [Fact]
    public void Critical_package_remains_locked_even_when_a_plan_is_displayed()
    {
        var package = Package("com.android.systemui");
        var assessment = new PackageAssessment(
            package.PackageName,
            PackageRiskLevel.Critical,
            PackageConfidence.Verified,
            "System UI",
            "Android system user interface.",
            "Keep",
            [],
            [],
            true,
            "test");
        var viewModel = new DebloatPlanItemViewModel(new DebloatPlanItem(
            package,
            assessment,
            DebloatAction.Disable,
            false,
            "Locked: critical package or active device role."));

        viewModel.CanSelect.Should().BeFalse();
        viewModel.SelectionLabel.Should().Be("Locked for safety");
    }

    [Fact]
    public void Keep_recommendation_remains_locked_even_without_runtime_protection()
    {
        var package = Package("com.google.android.apps.tv.launcherx");
        var assessment = new PackageAssessment(
            package.PackageName,
            PackageRiskLevel.HighRisk,
            PackageConfidence.High,
            "Google TV launcher",
            "Google TV launcher component.",
            "Keep",
            [],
            [],
            false,
            "test");
        var viewModel = new DebloatPlanItemViewModel(new DebloatPlanItem(
            package,
            assessment,
            DebloatAction.Disable,
            false,
            "Locked: critical package, Keep rule, or active device role."));

        viewModel.CanSelect.Should().BeFalse();
        viewModel.SelectionLabel.Should().Be("Locked for safety");
    }

    [Fact]
    public void Disabled_candidate_is_not_selectable()
    {
        var package = Package("com.android.dreams.basic") with { IsEnabled = false };
        var assessment = new PackageAssessment(
            package.PackageName,
            PackageRiskLevel.Caution,
            PackageConfidence.Medium,
            "Screensaver",
            "Basic screensaver.",
            "Disable",
            [],
            [],
            false,
            "test");
        var viewModel = new DebloatPlanItemViewModel(new DebloatPlanItem(
            package,
            assessment,
            DebloatAction.Disable,
            false,
            "Package is already disabled."));

        viewModel.CanSelect.Should().BeFalse();
        viewModel.SelectionLabel.Should().Be("Already disabled");
        viewModel.ActionSummary.Should().Be("Action: disable package");
    }

    private static PackageInventoryEntry Package(string name)
        => new(name, null, null, null, "0", false, false, true, true, false, [],
            DateTimeOffset.UtcNow, "tv-1", "14", "fingerprint");
}
