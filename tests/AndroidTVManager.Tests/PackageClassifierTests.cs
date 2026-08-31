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
    public void Chromecast_platform_rule_reports_source_and_keeps_platform_service_protected()
    {
        var context = Context("Google", "Chromecast with Google TV", product: "sabrina");

        var assessment = _classifier.Classify(
            Package("com.google.android.chromecast.chromecastservice"),
            context);

        assessment.Risk.Should().Be(PackageRiskLevel.Critical);
        assessment.RecommendedAction.Should().Be("Keep");
        assessment.Reasons.Should().Contain(reason => reason.Contains("uad-chromecast-100", StringComparison.Ordinal));
        assessment.Reasons.Should().Contain(reason => reason.Contains("not hardware-verified", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Vendor_rules_cover_shield_tcl_and_cultraview_without_marking_candidates_safe()
    {
        var shield = _classifier.Classify(
            Package("com.nvidia.nvaudiosvc"),
            Context("NVIDIA", "SHIELD Android TV"));
        var tclCore = _classifier.Classify(
            Package("com.tcl.framework.custom"),
            Context("TCL", "65C7K"));
        var tclCandidate = _classifier.Classify(
            Package("com.tcl.guard"),
            Context("TCL", "65C7K"));
        var cultraview = _classifier.Classify(
            Package("com.cultraview.setting"),
            Context("Cultraview", "CTV"));

        shield.Risk.Should().Be(PackageRiskLevel.Critical);
        tclCore.Risk.Should().Be(PackageRiskLevel.Critical);
        tclCandidate.Risk.Should().Be(PackageRiskLevel.Caution);
        cultraview.Risk.Should().Be(PackageRiskLevel.Critical);
        tclCandidate.Risk.Should().NotBe(PackageRiskLevel.Safe);
    }

    [Fact]
    public void Conflicting_katniss_evidence_remains_high_risk_with_voice_impact()
    {
        var assessment = _classifier.Classify(Package("com.google.android.katniss"), _context);

        assessment.Risk.Should().Be(PackageRiskLevel.HighRisk);
        assessment.Impacts.Should().Contain(impact => impact.Area == "Voice search");
        assessment.Reasons.Should().Contain(reason => reason.Contains("Conflicting", StringComparison.Ordinal));
    }

    [Fact]
    public void Model_scoped_tivo_and_xiaomi_rules_match_without_generic_vendor_assumptions()
    {
        var tivo = _classifier.Classify(
            Package("com.tivo.tivoplusplayer"),
            Context("SEI Robotics", "TiVo Stream 4K"));
        var xiaomi = _classifier.Classify(
            Package("com.xiaomi.mitv.advertise"),
            Context("Xiaomi", "MIBOX4"));
        var yandexKeyboard = _classifier.Classify(
            Package("ru.yandex.androidkeyboard.tv"),
            Context("Yandex", "Yandex TV"));

        tivo.Risk.Should().Be(PackageRiskLevel.Caution);
        xiaomi.Risk.Should().Be(PackageRiskLevel.Caution);
        yandexKeyboard.Risk.Should().Be(PackageRiskLevel.Critical);
    }

    [Fact]
    public void Source_catalog_is_present_and_references_are_unique()
    {
        var sources = PackageKnowledgeLoader.LoadSources();

        sources.Should().NotBeEmpty();
        sources.Select(source => source.Id).Should().OnlyHaveUniqueItems();
        sources.Select(source => source.Url).Should().OnlyHaveUniqueItems();
        sources.Should().OnlyContain(source =>
            !string.IsNullOrWhiteSpace(source.Title)
            && source.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(source.SourceType)
            && !string.IsNullOrWhiteSpace(source.Attribution));
        sources.Should().Contain(source => source.Id == "homatics-4pda-atv14");
        sources.Should().Contain(source => source.Id == "firestrip");
        sources.Should().Contain(source => source.Id == "onn-4k-plus-report");
        sources.Single(source => source.Id == "homatics-4pda-atv14").SourceConfidence
            .Should().Be(PackageSourceConfidence.RealHardwareDump);
        sources.Single(source => source.Id == "nokia-live-tv-regression").SourceConfidence
            .Should().Be(PackageSourceConfidence.SingleAnecdotalReport);
    }

    [Fact]
    public void Research_namespaces_are_recognized_but_remain_unknown()
    {
        var assessment = _classifier.Classify(
            Package("com.tianci.movieplatform"),
            Context("Skyworth", "Skyworth Android TV"));

        assessment.Risk.Should().Be(PackageRiskLevel.Unknown);
        assessment.Confidence.Should().Be(PackageConfidence.Low);
        assessment.RecommendedAction.Should().Be("Review manually");
        assessment.Reasons.Should().Contain(reason => reason.Contains("skyworth-tianci-report", StringComparison.Ordinal));
    }

    [Fact]
    public void Panasonic_and_Nokia_research_namespaces_never_become_automatic_candidates()
    {
        var panasonic = _classifier.Classify(Package("com.panasonic.tvservice"), Context("Panasonic", "TX-55LX650"));
        var nokia = _classifier.Classify(Package("com.nokia.livetv"), Context("Nokia", "Nokia TV"));

        panasonic.Risk.Should().Be(PackageRiskLevel.Unknown);
        nokia.Risk.Should().Be(PackageRiskLevel.Unknown);
        panasonic.RecommendedAction.Should().Be("Review manually");
        nokia.RecommendedAction.Should().Be("Review manually");
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

    private static PackageClassificationContext Context(string manufacturer, string model, string? product = null)
        => new(
            new AndroidDevice
            {
                Serial = "tv-1",
                Manufacturer = manufacturer,
                Model = model,
                Product = product,
                State = DeviceState.Device
            },
            null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}
