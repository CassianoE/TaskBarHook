using TaskBarHook.Logging;
using TaskBarHook.Media;
using TaskBarHook.Models;

namespace TaskBarHook.Tests;

public sealed class SimulatedSeekTests
{
    [Fact]
    public async Task Seek_while_paused_does_not_start_playback()
    {
        var media = new SimulatedMediaSessionService(new SystemClock(), new NullLogger());
        await media.StartAsync();
        await media.PlayPauseAsync();
        Assert.Equal(MediaPlaybackStatus.Paused, media.Current.Status);

        var ok = await media.SeekAsync(TimeSpan.FromSeconds(45).Ticks);

        Assert.True(ok);
        Assert.Equal(MediaPlaybackStatus.Paused, media.Current.Status);
        Assert.False(media.Current.Timeline.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(45), media.Current.Timeline.Position);
    }

    [Fact]
    public async Task Delayed_seek_can_fail_without_moving()
    {
        var media = new SimulatedMediaSessionService(new SystemClock(), new NullLogger())
        {
            SeekDelay = TimeSpan.FromMilliseconds(20),
            FailNextSeek = true
        };
        await media.StartAsync();
        var before = media.Current.Timeline.Position;

        var ok = await media.SeekAsync(TimeSpan.FromSeconds(80).Ticks);

        Assert.False(ok);
        Assert.Equal(before, media.Current.Timeline.Position);
    }

    [Fact]
    public async Task Track_change_during_seek_publishes_the_new_track()
    {
        var media = new SimulatedMediaSessionService(new SystemClock(), new NullLogger())
        {
            SeekDelay = TimeSpan.FromMilliseconds(15),
            ChangeTrackOnSeek = true
        };
        await media.StartAsync();
        var firstTitle = media.Current.Track.Title;
        var firstGeneration = media.Current.TrackGeneration;

        var ok = await media.SeekAsync(TimeSpan.FromSeconds(90).Ticks);

        Assert.True(ok);
        Assert.NotEqual(firstTitle, media.Current.Track.Title);
        Assert.NotEqual(firstGeneration, media.Current.TrackGeneration);
        Assert.NotEqual(media.Current.SessionId, media.Current.SessionInstanceId);
    }

    [Fact]
    public async Task Offset_start_track_reports_absolute_session_position()
    {
        var media = new SimulatedMediaSessionService(new SystemClock(), new NullLogger());
        await media.StartAsync();
        await media.SkipNextAsync();

        Assert.Equal("Início deslocado", media.Current.Track.Title);
        Assert.Equal(TimeSpan.FromMinutes(10), media.Current.Timeline.StartTime);
        Assert.Equal(TimeSpan.FromMinutes(10), media.Current.Timeline.Position);
        Assert.True(media.Current.Commands.Seek);

        Assert.True(await media.SeekAsync(TimeSpan.FromMinutes(12).Ticks));
        Assert.Equal(TimeSpan.FromMinutes(12), media.Current.Timeline.Position);
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
