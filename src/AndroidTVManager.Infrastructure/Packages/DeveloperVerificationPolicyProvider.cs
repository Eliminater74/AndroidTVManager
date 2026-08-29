using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Packages;

public sealed class DeveloperVerificationPolicyProvider : IDeveloperVerificationPolicyProvider
{
    public DeveloperVerificationPolicy GetPolicy(AndroidDevice? device)
        => new(
            "Manual on-device installation may require Developer Options, authentication, and a device-specific waiting period. Menu names and availability vary by Android build and manufacturer. Android TV Manager installs through ADB and does not bypass this process.",
            DurationIsDeviceDependent: true);
}
