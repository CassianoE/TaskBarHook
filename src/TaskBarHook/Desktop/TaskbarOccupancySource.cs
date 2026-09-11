using System.Text;
using System.Windows.Automation;
using TaskBarHook.Logging;

namespace TaskBarHook.Desktop;

public sealed class TaskbarOccupancySource : ITaskbarOccupancySource
{
    private readonly IDesktopEnvironment _environment;
    private readonly IAppLogger _logger;
    private string? _lastStatusKey;

    public TaskbarOccupancySource(IDesktopEnvironment environment, IAppLogger logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public TaskbarOccupancy Read()
    {
        try
        {
            var taskbar = _environment.GetTaskbar();
            var monitor = _environment.GetPrimaryMonitor();
            var tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (!taskbar.Found || tray == nint.Zero)
            {
                return Log(TaskbarOccupancy.Unavailable("taskbar-window-missing"));
            }

            var scale = _environment.GetScale(tray);
            var taskbarRect = new PixelRect(taskbar.Left, taskbar.Top, taskbar.Right, taskbar.Bottom);
            var supported = taskbar.Edge == TaskbarEdge.Bottom && !taskbar.AutoHide;
            var win32 = CollectWin32Children(tray, taskbarRect);
            var notify = CollectNotifyArea(taskbarRect);
            var automation = CollectAutomation(tray, taskbarRect);
            return Log(OccupancyReadAssembler.Assemble(
                true,
                taskbar,
                monitor,
                scale,
                supported,
                notify,
                automation,
                win32));
        }
        catch (Exception ex)
        {
            _logger.Error("Taskbar occupancy read failed.", ex);
            return Log(TaskbarOccupancy.Unavailable(ex.GetType().Name));
        }
    }

    public string Dump()
    {
        var snapshot = Read();
        var builder = new StringBuilder();
        builder.AppendLine($"success={snapshot.Success} status={snapshot.Status} supported={snapshot.LayoutSupported} scale={snapshot.Scale:0.##} error={snapshot.Error ?? "none"}");
        builder.AppendLine($"taskbar edge={snapshot.Taskbar.Edge} autoHide={snapshot.Taskbar.AutoHide} rect={snapshot.Taskbar.Left},{snapshot.Taskbar.Top}-{snapshot.Taskbar.Right},{snapshot.Taskbar.Bottom} {snapshot.Taskbar.Right - snapshot.Taskbar.Left}x{snapshot.Taskbar.Bottom - snapshot.Taskbar.Top}");
        builder.AppendLine($"monitor {snapshot.Monitor.Width}x{snapshot.Monitor.Height} work={snapshot.Monitor.WorkWidth}x{snapshot.Monitor.WorkHeight}");
        foreach (var region in snapshot.Occupied)
        {
            builder.AppendLine($"- {region.Kind} id={region.AutomationId ?? "-"} {region.Rect}");
        }

        if (snapshot.Success)
        {
            var occupied = snapshot.Occupied.Select(region => region.Rect).ToList();
            var full = CompactPlacementPolicy.Choose(new CompactPlacementRequest(
                new PixelRect(snapshot.Taskbar.Left, snapshot.Taskbar.Top, snapshot.Taskbar.Right, snapshot.Taskbar.Bottom),
                occupied,
                null,
                FullWidth: (int)Math.Round(200 * snapshot.Scale),
                MiniWidth: (int)Math.Round(72 * snapshot.Scale),
                Height: (int)Math.Round(32 * snapshot.Scale),
                HorizontalPadding: (int)Math.Round(8 * snapshot.Scale),
                VerticalInset: (int)Math.Round(4 * snapshot.Scale),
                OccupancyKnown: true,
                LayoutSupported: snapshot.LayoutSupported));
            builder.AppendLine($"placement fit={full.Fit} preserved={full.PreservedPrevious} reason={full.Reason} slot={full.Slot}");
        }
        else
        {
            builder.AppendLine("placement withheld; occupancy is not complete.");
        }

        return builder.ToString();
    }

    private TaskbarOccupancy Log(TaskbarOccupancy occupancy)
    {
        var key = $"{occupancy.Status}:{occupancy.Error ?? "none"}";
        if (key == _lastStatusKey)
        {
            return occupancy;
        }

        _lastStatusKey = key;
        if (occupancy.Success)
        {
            _logger.Info("Occupancy complete.");
        }
        else
        {
            _logger.Warn($"Occupancy {occupancy.Status}: {occupancy.Error}.");
        }

        return occupancy;
    }

    private static OccupancyFragment CollectWin32Children(nint tray, PixelRect taskbar)
    {
        var occupied = new List<OccupiedRegion>();
        NativeMethods.EnumChildWindows(tray, (hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || !NativeMethods.GetWindowRect(hwnd, out var rect))
            {
                return true;
            }

            var region = new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom).Intersect(taskbar);
            if (region.IsEmpty || region.Width >= taskbar.Width * 0.7)
            {
                return true;
            }

            var className = ClassName(hwnd);
            occupied.Add(new OccupiedRegion(region, KindFromClass(className), className));
            return true;
        }, nint.Zero);

        return OccupancyFragment.Complete(occupied);
    }

    private static OccupancyFragment CollectNotifyArea(PixelRect taskbar)
    {
        var notify = NativeMethods.FindWindowEx(
            NativeMethods.FindWindow("Shell_TrayWnd", null),
            nint.Zero,
            "TrayNotifyWnd",
            null);
        if (notify == nint.Zero || !NativeMethods.GetWindowRect(notify, out var rect))
        {
            return OccupancyFragment.Incomplete("notify-missing");
        }

        var region = new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom).Intersect(taskbar);
        if (region.IsEmpty)
        {
            return OccupancyFragment.Incomplete("notify-empty");
        }

        return OccupancyFragment.Complete([new OccupiedRegion(region, "Notify", "TrayNotifyWnd")]);
    }

    private static OccupancyFragment CollectAutomation(nint tray, PixelRect taskbar)
    {
        AutomationElement? root;
        try
        {
            root = AutomationElement.FromHandle(tray);
        }
        catch (Exception ex)
        {
            return OccupancyFragment.Unavailable(ex.GetType().Name);
        }

        if (root is null)
        {
            return OccupancyFragment.Unavailable("automation-root-missing");
        }

        return AutomationOccupancyWalker.Walk(new AutomationElementNode(root), taskbar);
    }

    private static string KindFromClass(string className)
    {
        if (className.Contains("Notify", StringComparison.OrdinalIgnoreCase) ||
            className.Contains("Tray", StringComparison.OrdinalIgnoreCase))
        {
            return "Notify";
        }

        if (className.Contains("TaskList", StringComparison.OrdinalIgnoreCase) ||
            className.Contains("MSTask", StringComparison.OrdinalIgnoreCase))
        {
            return "Apps";
        }

        return "Win32";
    }

    private static string ClassName(nint hwnd)
    {
        var buffer = new char[256];
        var length = NativeMethods.GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private sealed class AutomationElementNode : IAutomationNode
    {
        private readonly AutomationElement _element;

        public AutomationElementNode(AutomationElement element)
        {
            _element = element;
        }

        public OccupancyAttempt<IReadOnlyList<IAutomationNode>> GetChildren()
        {
            try
            {
                var children = _element.FindAll(TreeScope.Children, Condition.TrueCondition);
                if (children is null || children.Count == 0)
                {
                    return OccupancyAttempt<IReadOnlyList<IAutomationNode>>.Ok([]);
                }

                var nodes = new List<IAutomationNode>(children.Count);
                foreach (AutomationElement child in children)
                {
                    nodes.Add(new AutomationElementNode(child));
                }

                return OccupancyAttempt<IReadOnlyList<IAutomationNode>>.Ok(nodes);
            }
            catch (Exception ex)
            {
                return OccupancyAttempt<IReadOnlyList<IAutomationNode>>.Fail(ex.GetType().Name);
            }
        }

        public OccupancyAttempt<PixelRect> GetBounds()
        {
            try
            {
                var rect = _element.Current.BoundingRectangle;
                if (rect.IsEmpty)
                {
                    return OccupancyAttempt<PixelRect>.Ok(default);
                }

                return OccupancyAttempt<PixelRect>.Ok(new PixelRect(
                    (int)rect.X,
                    (int)rect.Y,
                    (int)(rect.X + rect.Width),
                    (int)(rect.Y + rect.Height)));
            }
            catch (Exception ex)
            {
                return OccupancyAttempt<PixelRect>.Fail(ex.GetType().Name);
            }
        }

        public OccupancyAttempt<string?> GetAutomationId()
        {
            try
            {
                return OccupancyAttempt<string?>.Ok(_element.Current.AutomationId);
            }
            catch
            {
                return OccupancyAttempt<string?>.Fail("automation-id-optional");
            }
        }
    }
}
