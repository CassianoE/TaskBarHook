using TaskBarHook.Media;
using TaskBarHook.Models;

namespace TaskBarHook.Tests;

public sealed class SnapshotUpdateCoordinatorTests
{
    [Fact]
    public void Playback_change_does_not_invalidate_a_pending_artwork_load()
    {
        var coordinator = new SnapshotUpdateCoordinator();
        var first = coordinator.BeginTrackUpdate();
        coordinator.TryApplyTrack(
            first,
            new TrackInfo("A", "Artist", "Album", [1, 2, 3], "A|Artist|Album"),
            MediaPlaybackStatus.Playing,
            TimelineInfo.Unknown,
            new CommandAvailability(true, true, true),
            "spotify",
            "Spotify.exe");

        var pending = coordinator.BeginTrackUpdate();
        coordinator.ApplyPlayback(
            MediaPlaybackStatus.Paused,
            new CommandAvailability(true, false, false),
            new TimelineInfo(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3), DateTimeOffset.UtcNow, 1, false));

        Assert.True(coordinator.IsTrackCurrent(pending));
        Assert.Equal("A", coordinator.Current.Track.Title);
        Assert.Equal(MediaPlaybackStatus.Paused, coordinator.Current.Status);

        var next = SnapshotUpdateCoordinator.TrackWithoutStaleArtwork(coordinator.Current.Track, "B", "Other", "Next");
        Assert.Null(next.ArtworkBytes);
        Assert.True(coordinator.TryApplyTrack(
            pending,
            new TrackInfo("B", "Other", "Next", [9], "B|Other|Next"),
            MediaPlaybackStatus.Paused,
            TimelineInfo.Unknown,
            CommandAvailability.None,
            "spotify",
            "Spotify.exe"));
        Assert.Equal("B", coordinator.Current.Track.Title);
        Assert.Equal(new byte[] { 9 }, coordinator.Current.Track.ArtworkBytes);
    }

    [Fact]
    public void Older_track_result_is_discarded_after_a_newer_identity()
    {
        var coordinator = new SnapshotUpdateCoordinator();
        var stale = coordinator.BeginTrackUpdate();
        var current = coordinator.BeginTrackUpdate();

        Assert.False(coordinator.TryApplyTrack(
            stale,
            new TrackInfo("Old", "A", null, [1], "Old|A|"),
            MediaPlaybackStatus.Playing,
            TimelineInfo.Unknown,
            CommandAvailability.None,
            "a",
            "a"));
        Assert.True(coordinator.TryApplyTrack(
            current,
            new TrackInfo("New", "B", null, [2], "New|B|"),
            MediaPlaybackStatus.Playing,
            TimelineInfo.Unknown,
            CommandAvailability.None,
            "b",
            "b"));
        Assert.Equal("New", coordinator.Current.Track.Title);
        Assert.Equal(new byte[] { 2 }, coordinator.Current.Track.ArtworkBytes);
    }
}
