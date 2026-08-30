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

    [Fact]
    public void Philips_demo_and_recommendation_rules_include_feature_impacts()
    {
        var context = Context("Philips", "55PUS9000");

        var demo = _classifier.Classify(Package("fusion.android.tv.demo"), context);
        var recommendations = _classifier.Classify(Package("com.smartdevice.recommendation"), context);

        demo.Risk.Should().Be(PackageRiskLevel.Caution);
        demo.Confidence.Should().Be(PackageConfidence.High);
        demo.Impacts.Should().ContainSingle(impact => impact.Area == "Demo mode");
        demo.RecommendedAction.Should().Be("Disable");
        recommendations.Risk.Should().Be(PackageRiskLevel.Caution);
        recommendations.Impacts.Should().ContainSingle(impact => impact.Area == "Home screen");
    }

    [Fact]
    public void Privileged_vendor_services_are_critical_only_for_their_matching_vendor()
    {
        var hisense = _classifier.Classify(Package("com.vt.tvservice"), Context("Hisense", "U8"));
        var philips = _classifier.Classify(Package("com.realtek.power"), Context("Philips", "OLED"));
        var wrongVendor = _classifier.Classify(Package("com.vt.tvservice"), Context("Philips", "OLED"));

        hisense.Risk.Should().Be(PackageRiskLevel.Critical);
        hisense.RecommendedAction.Should().Be("Keep");
        philips.Risk.Should().Be(PackageRiskLevel.Critical);
        philips.RecommendedAction.Should().Be("Keep");
        wrongVendor.Risk.Should().Be(PackageRiskLevel.Unknown);
    }

    private static PackageInventoryEntry Package(string name)
        => new(name, null, null, null, "0", false, false, true, true, false, [],
            DateTimeOffset.UtcNow, "tv-1", "14", "fingerprint");

    private static PackageClassificationContext Context(string manufacturer, string model)
        => new(
            new AndroidDevice
            {
                Serial = "tv-1",
                Manufacturer = manufacturer,
                Model = model,
                State = DeviceState.Device
            },
            null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}
