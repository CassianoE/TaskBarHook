using TaskBarHook.Models;
using TaskBarHook.Presentation;

namespace TaskBarHook.Tests;

public sealed class VisibilityPolicyTests
{
    private static VisibilityInput Live(bool compactSlot = true, bool panel = false) => new(
        UserHidden: false,
        HasLiveSession: true,
        Fullscreen: false,
        UserRequestedEmptyPanel: false,
        WithinNoMediaGrace: false,
        PanelRequested: panel,
        HasTaskbarSlot: compactSlot,
        OccupancyKnown: true,
        LayoutSupported: true,
        ShellConflict: false);

    [Fact]
    public void User_hide_is_not_undone_by_a_new_track()
    {
        var input = Live() with { UserHidden = true };
        var decision = VisibilityPolicy.Evaluate(input);

        Assert.False(decision.ShowCompact);
        Assert.False(decision.ShowPanel);
        Assert.Equal(HideReason.User, decision.Reason);
    }

    [Fact]
    public void Keeps_compact_visible_during_session_swap_grace()
    {
        var decision = VisibilityPolicy.Evaluate(Live() with
        {
            HasLiveSession = false,
            WithinNoMediaGrace = true
        });

        Assert.True(decision.ShowCompact);
        Assert.Equal(HideReason.None, decision.Reason);
    }

    [Fact]
    public void Hides_after_grace_when_there_is_no_session()
    {
        var decision = VisibilityPolicy.Evaluate(Live() with { HasLiveSession = false });

        Assert.False(decision.ShowCompact);
        Assert.False(decision.ShowPanel);
        Assert.Equal(HideReason.NoMedia, decision.Reason);
    }

    [Fact]
    public void Fullscreen_hides_without_clearing_user_preference()
    {
        var fullscreen = VisibilityPolicy.Evaluate(Live() with { Fullscreen = true });
        var after = VisibilityPolicy.Evaluate(Live());

        Assert.False(fullscreen.ShowCompact);
        Assert.Equal(HideReason.Fullscreen, fullscreen.Reason);
        Assert.True(after.ShowCompact);
        Assert.Equal(HideReason.None, after.Reason);
    }

    [Fact]
    public void Tray_can_show_an_empty_panel_without_fake_media()
    {
        var decision = VisibilityPolicy.Evaluate(Live() with
        {
            HasLiveSession = false,
            UserRequestedEmptyPanel = true,
            PanelRequested = true
        });

        Assert.True(decision.ShowPanel);
        Assert.Equal(HideReason.None, decision.Reason);
    }

    [Fact]
    public void User_hide_wins_over_empty_panel_and_fullscreen()
    {
        var decision = VisibilityPolicy.Evaluate(Live() with
        {
            UserHidden = true,
            HasLiveSession = false,
            Fullscreen = true,
            UserRequestedEmptyPanel = true,
            WithinNoMediaGrace = true,
            PanelRequested = true
        });

        Assert.False(decision.ShowCompact);
        Assert.False(decision.ShowPanel);
        Assert.Equal(HideReason.User, decision.Reason);
    }

    [Fact]
    public void Missing_slot_hides_compact_but_can_keep_an_explicit_panel()
    {
        var decision = VisibilityPolicy.Evaluate(Live(compactSlot: false, panel: true));

        Assert.False(decision.ShowCompact);
        Assert.True(decision.ShowPanel);
        Assert.Equal(HideReason.NoSlot, decision.Reason);
    }

    [Fact]
    public void Unknown_occupancy_is_conservative_and_does_not_guess_a_gap()
    {
        var decision = VisibilityPolicy.Evaluate(Live() with { OccupancyKnown = false, PanelRequested = true });

        Assert.False(decision.ShowCompact);
        Assert.True(decision.ShowPanel);
        Assert.Equal(HideReason.NoSlot, decision.Reason);
    }

    [Fact]
    public void Shell_conflict_hides_compact_without_using_the_old_overlay()
    {
        var decision = VisibilityPolicy.Evaluate(Live(panel: true) with { ShellConflict = true });

        Assert.False(decision.ShowCompact);
        Assert.True(decision.ShowPanel);
        Assert.Equal(HideReason.ShellConflict, decision.Reason);
    }
}
