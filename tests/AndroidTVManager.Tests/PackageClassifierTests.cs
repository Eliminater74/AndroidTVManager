using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Packages;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class PackageClassifierTests
{
    private readonly PackageClassifier _classifier = new();
    private readonly PackageClassificationContext _context = new(
        new AndroidDevice
        {
            Serial = "tv-1",
            Manufacturer = "Google",
            Model = "Google TV Streamer",
            State = DeviceState.Device
        },
        "com.google.android.tvlauncher",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Known_recommendation_rule_is_caution_with_impact()
    {
        var assessment = _classifier.Classify(Package("com.google.android.tvrecommendations"), _context);

        assessment.Risk.Should().Be(PackageRiskLevel.Caution);
        assessment.Confidence.Should().Be(PackageConfidence.High);
        assessment.Impacts.Should().ContainSingle();
        assessment.RecommendedAction.Should().Be("Disable");
    }

    [Fact]
    public void Unknown_package_stays_unknown_and_unprotected()
    {
        var assessment = _classifier.Classify(Package("com.vendor.undocumented"), _context);

        assessment.Risk.Should().Be(PackageRiskLevel.Unknown);
        assessment.Confidence.Should().Be(PackageConfidence.Low);
        assessment.IsProtected.Should().BeFalse();
    }

    [Fact]
    public void Active_launcher_is_critical_even_without_a_knowledge_rule()
    {
        var package = Package("com.example.launcher") with { IsActiveLauncher = true };

        var assessment = _classifier.Classify(package, _context);

        assessment.Risk.Should().Be(PackageRiskLevel.Critical);
        assessment.IsProtected.Should().BeTrue();
    }

    [Fact]
    public void Developer_verifier_is_critical_and_never_a_debloat_candidate()
    {
        var assessment = _classifier.Classify(Package("com.google.android.verifier"), _context);

        assessment.Risk.Should().Be(PackageRiskLevel.Critical);
        assessment.RecommendedAction.Should().Be("Keep");
    }

    private static PackageInventoryEntry Package(string name)
        => new(name, null, null, null, "0", false, false, true, true, false, [],
            DateTimeOffset.UtcNow, "tv-1", "14", "fingerprint");
}
