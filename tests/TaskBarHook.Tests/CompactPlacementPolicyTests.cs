using TaskBarHook.Desktop;

namespace TaskBarHook.Tests;

public sealed class CompactPlacementPolicyTests
{
    private static readonly PixelRect Taskbar = new(0, 1032, 1920, 1080);

    private static IReadOnlyList<PixelRect> WideRightGap() =>
    [
        new PixelRect(0, 1032, 1100, 1080),
        new PixelRect(1400, 1032, 1920, 1080)
    ];

    private static IReadOnlyList<PixelRect> NarrowRightGap() =>
    [
        new PixelRect(0, 1032, 1100, 1080),
        new PixelRect(1220, 1032, 1920, 1080)
    ];

    private static CompactPlacementRequest Request(
        IReadOnlyList<PixelRect> occupied,
        PixelRect? current = null,
        bool known = true,
        bool supported = true) =>
        new(Taskbar, occupied, current, 200, 72, 32, 8, 4, known, supported);

    [Fact]
    public void Places_full_compact_in_a_large_free_gap()
    {
        var occupied = new[]
        {
            new PixelRect(0, 1032, 120, 1080),
            new PixelRect(650, 1032, 1270, 1080),
            new PixelRect(1698, 1032, 1920, 1080)
        };

        var result = CompactPlacementPolicy.Choose(Request(occupied));

        Assert.Equal(CompactFit.Full, result.Fit);
        Assert.NotNull(result.Slot);
        Assert.True(Taskbar.Contains(result.Slot!.Value));
        Assert.True(result.Slot.Value.Width >= 200);
        Assert.True(result.Slot.Value.Left >= 128);
        Assert.True(result.Slot.Value.Right <= 642);
    }

    [Fact]
    public void Shrinks_to_mini_when_only_a_narrow_gap_remains()
    {
        var occupied = new[]
        {
            new PixelRect(0, 1032, 900, 1080),
            new PixelRect(1000, 1032, 1920, 1080)
        };

        var result = CompactPlacementPolicy.Choose(Request(occupied));

        Assert.Equal(CompactFit.Mini, result.Fit);
        Assert.NotNull(result.Slot);
        Assert.True(Taskbar.Contains(result.Slot!.Value));
        Assert.Equal(72, result.Slot.Value.Width);
    }

    [Fact]
    public void Hides_when_the_taskbar_is_full()
    {
        var occupied = new[] { new PixelRect(20, 1032, 1900, 1080) };

        var result = CompactPlacementPolicy.Choose(Request(occupied));

        Assert.Equal(CompactFit.Hidden, result.Fit);
        Assert.Null(result.Slot);
        Assert.Equal("no-fit", result.Reason);
    }

    [Fact]
    public void Recomputes_when_a_new_occupation_covers_the_current_slot()
    {
        var first = CompactPlacementPolicy.Choose(Request(
        [
            new PixelRect(650, 1032, 1270, 1080),
            new PixelRect(1698, 1032, 1920, 1080)
        ]));
        Assert.Equal(CompactFit.Full, first.Fit);

        var blocked = CompactPlacementPolicy.Choose(Request(
        [
            new PixelRect(first.Slot!.Value.Left - 10, 1032, first.Slot.Value.Right + 10, 1080),
            new PixelRect(1698, 1032, 1920, 1080)
        ], first.Slot));

        Assert.NotEqual(first.Slot, blocked.Slot);
        Assert.True(blocked.Fit is CompactFit.Full or CompactFit.Mini or CompactFit.Hidden);
        if (blocked.Slot is { } slot)
        {
            Assert.False(slot.Intersects(new PixelRect(first.Slot.Value.Left - 10, 1032, first.Slot.Value.Right + 10, 1080)));
        }
    }

    [Fact]
    public void Preserves_the_current_slot_while_it_stays_valid()
    {
        var first = CompactPlacementPolicy.Choose(Request(
        [
            new PixelRect(650, 1032, 1270, 1080),
            new PixelRect(1698, 1032, 1920, 1080)
        ]));

        var again = CompactPlacementPolicy.Choose(Request(
        [
            new PixelRect(650, 1032, 1270, 1080),
            new PixelRect(1698, 1032, 1920, 1080)
        ], first.Slot));

        Assert.True(again.PreservedPrevious);
        Assert.Equal(first.Fit, again.Fit);
        Assert.Equal(first.Slot!.Value.Left, again.Slot!.Value.Left);
    }

    [Fact]
    public void Failed_occupancy_read_does_not_invent_a_gap()
    {
        var result = CompactPlacementPolicy.Choose(Request([], known: false));

        Assert.Equal(CompactFit.Hidden, result.Fit);
        Assert.Equal("occupancy-unknown", result.Reason);
    }

    [Fact]
    public void Shrinks_from_full_to_mini_when_the_gap_narrows()
    {
        var full = CompactPlacementPolicy.Choose(Request(WideRightGap()));
        Assert.Equal(CompactFit.Full, full.Fit);

        var mini = CompactPlacementPolicy.Choose(Request(NarrowRightGap(), full.Slot));

        Assert.Equal(CompactFit.Mini, mini.Fit);
        Assert.True(mini.PreservedPrevious);
        Assert.Equal(full.Slot!.Value.Left, mini.Slot!.Value.Left);
        Assert.False(mini.Slot.Value.Intersects(new PixelRect(1220, 1032, 1920, 1080)));
    }

    [Fact]
    public void Recovers_from_mini_to_full_when_the_gap_returns()
    {
        var mini = CompactPlacementPolicy.Choose(Request(NarrowRightGap()));
        Assert.Equal(CompactFit.Mini, mini.Fit);

        var recovered = CompactPlacementPolicy.Choose(Request(WideRightGap(), mini.Slot));

        Assert.Equal(CompactFit.Full, recovered.Fit);
        Assert.True(recovered.PreservedPrevious);
        Assert.Equal("recover-full", recovered.Reason);
        Assert.Equal(mini.Slot!.Value.Left, recovered.Slot!.Value.Left);
        Assert.False(recovered.Slot.Value.Intersects(new PixelRect(1400, 1032, 1920, 1080)));
    }

    [Fact]
    public void Stays_mini_when_full_still_does_not_fit()
    {
        var mini = CompactPlacementPolicy.Choose(Request(NarrowRightGap()));

        var still = CompactPlacementPolicy.Choose(Request(
        [
            new PixelRect(0, 1032, 1100, 1080),
            new PixelRect(1210, 1032, 1920, 1080)
        ], mini.Slot));

        Assert.Equal(CompactFit.Mini, still.Fit);
        Assert.True(still.PreservedPrevious);
        Assert.Equal(72, still.Slot!.Value.Width);
    }

    [Fact]
    public void Recovers_from_hidden_to_an_available_presentation()
    {
        var hidden = CompactPlacementPolicy.Choose(Request(
        [
            new PixelRect(20, 1032, 1900, 1080)
        ]));
        Assert.Equal(CompactFit.Hidden, hidden.Fit);

        var recovered = CompactPlacementPolicy.Choose(Request(
        [
            new PixelRect(0, 1032, 120, 1080),
            new PixelRect(650, 1032, 1920, 1080)
        ], hidden.Slot));

        Assert.Equal(CompactFit.Full, recovered.Fit);
        Assert.NotNull(recovered.Slot);
        Assert.True(Taskbar.Contains(recovered.Slot!.Value));
    }

    [Fact]
    public void Unsupported_layout_never_falls_back_above_the_taskbar()
    {
        var result = CompactPlacementPolicy.Choose(Request([], supported: false));

        Assert.Equal(CompactFit.Hidden, result.Fit);
        Assert.Null(result.Slot);
        Assert.Equal("unsupported-layout", result.Reason);
    }
}
