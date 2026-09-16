using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Core.Adb;

public static class DeviceSelection
{
    public static bool Matches(AndroidDevice device, string? serialOrEndpoint)
        => !string.IsNullOrWhiteSpace(serialOrEndpoint)
           && (string.Equals(device.Serial, serialOrEndpoint, StringComparison.OrdinalIgnoreCase)
               || string.Equals(device.Endpoint, serialOrEndpoint, StringComparison.OrdinalIgnoreCase));

    public static AndroidDevice? Find(IEnumerable<AndroidDevice> devices, string? serialOrEndpoint)
        => string.IsNullOrWhiteSpace(serialOrEndpoint)
            ? null
            : devices.FirstOrDefault(device => Matches(device, serialOrEndpoint));

    public static AndroidDevice? Resolve(
        IEnumerable<AndroidDevice> devices,
        string? currentSerial,
        string? preferredSerial = null)
    {
        var snapshot = devices as IList<AndroidDevice> ?? devices.ToArray();
        return Find(snapshot, preferredSerial)
            ?? Find(snapshot, currentSerial)
            ?? snapshot.FirstOrDefault(device => device.State == DeviceState.Device)
            ?? snapshot.FirstOrDefault();
    }
}
