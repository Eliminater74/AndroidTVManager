using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Privacy;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class SensitiveDataRedactorTests
{
    private readonly SensitiveDataRedactor _redactor = new();

    [Fact]
    public void Support_shareable_mode_removes_identity_and_secrets()
    {
        var content = """
            pairing code: 847291
            token=super-secret
            serial=emulator-5554
            mac=AA:BB:CC:DD:EE:FF
            ip=192.168.1.20
            ipv6=fe80::1a2b:3c4d:5e6f:7788
            ssid=LivingRoom
            account=user@example.com
            url=https://example.invalid/update?access_token=abc123
            path=C:\Users\alex\Downloads\app.apk
            """;

        var redacted = _redactor.Redact(content, new SensitiveDataRedactionOptions(
            SensitiveDataRedactionLevel.SupportShareable,
            "emulator-5554"));

        redacted.Should().NotContain("847291");
        redacted.Should().NotContain("super-secret");
        redacted.Should().NotContain("emulator-5554");
        redacted.Should().NotContain("AA:BB:CC:DD:EE:FF");
        redacted.Should().NotContain("192.168.1.20");
        redacted.Should().NotContain("fe80::1a2b:3c4d:5e6f:7788");
        redacted.Should().NotContain("LivingRoom");
        redacted.Should().NotContain("user@example.com");
        redacted.Should().NotContain("abc123");
        redacted.Should().NotContain(@"\alex\");
        redacted.Should().Contain("<serial-redacted>");
        redacted.Should().Contain("<ipv6-redacted>");
        redacted.Should().Contain("<user-redacted>");
    }

    [Fact]
    public void Secrets_only_mode_keeps_device_identity()
    {
        var redacted = _redactor.Redact(
            "token: hunter2 serial=emulator-5554 ip=10.0.0.8",
            new SensitiveDataRedactionOptions(SensitiveDataRedactionLevel.SecretsOnly, "emulator-5554"));

        redacted.Should().NotContain("hunter2");
        redacted.Should().Contain("emulator-5554");
        redacted.Should().Contain("10.0.0.8");
    }

    [Fact]
    public void Argument_redaction_hides_pairing_codes_and_local_payload_paths()
    {
        var arguments = _redactor.RedactArguments([
            "-s", "192.168.1.20:5555",
            "install-multiple", "-r",
            @"C:\Users\alex\Apps\base.apk",
            @"C:\Users\alex\Apps\split.apk"
        ]);
        arguments.Should().Equal("-s", "192.168.1.20:5555", "install-multiple", "-r",
            "<apk-path-redacted>", "<apk-path-redacted>");

        _redactor.RedactArguments(["push", @"C:\Users\alex\file.txt", "/sdcard/file.txt"])
            .Should().Equal("push", "<local-path-redacted>", "/sdcard/file.txt");
        _redactor.RedactArguments(["pull", "/sdcard/file.txt", @"C:\Users\alex\file.txt"])
            .Should().Equal("pull", "/sdcard/file.txt", "<local-path-redacted>");
        _redactor.RedactArguments(["pair", "192.168.1.20:37123", "847291"])
            .Should().Equal("pair", "192.168.1.20:37123", "<pairing-code-redacted>");
        _redactor.RedactArguments(["sideload", @"C:\Users\alex\lineage.zip"])
            .Should().Equal("sideload", "<sideload-file-redacted>");
        _redactor.RedactArguments(["install", "-r", @"C:\Users\alex\app.apk"])
            .Should().Equal("install", "-r", "<apk-path-redacted>");
    }
}
