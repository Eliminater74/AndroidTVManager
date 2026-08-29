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
}
