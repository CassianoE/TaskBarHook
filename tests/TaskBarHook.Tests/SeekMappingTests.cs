using TaskBarHook.Media;
using TaskBarHook.Models;

namespace TaskBarHook.Tests;

public sealed class SeekMappingTests
{
    [Fact]
    public void Maps_bar_fraction_through_a_non_zero_start_time()
    {
        var timeline = new TimelineInfo(
            TimeSpan.FromMinutes(12),
            TimeSpan.FromMinutes(4),
            DateTimeOffset.UtcNow,
            1,
            true,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(14));
        var range = SeekMapping.From(timeline, true);

        Assert.Equal(TimeSpan.FromMinutes(2), SeekMapping.ToDisplay(TimeSpan.FromMinutes(12), range));
        Assert.Equal(0.5, SeekMapping.ToFraction(TimeSpan.FromMinutes(2), range.DisplayDuration));
        Assert.Equal(TimeSpan.FromMinutes(12), SeekMapping.ToSession(TimeSpan.FromMinutes(2), range));
        Assert.Equal(TimeSpan.FromMinutes(12).Ticks, SeekMapping.ToTicks(SeekMapping.ToSession(SeekMapping.FromFraction(0.5, range), range)));
    }

    [Fact]
    public void Clamps_to_the_seekable_window_inside_the_displayed_duration()
    {
        var timeline = new TimelineInfo(
            TimeSpan.FromMinutes(11),
            TimeSpan.FromMinutes(5),
            DateTimeOffset.UtcNow,
            1,
            true,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(11),
            TimeSpan.FromMinutes(13));
        var range = SeekMapping.From(timeline, true);

        Assert.True(range.CanSeek);
        Assert.Equal(TimeSpan.FromMinutes(11), SeekMapping.ToSession(TimeSpan.Zero, range));
        Assert.Equal(TimeSpan.FromMinutes(13), SeekMapping.ToSession(TimeSpan.FromMinutes(5), range));
        Assert.Equal(TimeSpan.FromMinutes(12), SeekMapping.ToSession(TimeSpan.FromMinutes(2), range));
    }

    [Fact]
    public void Session_without_seek_support_has_no_interactive_range()
    {
        var timeline = new TimelineInfo(
            TimeSpan.FromSeconds(20),
            TimeSpan.FromMinutes(3),
            DateTimeOffset.UtcNow,
            1,
            true);
        var range = SeekMapping.From(timeline, false);

        Assert.False(range.CanSeek);
        Assert.True(range.HasDuration);
    }

    [Fact]
    public void Unknown_duration_cannot_be_seeked()
    {
        var timeline = new TimelineInfo(TimeSpan.FromSeconds(15), null, DateTimeOffset.UtcNow, 1, true);
        var range = SeekMapping.From(timeline, true);

        Assert.False(range.CanSeek);
        Assert.False(range.HasDuration);
    }
}
