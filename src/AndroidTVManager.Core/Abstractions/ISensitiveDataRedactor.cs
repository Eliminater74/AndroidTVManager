namespace AndroidTVManager.Core.Abstractions;

public enum SensitiveDataRedactionLevel
{
    SecretsOnly,
    SupportShareable
}

public sealed record SensitiveDataRedactionOptions(
    SensitiveDataRedactionLevel Level = SensitiveDataRedactionLevel.SupportShareable,
    string? Serial = null);

public interface ISensitiveDataRedactor
{
    string Redact(string value, SensitiveDataRedactionOptions? options = null);
    IReadOnlyList<string> RedactArguments(IReadOnlyList<string> arguments);
}
