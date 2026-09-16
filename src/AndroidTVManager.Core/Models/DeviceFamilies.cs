namespace AndroidTVManager.Core.Models;

/// <summary>
/// Hardware-family matching for package profiles. Identity uses manufacturer, brand, model,
/// product, device, board, and build fingerprint. User-editable FriendlyName and ReportedName
/// never activate a hardware-specific profile.
/// </summary>
public static class DeviceFamilies
{
    public const string NvidiaShieldTv = "NVIDIA Shield TV";
    public const string GoogleTvStreamer4K = "Google TV Streamer 4K";
    public const string ChromecastWithGoogleTv4K = "Chromecast with Google TV 4K";
    public const string OnnGoogleTv4kBox = "onn. Google TV 4K Box";
    public const string GoogleAtvEmulator = "sdk_google_atv";

    public static bool IsKnownHardwareFamily(string? family)
        => EqualsToken(family, NvidiaShieldTv)
            || EqualsToken(family, GoogleTvStreamer4K)
            || EqualsToken(family, ChromecastWithGoogleTv4K)
            || EqualsToken(family, OnnGoogleTv4kBox)
            || EqualsToken(family, GoogleAtvEmulator);

    public static bool Matches(string? family, AndroidDevice device)
    {
        if (string.IsNullOrWhiteSpace(family))
            return true;
        if (EqualsToken(family, NvidiaShieldTv))
            return IsShieldTv(device);
        if (EqualsToken(family, GoogleTvStreamer4K))
            return IsGoogleTvStreamer4K(device);
        if (EqualsToken(family, ChromecastWithGoogleTv4K))
            return IsChromecastWithGoogleTv4K(device);
        if (EqualsToken(family, OnnGoogleTv4kBox))
            return IsOnnGoogleTv4kBox(device);
        if (EqualsToken(family, GoogleAtvEmulator))
            return IsGoogleAtvEmulator(device);
        return HasHardwareToken(device, family);
    }

    public static bool IsShieldTv(AndroidDevice device)
    {
        if (!EqualsToken(device.Manufacturer, "NVIDIA") && !EqualsToken(device.Brand, "NVIDIA"))
            return false;
        // Friendly names are user-editable; they must never enable a package profile.
        if (ContainsToken(device.Model, "tablet"))
            return false;
        return (ContainsToken(device.Model, "SHIELD") && ContainsToken(device.Model, "TV"))
            || HasExactHardwareCode(device, "foster", "foster_e", "darcy", "mdarcy", "sif");
    }

    /// <summary>
    /// Google TV Streamer (4K), kirkwood / GRS6B. Evidence: dumps.tadiphone.dev kirkwood vendor
    /// build.prop (google/kirkwood/kirkwood:14/UTT3.240625.001.K5/12147201:user/release-keys,
    /// model Google TV Streamer, board kirkwood, platform mt8696).
    /// </summary>
    public static bool IsGoogleTvStreamer4K(AndroidDevice device)
    {
        if (!IsGoogleBrand(device))
            return false;
        if (HasExactHardwareCode(device, "sabrina", "sabrina_prod_stable", "boreal"))
            return false;
        return HasExactHardwareCode(device, "kirkwood", "GRS6B")
            || ModelStartsWith(device, "Google TV Streamer");
    }

    /// <summary>
    /// Chromecast with Google TV (4K), sabrina / sabrina_prod_stable. Evidence: LineageOS Wiki
    /// sabrina device page and lineage_sabrina.mk (PRODUCT_DEVICE=sabrina,
    /// PRODUCT_SYSTEM_NAME=sabrina_prod_stable). Excludes boreal (HD) and kirkwood (Streamer).
    /// </summary>
    public static bool IsChromecastWithGoogleTv4K(AndroidDevice device)
    {
        if (!IsGoogleBrand(device))
            return false;
        if (HasExactHardwareCode(device, "kirkwood", "boreal", "GRS6B")
            || ModelStartsWith(device, "Google TV Streamer"))
            return false;
        if (HasExactHardwareCode(device, "sabrina", "sabrina_prod_stable"))
            return true;
        if (ContainsToken(device.Model, "GA01919"))
            return true;
        return ContainsToken(device.Model, "Chromecast with Google TV")
            && ContainsToken(device.Model, "4K")
            && !ContainsToken(device.Model, "HD");
    }

    /// <summary>
    /// onn. Google TV 4K Box (2023), YOC / onn_4k_gtv / DV6105Z. Evidence: OTA fingerprint
    /// onn/onn_4k_gtv/YOC:12/SGZ2.230609.049.A1/11261715:user/release-keys. Excludes 2021
    /// dopinder, 4K Pro jarvis/SNA, and Full HD XNA.
    /// </summary>
    public static bool IsOnnGoogleTv4kBox(AndroidDevice device)
    {
        if (HasExactHardwareCode(device, "dopinder", "jarvis", "XNA", "SNA", "sti6140d360"))
            return false;
        if (HasExactHardwareCode(device, "YOC", "onn_4k_gtv", "DV6105Z")
            || ContainsToken(device.Model, "DV6105Z"))
            return true;
        if (!IsOnnBrand(device))
            return false;
        if (ContainsToken(device.Model, "Pro")
            || ContainsToken(device.Model, "Plus")
            || ContainsToken(device.Model, "Full HD")
            || ContainsToken(device.Model, "FHD"))
            return false;
        return ContainsToken(device.Model, "4K")
            && (ContainsToken(device.Model, "Streaming Box")
                || (ContainsToken(device.Model, "Google TV") && ContainsToken(device.Model, "Box")));
    }

    public static bool IsGoogleAtvEmulator(AndroidDevice device)
        => HasHardwareToken(device, "sdk_google_atv");

    public static bool HasHardwareToken(AndroidDevice device, string value)
        => ContainsToken(device.Model, value)
            || ContainsToken(device.DeviceName, value)
            || ContainsToken(device.Product, value)
            || ContainsToken(device.Board, value)
            || ContainsToken(device.BuildFingerprint, value);

    private static bool IsGoogleBrand(AndroidDevice device)
        => EqualsToken(device.Manufacturer, "Google") || EqualsToken(device.Brand, "Google");

    private static bool IsOnnBrand(AndroidDevice device)
        => EqualsOnnLabel(device.Manufacturer) || EqualsOnnLabel(device.Brand);

    private static bool EqualsOnnLabel(string? value)
        => EqualsToken(value?.TrimEnd('.'), "onn");

    private static bool HasExactHardwareCode(AndroidDevice device, params string[] codes)
        => codes.Any(code =>
            EqualsToken(device.DeviceName, code)
            || EqualsToken(device.Product, code)
            || EqualsToken(device.Board, code)
            || HasFingerprintSegment(device.BuildFingerprint, code));

    private static bool HasFingerprintSegment(string? fingerprint, string token)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return false;
        return fingerprint.Split('/', ':')
            .Any(segment => EqualsToken(segment, token));
    }

    private static bool ModelStartsWith(AndroidDevice device, string value)
        => device.Model?.StartsWith(value, StringComparison.OrdinalIgnoreCase) == true;

    private static bool ContainsToken(string? value, string token)
        => value?.Contains(token, StringComparison.OrdinalIgnoreCase) == true;

    private static bool EqualsToken(string? left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
