using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Core.Privacy;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DiagnosticBundlePrivacyTests
{
    [Fact]
    public void Redacted_bundle_content_removes_serial_and_network_identity()
    {
        var content = """
            {"serial":"emulator-5554","mac":"AA:BB:CC:DD:EE:FF","ip":"192.168.1.20","wifi":"LivingRoom"}
            """;

        var redacted = new SensitiveDataRedactor().Redact(
            content,
            new SensitiveDataRedactionOptions(SensitiveDataRedactionLevel.SupportShareable, "emulator-5554"));

        redacted.Should().NotContain("emulator-5554");
        redacted.Should().NotContain("AA:BB:CC:DD:EE:FF");
        redacted.Should().NotContain("192.168.1.20");
        redacted.Should().Contain("<serial-redacted>");
    }
}
