namespace TaskBarHook.Desktop;

public sealed record OccupiedRegion(PixelRect Rect, string Kind, string? AutomationId);

public sealed record TaskbarOccupancy(
    OccupancyReadStatus Status,
    bool LayoutSupported,
    TaskbarGeometry Taskbar,
    MonitorGeometry Monitor,
    double Scale,
    IReadOnlyList<OccupiedRegion> Occupied,
    string? Error)
{
    public bool Success => Status == OccupancyReadStatus.Complete;

    public static TaskbarOccupancy Unavailable(string error) =>
        new(OccupancyReadStatus.Unavailable, false, default, default, 1, [], error);

    public static TaskbarOccupancy Incomplete(
        string error,
        bool layoutSupported,
        TaskbarGeometry taskbar,
        MonitorGeometry monitor,
        double scale,
        IReadOnlyList<OccupiedRegion> occupied) =>
        new(OccupancyReadStatus.Incomplete, layoutSupported, taskbar, monitor, scale, occupied, error);
}

public interface ITaskbarOccupancySource
{
    TaskbarOccupancy Read();
}
