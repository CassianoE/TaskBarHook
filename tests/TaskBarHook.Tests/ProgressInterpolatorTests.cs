using TaskBarHook.Media;
using TaskBarHook.Models;

namespace TaskBarHook.Tests;

public sealed class ProgressInterpolatorTests
{
    [Fact]
    public void Advances_position_only_while_playing()
    {
        var origin = new DateTimeOffset(2026, 9, 6, 4, 0, 0, TimeSpan.Zero);
        var timeline = new TimelineInfo(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3), origin, 1, true);

        var playing = ProgressInterpolator.Interpolate(timeline, origin.AddSeconds(5));
        var paused = ProgressInterpolator.Interpolate(timeline with { IsPlaying = false }, origin.AddSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(15), playing);
        Assert.Equal(TimeSpan.FromSeconds(10), paused);
    }

    [Fact]
    public void Resumes_from_the_last_reported_origin()
    {
        var first = new DateTimeOffset(2026, 9, 6, 4, 0, 0, TimeSpan.Zero);
        var paused = new TimelineInfo(TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(4), first, 1, false);
        Assert.Equal(TimeSpan.FromSeconds(20), ProgressInterpolator.Interpolate(paused, first.AddSeconds(30)));

        var resumedOrigin = first.AddSeconds(30);
        var resumed = paused with { IsPlaying = true, LastUpdatedUtc = resumedOrigin, Position = TimeSpan.FromSeconds(20) };
        Assert.Equal(TimeSpan.FromSeconds(25), ProgressInterpolator.Interpolate(resumed, resumedOrigin.AddSeconds(5)));
    }

    [Fact]
    public void Does_not_invent_progress_when_position_is_missing()
    {
        var now = DateTimeOffset.UtcNow;
        var timeline = new TimelineInfo(null, TimeSpan.FromMinutes(3), now, 1, true);

        Assert.Null(ProgressInterpolator.Interpolate(timeline, now.AddSeconds(10)));
        Assert.Null(ProgressInterpolator.Fraction(null, TimeSpan.FromMinutes(3)));
    }

    [Fact]
    public void Leaves_duration_unknown_without_inventing_a_fraction()
    {
        var now = DateTimeOffset.UtcNow;
        var timeline = new TimelineInfo(TimeSpan.FromSeconds(15), null, now, 1, true);
        var position = ProgressInterpolator.Interpolate(timeline, now.AddSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(20), position);
        Assert.Null(ProgressInterpolator.Fraction(position, null));
    }

    [Fact]
    public void Applies_playback_rate_and_clamps_to_duration()
    {
        var origin = DateTimeOffset.UtcNow;
        var timeline = new TimelineInfo(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(60), origin, 2, true);

        Assert.Equal(TimeSpan.FromSeconds(60), ProgressInterpolator.Interpolate(timeline, origin.AddSeconds(20)));
    }
}
