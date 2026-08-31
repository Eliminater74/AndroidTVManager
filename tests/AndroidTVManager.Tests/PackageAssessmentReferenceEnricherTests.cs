using AndroidTVManager.Core.Models;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class PackageAssessmentReferenceEnricherTests
{
    [Fact]
    public void Active_role_reference_promotes_unknown_package_to_critical_keep()
    {
        var assessment = Unknown("com.android.tv.settings");
        var reference = Reference(
            "com.android.tv.settings",
            PackageOrigin.AospTvCore,
            role: "TV settings",
            activeRoleProtection: true);

        var enriched = PackageAssessmentReferenceEnricher.ApplyReferenceEvidence(assessment, reference);

        enriched.Risk.Should().Be(PackageRiskLevel.Critical);
        enriched.Confidence.Should().Be(PackageConfidence.High);
        enriched.Category.Should().Be("TV settings");
        enriched.RecommendedAction.Should().Be("Keep");
        enriched.IsProtected.Should().BeTrue();
        enriched.Reasons.Should().Contain(reason =>
            reason.Contains("Reference profile protects this package", StringComparison.Ordinal));
    }

    [Fact]
    public void Non_protective_reference_enriches_unknown_without_turning_it_safe()
    {
        var assessment = Unknown("com.google.android.apps.tv.netoscope");
        var reference = Reference(
            "com.google.android.apps.tv.netoscope",
            PackageOrigin.GoogleTvGms,
            role: "Chromecast diagnostics",
            activeRoleProtection: false);

        var enriched = PackageAssessmentReferenceEnricher.ApplyReferenceEvidence(assessment, reference);

        enriched.Risk.Should().Be(PackageRiskLevel.Unknown);
        enriched.Confidence.Should().Be(PackageConfidence.High);
        enriched.Category.Should().Be("Chromecast diagnostics");
        enriched.RecommendedAction.Should().Be("Review manually");
        enriched.IsProtected.Should().BeFalse();
        PackageAssessmentReferenceEnricher.IsAutoDebloatAction(enriched).Should().BeFalse();
    }

    [Fact]
    public void Reviewed_reference_recommendation_can_make_unknown_package_a_caution_candidate()
    {
        var assessment = Unknown("com.tcl.initsetup");
        var reference = Reference(
            "com.tcl.initsetup",
            PackageOrigin.Oem,
            role: "TCL setup/customization",
            activeRoleProtection: false,
            risk: PackageRiskLevel.Caution,
            recommendedAction: "Disable",
            sourceConfidence: PackageSourceConfidence.MultiSourceCommunityEvidence);

        var enriched = PackageAssessmentReferenceEnricher.ApplyReferenceEvidence(assessment, reference);

        enriched.Risk.Should().Be(PackageRiskLevel.Caution);
        enriched.Confidence.Should().Be(PackageConfidence.High);
        enriched.Category.Should().Be("TCL setup/customization");
        enriched.RecommendedAction.Should().Be("Disable");
        enriched.IsProtected.Should().BeFalse();
        PackageAssessmentReferenceEnricher.IsAutoDebloatAction(enriched).Should().BeTrue();
    }

    [Fact]
    public void Imported_safe_reference_recommendation_is_capped_at_caution()
    {
        var assessment = Unknown("com.vendor.optional");
        var reference = Reference(
            "com.vendor.optional",
            PackageOrigin.Oem,
            role: "Optional vendor app",
            activeRoleProtection: false,
            risk: PackageRiskLevel.Safe,
            recommendedAction: "Disable",
            sourceConfidence: PackageSourceConfidence.MultiSourceCommunityEvidence,
            confidence: PackageConfidence.Verified);

        var enriched = PackageAssessmentReferenceEnricher.ApplyReferenceEvidence(assessment, reference);

        enriched.Risk.Should().Be(PackageRiskLevel.Caution);
        enriched.Confidence.Should().Be(PackageConfidence.High);
        enriched.RecommendedAction.Should().Be("Disable");
    }

    [Fact]
    public void Keep_recommendations_are_safety_locked_even_when_not_runtime_roles()
    {
        var assessment = Unknown("com.google.android.apps.tv.launcherx") with
        {
            Risk = PackageRiskLevel.HighRisk,
            RecommendedAction = "Keep"
        };

        PackageAssessmentReferenceEnricher.IsSafetyLocked(assessment).Should().BeTrue();
        PackageAssessmentReferenceEnricher.IsAutoDebloatAction(assessment).Should().BeFalse();
    }

    [Fact]
    public void Candidate_reference_recommendation_cannot_downgrade_existing_keep_rule()
    {
        var assessment = Unknown("com.android.settings.intelligence") with
        {
            Risk = PackageRiskLevel.HighRisk,
            Category = "Settings intelligence",
            RecommendedAction = "Keep"
        };
        var reference = Reference(
            "com.android.settings.intelligence",
            PackageOrigin.AospTvCore,
            role: "Settings intelligence",
            activeRoleProtection: false,
            risk: PackageRiskLevel.Caution,
            recommendedAction: "Disable");

        var enriched = PackageAssessmentReferenceEnricher.ApplyReferenceEvidence(assessment, reference);

        enriched.Risk.Should().Be(PackageRiskLevel.HighRisk);
        enriched.RecommendedAction.Should().Be("Keep");
        PackageAssessmentReferenceEnricher.IsSafetyLocked(enriched).Should().BeTrue();
    }

    private static PackageAssessment Unknown(string packageName)
        => new(
            packageName,
            PackageRiskLevel.Unknown,
            PackageConfidence.Low,
            "Unknown",
            "No trusted description is available.",
            "Review manually",
            ["No device-specific knowledge rule matches this package."],
            [],
            false,
            "test");

    private static PackageReferenceAnalysisItem Reference(
        string packageName,
        PackageOrigin origin,
        string role,
        bool activeRoleProtection,
        PackageRiskLevel? risk = null,
        string? recommendedAction = null,
        PackageSourceConfidence sourceConfidence = PackageSourceConfidence.OfficialAosp,
        PackageConfidence confidence = PackageConfidence.High)
        => new(
            packageName,
            origin,
            [
                new PackageReferenceMatch(
                    "baseline",
                    "Reference baseline",
                    origin,
                    "Android TV",
                    role,
                    sourceConfidence,
                    confidence,
                    ["Reference device"],
                    ["source"],
                    [
                        new PackageImpact(
                            role,
                            $"Disabling {packageName} can break {role}.",
                            true)
                    ],
                    [],
                    [],
                    activeRoleProtection,
                    risk,
                    recommendedAction,
                    null,
                    null)
            ],
            ["Reference device"],
            role);
}
