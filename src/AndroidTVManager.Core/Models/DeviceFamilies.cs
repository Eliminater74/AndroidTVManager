namespace AndroidTVManager.Core.Models;

public static class DeviceFamilies
{
    public const string NvidiaShieldTv = "NVIDIA Shield TV";

    public static bool IsShieldTv(AndroidDevice device)
    {
        if (!string.Equals(device.Manufacturer, "NVIDIA", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(device.Brand, "NVIDIA", StringComparison.OrdinalIgnoreCase))
            return false;
        // Friendly names are user-editable; they must never enable a package profile.
        if (device.Model?.Contains("tablet", StringComparison.OrdinalIgnoreCase) == true)
            return false;
        return (device.Model?.Contains("SHIELD", StringComparison.OrdinalIgnoreCase) == true
                && device.Model.Contains("TV", StringComparison.OrdinalIgnoreCase))
            || IsShieldTvCodename(device.DeviceName)
            || IsShieldTvCodename(device.Product);
    }

    private static bool IsShieldTvCodename(string? value)
        => value is not null
            && new[] { "foster", "foster_e", "darcy", "mdarcy", "sif" }
                .Contains(value, StringComparer.OrdinalIgnoreCase);
}
