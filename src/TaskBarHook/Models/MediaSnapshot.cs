namespace TaskBarHook.Models;

public sealed record MediaSnapshot(
    MediaPlaybackStatus Status,
    TrackInfo Track,
    TimelineInfo Timeline,
    CommandAvailability Commands,
    string? SessionId,
    string? SourceAppId,
    string? SessionInstanceId = null,
    int TrackGeneration = 0)
{
    public static MediaSnapshot Empty { get; } = new(
        MediaPlaybackStatus.None,
        TrackInfo.Empty,
        TimelineInfo.Unknown,
        CommandAvailability.None,
        null,
        null);

    public bool HasLiveSession => Status is not MediaPlaybackStatus.None;
}
