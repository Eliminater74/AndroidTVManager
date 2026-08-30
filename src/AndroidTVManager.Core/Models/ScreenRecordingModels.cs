namespace AndroidTVManager.Core.Models;

public sealed record ScreenRecordingInfo(
    string Serial,
    string RemotePath,
    DateTimeOffset StartedUtc);
