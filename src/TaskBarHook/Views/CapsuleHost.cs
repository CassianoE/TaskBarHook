using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TaskBarHook.Desktop;
using TaskBarHook.Logging;
using TaskBarHook.Models;
using TaskBarHook.Presentation;
using TaskBarHook.Theming;

namespace TaskBarHook.Views;

public sealed class CapsuleHost : IDisposable
{
    public const double FullWidthDip = 200;
    public const double MiniWidthDip = 72;
    public const double HeightDip = 32;
    public const double PaddingDip = 8;
    public const double InsetDip = 4;
    public const double PanelGapDip = 8;

    private readonly CapsuleViewModel _viewModel;
    private readonly IDesktopEnvironment _environment;
    private readonly IAppLogger _logger;
    private readonly CompactWindow _compact;
    private readonly PanelWindow _panel;
    private readonly DesktopShellMonitor _shell;
    private readonly OccupancyRefreshCoordinator _occupancyRefresh;

    private PixelRect? _slot;
    private nint _previousForeground;
    private ThemeManager? _themes;
    private HwndSource? _compactThemeHook;
    private HwndSource? _panelThemeHook;
    private bool _shellConflict;
    private bool _disposed;
    private string? _lastOccupancyKey;
    private bool _incompleteRetryQueued;
    private int _appliedOccupancyGeneration;
    private bool _lastKnownLayoutSupported = true;

    public CapsuleHost(
        CapsuleViewModel viewModel,
        IDesktopEnvironment environment,
        ITaskbarOccupancySource occupancy,
        IAppLogger logger)
    {
        _viewModel = viewModel;
        _environment = environment;
        _logger = logger;
        _occupancyRefresh = new OccupancyRefreshCoordinator(occupancy, logger: logger);
        _compact = new CompactWindow(viewModel);
        _panel = new PanelWindow(viewModel)
        {
            ShouldStayOpen = ShouldKeepPanelOpen
        };
        _shell = new DesktopShellMonitor(environment, logger);
    }

    public DesktopShellMonitor Shell => _shell;

    public event EventHandler<TaskbarOccupancy>? OccupancyApplied;

    public CompactPlacementResult LastPlacement { get; private set; } =
        CompactPlacementResult.Hide("pending");

    public TaskbarOccupancy LastOccupancy { get; private set; } =
        TaskbarOccupancy.Unavailable("pending");

    public void Start()
    {
        _compact.Attach();
        _panel.Attach();
        HookMouseActivate(_compact);

        if (System.Windows.Application.Current is { } app)
        {
            _themes = new ThemeManager(app.Resources, _logger);
            _themes.EnsureRegistered();
            _themes.Refresh();
        }

        HookTheme(_compact, source => _compactThemeHook = source);
        HookTheme(_panel, source => _panelThemeHook = source);

        _viewModel.VisibilityChanged += (_, _) => ApplyWindowVisibility();
        _viewModel.ActivateRequested += (_, _) => OpenPanel();
        _viewModel.RestoreForegroundRequested += (_, _) => RestoreForeground();
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CapsuleViewModel.IsExpanded))
            {
                ApplyWindowVisibility();
            }
        };

        _occupancyRefresh.OccupancyReady += OnOccupancyReady;
        _shell.PlacementInvalidated += (_, _) => RefreshPlacement();
        _shell.ShellConflictChanged += (_, conflict) =>
        {
            _shellConflict = conflict;
            PushDesktopState();
            ApplyWindowVisibility();
            if (!conflict)
            {
                RefreshPlacement();
            }
        };
        _shell.SurfacesMayNeedRestack += (_, _) =>
        {
            DismissPanelIfNeeded();
            if (_viewModel.ShowCompact && !_shellConflict)
            {
                WindowChromeHelper.RaiseWithoutActivating(_compact);
            }
        };

        _shell.Start(OurHandles, () => _slot);
        RefreshPlacement();
        _shell.RefreshFullscreen();
        ApplyWindowVisibility();
    }

    public void RefreshPlacement()
    {
        _occupancyRefresh.Request();
    }

    private void OnOccupancyReady(object? sender, OccupancyRefreshResult result)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyOccupancy(result);
            return;
        }

        dispatcher.BeginInvoke(() => ApplyOccupancy(result), DispatcherPriority.Normal);
    }

    private void ApplyOccupancy(OccupancyRefreshResult result)
    {
        if (_disposed)
        {
            return;
        }

        if (!OccupancyApplyGate.ShouldApply(result, _occupancyRefresh.Generation, _appliedOccupancyGeneration))
        {
            return;
        }

        _appliedOccupancyGeneration = Math.Max(_appliedOccupancyGeneration, result.Generation);
        var occupancy = result.Occupancy;
        LastOccupancy = occupancy;
        var key = $"{occupancy.Status}:{occupancy.Error ?? "none"}";
        if (key != _lastOccupancyKey)
        {
            _lastOccupancyKey = key;
            if (!occupancy.Success)
            {
                _logger.Warn($"Applying occupancy {occupancy.Status}: {occupancy.Error}.");
            }
        }

        var scale = Math.Max(0.5, occupancy.Scale);
        var taskbar = occupancy.Success
            ? new PixelRect(occupancy.Taskbar.Left, occupancy.Taskbar.Top, occupancy.Taskbar.Right, occupancy.Taskbar.Bottom)
            : default;
        var request = new CompactPlacementRequest(
            taskbar,
            occupancy.Occupied.Select(region => region.Rect).ToList(),
            _slot,
            (int)Math.Round(FullWidthDip * scale),
            (int)Math.Round(MiniWidthDip * scale),
            (int)Math.Round(HeightDip * scale),
            (int)Math.Round(PaddingDip * scale),
            (int)Math.Round(InsetDip * scale),
            occupancy.Success,
            occupancy.LayoutSupported);
        var placement = CompactPlacementPolicy.Choose(request);
        if (placement.Slot != _slot || placement.Fit != LastPlacement.Fit)
        {
            _logger.Info($"Placement {placement.Fit} {placement.Reason} {placement.Slot} preserved={placement.PreservedPrevious}.");
        }

        LastPlacement = placement;
        _slot = placement.Slot;
        if (occupancy.Status != OccupancyReadStatus.Unavailable)
        {
            _lastKnownLayoutSupported = occupancy.LayoutSupported;
        }

        PushDesktopState();
        ApplyWindowVisibility();
        OccupancyApplied?.Invoke(this, occupancy);
        ScheduleIncompleteRetry(occupancy);
    }

    private void ScheduleIncompleteRetry(TaskbarOccupancy occupancy)
    {
        if (occupancy.Success)
        {
            _incompleteRetryQueued = false;
            return;
        }

        if (_incompleteRetryQueued || occupancy.Status != OccupancyReadStatus.Incomplete)
        {
            return;
        }

        _incompleteRetryQueued = true;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        var retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400), IsEnabled = true };
        retry.Tick += (_, _) =>
        {
            retry.Stop();
            if (!_disposed)
            {
                RefreshPlacement();
            }
        };
    }

    private void PushDesktopState()
    {
        _viewModel.UpdateDesktopState(
            LastPlacement.Fit != CompactFit.Hidden,
            LastOccupancy.Success,
            _lastKnownLayoutSupported,
            _shellConflict,
            LastPlacement.Fit);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _compactThemeHook?.RemoveHook(OnThemeMessage);
        _compactThemeHook = null;
        _panelThemeHook?.RemoveHook(OnThemeMessage);
        _panelThemeHook = null;
        _occupancyRefresh.OccupancyReady -= OnOccupancyReady;
        _occupancyRefresh.Dispose();
        _shell.Dispose();
        _panel.Close();
        _compact.Close();
    }

    private void HookTheme(Window window, Action<HwndSource?> store)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook(OnThemeMessage);
        store(source);
    }

    private nint OnThemeMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // Refresh on any setting change: theme flips broadcast
        // "ImmersiveColorSet", but high-contrast toggles use other sections.
        // Refresh is cheap and a no-op when nothing changed.
        if (msg == NativeMethods.WM_SETTINGCHANGE)
        {
            _themes?.Refresh();
        }

        return nint.Zero;
    }

    private void ApplyWindowVisibility()
    {
        if (_viewModel.ShowCompact && _slot is { } slot)
        {
            WindowChromeHelper.Place(_compact, slot, LastOccupancy.Scale);
            _compact.ShowPassive();
        }
        else
        {
            _compact.Hide();
        }

        if (_viewModel.ShowPanel)
        {
            PlacePanel();
            if (!_panel.IsVisible)
            {
                OpenPanel();
            }
        }
        else if (_panel.IsVisible)
        {
            if (_viewModel.HideReason == HideReason.Fullscreen)
            {
                _panel.HideImmediate();
            }
            else
            {
                _panel.HidePanel();
            }
        }
    }

    private void OpenPanel()
    {
        RememberForeground();
        PlacePanel();
        _panel.ShowForKeyboard();
    }

    private void PlacePanel()
    {
        var scale = Math.Max(0.5, LastOccupancy.Scale);
        var monitor = _environment.GetPrimaryMonitor();
        var taskbar = _environment.GetTaskbar();
        var width = PanelWindow.PanelWidth;
        var height = PanelWindow.PanelHeight;
        double left;
        double top;
        if (_viewModel.ShowCompact && _slot is { } slot)
        {
            left = ((slot.Left + slot.Right) / 2.0 / scale) - (width / 2);
            top = (slot.Top / scale) - PanelGapDip - height;
        }
        else
        {
            var notify = LastOccupancy.Occupied
                .Where(region => region.Kind == "Notify")
                .Select(region => region.Rect)
                .OrderBy(rect => rect.Left)
                .FirstOrDefault();
            var anchor = notify.IsEmpty
                ? (taskbar.Found ? taskbar.Right : monitor.X + monitor.Width)
                : notify.Left;
            left = (anchor / scale) - width;
            top = (taskbar.Found ? taskbar.Top / scale : (monitor.WorkY + monitor.WorkHeight) / scale) - PanelGapDip - height;
        }

        var minLeft = monitor.X / scale;
        var maxLeft = (monitor.X + monitor.Width) / scale - width;
        _panel.Left = Math.Clamp(left, minLeft, Math.Max(minLeft, maxLeft));
        _panel.Top = Math.Max(monitor.Y / scale, top);
        _panel.Width = width;
        _panel.Height = height;
    }

    private IReadOnlyList<nint> OurHandles()
    {
        return [_compact.Handle, _panel.Handle];
    }

    private void DismissPanelIfNeeded()
    {
        if (!_viewModel.IsExpanded || ShouldKeepPanelOpen())
        {
            return;
        }

        _viewModel.Collapse(restoreForeground: false);
    }

    private bool ShouldKeepPanelOpen()
    {
        return PanelDismissPolicy.Evaluate(CurrentDismissInput()) == PanelDismissAction.Keep;
    }

    private PanelDismissInput CurrentDismissInput()
    {
        return new PanelDismissInput(
            IsFocusOnOurSurfaces(),
            IsPointerOverOurSurfaces(),
            IsPrimaryButtonDown());
    }

    private static bool IsPrimaryButtonDown()
    {
        return (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
    }

    private bool IsFocusOnOurSurfaces()
    {
        return IsOurRoot(NativeMethods.GetForegroundWindow());
    }

    private bool IsPointerOverOurSurfaces()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            return false;
        }

        return IsOurRoot(NativeMethods.WindowFromPoint(point));
    }

    private bool IsOurRoot(nint hwnd)
    {
        if (hwnd == nint.Zero)
        {
            return false;
        }

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == nint.Zero)
        {
            root = hwnd;
        }

        return root == _compact.Handle || root == _panel.Handle;
    }

    private void RememberForeground()
    {
        var current = NativeMethods.GetForegroundWindow();
        if (current != nint.Zero && current != _compact.Handle && current != _panel.Handle)
        {
            _previousForeground = current;
        }
    }

    private void RestoreForeground()
    {
        if (_previousForeground != nint.Zero && NativeMethods.IsWindow(_previousForeground))
        {
            NativeMethods.SetForegroundWindow(_previousForeground);
        }
    }

    private static void HookMouseActivate(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook((_, msg, _, _, ref handled) =>
        {
            if (msg == NativeMethods.WM_MOUSEACTIVATE)
            {
                handled = true;
                return NativeMethods.MA_NOACTIVATE;
            }

            return nint.Zero;
        });
    }
}
