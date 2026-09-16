using System.Text.RegularExpressions;
using AndroidTVManager.Core.Abstractions;

namespace AndroidTVManager.Core.Privacy;

public sealed partial class SensitiveDataRedactor : ISensitiveDataRedactor
{
    public const string PolicyVersion = "2026-09-15-v1";

    public string Redact(string value, SensitiveDataRedactionOptions? options = null)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var settings = options ?? new SensitiveDataRedactionOptions();
        value = CredentialPairRegex().Replace(value, "${key}${separator}<redacted>");
        value = QueryCredentialRegex().Replace(value, "${prefix}<redacted>");

        if (settings.Level == SensitiveDataRedactionLevel.SecretsOnly)
            return value;

        if (!string.IsNullOrWhiteSpace(settings.Serial))
            value = value.Replace(settings.Serial, "<serial-redacted>", StringComparison.OrdinalIgnoreCase);

        value = MacAddressRegex().Replace(value, "<mac-redacted>");
        value = Ipv4Regex().Replace(value, "<ip-redacted>");
        value = Ipv6Regex().Replace(value, "<ipv6-redacted>");
        value = SsidPairRegex().Replace(value, "${key}${separator}<redacted>");
        value = EmailRegex().Replace(value, "<account-redacted>");
        value = WindowsUserPathRegex().Replace(value, @"${root}<user-redacted>\");
        value = UnixUserPathRegex().Replace(value, "${root}<user-redacted>/");
        return value;
    }

    public IReadOnlyList<string> RedactArguments(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
            return arguments;

        var redacted = arguments.ToArray();
        for (var index = 0; index < redacted.Length; index++)
            redacted[index] = RedactPathArgument(redacted[index]);

        RedactAfterCommand(redacted, "pair", startOffset: 2, count: 1);
        RedactLocalOperand(redacted, "pull", operandIndex: 2);
        RedactLocalOperand(redacted, "push", operandIndex: 1);
        RedactLocalOperand(redacted, "sideload", operandIndex: 1);
        RedactInstallPaths(redacted);
        return redacted;
    }

    private static void RedactAfterCommand(string[] arguments, string command, int startOffset, int count)
    {
        var commandIndex = FindCommandIndex(arguments, command);
        if (commandIndex < 0)
            return;
        var start = commandIndex + startOffset;
        for (var index = start; index < start + count && index < arguments.Length; index++)
            arguments[index] = "<pairing-code-redacted>";
    }

    private static void RedactLocalOperand(string[] arguments, string command, int operandIndex)
    {
        var commandIndex = FindCommandIndex(arguments, command);
        if (commandIndex < 0)
            return;
        var index = commandIndex + operandIndex;
        if (index < arguments.Length)
            arguments[index] = PlaceholderFor(command);
    }

    private static void RedactInstallPaths(string[] arguments)
    {
        var commandIndex = FindCommandIndex(arguments, "install");
        commandIndex = commandIndex >= 0 ? commandIndex : FindCommandIndex(arguments, "install-multiple");
        if (commandIndex < 0)
            return;
        for (var index = commandIndex + 1; index < arguments.Length; index++)
        {
            if (arguments[index].StartsWith('-') && arguments[index].Length <= 4)
                continue;
            arguments[index] = "<apk-path-redacted>";
        }
    }

    private static int FindCommandIndex(IReadOnlyList<string> arguments, string command)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (arguments[index].Equals("-s", StringComparison.OrdinalIgnoreCase) && index + 1 < arguments.Count)
            {
                index++;
                continue;
            }

            if (arguments[index].Equals(command, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    private static string PlaceholderFor(string command) => command.ToLowerInvariant() switch
    {
        "pull" => "<local-path-redacted>",
        "push" => "<local-path-redacted>",
        "sideload" => "<sideload-file-redacted>",
        _ => "<path-redacted>"
    };

    private static string RedactPathArgument(string argument)
    {
        if (LooksLikeLocalUserPath(argument))
            return "<local-path-redacted>";
        return argument;
    }

    private static bool LooksLikeLocalUserPath(string value)
        => WindowsUserPathRegex().IsMatch(value) || UnixUserPathRegex().IsMatch(value);

    [GeneratedRegex(@"(?i)(?<key>pairing[-_ ]?code|password|passwd|token|secret|credential|authorization)(?<separator>\s*[:=]\s*)\S+")]
    private static partial Regex CredentialPairRegex();

    [GeneratedRegex(@"(?i)(?<prefix>[?&](?:token|access_token|auth|key|code|password|secret)=)[^&\s]+")]
    private static partial Regex QueryCredentialRegex();

    [GeneratedRegex(@"(?i)\b[0-9a-f]{2}([: -][0-9a-f]{2}){5}\b")]
    private static partial Regex MacAddressRegex();

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(@"\b(?:(?:[0-9a-f]{1,4}:){2,7}[0-9a-f]{1,4}|(?:[0-9a-f]{1,4}:){1,7}:[0-9a-f]{0,4}|::(?:[0-9a-f]{1,4}:){0,6}[0-9a-f]{1,4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex Ipv6Regex();

    [GeneratedRegex(@"(?i)(?<key>ssid|wifi|network)(?<separator>\s*[:=]\s*)\S+")]
    private static partial Regex SsidPairRegex();

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?i)(?<root>(?:[A-Z]:\\|\\\\)Users\\)[^\\]+\\")]
    private static partial Regex WindowsUserPathRegex();

    [GeneratedRegex(@"(?<root>/(?:Users|home)/)[^/\s]+/")]
    private static partial Regex UnixUserPathRegex();
}
