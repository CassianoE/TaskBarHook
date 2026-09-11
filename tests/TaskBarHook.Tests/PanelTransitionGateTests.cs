using TaskBarHook.Views;

namespace TaskBarHook.Tests;

// The open/close animations interrupt and reverse each other; the gate is
// what stops a stale exit completion from hiding a reopened panel.
public sealed class PanelTransitionGateTests
{
    [Fact]
    public void Show_hide_show_rejects_the_stale_hide_completion()
    {
        var gate = new PanelTransitionGate();
        var show = gate.Begin(open: true);
        var hide = gate.Begin(open: false);
        var reshow = gate.Begin(open: true);

        Assert.False(gate.ShouldFinish(hide, open: false));
        Assert.False(gate.ShouldFinish(show, open: true));
        Assert.True(gate.ShouldFinish(reshow, open: true));
    }

    [Fact]
    public void Hide_completes_only_while_it_is_the_latest_intent()
    {
        var gate = new PanelTransitionGate();
        gate.Begin(open: true);
        var hide = gate.Begin(open: false);

        Assert.True(gate.ShouldFinish(hide, open: false));
        Assert.False(gate.ShouldFinish(hide, open: true));
    }

    [Fact]
    public void Double_hide_keeps_only_the_latest_completion()
    {
        var gate = new PanelTransitionGate();
        var first = gate.Begin(open: false);
        var second = gate.Begin(open: false);

        Assert.False(gate.ShouldFinish(first, open: false));
        Assert.True(gate.ShouldFinish(second, open: false));
    }

    [Fact]
    public void Hide_show_hide_rejects_the_stale_show()
    {
        var gate = new PanelTransitionGate();
        gate.Begin(open: false);
        var show = gate.Begin(open: true);
        var hide = gate.Begin(open: false);

        Assert.False(gate.ShouldFinish(show, open: true));
        Assert.True(gate.ShouldFinish(hide, open: false));
    }
}
