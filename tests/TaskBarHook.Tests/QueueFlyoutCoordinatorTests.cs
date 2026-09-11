using TaskBarHook.Presentation;

namespace TaskBarHook.Tests;

public sealed class QueueFlyoutCoordinatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Hover_waits_for_intent_before_opening_without_stealing_focus()
    {
        var flyout = new QueueFlyoutCoordinator();
        flyout.SetTrigger(true, T0);
        flyout.Tick(T0 + TimeSpan.FromMilliseconds(100));
        Assert.False(flyout.IsOpen);

        flyout.Tick(T0 + QueueFlyoutCoordinator.HoverOpenDelay);
        Assert.True(flyout.IsOpen);
        Assert.False(flyout.WantsFocus);
    }

    [Fact]
    public void Quick_leave_before_intent_never_opens()
    {
        var flyout = new QueueFlyoutCoordinator();
        flyout.SetTrigger(true, T0);
        flyout.SetTrigger(false, T0 + TimeSpan.FromMilliseconds(80));
        flyout.Tick(T0 + TimeSpan.FromSeconds(1));
        Assert.False(flyout.IsOpen);
    }

    [Fact]
    public void Gap_between_trigger_and_list_does_not_close()
    {
        var flyout = new QueueFlyoutCoordinator();
        flyout.OpenExplicit(stealFocus: false);
        flyout.SetTrigger(true, T0);
        flyout.SetTrigger(false, T0 + TimeSpan.FromMilliseconds(10));
        flyout.Tick(T0 + TimeSpan.FromMilliseconds(80));
        Assert.True(flyout.IsOpen);

        flyout.SetFlyout(true, T0 + TimeSpan.FromMilliseconds(90));
        flyout.Tick(T0 + TimeSpan.FromSeconds(1));
        Assert.True(flyout.IsOpen);
    }

    [Fact]
    public void Leave_closes_after_the_tolerance()
    {
        var flyout = new QueueFlyoutCoordinator();
        flyout.OpenExplicit(stealFocus: false);
        flyout.SetTrigger(true, T0);
        flyout.SetTrigger(false, T0);
        flyout.Tick(T0 + QueueFlyoutCoordinator.CloseDelay);
        Assert.False(flyout.IsOpen);
    }

    [Fact]
    public void Click_opens_with_focus_and_click_outside_closes()
    {
        var flyout = new QueueFlyoutCoordinator();
        flyout.ToggleExplicit();
        Assert.True(flyout.IsOpen);
        Assert.True(flyout.WantsFocus);

        flyout.CloseImmediate();
        Assert.False(flyout.IsOpen);
    }

    [Fact]
    public void Escape_closes_the_list_only()
    {
        var flyout = new QueueFlyoutCoordinator();
        flyout.OpenExplicit(true);
        Assert.True(flyout.CloseFromEscape());
        Assert.False(flyout.IsOpen);
        Assert.False(flyout.CloseFromEscape());
    }

    [Fact]
    public void Fullscreen_or_panel_close_is_immediate()
    {
        var flyout = new QueueFlyoutCoordinator();
        flyout.SetTrigger(true, T0);
        flyout.Tick(T0 + QueueFlyoutCoordinator.HoverOpenDelay);
        flyout.CloseImmediate();
        Assert.False(flyout.IsOpen);
        flyout.Tick(T0 + TimeSpan.FromSeconds(1));
        Assert.False(flyout.IsOpen);
    }
}
