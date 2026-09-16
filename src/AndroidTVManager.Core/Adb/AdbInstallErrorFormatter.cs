using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Core.Adb;

public static class AdbInstallErrorFormatter
{
    public static string Explain(AdbCommandResult result)
    {
        var code = FindInstallCode(result.StandardError) ?? FindInstallCode(result.StandardOutput);
        var detail = FirstUsefulLine(result.StandardError) ?? FirstUsefulLine(result.StandardOutput);
        if (code is null && detail is null)
            return result.WasCanceled
                ? "Installation was canceled."
                : "Installation failed.";

        var explanation = code switch
        {
            "INSTALL_FAILED_VERSION_DOWNGRADE" =>
                "Install failed: the selected package is older than the version already on the device.",
            "INSTALL_FAILED_UPDATE_INCOMPATIBLE" =>
                "Install failed: signing certificate does not match the currently installed application. Android TV Manager will not uninstall the existing app to bypass this.",
            "INSTALL_FAILED_NO_MATCHING_ABIS" =>
                "Install failed: this package has no native libraries matching the device ABI.",
            "INSTALL_FAILED_INSUFFICIENT_STORAGE" =>
                "Install failed: the device does not have enough storage.",
            "INSTALL_PARSE_FAILED_NO_CERTIFICATES" =>
                "Install failed: the APK is not signed or the certificate could not be read.",
            "INSTALL_FAILED_OLDER_SDK" =>
                "Install failed: the package requires a newer Android version than the selected device.",
            "INSTALL_FAILED_MISSING_SPLIT" =>
                "Install failed: a required split APK was missing from the install set.",
            "INSTALL_FAILED_INVALID_APK" =>
                "Install failed: Android rejected the APK as invalid.",
            "INSTALL_FAILED_ALREADY_EXISTS" =>
                "Install failed: the package is already installed and reinstall was not used.",
            _ when code is not null => $"Install failed ({code}).",
            _ => "Install failed."
        };

        var androidError = code ?? detail;
        return string.IsNullOrWhiteSpace(androidError)
            ? explanation
            : $"{explanation}{Environment.NewLine}{Environment.NewLine}Android error:{Environment.NewLine}{androidError}";
    }

    public static string? FindInstallCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        foreach (var token in text.Split([' ', '\t', '\r', '\n', ':', '[', ']', ',', ';'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("INSTALL_FAILED_", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("INSTALL_PARSE_FAILED_", StringComparison.OrdinalIgnoreCase))
                return token.Trim();
        }
        return null;
    }

    private static string? FirstUsefulLine(string? text)
        => text?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
}
