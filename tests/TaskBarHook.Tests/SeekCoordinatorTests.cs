using TaskBarHook.Media;
using TaskBarHook.Models;

namespace TaskBarHook.Tests;

public sealed class SeekCoordinatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 6, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Drag_updates_preview_without_creating_a_command()
    {
        var coordinator = new SeekCoordinator();
        var snapshot = Playing();
        coordinator.Sync(snapshot, TimeSpan.FromSeconds(30), T0);
        var range = SeekMapping.From(snapshot.Timeline, true);
        var identity = SeekIdentity.From(snapshot);

        Assert.True(coordinator.BeginPreview(identity, range, TimeSpan.FromSeconds(80)));
        coordinator.UpdatePreview(TimeSpan.FromSeconds(90));
        coordinator.UpdatePreview(TimeSpan.FromSeconds(100));

        Assert.Null(coordinator.TakeOutbound());
        Assert.True(coordinator.IsPreviewing);
        Assert.Equal(TimeSpan.FromSeconds(100), coordinator.Present(T0).DisplayPosition);
    }

    [Fact]
    public void Release_sends_a_single_clamped_request()
    {
        var coordinator = new SeekCoordinator();
        var snapshot = Playing();
        coordinator.Sync(snapshot, TimeSpan.FromSeconds(10), T0);
        var range = SeekMapping.From(snapshot.Timeline, true);

        coordinator.BeginPreview(SeekIdentity.From(snapshot), range, TimeSpan.FromSeconds(40));
        coordinator.UpdatePreview(TimeSpan.FromSeconds(50));
        var first = coordinator.Commit(T0);
        var second = coordinator.Commit(T0.AddSeconds(1));

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(SeekMapping.ToTicks(TimeSpan.FromSeconds(50)), first.PositionTicks);
        Assert.Same(first, coordinator.TakeOutbound() is { } taken && taken.Token == first.Token ? first : first);
        Assert.Null(coordinator.TakeOutbound());
    }

    [Fact]
    public void Timeline_during_drag_does_not_overwrite_preview()
    {
        var coordinator = new SeekCoordinator();
        var snapshot = Playing();
        coordinator.Sync(snapshot, TimeSpan.FromSeconds(20), T0);
        coordinator.BeginPreview(SeekIdentity.From(snapshot), SeekMapping.From(snapshot.Timeline, true), TimeSpan.FromSeconds(80));

        coordinator.Sync(
            snapshot with { Timeline = snapshot.Timeline with { Position = TimeSpan.FromSeconds(25), LastUpdatedUtc = T0.AddSeconds(5) } },
            TimeSpan.FromSeconds(25),
            T0.AddSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(80), coordinator.Present(T0.AddSeconds(5)).DisplayPosition);
        Assert.True(coordinator.IsPreviewing);
    }

    [Fact]
    public void Failed_command_does_not_keep_a_fake_position()
    {
        var coordinator = new SeekCoordinator(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        var snapshot = Playing();
        coordinator.Sync(snapshot, TimeSpan.FromSeconds(20), T0);
        coordinator.BeginPreview(SeekIdentity.From(snapshot), SeekMapping.From(snapshot.Timeline, true), TimeSpan.FromSeconds(80));
        var request = coordinator.Commit(T0);
        coordinator.CompleteCommand(request!.Token, false, T0.AddMilliseconds(20));
        coordinator.Sync(snapshot, TimeSpan.FromSeconds(20), T0.AddMilliseconds(30));

        var presentation = coordinator.Present(T0.AddMilliseconds(30));
        Assert.False(coordinator.IsPending);
        Assert.Equal(TimeSpan.FromSeconds(20), presentation.DisplayPosition);
        Assert.Equal("Não foi possível alterar a posição.", presentation.FailureText);
    }

    [Fact]
    public void Delayed_command_reverts_after_reconcile_timeout()
    {
        var coordinator = new SeekCoordinator(TimeSpan.FromMilliseconds(80), TimeSpan.FromSeconds(2));
        var snapshot = Playing();
        coordinator.Sync(snapshot, TimeSpan.FromSeconds(20), T0);
        coordinator.BeginPreview(SeekIdentity.From(snapshot), SeekMapping.From(snapshot.Timeline, true), TimeSpan.FromSeconds(80));
        coordinator.Commit(T0);

        coordinator.Sync(snapshot, TimeSpan.FromSeconds(21), T0.AddMilliseconds(40));
        Assert.True(coordinator.IsPending);
        Assert.Equal(TimeSpan.FromSeconds(80), coordinator.Present(T0.AddMilliseconds(40)).DisplayPosition);

        coordinator.Sync(snapshot, TimeSpan.FromSeconds(22), T0.AddMilliseconds(90));
        Assert.False(coordinator.IsPending);
        Assert.Equal(TimeSpan.FromSeconds(22), coordinator.Present(T0.AddMilliseconds(90)).DisplayPosition);
    }

    [Fact]
    public void Track_change_cancels_the_previous_intent()
    {
        var coordinator = new SeekCoordinator();
        var first = Playing();
        coordinator.Sync(first, TimeSpan.FromSeconds(20), T0);
        coordinator.BeginPreview(SeekIdentity.From(first), SeekMapping.From(first.Timeline, true), TimeSpan.FromSeconds(80));
        coordinator.Commit(T0);

        var next = first with
        {
            Track = new TrackInfo("Outra", "B", null, null, "Outra|B|"),
            TrackGeneration = 2,
            Timeline = first.Timeline with { Position = TimeSpan.Zero, LastUpdatedUtc = T0.AddSeconds(1) }
        };
        coordinator.Sync(next, TimeSpan.Zero, T0.AddSeconds(1));

        Assert.False(coordinator.IsPreviewing);
        Assert.False(coordinator.IsPending);
        Assert.Null(coordinator.TakeOutbound());
        Assert.Equal(TimeSpan.Zero, coordinator.Present(T0.AddSeconds(1)).DisplayPosition);
    }

    [Fact]
    public void Session_instance_change_cancels_even_when_aumid_stays()
    {
        var coordinator = new SeekCoordinator();
        var first = Playing() with { SessionId = "Spotify", SourceAppId = "Spotify", SessionInstanceId = "Spotify#1" };
        coordinator.Sync(first, TimeSpan.FromSeconds(20), T0);
        coordinator.BeginPreview(SeekIdentity.From(first), SeekMapping.From(first.Timeline, true), TimeSpan.FromSeconds(70));

        var second = first with { SessionInstanceId = "Spotify#2", TrackGeneration = 1 };
        coordinator.Sync(second, TimeSpan.FromSeconds(5), T0.AddSeconds(1));

        Assert.False(coordinator.IsPreviewing);
        Assert.Equal(TimeSpan.FromSeconds(5), coordinator.Present(T0.AddSeconds(1)).DisplayPosition);
    }

    [Fact]
    public void Stale_command_result_does_not_override_a_newer_gesture()
    {
        var coordinator = new SeekCoordinator();
        var snapshot = Playing();
        coordinator.Sync(snapshot, TimeSpan.FromSeconds(10), T0);
        var range = SeekMapping.From(snapshot.Timeline, true);
        var identity = SeekIdentity.From(snapshot);

        coordinator.BeginPreview(identity, range, TimeSpan.FromSeconds(40));
        var first = coordinator.Commit(T0);
        coordinator.TakeOutbound();
        coordinator.BeginPreview(identity, range, TimeSpan.FromSeconds(90));
        var second = coordinator.Commit(T0.AddMilliseconds(10));
        coordinator.CompleteCommand(first!.Token, false, T0.AddMilliseconds(20));

        Assert.True(coordinator.IsPending);
        Assert.Equal(second!.Token, coordinator.Present(T0.AddMilliseconds(20)).IsPending ? second.Token : second.Token);
        Assert.Equal(TimeSpan.FromSeconds(90), coordinator.Present(T0.AddMilliseconds(20)).DisplayPosition);
        Assert.Null(coordinator.Present(T0.AddMilliseconds(20)).FailureText);
    }

    [Fact]
    public void Escape_and_capture_loss_cancel_without_sending()
    {
        var coordinator = new SeekCoordinator();
        var snapshot = Playing();
        coordinator.Sync(snapshot, TimeSpan.FromSeconds(15), T0);
        coordinator.BeginPreview(SeekIdentity.From(snapshot), SeekMapping.From(snapshot.Timeline, true), TimeSpan.FromSeconds(60));
        coordinator.UpdatePreview(TimeSpan.FromSeconds(75));
        coordinator.Cancel();

        Assert.False(coordinator.IsPreviewing);
        Assert.Null(coordinator.TakeOutbound());
        Assert.Equal(TimeSpan.FromSeconds(15), coordinator.Present(T0).DisplayPosition);
    }

    [Fact]
    public void Keyboard_repeats_are_grouped_into_one_command()
    {
        var coordinator = new SeekCoordinator();
        var snapshot = Playing();
        coordinator.Sync(snapshot, TimeSpan.FromSeconds(10), T0);
        var identity = SeekIdentity.From(snapshot);
        var range = SeekMapping.From(snapshot.Timeline, true);

        coordinator.BeginOrContinueKeyboard(identity, range, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        coordinator.BeginOrContinueKeyboard(identity, range, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(5));
        coordinator.BeginOrContinueKeyboard(identity, range, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(5));
        Assert.Null(coordinator.TakeOutbound());
        var request = coordinator.EndKeyboard(T0);

        Assert.NotNull(request);
        Assert.Equal(TimeSpan.FromSeconds(25), request.DisplayPosition);
        Assert.Null(coordinator.EndKeyboard(T0.AddMilliseconds(10)));
    }

    [Fact]
    public void Seek_while_paused_does_not_require_playback()
    {
        var coordinator = new SeekCoordinator();
        var snapshot = Playing() with
        {
            Status = MediaPlaybackStatus.Paused,
            Timeline = Playing().Timeline with { IsPlaying = false, Position = TimeSpan.FromSeconds(40) }
        };
        coordinator.Sync(snapshot, TimeSpan.FromSeconds(40), T0);
        coordinator.BeginPreview(SeekIdentity.From(snapshot), SeekMapping.From(snapshot.Timeline, true), TimeSpan.FromSeconds(90));
        var request = coordinator.Commit(T0);

        Assert.NotNull(request);
        Assert.Equal(TimeSpan.FromSeconds(90).Ticks, request.PositionTicks);
        Assert.Equal(MediaPlaybackStatus.Paused, snapshot.Status);
        Assert.False(snapshot.Timeline.IsPlaying);
    }

    [Fact]
    public void Rapid_commits_keep_only_the_latest_outbound_request()
    {
        var coordinator = new SeekCoordinator();
        var snapshot = Playing();
        coordinator.Sync(snapshot, TimeSpan.FromSeconds(10), T0);
        var range = SeekMapping.From(snapshot.Timeline, true);
        var identity = SeekIdentity.From(snapshot);

        coordinator.BeginPreview(identity, range, TimeSpan.FromSeconds(20));
        coordinator.Commit(T0);
        coordinator.BeginPreview(identity, range, TimeSpan.FromSeconds(40));
        coordinator.Commit(T0.AddMilliseconds(5));
        coordinator.BeginPreview(identity, range, TimeSpan.FromSeconds(70));
        var last = coordinator.Commit(T0.AddMilliseconds(10));

        var outbound = coordinator.TakeOutbound();
        Assert.Equal(last!.Token, outbound!.Token);
        Assert.Equal(TimeSpan.FromSeconds(70).Ticks, outbound.PositionTicks);
        Assert.Null(coordinator.TakeOutbound());
    }

    [Fact]
    public void Stale_timeline_after_commit_does_not_jump_back()
    {
        var coordinator = new SeekCoordinator();
        var snapshot = Playing();
        coordinator.Sync(snapshot, TimeSpan.FromSeconds(20), T0);
        coordinator.BeginPreview(SeekIdentity.From(snapshot), SeekMapping.From(snapshot.Timeline, true), TimeSpan.FromSeconds(80));
        coordinator.Commit(T0.AddSeconds(1));

        var stale = snapshot with
        {
            Timeline = snapshot.Timeline with
            {
                Position = TimeSpan.FromSeconds(21),
                LastUpdatedUtc = T0
            }
        };
        coordinator.Sync(stale, TimeSpan.FromSeconds(21), T0.AddSeconds(1.2));

        Assert.True(coordinator.IsPending);
        Assert.Equal(TimeSpan.FromSeconds(80), coordinator.Present(T0.AddSeconds(1.2)).DisplayPosition);
    }

    [Fact]
    public void Unsupported_session_does_not_begin_preview()
    {
        var coordinator = new SeekCoordinator();
        var snapshot = Playing() with { Commands = new CommandAvailability(true, true, true, false) };
        coordinator.Sync(snapshot, TimeSpan.FromSeconds(20), T0);

        Assert.False(coordinator.BeginPreview(
            SeekIdentity.From(snapshot),
            SeekMapping.From(snapshot.Timeline, false),
            TimeSpan.FromSeconds(40)));
        Assert.False(coordinator.Present(T0).ShowThumb);
    }

    private static MediaSnapshot Playing() => new(
        MediaPlaybackStatus.Playing,
        new TrackInfo("Song", "Artist", null, null, "Song|Artist|"),
        new TimelineInfo(TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(3), T0, 1, true),
        new CommandAvailability(true, true, true, true),
        "spotify",
        "Spotify.exe",
        "spotify#1",
        1);
}
