using TaskBarHook.Models;

namespace TaskBarHook.Media;

public readonly record struct SeekIdentity(string SessionInstanceId, int TrackGeneration)
{
    public static SeekIdentity From(MediaSnapshot snapshot) =>
        new(
            snapshot.SessionInstanceId ?? snapshot.SessionId ?? string.Empty,
            snapshot.TrackGeneration);

    public bool IsEmpty => string.IsNullOrEmpty(SessionInstanceId) && TrackGeneration == 0;
}

public readonly record struct SeekRange(
    TimeSpan StartTime,
    TimeSpan EndTime,
    TimeSpan MinSeek,
    TimeSpan MaxSeek,
    bool CanSeek)
{
    public static SeekRange None { get; } = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, false);

    public TimeSpan DisplayDuration => EndTime > StartTime ? EndTime - StartTime : TimeSpan.Zero;

    public bool HasDuration => DisplayDuration > TimeSpan.Zero;
}

public static class SeekMapping
{
    public static SeekRange From(TimelineInfo timeline, bool commandAllowsSeek)
    {
        var start = timeline.StartTime;
        var end = timeline.EndTime ?? (timeline.Duration is { } duration && duration > TimeSpan.Zero
            ? start + duration
            : start);
        if (end <= start)
        {
            return new SeekRange(start, end, start, end, false);
        }

        var min = start;
        var max = end;
        if (timeline.MinSeekTime is { } reportedMin &&
            timeline.MaxSeekTime is { } reportedMax &&
            reportedMax > reportedMin)
        {
            min = reportedMin;
            max = reportedMax;
        }

        if (min < start)
        {
            min = start;
        }

        if (max > end)
        {
            max = end;
        }

        var canSeek = commandAllowsSeek && max > min;
        return new SeekRange(start, end, min, max, canSeek);
    }

    public static TimeSpan ToDisplay(TimeSpan sessionPosition, SeekRange range)
    {
        var display = sessionPosition - range.StartTime;
        if (display < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (range.HasDuration && display > range.DisplayDuration)
        {
            return range.DisplayDuration;
        }

        return display;
    }

    public static TimeSpan ToSession(TimeSpan displayPosition, SeekRange range)
    {
        if (displayPosition < TimeSpan.Zero)
        {
            displayPosition = TimeSpan.Zero;
        }

        if (range.HasDuration && displayPosition > range.DisplayDuration)
        {
            displayPosition = range.DisplayDuration;
        }

        return ClampSession(range.StartTime + displayPosition, range);
    }

    public static TimeSpan ClampSession(TimeSpan sessionPosition, SeekRange range)
    {
        if (sessionPosition < range.MinSeek)
        {
            return range.MinSeek;
        }

        if (sessionPosition > range.MaxSeek)
        {
            return range.MaxSeek;
        }

        return sessionPosition;
    }

    public static double? ToFraction(TimeSpan? displayPosition, TimeSpan? displayDuration)
    {
        if (displayPosition is null || displayDuration is null || displayDuration.Value <= TimeSpan.Zero)
        {
            return null;
        }

        var value = displayPosition.Value.TotalSeconds / displayDuration.Value.TotalSeconds;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return null;
        }

        return Math.Clamp(value, 0, 1);
    }

    public static TimeSpan FromFraction(double fraction, SeekRange range)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var display = TimeSpan.FromTicks((long)(range.DisplayDuration.Ticks * fraction));
        return ToDisplay(ToSession(display, range), range);
    }

    public static long ToTicks(TimeSpan sessionPosition) => sessionPosition.Ticks;
}
