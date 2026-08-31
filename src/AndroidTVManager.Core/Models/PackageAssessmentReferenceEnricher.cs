namespace AndroidTVManager.Core.Models;

public static class PackageAssessmentReferenceEnricher
{
    private const string MissingDescription = "No trusted description is available.";

    public static PackageAssessment ApplyReferenceEvidence(
        PackageAssessment assessment,
        PackageReferenceAnalysisItem? reference)
    {
        if (reference is not { Matches.Count: > 0 })
            return assessment;

        var protectiveMatches = reference.Matches
            .Where(match => match.ActiveRoleProtection || match.Risk == PackageRiskLevel.Critical)
            .ToArray();
        if (protectiveMatches.Length > 0 && !assessment.IsProtected)
            return ApplyReferenceProtection(assessment, reference, protectiveMatches);

        var recommendation = BuildRecommendation(reference);
        if (recommendation is not null && ShouldApplyReferenceRecommendation(assessment, recommendation))
            return ApplyReferenceRecommendation(assessment, reference, recommendation);

        if (assessment.Risk == PackageRiskLevel.Unknown)
            return EnrichUnknownAssessment(assessment, reference);

        return assessment with
        {
            Reasons = MergeReasons(assessment.Reasons, BuildReferenceReasons(reference, reference.Matches)),
            Impacts = MergeImpacts(assessment.Impacts, reference.Matches.SelectMany(match => match.FeatureImpacts))
        };
    }

    public static bool IsAutoDebloatAction(PackageAssessment assessment)
        => string.Equals(assessment.RecommendedAction, "Disable", StringComparison.OrdinalIgnoreCase)
           || string.Equals(assessment.RecommendedAction, "Uninstall for user 0", StringComparison.OrdinalIgnoreCase)
           || string.Equals(assessment.RecommendedAction, "UninstallForUser", StringComparison.OrdinalIgnoreCase);

    public static bool IsSafetyLocked(PackageAssessment assessment)
        => assessment.IsProtected
           || assessment.Risk == PackageRiskLevel.Critical
           || string.Equals(assessment.RecommendedAction, "Keep", StringComparison.OrdinalIgnoreCase);

    private static PackageAssessment ApplyReferenceProtection(
        PackageAssessment assessment,
        PackageReferenceAnalysisItem reference,
        IReadOnlyList<PackageReferenceMatch> protectiveMatches)
    {
        var role = FirstKnownRole(reference, protectiveMatches);
        var reasons = MergeReasons(
            assessment.Reasons,
            BuildReferenceReasons(reference, protectiveMatches)
                .Prepend($"Reference profile protects this package as {role ?? "a core TV component"}; origin is {OriginLabel(reference.Origin)}."));
        var impacts = MergeImpacts(
            assessment.Impacts,
            protectiveMatches.SelectMany(match => match.FeatureImpacts));

        return assessment with
        {
            Risk = PackageRiskLevel.Critical,
            Confidence = Max(assessment.Confidence, protectiveMatches.Max(match => match.Confidence)),
            Category = role ?? assessment.Category,
            Description = BuildReferenceDescription(assessment, reference, role, locked: true),
            RecommendedAction = "Keep",
            Reasons = reasons,
            Impacts = impacts,
            IsProtected = true
        };
    }

    private static PackageAssessment EnrichUnknownAssessment(
        PackageAssessment assessment,
        PackageReferenceAnalysisItem reference)
    {
        var role = FirstKnownRole(reference, reference.Matches);
        return assessment with
        {
            Confidence = Max(assessment.Confidence, reference.Matches.Max(match => match.Confidence)),
            Category = IsUnknownCategory(assessment.Category)
                ? role ?? OriginLabel(reference.Origin)
                : assessment.Category,
            Description = BuildReferenceDescription(assessment, reference, role, locked: false),
            Reasons = MergeReasons(assessment.Reasons, BuildReferenceReasons(reference, reference.Matches)),
            Impacts = MergeImpacts(assessment.Impacts, reference.Matches.SelectMany(match => match.FeatureImpacts))
        };
    }

    private static PackageAssessment ApplyReferenceRecommendation(
        PackageAssessment assessment,
        PackageReferenceAnalysisItem reference,
        ReferenceRecommendation recommendation)
    {
        var role = FirstKnownRole(reference, recommendation.Matches);
        var reasons = MergeReasons(
            assessment.Reasons,
            BuildReferenceReasons(reference, recommendation.Matches)
                .Prepend(BuildRecommendationReason(recommendation)));
        var impacts = MergeImpacts(
            assessment.Impacts,
            recommendation.Matches.SelectMany(match => match.FeatureImpacts));

        return assessment with
        {
            Risk = recommendation.Risk,
            Confidence = Max(assessment.Confidence, recommendation.Confidence),
            Category = role ?? (IsUnknownCategory(assessment.Category)
                ? OriginLabel(reference.Origin)
                : assessment.Category),
            Description = BuildReferenceDescription(assessment, reference, role, recommendation.IsSafetyLocked),
            RecommendedAction = recommendation.Action,
            Reasons = reasons,
            Impacts = impacts,
            IsProtected = assessment.IsProtected || recommendation.IsSafetyLocked
        };
    }

    private static string BuildReferenceDescription(
        PackageAssessment assessment,
        PackageReferenceAnalysisItem reference,
        string? role,
        bool locked)
    {
        if (!string.Equals(assessment.Description, MissingDescription, StringComparison.Ordinal)
            && !IsUnknownCategory(assessment.Category))
            return assessment.Description;

        var subject = role ?? OriginLabel(reference.Origin);
        return locked
            ? $"Reference profiles identify this package as {subject}; keep it installed."
            : $"Reference profiles identify this package as {subject}; no reviewed debloat action is assigned.";
    }

    private static IEnumerable<string> BuildReferenceReasons(
        PackageReferenceAnalysisItem reference,
        IEnumerable<PackageReferenceMatch> matches)
    {
        foreach (var match in matches)
        {
            yield return $"Reference match [{match.BaselineId}]: {match.BaselineName} "
                         + $"({match.SourceConfidence}, {match.Confidence}); origin is {OriginLabel(reference.Origin)}.";
        }
    }

    private static ReferenceRecommendation? BuildRecommendation(PackageReferenceAnalysisItem reference)
    {
        var matches = reference.Matches
            .Where(match => match.Risk.HasValue && !string.IsNullOrWhiteSpace(match.RecommendedAction))
            .ToArray();
        if (matches.Length == 0)
            return null;

        var actionableMatches = matches
            .Where(match => IsKeepAction(match.RecommendedAction) || IsCandidateAction(match.RecommendedAction))
            .ToArray();
        if (actionableMatches.Length == 0)
            return null;

        var hasKeep = actionableMatches.Any(match => IsKeepAction(match.RecommendedAction));
        var hasCandidate = actionableMatches.Any(match => IsCandidateAction(match.RecommendedAction));
        var conflict = hasKeep && hasCandidate;
        var risk = actionableMatches
            .Select(match => NormalizeImportedRisk(match.Risk!.Value, match.RecommendedAction!))
            .OrderByDescending(RiskScore)
            .First();
        if ((hasKeep || conflict) && RiskScore(risk) < RiskScore(PackageRiskLevel.HighRisk))
            risk = PackageRiskLevel.HighRisk;

        var action = hasKeep || conflict
            ? "Keep"
            : actionableMatches.Select(match => match.RecommendedAction!)
                .First(IsCandidateAction);
        var confidence = CapImportedConfidence(actionableMatches.Max(match => match.Confidence));

        return new ReferenceRecommendation(risk, confidence, action, hasKeep || conflict, conflict, actionableMatches);
    }

    private static bool ShouldApplyReferenceRecommendation(
        PackageAssessment assessment,
        ReferenceRecommendation recommendation)
    {
        if (recommendation.IsSafetyLocked)
            return true;
        if (IsSafetyLocked(assessment))
            return false;
        if (assessment.Risk == PackageRiskLevel.Unknown)
            return true;
        if (!IsAutoDebloatAction(assessment) && IsCandidateAction(recommendation.Action))
            return true;
        return RiskScore(recommendation.Risk) > RiskScore(assessment.Risk);
    }

    private static string BuildRecommendationReason(ReferenceRecommendation recommendation)
    {
        var prefix = recommendation.Conflict
            ? "Reference recommendation has conflicting evidence; defaulting to Keep"
            : "Reference recommendation applied";
        return $"{prefix}: {recommendation.Risk} / {recommendation.Action} from {recommendation.Matches.Count} recommendation match(es).";
    }

    private static string? FirstKnownRole(
        PackageReferenceAnalysisItem reference,
        IEnumerable<PackageReferenceMatch> matches)
        => reference.Role
           ?? matches.Select(match => match.Role)
               .FirstOrDefault(role => !string.IsNullOrWhiteSpace(role));

    private static string OriginLabel(PackageOrigin origin)
        => origin switch
        {
            PackageOrigin.AospTvCore => "AOSP Android TV core",
            PackageOrigin.GoogleTvGms => "Google TV/GMS",
            PackageOrigin.Oem => "OEM package",
            PackageOrigin.SocPlatform => "SoC/platform package",
            PackageOrigin.RegionalOperator => "regional/operator package",
            PackageOrigin.ThirdParty => "third-party package",
            _ => "unknown origin"
        };

    private static bool IsUnknownCategory(string category)
        => string.Equals(category, "Unknown", StringComparison.OrdinalIgnoreCase)
           || string.Equals(category, "Unreviewed system package", StringComparison.OrdinalIgnoreCase);

    private static PackageRiskLevel NormalizeImportedRisk(
        PackageRiskLevel risk,
        string recommendedAction)
    {
        if (risk == PackageRiskLevel.Safe && !IsKeepAction(recommendedAction))
            return PackageRiskLevel.Caution;
        return risk;
    }

    private static PackageConfidence CapImportedConfidence(PackageConfidence confidence)
        => confidence == PackageConfidence.Verified ? PackageConfidence.High : confidence;

    private static bool IsKeepAction(string? action)
        => string.Equals(action, "Keep", StringComparison.OrdinalIgnoreCase);

    private static bool IsCandidateAction(string? action)
        => string.Equals(action, "Disable", StringComparison.OrdinalIgnoreCase)
           || string.Equals(action, "Uninstall for user 0", StringComparison.OrdinalIgnoreCase)
           || string.Equals(action, "UninstallForUser", StringComparison.OrdinalIgnoreCase);

    private static int RiskScore(PackageRiskLevel risk)
        => risk switch
        {
            PackageRiskLevel.Critical => 5,
            PackageRiskLevel.HighRisk => 4,
            PackageRiskLevel.Caution => 3,
            PackageRiskLevel.Safe => 2,
            _ => 1
        };

    private static IReadOnlyList<string> MergeReasons(
        IReadOnlyList<string> existing,
        IEnumerable<string> incoming)
        => existing.Concat(incoming)
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<PackageImpact> MergeImpacts(
        IReadOnlyList<PackageImpact> existing,
        IEnumerable<PackageImpact> incoming)
        => existing.Concat(incoming)
            .DistinctBy(impact => (impact.Area, impact.Description, impact.IsKnownDependency))
            .ToArray();

    private static PackageConfidence Max(PackageConfidence left, PackageConfidence right)
        => left > right ? left : right;

    private sealed record ReferenceRecommendation(
        PackageRiskLevel Risk,
        PackageConfidence Confidence,
        string Action,
        bool IsSafetyLocked,
        bool Conflict,
        IReadOnlyList<PackageReferenceMatch> Matches);
}
