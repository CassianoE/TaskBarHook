namespace TaskBarHook.Desktop;

public static class AutomationOccupancyWalker
{
    public const int MaxDepth = 10;

    public static OccupancyFragment Walk(IAutomationNode? root, PixelRect taskbar)
    {
        if (root is null)
        {
            return OccupancyFragment.Unavailable("automation-root-missing");
        }

        var regions = new List<OccupiedRegion>();
        var status = WalkNode(root, taskbar, regions, 0);
        return new OccupancyFragment(status.Status, regions, status.Error);
    }

    private static OccupancyFragment WalkNode(
        IAutomationNode node,
        PixelRect taskbar,
        List<OccupiedRegion> regions,
        int depth)
    {
        var children = node.GetChildren();
        if (children.Failed)
        {
            return OccupancyFragment.Incomplete(children.Error ?? "automation-children-failed", regions);
        }

        var nodes = children.Value ?? [];
        if (depth > MaxDepth && nodes.Count > 0)
        {
            return OccupancyFragment.Incomplete("automation-depth-limit", regions);
        }

        if (nodes.Count == 0)
        {
            return AddLeaf(node, taskbar, regions);
        }

        foreach (var child in nodes)
        {
            var childResult = WalkNode(child, taskbar, regions, depth + 1);
            if (childResult.Status != OccupancyReadStatus.Complete)
            {
                return childResult;
            }
        }

        return OccupancyFragment.Complete(regions);
    }

    private static OccupancyFragment AddLeaf(
        IAutomationNode node,
        PixelRect taskbar,
        List<OccupiedRegion> regions)
    {
        var bounds = node.GetBounds();
        if (bounds.Failed)
        {
            return OccupancyFragment.Incomplete(bounds.Error ?? "automation-leaf-geometry-failed", regions);
        }

        var rect = bounds.Value;
        if (rect.IsEmpty)
        {
            return OccupancyFragment.Complete(regions);
        }

        var region = rect.Intersect(taskbar);
        if (region.IsEmpty || region.Width >= taskbar.Width * 0.7)
        {
            return OccupancyFragment.Complete(regions);
        }

        string? automationId = null;
        var id = node.GetAutomationId();
        if (!id.Failed)
        {
            automationId = id.Value;
        }

        regions.Add(new OccupiedRegion(
            region,
            KindFromAutomation(automationId),
            string.IsNullOrWhiteSpace(automationId) ? null : automationId));
        return OccupancyFragment.Complete(regions);
    }

    internal static string KindFromAutomation(string? automationId)
    {
        if (string.IsNullOrWhiteSpace(automationId))
        {
            return "Leaf";
        }

        if (automationId.Contains("Start", StringComparison.OrdinalIgnoreCase))
        {
            return "Start";
        }

        if (automationId.Contains("Search", StringComparison.OrdinalIgnoreCase))
        {
            return "Search";
        }

        if (automationId.Contains("Widget", StringComparison.OrdinalIgnoreCase) ||
            automationId.Contains("Dashboard", StringComparison.OrdinalIgnoreCase))
        {
            return "Widgets";
        }

        if (automationId.Contains("TaskView", StringComparison.OrdinalIgnoreCase))
        {
            return "TaskView";
        }

        if (automationId.Contains("Notify", StringComparison.OrdinalIgnoreCase) ||
            automationId.Contains("SystemTray", StringComparison.OrdinalIgnoreCase) ||
            automationId.Contains("Clock", StringComparison.OrdinalIgnoreCase) ||
            automationId.Contains("Tray", StringComparison.OrdinalIgnoreCase))
        {
            return "Notify";
        }

        if (automationId.Contains("App", StringComparison.OrdinalIgnoreCase))
        {
            return "Apps";
        }

        return "Leaf";
    }
}
