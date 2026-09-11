using TaskBarHook.Presentation;

namespace TaskBarHook.Tests;

public sealed class PanelDismissPolicyTests
{
    [Fact]
    public void Keeps_the_panel_when_focus_moves_between_our_surfaces()
    {
        Assert.Equal(
            PanelDismissAction.Keep,
            PanelDismissPolicy.Evaluate(new PanelDismissInput(true, false, false)));
    }

    [Fact]
    public void Passive_pointer_does_not_keep_the_panel_open()
    {
        Assert.Equal(
            PanelDismissAction.Dismiss,
            PanelDismissPolicy.Evaluate(new PanelDismissInput(false, true, false)));
    }

    [Fact]
    public void Click_landing_on_our_hwnd_keeps_the_panel_for_the_click_handler()
    {
        Assert.Equal(
            PanelDismissAction.Keep,
            PanelDismissPolicy.Evaluate(new PanelDismissInput(false, true, true)));
    }

    [Fact]
    public void Click_outside_dismisses()
    {
        Assert.Equal(
            PanelDismissAction.Dismiss,
            PanelDismissPolicy.Evaluate(new PanelDismissInput(false, false, true)));
    }

    [Fact]
    public void Alt_tab_closes_even_when_the_pointer_stays_over_the_panel()
    {
        var coordinator = new PanelDismissCoordinator();
        coordinator.Open();

        Assert.Equal(PanelDismissAction.Keep, coordinator.Apply(new PanelDismissInput(true, true, false)));
        Assert.True(coordinator.IsOpen);

        Assert.Equal(PanelDismissAction.Dismiss, coordinator.Apply(new PanelDismissInput(false, true, false)));
        Assert.False(coordinator.IsOpen);

        coordinator.Apply(new PanelDismissInput(false, false, false));
        Assert.False(coordinator.IsOpen);
    }

    [Fact]
    public void Compact_click_does_not_dismiss_before_the_toggle_handler()
    {
        var coordinator = new PanelDismissCoordinator();
        coordinator.Open();

        Assert.Equal(PanelDismissAction.Keep, coordinator.Apply(new PanelDismissInput(false, true, true)));
        Assert.True(coordinator.IsOpen);
    }

    [Fact]
    public void Foreground_change_to_another_app_closes_an_open_panel()
    {
        var coordinator = new PanelDismissCoordinator();
        coordinator.Open();
        coordinator.Apply(new PanelDismissInput(true, false, false));

        var action = coordinator.Apply(new PanelDismissInput(false, false, false));

        Assert.Equal(PanelDismissAction.Dismiss, action);
        Assert.False(coordinator.IsOpen);
    }
}
