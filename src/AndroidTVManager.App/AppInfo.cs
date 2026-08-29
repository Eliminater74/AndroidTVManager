using System.Reflection;

namespace AndroidTVManager.App;

public static class AppInfo
{
    public static string Version =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "1.0.0-B2";
}
