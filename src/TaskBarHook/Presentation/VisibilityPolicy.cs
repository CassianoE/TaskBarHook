using TaskBarHook.Models;

namespace TaskBarHook.Presentation;

public sealed record VisibilityInput(
    bool UserHidden,
    bool HasLiveSession,
    bool Fullscreen,
    bool UserRequestedEmptyPanel,
    bool WithinNoMediaGrace,
    bool PanelRequested,
    bool HasTaskbarSlot,
    bool OccupancyKnown,
    bool LayoutSupported,
    bool ShellConflict);

public sealed record VisibilityDecision(bool ShowCompact, bool ShowPanel, HideReason Reason);

public static class VisibilityPolicy
{
    public static VisibilityDecision Evaluate(VisibilityInput input)
    {
        if (input.UserHidden)
        {
            return new VisibilityDecision(false, false, HideReason.User);
        }

        if (input.Fullscreen)
        {
            return new VisibilityDecision(false, false, HideReason.Fullscreen);
        }

        var wantsSurface = input.HasLiveSession || input.WithinNoMediaGrace || input.UserRequestedEmptyPanel;
        var wantsPanel = input.PanelRequested && (wantsSurface || input.UserRequestedEmptyPanel);

        if (!wantsSurface && !wantsPanel)
        {
            return new VisibilityDecision(false, false, HideReason.NoMedia);
        }

        if (!input.LayoutSupported)
        {
            return new VisibilityDecision(false, wantsPanel, HideReason.UnsupportedLayout);
        }

        if (!input.OccupancyKnown)
        {
            return new VisibilityDecision(false, wantsPanel, HideReason.NoSlot);
        }

        if (input.ShellConflict)
        {
            return new VisibilityDecision(false, wantsPanel, HideReason.ShellConflict);
        }

        if (!input.HasTaskbarSlot)
        {
            return new VisibilityDecision(false, wantsPanel, HideReason.NoSlot);
        }

        return new VisibilityDecision(wantsSurface, wantsPanel, HideReason.None);
    }
}
