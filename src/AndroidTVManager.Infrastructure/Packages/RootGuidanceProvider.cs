using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Packages;

public sealed class RootGuidanceProvider : IRootGuidanceProvider
{
    public string GetGuidance(
        AndroidDevice? device,
        OemUnlockInfo oemUnlock,
        SecurityInfo security,
        RootInfo root)
    {
        var identity = string.Join(" ", new[]
        {
            device?.Manufacturer,
            device?.Model,
            device?.Product,
            device?.BuildFingerprint
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        var deviceHint = identity.Contains("chromecast", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("google tv streamer", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("sabrina", StringComparison.OrdinalIgnoreCase)
            ? "Google retail streaming devices commonly restrict bootloader unlocking; verify the exact product policy before assuming a root path."
            : identity.Contains("philips", StringComparison.OrdinalIgnoreCase)
                ? "A Philips OEM unlock setting may be configurable, but this does not establish a supported root path. Exact model and firmware documentation is required."
                : "No device-specific root method is inferred from the connected device identity.";

        return $"{deviceHint} Current evidence: shell root={security.RootAvailability}, " +
            $"su={root.SuAvailability}, OEM unlock capability={oemUnlock.ActualUnlockCapability}. " +
            "This app does not attempt privilege escalation, unlock commands, or reboots.";
    }
}
