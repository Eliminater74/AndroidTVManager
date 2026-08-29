namespace AndroidTVManager.Core.Models;

public sealed record DeveloperVerificationPolicy(
    string ManualInstallGuidance,
    bool DurationIsDeviceDependent);
