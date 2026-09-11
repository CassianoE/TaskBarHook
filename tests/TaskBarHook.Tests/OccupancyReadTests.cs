using TaskBarHook.Desktop;

namespace TaskBarHook.Tests;

public sealed class OccupancyReadTests
{
    private static readonly PixelRect Taskbar = new(0, 1032, 1920, 1080);
    private static readonly TaskbarGeometry Geometry = new(TaskbarEdge.Bottom, 0, 1032, 1920, 1080, false, true);
    private static readonly MonitorGeometry Monitor = new(0, 0, 1920, 1080, 0, 0, 1920, 1032);

    [Fact]
    public void Notify_found_with_failed_app_walk_is_not_a_known_gap()
    {
        var notify = OccupancyFragment.Complete(
        [
            new OccupiedRegion(new PixelRect(1698, 1032, 1920, 1080), "Notify", "TrayNotifyWnd")
        ]);
        var automation = OccupancyFragment.Unavailable("automation-root-failed");
        var occupancy = OccupancyReadAssembler.Assemble(
            true, Geometry, Monitor, 1, true, notify, automation, OccupancyFragment.Complete([]));

        Assert.Equal(OccupancyReadStatus.Incomplete, occupancy.Status);
        Assert.False(occupancy.Success);

        var placement = CompactPlacementPolicy.Choose(new CompactPlacementRequest(
            Taskbar,
            occupancy.Occupied.Select(region => region.Rect).ToList(),
            null,
            200,
            72,
            32,
            8,
            4,
            occupancy.Success,
            true));

        Assert.Equal(CompactFit.Hidden, placement.Fit);
        Assert.Equal("occupancy-unknown", placement.Reason);
    }

    [Fact]
    public void Mid_tree_child_failure_marks_the_walk_incomplete()
    {
        var root = new FakeNode
        {
            Children = OccupancyAttempt<IReadOnlyList<IAutomationNode>>.Ok(
            [
                Leaf("Start", new PixelRect(650, 1032, 695, 1080)),
                new FakeNode
                {
                    Children = OccupancyAttempt<IReadOnlyList<IAutomationNode>>.Fail("ElementNotAvailableException")
                }
            ])
        };

        var walk = AutomationOccupancyWalker.Walk(root, Taskbar);

        Assert.Equal(OccupancyReadStatus.Incomplete, walk.Status);
        Assert.Equal("ElementNotAvailableException", walk.Error);
    }

    [Fact]
    public void Complete_walk_with_notify_is_trusted()
    {
        var notify = OccupancyFragment.Complete(
        [
            new OccupiedRegion(new PixelRect(1698, 1032, 1920, 1080), "Notify", "TrayNotifyWnd")
        ]);
        var automation = AutomationOccupancyWalker.Walk(
            new FakeNode
            {
                Children = OccupancyAttempt<IReadOnlyList<IAutomationNode>>.Ok(
                [
                    Leaf("StartButton", new PixelRect(650, 1032, 695, 1080)),
                    Leaf("SearchButton", new PixelRect(695, 1032, 917, 1080)),
                    Leaf("App_Explorer", new PixelRect(963, 1032, 1007, 1080))
                ])
            },
            Taskbar);

        var occupancy = OccupancyReadAssembler.Assemble(
            true, Geometry, Monitor, 1, true, notify, automation, OccupancyFragment.Complete([]));

        Assert.Equal(OccupancyReadStatus.Complete, occupancy.Status);
        Assert.True(occupancy.Success);
        Assert.Contains(occupancy.Occupied, region => region.Kind == "Start");
    }

    [Fact]
    public void Optional_automation_id_failure_does_not_invalidate_geometry()
    {
        var leaf = new FakeNode
        {
            Bounds = OccupancyAttempt<PixelRect>.Ok(new PixelRect(650, 1032, 695, 1080)),
            AutomationId = OccupancyAttempt<string?>.Fail("automation-id-optional")
        };
        var walk = AutomationOccupancyWalker.Walk(leaf, Taskbar);

        Assert.Equal(OccupancyReadStatus.Complete, walk.Status);
        Assert.Single(walk.Regions);
        Assert.Null(walk.Regions[0].AutomationId);
        Assert.Equal("Leaf", walk.Regions[0].Kind);
    }

    [Fact]
    public void Leaf_geometry_failure_is_not_treated_as_an_empty_gap()
    {
        var walk = AutomationOccupancyWalker.Walk(
            new FakeNode
            {
                Bounds = OccupancyAttempt<PixelRect>.Fail("ElementNotAvailableException")
            },
            Taskbar);

        Assert.Equal(OccupancyReadStatus.Incomplete, walk.Status);
        Assert.Equal("ElementNotAvailableException", walk.Error);
    }

    [Fact]
    public void Empty_automation_walk_is_complete_when_win32_covers_start_and_apps()
    {
        var notify = OccupancyFragment.Complete(
        [
            new OccupiedRegion(new PixelRect(1698, 1032, 1920, 1080), "Notify", "TrayNotifyWnd")
        ]);
        var win32 = OccupancyFragment.Complete(
        [
            new OccupiedRegion(new PixelRect(650, 1032, 695, 1080), "Start", "Start"),
            new OccupiedRegion(new PixelRect(963, 1032, 1271, 1080), "Apps", "MSTaskListWClass")
        ]);
        var occupancy = OccupancyReadAssembler.Assemble(
            true,
            Geometry,
            Monitor,
            1,
            true,
            notify,
            OccupancyFragment.Complete([]),
            win32);

        Assert.True(occupancy.Success);
        Assert.Equal(OccupancyReadStatus.Complete, occupancy.Status);
    }

    [Fact]
    public void Empty_automation_walk_with_only_widgets_is_incomplete()
    {
        var notify = OccupancyFragment.Complete(
        [
            new OccupiedRegion(new PixelRect(1698, 1032, 1920, 1080), "Notify", "TrayNotifyWnd")
        ]);
        var win32 = OccupancyFragment.Complete(
        [
            new OccupiedRegion(new PixelRect(48, 1032, 120, 1080), "Widgets", "Widgets")
        ]);
        var occupancy = OccupancyReadAssembler.Assemble(
            true,
            Geometry,
            Monitor,
            1,
            true,
            notify,
            OccupancyFragment.Complete([]),
            win32);

        Assert.Equal(OccupancyReadStatus.Incomplete, occupancy.Status);
        Assert.False(occupancy.Success);
        Assert.Equal("structural-coverage-insufficient", occupancy.Error);

        var placement = CompactPlacementPolicy.Choose(new CompactPlacementRequest(
            Taskbar,
            occupancy.Occupied.Select(region => region.Rect).ToList(),
            null,
            200,
            72,
            32,
            8,
            4,
            occupancy.Success,
            true));
        Assert.Equal(CompactFit.Hidden, placement.Fit);
        Assert.Equal("occupancy-unknown", placement.Reason);
    }

    [Fact]
    public void Empty_automation_walk_with_generic_win32_is_not_whole_bar_coverage()
    {
        var notify = OccupancyFragment.Complete(
        [
            new OccupiedRegion(new PixelRect(1698, 1032, 1920, 1080), "Notify", "TrayNotifyWnd")
        ]);
        var win32 = OccupancyFragment.Complete(
        [
            new OccupiedRegion(new PixelRect(200, 1032, 280, 1080), "Win32", "UnknownChrome")
        ]);
        var occupancy = OccupancyReadAssembler.Assemble(
            true,
            Geometry,
            Monitor,
            1,
            true,
            notify,
            OccupancyFragment.Complete([]),
            win32);

        Assert.Equal(OccupancyReadStatus.Incomplete, occupancy.Status);
        Assert.False(occupancy.Success);
        Assert.Equal("structural-coverage-insufficient", occupancy.Error);
    }

    [Fact]
    public void Empty_automation_walk_with_only_apps_is_incomplete()
    {
        var notify = OccupancyFragment.Complete(
        [
            new OccupiedRegion(new PixelRect(1698, 1032, 1920, 1080), "Notify", "TrayNotifyWnd")
        ]);
        var win32 = OccupancyFragment.Complete(
        [
            new OccupiedRegion(new PixelRect(963, 1032, 1271, 1080), "Apps", "MSTaskListWClass")
        ]);
        var occupancy = OccupancyReadAssembler.Assemble(
            true,
            Geometry,
            Monitor,
            1,
            true,
            notify,
            OccupancyFragment.Complete([]),
            win32);

        Assert.Equal(OccupancyReadStatus.Incomplete, occupancy.Status);
        Assert.False(occupancy.Success);
    }

    [Fact]
    public void Recovery_after_unavailability_becomes_complete()
    {
        var notify = OccupancyFragment.Complete(
        [
            new OccupiedRegion(new PixelRect(1698, 1032, 1920, 1080), "Notify", "TrayNotifyWnd")
        ]);
        var failed = OccupancyReadAssembler.Assemble(
            true,
            Geometry,
            Monitor,
            1,
            true,
            notify,
            OccupancyFragment.Unavailable("automation-root-failed"),
            OccupancyFragment.Complete([]));
        Assert.False(failed.Success);

        var recovered = OccupancyReadAssembler.Assemble(
            true,
            Geometry,
            Monitor,
            1,
            true,
            notify,
            OccupancyFragment.Complete(
            [
                new OccupiedRegion(new PixelRect(650, 1032, 695, 1080), "Start", "StartButton"),
                new OccupiedRegion(new PixelRect(963, 1032, 1007, 1080), "Apps", "App_Explorer")
            ]),
            OccupancyFragment.Complete([]));

        Assert.True(recovered.Success);
        Assert.Equal(OccupancyReadStatus.Complete, recovered.Status);
        var placement = CompactPlacementPolicy.Choose(new CompactPlacementRequest(
            Taskbar,
            recovered.Occupied.Select(region => region.Rect).ToList(),
            null,
            200,
            72,
            32,
            8,
            4,
            recovered.Success,
            true));
        Assert.Equal(CompactFit.Full, placement.Fit);
    }

    private static FakeNode Leaf(string id, PixelRect rect) => new()
    {
        Bounds = OccupancyAttempt<PixelRect>.Ok(rect),
        AutomationId = OccupancyAttempt<string?>.Ok(id)
    };

    private sealed class FakeNode : IAutomationNode
    {
        public OccupancyAttempt<IReadOnlyList<IAutomationNode>> Children { get; init; } =
            OccupancyAttempt<IReadOnlyList<IAutomationNode>>.Ok([]);

        public OccupancyAttempt<PixelRect> Bounds { get; init; } =
            OccupancyAttempt<PixelRect>.Ok(default);

        public OccupancyAttempt<string?> AutomationId { get; init; } =
            OccupancyAttempt<string?>.Ok(null);

        public OccupancyAttempt<IReadOnlyList<IAutomationNode>> GetChildren() => Children;

        public OccupancyAttempt<PixelRect> GetBounds() => Bounds;

        public OccupancyAttempt<string?> GetAutomationId() => AutomationId;
    }
}
