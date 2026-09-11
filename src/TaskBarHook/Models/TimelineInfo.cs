namespace TaskBarHook.Models;

public sealed record TimelineInfo(
    TimeSpan? Position,
    TimeSpan? Duration,
    DateTimeOffset? LastUpdatedUtc,
    double PlaybackRate,
    bool IsPlaying,
    TimeSpan StartTime = default,
    TimeSpan? EndTime = null,
    TimeSpan? MinSeekTime = null,
    TimeSpan? MaxSeekTime = null)
{
    public static TimelineInfo Unknown { get; } = new(null, null, null, 1, false);

    public bool HasUsablePosition => Position is not null;

    public TimeSpan? DisplayDuration
    {
        get
        {
            if (EndTime is { } end && end > StartTime)
            {
                return end - StartTime;
            }

            return Duration is { } duration && duration > TimeSpan.Zero ? duration : null;
        }
    }
}
