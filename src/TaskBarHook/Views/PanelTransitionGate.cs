namespace TaskBarHook.Views;

// Pure open/close intent guard for the panel transitions. Rapid toggles bump
// the generation, so a stale exit completion can never hide a reopened panel
// (and a stale enter completion never matters). Tested without a window.
internal sealed class PanelTransitionGate
{
    public int Generation { get; private set; }

    public bool OpenIntent { get; private set; }

    public int Begin(bool open)
    {
        Generation++;
        OpenIntent = open;
        return Generation;
    }

    public bool ShouldFinish(int generation, bool open) =>
        generation == Generation && OpenIntent == open;
}
