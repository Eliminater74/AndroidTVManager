using AndroidTVManager.Core.Adb;
using AndroidTVManager.Core.Models;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class ConfigurationExplorerTests
{
    [Fact]
    public void Parses_runtime_and_partition_values_and_reports_conflicts()
    {
        var runtime = ConfigurationPropertyParser.ParseRuntime("""
            [ro.product.model]: [Runtime TV]
            [ro.build.version.release]: [14]
            """);
        var files = new Dictionary<ConfigurationSource, IReadOnlyDictionary<string, string>>
        {
            [ConfigurationSource.System] = ConfigurationPropertyParser.ParseFile("ro.product.model=System TV\n"),
            [ConfigurationSource.Product] = ConfigurationPropertyParser.ParseFile("ro.build.version.release=14\n")
        };

        var conflict = ConfigurationPropertyParser.CreateProperty("ro.product.model", runtime, files);
        var match = ConfigurationPropertyParser.CreateProperty("ro.build.version.release", runtime, files);

        conflict.RuntimeValue.Should().Be("Runtime TV");
        conflict.StaticValues.Should().Contain(value => value.Source == ConfigurationSource.System
            && value.Value == "System TV");
        conflict.Status.Should().Be(ConfigurationValueStatus.Conflict);
        match.Status.Should().Be(ConfigurationValueStatus.Match);
    }

    [Fact]
    public void Keeps_runtime_only_file_only_unavailable_and_redacted_states_distinct()
    {
        var runtime = ConfigurationPropertyParser.ParseRuntime("[ro.runtime.only]: [runtime]\n[ro.secret.token]: [private]\n");
        var files = new Dictionary<ConfigurationSource, IReadOnlyDictionary<string, string>>
        {
            [ConfigurationSource.System] = ConfigurationPropertyParser.ParseFile("ro.file.only=static\n"),
            [ConfigurationSource.Vendor] = new Dictionary<string, string>()
        };
        var availability = new Dictionary<ConfigurationSource, bool>
        {
            [ConfigurationSource.System] = true,
            [ConfigurationSource.Vendor] = false
        };
        var errors = new Dictionary<ConfigurationSource, string?>
        {
            [ConfigurationSource.Vendor] = "Permission denied"
        };

        var runtimeOnly = ConfigurationPropertyParser.CreateProperty("ro.runtime.only", runtime, files, availability, errors);
        var fileOnly = ConfigurationPropertyParser.CreateProperty("ro.file.only", runtime, files, availability, errors);
        var unavailable = ConfigurationPropertyParser.CreateProperty("ro.missing", runtime, files, availability, errors);
        var redacted = ConfigurationPropertyParser.CreateProperty("ro.secret.token", runtime, files, availability, errors);

        runtimeOnly.Status.Should().Be(ConfigurationValueStatus.RuntimeOnly);
        fileOnly.Status.Should().Be(ConfigurationValueStatus.FileOnly);
        unavailable.Status.Should().Be(ConfigurationValueStatus.Unavailable);
        unavailable.SourceSummary.Should().Contain("Vendor unavailable");
        redacted.DisplayValue.Should().Be("[redacted]");
        redacted.RedactionLabel.Should().Be("Value redacted for security");
    }

    [Fact]
    public void Snapshot_comparison_identifies_runtime_and_file_changes()
    {
        var previous = Snapshot(
            new ConfigurationProperty(
                "ro.product.model", "Hardware", "Product model", "Old",
                [new(ConfigurationSource.Vendor, "Old")],
                ConfigurationValueStatus.Conflict));
        var current = Snapshot(
            new ConfigurationProperty(
                "ro.product.model", "Hardware", "Product model", "New",
                [new(ConfigurationSource.Vendor, "Old")],
                ConfigurationValueStatus.Conflict));

        var difference = ConfigurationSnapshotComparer.Compare(previous, current);

        difference.ChangedCount.Should().Be(1);
        difference.Changes[0].RuntimeChanged.Should().BeTrue();
        difference.Changes[0].ChangeSummary.Should().Be("Runtime value changed");
    }

    [Fact]
    public void Snapshot_comparison_identifies_partition_only_changes()
    {
        var previous = Snapshot(new ConfigurationProperty(
            "ro.vendor.feature", "Vendor", "Vendor feature", "enabled",
            [new(ConfigurationSource.Vendor, "old")],
            ConfigurationValueStatus.Conflict));
        var current = Snapshot(new ConfigurationProperty(
            "ro.vendor.feature", "Vendor", "Vendor feature", "enabled",
            [new(ConfigurationSource.Vendor, "new")],
            ConfigurationValueStatus.Conflict));

        var difference = ConfigurationSnapshotComparer.Compare(previous, current);

        difference.ChangedCount.Should().Be(1);
        difference.Changes[0].RuntimeChanged.Should().BeFalse();
        difference.Changes[0].ChangeSummary.Should().Be("Vendor file changed");
    }

    private static ConfigurationSnapshot Snapshot(ConfigurationProperty property)
        => new(
            "tv-1",
            DateTimeOffset.UtcNow,
            "Living Room TV",
            "Philips",
            "TV",
            "fingerprint",
            "14",
            34,
            "2026-06-05",
            [new ConfigurationSection(
                property.Category,
                [property],
                InspectionSectionState.Completed,
                [])],
            []);
}
