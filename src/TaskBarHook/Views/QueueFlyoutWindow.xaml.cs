using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using TaskBarHook.Desktop;
using TaskBarHook.Presentation;

namespace TaskBarHook.Views;

public partial class QueueFlyoutWindow : Window
{
    public const int EnterMillis = 180;
    public const int ExitMillis = 150;
    public const double OffsetDip = 6;

    private readonly PanelTransitionGate _transition = new();
    private Storyboard? _transitionStoryboard;
    private QueueFlyoutEdge _edge = QueueFlyoutEdge.Right;

    public QueueFlyoutWindow(CapsuleViewModel viewModel)
    {
        DataContext = viewModel;
        ViewModel = viewModel;
        InitializeComponent();
        MouseEnter += (_, _) => ViewModel.Queue.SetFlyoutPointer(true);
        MouseLeave += (_, _) => ViewModel.Queue.SetFlyoutPointer(false);
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public CapsuleViewModel ViewModel { get; }

    public nint Handle { get; private set; }

    public void Attach()
    {
        Handle = WindowChromeHelper.Apply(this, allowActivation: true);
    }

    public void Place(QueueFlyoutRect placement)
    {
        _edge = placement.Edge;
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;
    }

    public void ShowFlyout(bool activate)
    {
        ShowActivated = activate;
        var animate = AreAnimationsEnabled();
        var transition = _transition.Begin(open: true);
        if (!IsVisible)
        {
            ApplyEnterOffset(animate);
            Opacity = animate ? 0 : 1;
            Show();
        }

        if (activate)
        {
            Activate();
            Focus();
            QueueScroll.Focus();
        }
        else
        {
            WindowChromeHelper.RaiseWithoutActivating(this);
        }

        if (!animate)
        {
            EnterTranslate.X = 0;
            EnterTranslate.Y = 0;
            Opacity = 1;
            return;
        }

        PlayEnter();
        _ = transition;
    }

    public void HideFlyout()
    {
        DismissTooltips();
        var animate = AreAnimationsEnabled() && IsVisible;
        var transition = _transition.Begin(open: false);
        if (!animate)
        {
            Hide();
            return;
        }

        PlayExit(transition);
    }

    public void HideImmediate()
    {
        DismissTooltips();
        _transition.Begin(open: false);
        _transitionStoryboard?.Stop();
        EnterTranslate.X = 0;
        EnterTranslate.Y = 0;
        Opacity = 1;
        Hide();
    }

    internal void QueueScroll_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        QueueScroll.ScrollToVerticalOffset(QueueScroll.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ViewModel.Queue.CloseFromEscape();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Down or Key.Up or Key.PageDown or Key.PageUp or Key.Home or Key.End)
        {
            var delta = e.Key switch
            {
                Key.Down => QueueFlyoutPlacement.RowHeightDip,
                Key.Up => -QueueFlyoutPlacement.RowHeightDip,
                Key.PageDown => QueueFlyoutPlacement.RowHeightDip * 4,
                Key.PageUp => -QueueFlyoutPlacement.RowHeightDip * 4,
                Key.Home => -QueueScroll.VerticalOffset,
                _ => QueueScroll.ScrollableHeight
            };
            QueueScroll.ScrollToVerticalOffset(QueueScroll.VerticalOffset + delta);
            e.Handled = true;
        }
    }

    private void PlayEnter()
    {
        _transitionStoryboard?.Stop();
        var storyboard = new Storyboard();
        var fade = new DoubleAnimation(Opacity, 1, TimeSpan.FromMilliseconds(EnterMillis))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, this);
        Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
        var offset = OffsetAnimation(0, EnterMillis, EasingMode.EaseOut);
        storyboard.Children.Add(fade);
        storyboard.Children.Add(offset);
        _transitionStoryboard = storyboard;
        storyboard.Begin();
    }

    private void PlayExit(int generation)
    {
        _transitionStoryboard?.Stop();
        var storyboard = new Storyboard();
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(ExitMillis))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(fade, this);
        Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
        var target = _edge == QueueFlyoutEdge.Above ? OffsetDip : (_edge == QueueFlyoutEdge.Left ? -OffsetDip : OffsetDip);
        var offset = OffsetAnimation(target, ExitMillis, EasingMode.EaseIn);
        storyboard.Children.Add(fade);
        storyboard.Children.Add(offset);
        storyboard.Completed += (_, _) =>
        {
            if (_transition.ShouldFinish(generation, open: false))
            {
                Hide();
            }
        };
        _transitionStoryboard = storyboard;
        storyboard.Begin();
    }

    private DoubleAnimation OffsetAnimation(double to, int millis, EasingMode ease)
    {
        var path = _edge == QueueFlyoutEdge.Above ? "RenderTransform.Y" : "RenderTransform.X";
        var from = _edge == QueueFlyoutEdge.Above ? EnterTranslate.Y : EnterTranslate.X;
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(millis))
        {
            EasingFunction = new CubicEase { EasingMode = ease }
        };
        Storyboard.SetTarget(animation, RootBorder);
        Storyboard.SetTargetProperty(animation, new PropertyPath(path));
        return animation;
    }

    private void ApplyEnterOffset(bool animate)
    {
        EnterTranslate.X = 0;
        EnterTranslate.Y = 0;
        if (!animate)
        {
            return;
        }

        if (_edge == QueueFlyoutEdge.Above)
        {
            EnterTranslate.Y = OffsetDip;
        }
        else if (_edge == QueueFlyoutEdge.Left)
        {
            EnterTranslate.X = -OffsetDip;
        }
        else
        {
            EnterTranslate.X = OffsetDip;
        }
    }

    private void DismissTooltips()
    {
        ToolTipService.SetIsEnabled(this, false);
        ToolTipService.SetIsEnabled(this, true);
    }

    private static bool AreAnimationsEnabled()
    {
        try
        {
            if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
            {
                return false;
            }
        }
        catch
        {
        }

        return SystemParameters.ClientAreaAnimation;
    }
}
