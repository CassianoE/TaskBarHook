using System.Windows;
using System.Windows.Interop;

namespace TaskBarHook.Desktop;

internal static class WindowChromeHelper
{
    public static nint Apply(Window window, bool allowActivation)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
        style = (nint)((long)style & ~NativeMethods.WS_EX_APPWINDOW);
        if (allowActivation)
        {
            style = (nint)((long)style & ~NativeMethods.WS_EX_NOACTIVATE);
        }
        else
        {
            style |= NativeMethods.WS_EX_NOACTIVATE;
        }

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, style);
        var preference = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        return hwnd;
    }

    public static void Place(Window window, PixelRect slot, double scale)
    {
        scale = Math.Max(0.5, scale);
        window.Left = slot.Left / scale;
        window.Top = slot.Top / scale;
        window.Width = slot.Width / scale;
        window.Height = slot.Height / scale;
        RaiseWithoutActivating(window);
    }

    public static void RaiseWithoutActivating(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == nint.Zero || !window.IsVisible)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }
}
