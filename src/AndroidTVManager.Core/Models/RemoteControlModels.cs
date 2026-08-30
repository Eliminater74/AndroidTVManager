namespace AndroidTVManager.Core.Models;

public enum RemoteKey
{
    Up,
    Down,
    Left,
    Right,
    Select,
    Back,
    Home,
    Menu,
    PlayPause,
    Rewind,
    FastForward,
    VolumeUp,
    VolumeDown,
    Mute,
    Power,
    Digit0,
    Digit1,
    Digit2,
    Digit3,
    Digit4,
    Digit5,
    Digit6,
    Digit7,
    Digit8,
    Digit9
}

public sealed record RemoteFavorite(string Label, string PackageName);
