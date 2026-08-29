using System.Reflection;

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
        => InformationalVersion.Contains("-B2", StringComparison.OrdinalIgnoreCase) ? "BETA 2"
            : InformationalVersion.Contains("-B1", StringComparison.OrdinalIgnoreCase) ? "BETA 1"
            : "RELEASE";
}
