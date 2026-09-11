namespace TaskBarHook.Desktop;

public interface IDesktopEnvironment
{
    MonitorGeometry GetPrimaryMonitor();

    TaskbarGeometry GetTaskbar();

    FullscreenAssessment AssessFullscreen(IReadOnlyList<nint> excludeHwnds);

    double GetScale(nint hwnd);
}
