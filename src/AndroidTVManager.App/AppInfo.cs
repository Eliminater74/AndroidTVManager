using System.Reflection;
using System.Text.RegularExpressions;

namespace AndroidTVManager.App;

public static class AppInfo
{
    public static string InformationalVersion =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "1.0.0-B2";

    public static string Version => InformationalVersion.Split('+')[0];

    public static string BuildIdentifier
        => InformationalVersion.Contains('+', StringComparison.Ordinal)
            ? InformationalVersion[(InformationalVersion.IndexOf('+') + 1)..]
            : string.Empty;

    public static string ReleaseChannel
    {
        get
        {
            var match = Regex.Match(InformationalVersion, @"-B(?<number>\d+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success ? $"BETA {match.Groups["number"].Value}" : "RELEASE";
        }
    }
}
