using AndroidTVManager.Core.Recovery;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class RecoveryZipMetadataParserTests
{
    [Fact]
    public void Parses_pre_device_and_updater_script_targets()
    {
        var declaration = RecoveryZipMetadataParser.Parse(
            "ota-type=BLOCK\npre-device=dragon,sprout\npre-build=lineage\n",
            """getprop("ro.product.device") == "dragon" || abort("This package is for dragon");""");

        declaration.DeclaredDevices.Should().Equal("dragon", "sprout");
        declaration.PreBuild.Should().Be("lineage");
    }

    [Fact]
    public void Missing_device_declaration_is_unknown_not_compatible()
    {
        var declaration = RecoveryZipMetadataParser.Parse("ota-type=BLOCK\n", "");
        var compatibility = RecoveryZipMetadataParser.Evaluate(declaration, "dragon");

        compatibility.State.Should().Be(RecoveryCompatibilityState.Unknown);
        compatibility.Reasons.Should().Contain(reason => reason.Contains("not verified"));
    }

    [Fact]
    public void Declared_device_without_live_identity_is_unknown_required_evidence()
    {
        var declaration = RecoveryZipMetadataParser.Parse("pre-device=dragon\n", null);
        var compatibility = RecoveryZipMetadataParser.Evaluate(declaration, "  ");

        compatibility.State.Should().Be(RecoveryCompatibilityState.UnknownRequiredEvidence);
    }

    [Fact]
    public void Mismatched_live_product_is_incompatible()
    {
        var declaration = RecoveryZipMetadataParser.Parse("pre-device=dragon\n", null);
        var compatibility = RecoveryZipMetadataParser.Evaluate(declaration, "foster");

        compatibility.State.Should().Be(RecoveryCompatibilityState.Incompatible);
        compatibility.Reasons.Should().Contain(reason => reason.Contains("foster"));
    }

    [Fact]
    public void Matching_live_product_is_compatible()
    {
        var declaration = RecoveryZipMetadataParser.Parse(null, "getprop(\"ro.product.device\") == \"dragon\"");
        var compatibility = RecoveryZipMetadataParser.Evaluate(declaration, "dragon");

        compatibility.State.Should().Be(RecoveryCompatibilityState.Compatible);
    }
}
