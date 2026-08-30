using System.Reflection;
using System.Text.RegularExpressions;

namespace AndroidTVManager.App;

public static class AppInfo
{
    private static Assembly CurrentAssembly => Assembly.GetExecutingAssembly();

    public static string ProductName =>
        CurrentAssembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
        ?? "Android TV Manager";

    public static string DeveloperName =>
        CurrentAssembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company
        ?? "Eliminater74";

    public static string Description =>
        CurrentAssembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description
        ?? "Android TV / Google TV device management toolbox.";

    public static string InformationalVersion =>
        CurrentAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? CurrentAssembly.GetName().Version?.ToString()
        ?? "Unknown";

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
            return match.Success ? $"Beta {match.Groups["number"].Value}" : "Stable";
        }
    }
}
