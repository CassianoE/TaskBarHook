using TaskBarHook.Models;

namespace TaskBarHook.Media;

public sealed class SnapshotUpdateCoordinator
{
    private readonly GenerationGate _trackGate = new();
    private int _trackGeneration;
    private string? _appliedTrackKey;

    public MediaSnapshot Current { get; private set; } = MediaSnapshot.Empty;

    public int BeginTrackUpdate() => _trackGate.Begin();

    public bool IsTrackCurrent(int token) => _trackGate.IsCurrent(token);

    public void ApplyPlayback(MediaPlaybackStatus status, CommandAvailability commands, TimelineInfo timeline)
    {
        Current = Current with { Status = status, Commands = commands, Timeline = timeline };
    }

    public void ApplyTimeline(TimelineInfo timeline)
    {
        Current = Current with { Timeline = timeline };
    }

    public bool TryApplyTrack(
        int token,
        TrackInfo track,
        MediaPlaybackStatus status,
        TimelineInfo timeline,
        CommandAvailability commands,
        string? sessionId,
        string? sourceAppId,
        string? sessionInstanceId = null)
    {
        if (!_trackGate.IsCurrent(token))
        {
            return false;
        }

        if (!string.Equals(track.ArtworkKey, _appliedTrackKey, StringComparison.Ordinal))
        {
            _trackGeneration++;
            _appliedTrackKey = track.ArtworkKey;
        }

        Current = new MediaSnapshot(
            status,
            track,
            timeline,
            commands,
            sessionId,
            sourceAppId,
            sessionInstanceId,
            _trackGeneration);
        return true;
    }

    public void Clear()
    {
        _trackGate.Begin();
        _trackGeneration++;
        _appliedTrackKey = null;
        Current = MediaSnapshot.Empty;
    }

    public static TrackInfo TrackWithoutStaleArtwork(TrackInfo previous, string? title, string? artist, string? album)
    {
        var key = $"{title}|{artist}|{album}";
        if (string.Equals(previous.ArtworkKey, key, StringComparison.Ordinal))
        {
            return previous with { Title = title, Artist = artist, Album = album };
        }

        return new TrackInfo(title, artist, album, null, key);
    }
}
