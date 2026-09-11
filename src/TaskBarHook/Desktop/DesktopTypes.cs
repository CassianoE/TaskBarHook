namespace TaskBarHook.Desktop;

public enum TaskbarEdge
{
    Left,
    Top,
    Right,
    Bottom,
    Unknown
}

public readonly record struct MonitorGeometry(
    int X,
    int Y,
    int Width,
    int Height,
    int WorkX,
    int WorkY,
    int WorkWidth,
    int WorkHeight);

public readonly record struct TaskbarGeometry(
    TaskbarEdge Edge,
    int Left,
    int Top,
    int Right,
    int Bottom,
    bool AutoHide,
    bool Found);

public readonly record struct FullscreenAssessment(bool IsFullscreen, string? Detail);

public readonly record struct WindowPlacement(
    double Left,
    double Top,
    double Width,
    double Height,
    TaskbarGeometry Taskbar,
    bool LayoutSupported,
    string? LayoutWarning);
