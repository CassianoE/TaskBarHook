namespace TaskBarHook.Desktop;

public readonly record struct FullscreenGeometry(
    bool CoversMonitor,
    bool IsMaximized,
    bool TaskbarEffectivelyVisible);

public static class FullscreenPolicy
{
    public static bool IsExclusiveFullscreen(FullscreenGeometry geometry)
    {
        if (!geometry.CoversMonitor)
        {
            return false;
        }

        // A maximized window with a genuinely visible taskbar is not
        // fullscreen. TaskbarEffectivelyVisible means the tray window is
        // shown, uncloaked, and NOT covered by the foreground window — a work
        // area reservation alone proves nothing (Explorer keeps reserving it
        // while a video covers the taskbar, and Chrome keeps WS_MAXIMIZE).
        if (geometry.IsMaximized && geometry.TaskbarEffectivelyVisible)
        {
            return false;
        }

        return true;
    }
}
