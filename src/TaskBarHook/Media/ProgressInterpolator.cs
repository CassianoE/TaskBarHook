using TaskBarHook.Models;

namespace TaskBarHook.Media;

public static class ProgressInterpolator
{
    public static TimeSpan? Interpolate(TimelineInfo timeline, DateTimeOffset utcNow)
    {
        if (timeline.Position is null)
        {
            return null;
        }

        var position = timeline.Position.Value;
        if (!timeline.IsPlaying || timeline.LastUpdatedUtc is null)
        {
            return Clamp(position, timeline);
        }

        var rate = timeline.PlaybackRate > 0 ? timeline.PlaybackRate : 1;
        var elapsed = utcNow - timeline.LastUpdatedUtc.Value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var projected = position + TimeSpan.FromTicks((long)(elapsed.Ticks * rate));
        return Clamp(projected, timeline);
    }

    public static double? Fraction(TimeSpan? position, TimeSpan? duration)
    {
        return SeekMapping.ToFraction(position, duration);
    }

    private static TimeSpan Clamp(TimeSpan position, TimelineInfo timeline)
    {
        var min = timeline.StartTime;
        var max = timeline.EndTime ?? (timeline.Duration is { } duration ? min + duration : (TimeSpan?)null);
        if (position < min)
        {
            return min;
        }

        if (max is { } limit && position > limit)
        {
            return limit;
        }

        return position;
    }
}
