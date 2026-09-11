namespace TaskBarHook.Desktop;

public static class OccupancyReadAssembler
{
    public static TaskbarOccupancy Assemble(
        bool taskbarFound,
        TaskbarGeometry taskbar,
        MonitorGeometry monitor,
        double scale,
        bool layoutSupported,
        OccupancyFragment notify,
        OccupancyFragment automation,
        OccupancyFragment win32)
    {
        var regions = Deduplicate(win32.Regions.Concat(notify.Regions).Concat(automation.Regions).ToList());

        if (!taskbarFound)
        {
            return TaskbarOccupancy.Unavailable("taskbar-window-missing");
        }

        if (automation.Status == OccupancyReadStatus.Unavailable)
        {
            return TaskbarOccupancy.Incomplete(
                automation.Error ?? "automation-unavailable",
                layoutSupported,
                taskbar,
                monitor,
                scale,
                regions);
        }

        if (automation.Status == OccupancyReadStatus.Incomplete)
        {
            return TaskbarOccupancy.Incomplete(
                automation.Error ?? "automation-incomplete",
                layoutSupported,
                taskbar,
                monitor,
                scale,
                regions);
        }

        if (notify.Status != OccupancyReadStatus.Complete)
        {
            return TaskbarOccupancy.Incomplete(
                notify.Error ?? "notify-missing",
                layoutSupported,
                taskbar,
                monitor,
                scale,
                regions);
        }

        if (!HasAdequateCoverage(regions))
        {
            return TaskbarOccupancy.Incomplete(
                automation.Regions.Count == 0
                    ? (HasAnyNonNotifyRegion(regions)
                        ? "structural-coverage-insufficient"
                        : "automation-no-usable-leaves")
                    : "structural-coverage-insufficient",
                layoutSupported,
                taskbar,
                monitor,
                scale,
                regions);
        }

        return new TaskbarOccupancy(
            OccupancyReadStatus.Complete,
            layoutSupported,
            taskbar,
            monitor,
            scale,
            regions,
            null);
    }

    internal static bool HasAdequateCoverage(IReadOnlyList<OccupiedRegion> regions)
    {
        var hasStartCluster = regions.Any(region => region.Kind is "Start" or "Search" or "TaskView");
        var hasApps = regions.Any(region => region.Kind == "Apps");
        return hasStartCluster && hasApps;
    }

    private static bool HasAnyNonNotifyRegion(IReadOnlyList<OccupiedRegion> regions) =>
        regions.Any(region => region.Kind is not "Notify");

    public static List<OccupiedRegion> Deduplicate(IReadOnlyList<OccupiedRegion> regions)
    {
        return regions
            .GroupBy(region => $"{region.Rect.Left}:{region.Rect.Top}:{region.Rect.Right}:{region.Rect.Bottom}")
            .Select(group => group.First())
            .ToList();
    }
}
