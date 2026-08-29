using AndroidTVManager.Core.Models;
using AndroidTVManager.Core.Scripts;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class ScriptTests
{
    [Fact]
    public void Parses_and_validates_a_supported_script()
    {
        var script = ScriptDefinitionParser.Parse("""
            {
              "schemaVersion": 1,
              "name": "Safe cleanup",
              "description": "Disable one package",
              "supportedDevices": [{ "manufacturer": "onn.", "modelContains": "4K" }],
              "actions": [{ "type": "disablePackage", "package": "com.example.app", "reversible": true }]
            }
            """);

        script.Name.Should().Be("Safe cleanup");
        ScriptDefinitionParser.IsCompatible(script, new AndroidDevice
        {
            Manufacturer = "onn.",
            Model = "ONN 4K Pro"
        }).Should().BeTrue();
    }

    [Fact]
    public void Rejects_unknown_action_types()
    {
        var validation = ScriptDefinitionParser.Validate(new ScriptDefinition
        {
            SchemaVersion = 1,
            Name = "Bad",
            Actions = [new ScriptAction { Type = "deleteEverything" }]
        });

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(error => error.Contains("unknown type"));
    }

    [Fact]
    public void Rejects_actions_missing_required_target_values()
    {
        var validation = ScriptDefinitionParser.Validate(new ScriptDefinition
        {
            SchemaVersion = 1,
            Name = "Incomplete",
            Actions =
            [
                new ScriptAction { Type = "uninstallUser" },
                new ScriptAction { Type = "deleteFile" },
                new ScriptAction { Type = "setSetting" }
            ]
        });

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(error => error.Contains("requires a package"));
        validation.Errors.Should().Contain(error => error.Contains("requires a path"));
        validation.Errors.Should().Contain(error => error.Contains("namespace:key=value"));
    }

    [Fact]
    public void Warns_for_advanced_and_destructive_actions()
    {
        var validation = ScriptDefinitionParser.Validate(new ScriptDefinition
        {
            SchemaVersion = 1,
            Name = "Review me",
            Actions =
            [
                new ScriptAction { Type = "shell", Value = "settings put global test 1" },
                new ScriptAction { Type = "clearData", Package = "com.example.app" }
            ]
        });

        validation.IsValid.Should().BeTrue();
        validation.Warnings.Should().HaveCount(2);
    }

    [Fact]
    public void Rejects_incompatible_manufacturer_and_model()
    {
        var script = new ScriptDefinition
        {
            SchemaVersion = 1,
            Name = "Philips only",
            SupportedDevices = [new SupportedDevice { Manufacturer = "Philips", ModelContains = "OLED" }],
            Actions = [new ScriptAction { Type = "forceStop", Package = "com.example.app" }]
        };

        ScriptDefinitionParser.IsCompatible(script, new AndroidDevice
        {
            Manufacturer = "Sony",
            Model = "OLED TV"
        }).Should().BeFalse();
    }
}
