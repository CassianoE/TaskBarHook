namespace TaskBarHook.Desktop;

public enum OccupancyReadStatus
{
    Complete,
    Incomplete,
    Unavailable
}

public readonly record struct OccupancyAttempt<T>(bool Failed, T? Value, string? Error)
{
    public static OccupancyAttempt<T> Ok(T value) => new(false, value, null);

    public static OccupancyAttempt<T> Fail(string error) => new(true, default, error);
}

public sealed record OccupancyFragment(
    OccupancyReadStatus Status,
    IReadOnlyList<OccupiedRegion> Regions,
    string? Error)
{
    public static OccupancyFragment Complete(IReadOnlyList<OccupiedRegion> regions) =>
        new(OccupancyReadStatus.Complete, regions, null);

    public static OccupancyFragment Incomplete(string error, IReadOnlyList<OccupiedRegion>? regions = null) =>
        new(OccupancyReadStatus.Incomplete, regions ?? [], error);

    public static OccupancyFragment Unavailable(string error) =>
        new(OccupancyReadStatus.Unavailable, [], error);
}

public interface IAutomationNode
{
    OccupancyAttempt<IReadOnlyList<IAutomationNode>> GetChildren();

    OccupancyAttempt<PixelRect> GetBounds();

    OccupancyAttempt<string?> GetAutomationId();
}
