using System.ComponentModel;
using System.Diagnostics;

namespace AndroidTVManager.Infrastructure.Adb;

public static class AdbProcessLifetime
{
    public static void TryKillClient(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: false);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or Win32Exception
            or NotSupportedException)
        {
        }
    }
}
