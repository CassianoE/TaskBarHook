using System.Runtime.InteropServices;
using System.Linq;

namespace TaskBarHook.Desktop;

public sealed class Win32DesktopEnvironment : IDesktopEnvironment
{
    public MonitorGeometry GetPrimaryMonitor()
    {
        var handle = NativeMethods.MonitorFromWindow(nint.Zero, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var info = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };

        if (handle == nint.Zero || !NativeMethods.GetMonitorInfo(handle, ref info))
        {
            var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
            return new MonitorGeometry(0, 0, width, height, 0, 0, width, height);
        }

        var monitor = info.rcMonitor;
        var work = info.rcWork;
        return new MonitorGeometry(
            monitor.Left,
            monitor.Top,
            monitor.Width,
            monitor.Height,
            work.Left,
            work.Top,
            work.Width,
            work.Height);
    }

    public TaskbarGeometry GetTaskbar()
    {
        var data = new NativeMethods.APPBARDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>()
        };

        var found = NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref data) != 0;
        var state = NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref data);
        var autoHide = ((int)state & NativeMethods.ABS_AUTOHIDE) != 0;

        if (!found)
        {
            return new TaskbarGeometry(TaskbarEdge.Unknown, 0, 0, 0, 0, autoHide, false);
        }

        var edge = data.uEdge switch
        {
            NativeMethods.ABE_LEFT => TaskbarEdge.Left,
            NativeMethods.ABE_TOP => TaskbarEdge.Top,
            NativeMethods.ABE_RIGHT => TaskbarEdge.Right,
            NativeMethods.ABE_BOTTOM => TaskbarEdge.Bottom,
            _ => TaskbarEdge.Unknown
        };

        return new TaskbarGeometry(
            edge,
            data.rc.Left,
            data.rc.Top,
            data.rc.Right,
            data.rc.Bottom,
            autoHide,
            true);
    }

    public FullscreenAssessment AssessFullscreen(IReadOnlyList<nint> excludeHwnds)
    {
        if (NativeMethods.SHQueryUserNotificationState(out var state) == 0 &&
            state == NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN)
        {
            return new FullscreenAssessment(true, "QUNS_RUNNING_D3D_FULL_SCREEN");
        }

        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == nint.Zero)
        {
            return new FullscreenAssessment(false, null);
        }

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root != nint.Zero)
        {
            hwnd = root;
        }

        if (excludeHwnds.Contains(hwnd))
        {
            return new FullscreenAssessment(false, null);
        }

        var classBuffer = new char[256];
        var length = NativeMethods.GetClassName(hwnd, classBuffer, classBuffer.Length);
        var className = length > 0 ? new string(classBuffer, 0, length) : string.Empty;
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            return new FullscreenAssessment(false, className);
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return new FullscreenAssessment(false, null);
        }

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var primary = NativeMethods.MonitorFromWindow(nint.Zero, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        if (monitor == nint.Zero || monitor != primary)
        {
            return new FullscreenAssessment(false, "foreground-not-on-primary");
        }

        var info = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return new FullscreenAssessment(false, null);
        }

        var monitorRect = info.rcMonitor;
        var coversMonitor =
            rect.Left <= monitorRect.Left + 2 &&
            rect.Top <= monitorRect.Top + 2 &&
            rect.Right >= monitorRect.Right - 2 &&
            rect.Bottom >= monitorRect.Bottom - 2;
        var isMaximized = NativeMethods.IsZoomed(hwnd);
        var exclusive = FullscreenPolicy.IsExclusiveFullscreen(
            new FullscreenGeometry(coversMonitor, isMaximized, IsTaskbarEffectivelyVisible(rect)));
        var detail = exclusive
            ? $"exclusive-fullscreen class={className} {rect.Left},{rect.Top}-{rect.Right},{rect.Bottom} zoomed={isMaximized}"
            : isMaximized
                ? $"maximized class={className} {rect.Left},{rect.Top}-{rect.Right},{rect.Bottom}"
                : null;
        return new FullscreenAssessment(exclusive, detail);
    }

    private static bool IsTaskbarEffectivelyVisible(NativeMethods.RECT foreground)
    {
        var tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (tray == nint.Zero || !NativeMethods.IsWindowVisible(tray))
        {
            return false;
        }

        if (NativeMethods.DwmGetWindowAttribute(tray, NativeMethods.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 &&
            cloaked != 0)
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(tray, out var trayRect))
        {
            return true;
        }

        // Covered only when the foreground window contains the whole taskbar
        // rect: a maximized window's ~8px invisible border merely nibbles its
        // edge, while a true fullscreen window swallows it entirely.
        var covered =
            foreground.Left <= trayRect.Left + 2 &&
            foreground.Top <= trayRect.Top + 2 &&
            foreground.Right >= trayRect.Right - 2 &&
            foreground.Bottom >= trayRect.Bottom - 2;
        return !covered;
    }

    public double GetScale(nint hwnd)
    {
        if (hwnd != nint.Zero)
        {
            var dpi = NativeMethods.GetDpiForWindow(hwnd);
            if (dpi > 0)
            {
                return dpi / 96.0;
            }
        }

        return 1;
    }
}
