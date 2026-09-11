namespace TaskBarHook.Presentation;

public readonly record struct PanelDismissInput(
    bool FocusOnOurSurfaces,
    bool PointerOverOurSurfaces,
    bool PrimaryButtonDown);

public enum PanelDismissAction
{
    Keep,
    Dismiss
}

public static class PanelDismissPolicy
{
    public static PanelDismissAction Evaluate(PanelDismissInput input)
    {
        if (input.FocusOnOurSurfaces)
        {
            return PanelDismissAction.Keep;
        }

        if (input.PointerOverOurSurfaces && input.PrimaryButtonDown)
        {
            return PanelDismissAction.Keep;
        }

        return PanelDismissAction.Dismiss;
    }
}

public sealed class PanelDismissCoordinator
{
    public bool IsOpen { get; private set; }

    public void Open() => IsOpen = true;

    public void Close() => IsOpen = false;

    public PanelDismissAction Apply(PanelDismissInput input)
    {
        if (!IsOpen)
        {
            return PanelDismissAction.Dismiss;
        }

        var action = PanelDismissPolicy.Evaluate(input);
        if (action == PanelDismissAction.Dismiss)
        {
            IsOpen = false;
        }

        return action;
    }
}
