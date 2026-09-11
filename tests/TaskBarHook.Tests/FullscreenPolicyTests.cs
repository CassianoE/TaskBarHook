using TaskBarHook.Desktop;

namespace TaskBarHook.Tests;

public sealed class FullscreenPolicyTests
{
    [Fact]
    public void Browser_fullscreen_video_covering_a_hidden_taskbar_is_fullscreen()
    {
        // Live Chrome/Edge/Brave HTML fullscreen: rect 0,0-1920,1080 covering
        // the monitor and the taskbar, WS_MAXIMIZE still set, tray hidden.
        // A work-area reservation alone must never veto this.
        var geometry = new FullscreenGeometry(
            CoversMonitor: true,
            IsMaximized: true,
            TaskbarEffectivelyVisible: false);

        Assert.True(FullscreenPolicy.IsExclusiveFullscreen(geometry));
    }

    [Fact]
    public void Maximized_app_that_only_fills_the_work_area_is_not_fullscreen()
    {
        // Live maximized window: rect -8,-8-1928,1040 (invisible borders),
        // which never reaches the monitor bottom edge, so CoversMonitor is
        // false and the compact must stay visible.
        var geometry = new FullscreenGeometry(
            CoversMonitor: false,
            IsMaximized: true,
            TaskbarEffectivelyVisible: true);

        Assert.False(FullscreenPolicy.IsExclusiveFullscreen(geometry));
    }

    [Fact]
    public void Borderless_or_f11_covering_the_monitor_is_fullscreen()
    {
        var geometry = new FullscreenGeometry(
            CoversMonitor: true,
            IsMaximized: false,
            TaskbarEffectivelyVisible: false);

        Assert.True(FullscreenPolicy.IsExclusiveFullscreen(geometry));
    }

    [Fact]
    public void Covering_window_with_a_genuinely_visible_taskbar_is_not_fullscreen()
    {
        // The restored maximized veto, now against real visibility: only a
        // taskbar that is shown, uncloaked, and NOT covered by the foreground
        // window keeps a maximized-style covering window out of fullscreen.
        var geometry = new FullscreenGeometry(
            CoversMonitor: true,
            IsMaximized: true,
            TaskbarEffectivelyVisible: true);

        Assert.False(FullscreenPolicy.IsExclusiveFullscreen(geometry));
    }
}
