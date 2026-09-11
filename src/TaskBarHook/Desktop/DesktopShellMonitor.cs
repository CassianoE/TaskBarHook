using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using TaskBarHook.Logging;

namespace TaskBarHook.Desktop;

public sealed class DesktopShellMonitor : IDisposable
{
    private static readonly HashSet<string> ConflictProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "StartMenuExperienceHost",
        "SearchHost",
        "SearchApp",
        "ShellExperienceHost"
    };

    private static readonly HashSet<string> ConflictClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "TaskListThumbnailWnd",
        "NotifyIconOverflowWindow",
        "DV2ControlHost"
    };

    private readonly IDesktopEnvironment _environment;
    private readonly IAppLogger _logger;
    private readonly NativeMethods.WinEventDelegate _winEventHandler;
    private readonly DispatcherTimer _fullscreenTimer;
    private readonly DispatcherTimer _occupancyTimer;
    private readonly int _taskbarCreatedMessage;

    private HwndSource? _source;
    private nint _foregroundHook;
    private nint _objectHook;
    private Func<IReadOnlyList<nint>> _exclude = () => [];
    internal static readonly TimeSpan DefaultEntryDelay = TimeSpan.FromMilliseconds(150);
    internal static readonly TimeSpan DefaultExitDelay = TimeSpan.FromMilliseconds(300);

    private Func<PixelRect?> _slot = () => null;
    private bool? _lastFullscreen;
    private DateTimeOffset? _fullscreenCandidateSince;
    private DateTimeOffset? _exitCandidateSince;
    private readonly TimeSpan _entryDelay;
    private readonly TimeSpan _exitDelay;
    private bool _lastConflict;
    private bool _disposed;
    private nint _lastForegroundRoot;
    private nint _trayHwnd;

    public DesktopShellMonitor(
        IDesktopEnvironment environment,
        IAppLogger logger,
        TimeSpan? entryDelay = null,
        TimeSpan? exitDelay = null)
    {
        _environment = environment;
        _logger = logger;
        // Fast entry (~150-400ms perceived): the first sample usually arrives
        // immediately via win-event hooks; 150ms still rejects single-sample
        // glitches. Exit at 300ms: quick return (~300-550ms) while isolated
        // false samples (cleared by the next true) still never end it.
        _entryDelay = entryDelay ?? DefaultEntryDelay;
        _exitDelay = exitDelay ?? DefaultExitDelay;
        _winEventHandler = OnWinEvent;
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _fullscreenTimer.Tick += (_, _) => RefreshFullscreen();
        _occupancyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _occupancyTimer.Tick += (_, _) =>
        {
            _occupancyTimer.Stop();
            PlacementInvalidated?.Invoke(this, EventArgs.Empty);
            RefreshShellConflict();
        };
    }

    public event EventHandler<bool>? FullscreenChanged;

    public event EventHandler? PlacementInvalidated;

    public event EventHandler? ExplorerRestarted;

    public event EventHandler? PowerResumed;

    public event EventHandler<bool>? ShellConflictChanged;

    public event EventHandler? SurfacesMayNeedRestack;

    public void Start(Func<IReadOnlyList<nint>> excludeHwnds, Func<PixelRect?> compactSlot)
    {
        _exclude = excludeHwnds;
        _slot = compactSlot;
        RefreshTrayHandle();
        _source = new HwndSource(new HwndSourceParameters("TaskBarHook.ShellMonitor")
        {
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE
        });
        _source.AddHook(WndProc);
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            nint.Zero,
            _winEventHandler,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        _objectHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_SHOW,
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            nint.Zero,
            _winEventHandler,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _fullscreenTimer.Start();
    }

    public void RefreshFullscreen()
    {
        var assessment = _environment.AssessFullscreen(_exclude());
        var now = DateTimeOffset.UtcNow;
        if (!assessment.IsFullscreen)
        {
            _fullscreenCandidateSince = null;
            if (_lastFullscreen != true)
            {
                ApplyFullscreen(false, assessment.Detail);
                return;
            }

            // Symmetric debounce: a single false sample (focus flaps, window
            // transitions, hook races) must not pop the compact back over a
            // sustained fullscreen video.
            _exitCandidateSince ??= now;
            if (now - _exitCandidateSince.Value < _exitDelay)
            {
                return;
            }

            ApplyFullscreen(false, assessment.Detail);
            return;
        }

        _exitCandidateSince = null;
        _fullscreenCandidateSince ??= now;
        if (_lastFullscreen == true)
        {
            return;
        }

        if (now - _fullscreenCandidateSince.Value < _entryDelay)
        {
            return;
        }

        ApplyFullscreen(true, assessment.Detail);
    }

    private void ApplyFullscreen(bool fullscreen, string? detail)
    {
        if (_lastFullscreen == fullscreen)
        {
            return;
        }

        _lastFullscreen = fullscreen;
        _fullscreenCandidateSince = null;
        _exitCandidateSince = null;
        _logger.Info(fullscreen
            ? $"Fullscreen detected ({detail})."
            : $"Fullscreen ended ({detail ?? "no-detail"}).");
        FullscreenChanged?.Invoke(this, fullscreen);
    }

    public void RefreshShellConflict()
    {
        var conflict = DetectShellConflict();
        if (_lastConflict == conflict)
        {
            return;
        }

        _lastConflict = conflict;
        _logger.Info(conflict ? "Shell surface overlap; hiding compact." : "Shell surface cleared.");
        ShellConflictChanged?.Invoke(this, conflict);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fullscreenTimer.Stop();
        _occupancyTimer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        if (_foregroundHook != nint.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
        }

        if (_objectHook != nint.Zero)
        {
            NativeMethods.UnhookWinEvent(_objectHook);
        }

        _source?.RemoveHook(WndProc);
        _source?.Dispose();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == _taskbarCreatedMessage)
        {
            _logger.Info("Explorer TaskbarCreated received.");
            RefreshTrayHandle();
            ExplorerRestarted?.Invoke(this, EventArgs.Empty);
            ScheduleOccupancy();
        }

        if (msg is NativeMethods.WM_DPICHANGED or NativeMethods.WM_DISPLAYCHANGE)
        {
            ScheduleOccupancy();
            RefreshFullscreen();
        }

        return nint.Zero;
    }

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
        {
            _lastForegroundRoot = Root(hwnd);
            _source?.Dispatcher.BeginInvoke(() =>
            {
                RefreshFullscreen();
                RefreshShellConflict();
                SurfacesMayNeedRestack?.Invoke(this, EventArgs.Empty);
            });
            return;
        }

        if (idObject != NativeMethods.OBJID_WINDOW)
        {
            return;
        }

        if (eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE)
        {
            var root = Root(hwnd);
            if (root != nint.Zero && root == _lastForegroundRoot)
            {
                _source?.Dispatcher.BeginInvoke(RefreshFullscreen);
            }

            if (IsTrayRelated(hwnd, root))
            {
                _source?.Dispatcher.BeginInvoke(ScheduleOccupancy);
            }

            return;
        }

        if (eventType is NativeMethods.EVENT_OBJECT_SHOW or NativeMethods.EVENT_OBJECT_HIDE)
        {
            _source?.Dispatcher.BeginInvoke(ScheduleOccupancy);
        }
    }

    private void ScheduleOccupancy()
    {
        _occupancyTimer.Stop();
        _occupancyTimer.Start();
    }

    private bool DetectShellConflict()
    {
        var slot = _slot();
        var taskbar = _environment.GetTaskbar();
        var taskbarRect = taskbar.Found
            ? new PixelRect(taskbar.Left, taskbar.Top, taskbar.Right, taskbar.Bottom)
            : default;
        var ours = _exclude();
        var foreground = Root(NativeMethods.GetForegroundWindow());
        if (foreground != nint.Zero && !ours.Contains(foreground) && IsConflictWindow(foreground))
        {
            return true;
        }

        var conflict = false;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (ours.Contains(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            {
                return true;
            }

            var bounds = new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
            var overlapsSlot = slot is { } current && bounds.Intersects(current);
            var overlapsTaskbar = !taskbarRect.IsEmpty && bounds.Intersects(taskbarRect);
            if (!overlapsSlot && !overlapsTaskbar)
            {
                return true;
            }

            if (!IsConflictWindow(hwnd))
            {
                return true;
            }

            conflict = true;
            return false;
        }, nint.Zero);

        return conflict;
    }

    private static bool IsConflictWindow(nint hwnd)
    {
        if (ConflictClasses.Contains(ClassName(hwnd)))
        {
            return true;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        var process = ProcessName(processId);
        return process is not null && ConflictProcesses.Contains(process);
    }

    private void RefreshTrayHandle()
    {
        _trayHwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
    }

    private bool IsTrayRelated(nint hwnd, nint root)
    {
        if (_trayHwnd == nint.Zero)
        {
            RefreshTrayHandle();
        }

        return _trayHwnd != nint.Zero && (hwnd == _trayHwnd || root == _trayHwnd);
    }

    private static nint Root(nint hwnd)
    {
        if (hwnd == nint.Zero)
        {
            return nint.Zero;
        }

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        return root != nint.Zero ? root : hwnd;
    }

    private static string ClassName(nint hwnd)
    {
        var buffer = new char[256];
        var length = NativeMethods.GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static string? ProcessName(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == nint.Zero)
        {
            return null;
        }

        try
        {
            var size = 260;
            var name = new StringBuilder(size);
            return NativeMethods.QueryFullProcessImageName(handle, 0, name, ref size)
                ? System.IO.Path.GetFileNameWithoutExtension(name.ToString())
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _source?.Dispatcher.BeginInvoke(() =>
        {
            _logger.Info("Display settings changed.");
            ScheduleOccupancy();
            RefreshFullscreen();
        });
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
        {
            return;
        }

        _source?.Dispatcher.BeginInvoke(() =>
        {
            _logger.Info("Power resume.");
            PowerResumed?.Invoke(this, EventArgs.Empty);
            ScheduleOccupancy();
            RefreshFullscreen();
        });
    }
}
