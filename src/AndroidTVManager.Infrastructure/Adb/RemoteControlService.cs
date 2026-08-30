using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;

namespace AndroidTVManager.Infrastructure.Adb;

public sealed class RemoteControlService : IRemoteControlService
{
    private static readonly IReadOnlyDictionary<RemoteKey, string> KeyCodes =
        new Dictionary<RemoteKey, string>
        {
            [RemoteKey.Up] = "KEYCODE_DPAD_UP",
            [RemoteKey.Down] = "KEYCODE_DPAD_DOWN",
            [RemoteKey.Left] = "KEYCODE_DPAD_LEFT",
            [RemoteKey.Right] = "KEYCODE_DPAD_RIGHT",
            [RemoteKey.Select] = "KEYCODE_DPAD_CENTER",
            [RemoteKey.Back] = "KEYCODE_BACK",
            [RemoteKey.Home] = "KEYCODE_HOME",
            [RemoteKey.Menu] = "KEYCODE_MENU",
            [RemoteKey.PlayPause] = "KEYCODE_MEDIA_PLAY_PAUSE",
            [RemoteKey.Rewind] = "KEYCODE_MEDIA_REWIND",
            [RemoteKey.FastForward] = "KEYCODE_MEDIA_FAST_FORWARD",
            [RemoteKey.VolumeUp] = "KEYCODE_VOLUME_UP",
            [RemoteKey.VolumeDown] = "KEYCODE_VOLUME_DOWN",
            [RemoteKey.Mute] = "KEYCODE_VOLUME_MUTE",
            [RemoteKey.Power] = "KEYCODE_POWER",
            [RemoteKey.Digit0] = "KEYCODE_0",
            [RemoteKey.Digit1] = "KEYCODE_1",
            [RemoteKey.Digit2] = "KEYCODE_2",
            [RemoteKey.Digit3] = "KEYCODE_3",
            [RemoteKey.Digit4] = "KEYCODE_4",
            [RemoteKey.Digit5] = "KEYCODE_5",
            [RemoteKey.Digit6] = "KEYCODE_6",
            [RemoteKey.Digit7] = "KEYCODE_7",
            [RemoteKey.Digit8] = "KEYCODE_8",
            [RemoteKey.Digit9] = "KEYCODE_9"
        };

    private readonly IAdbProcessRunner _runner;

    public RemoteControlService(IAdbProcessRunner runner)
    {
        _runner = runner;
    }

    public Task<AdbCommandResult> PressAsync(
        string serial,
        RemoteKey key,
        CancellationToken cancellationToken = default)
        => _runner.RunForDeviceAsync(
            serial.Trim(),
            ["shell", "input", "keyevent", KeyCodes[key]],
            TimeSpan.FromSeconds(15),
            cancellationToken);

    public Task<AdbCommandResult> TypeTextAsync(
        string serial,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("Text is required.", nameof(text));
        return _runner.RunForDeviceAsync(
            serial.Trim(),
            ["shell", "input", "text", EncodeText(text)],
            TimeSpan.FromSeconds(15),
            cancellationToken);
    }

    private static string EncodeText(string text)
        => text.Replace("%", "%25", StringComparison.Ordinal)
            .Replace(" ", "%s", StringComparison.Ordinal)
            .Replace("\"", "%22", StringComparison.Ordinal)
            .Replace("'", "%27", StringComparison.Ordinal)
            .Replace("&", "%26", StringComparison.Ordinal)
            .Replace("<", "%3C", StringComparison.Ordinal)
            .Replace(">", "%3E", StringComparison.Ordinal);
}
