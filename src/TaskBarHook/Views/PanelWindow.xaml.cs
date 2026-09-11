using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TaskBarHook.Desktop;
using TaskBarHook.Presentation;

namespace TaskBarHook.Views;

public partial class PanelWindow : Window
{
    internal Storyboard? _trackStoryboard;
    internal Storyboard? _transitionStoryboard;
    private readonly PanelTransitionGate _transition = new();
    private string? _lastTrackKey;
    private int _layoutGeneration;
    private bool _seekInteracting;
    private bool _keyboardSeeking;
    private bool _suppressSlider;
    private bool _completingSeek;
    private bool _thumbDragStarted;
    private double _grabOffset;

    public const double PanelWidth = 320;
    public const double PanelHeight = 192;

    public PanelWindow(CapsuleViewModel viewModel)
    {
        DataContext = viewModel;
        ViewModel = viewModel;
        InitializeComponent();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Deactivated += OnDeactivated;
        PreviewMouseLeftButtonDown += Panel_OnPreviewMouseLeftButtonDown;
        PreviewMouseLeftButtonUp += SeekSlider_OnPreviewMouseLeftButtonUp;
        PreviewMouseMove += Panel_OnPreviewMouseMove;
        WireSeekSlider();
        ApplyButtonStyles();
        SyncSliderFromViewModel();
        UpdateThumbPresence();
    }

    public CapsuleViewModel ViewModel { get; }

    public nint Handle { get; private set; }

    public Func<bool>? ShouldStayOpen { get; set; }

    public void Attach()
    {
        Handle = WindowChromeHelper.Apply(this, allowActivation: true);
    }

    public const double EnterRiseDip = 8;
    public const double ExitDipDip = 6;
    public const int EnterMillis = 180;
    public const int ExitMillis = 130;

    public void ShowForKeyboard() => ShowCore(activate: true);

    internal void ShowCore(bool activate)
    {
        var generation = ++_layoutGeneration;
        ShowActivated = activate;
        var animate = AreAnimationsEnabled();
        var transition = _transition.Begin(open: true);
        if (!IsVisible)
        {
            // Fresh show: start below at zero opacity so the panel rises
            // into place. The rise never exceeds the 8 DIP gap, so the
            // bottom edge touches the taskbar top at most, never crossing it.
            EnterTranslate.Y = animate ? EnterRiseDip : 0;
            Opacity = animate ? 0 : 1;
            Show();
        }

        if (activate)
        {
            Activate();
            Focus();
            MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }

        _ = generation;
        if (!animate)
        {
            EnterTranslate.Y = 0;
            Opacity = 1;
            return;
        }

        PlayEnterTransition();
    }

    public void HidePanel()
    {
        _layoutGeneration++;
        CancelSeekInteraction();
        var animate = AreAnimationsEnabled() && IsVisible;
        var transition = _transition.Begin(open: false);
        if (!animate)
        {
            Hide();
            return;
        }

        PlayExitTransition(transition);
    }

    public void HideImmediate()
    {
        // Fullscreen preempts decoration: no exit fade fighting the video, no
        // hint popup, no mouse capture left behind.
        _layoutGeneration++;
        _transition.Begin(open: false);
        _transitionStoryboard?.Stop();
        CancelSeekInteraction();
        EnterTranslate.Y = 0;
        Opacity = 1;
        Hide();
    }

    private void PlayEnterTransition()
    {
        _transitionStoryboard?.Stop();
        if (Math.Abs(EnterTranslate.Y) < 0.01 && Math.Abs(Opacity - 1) < 0.01)
        {
            return;
        }

        var storyboard = new Storyboard();
        var rise = new DoubleAnimation(EnterTranslate.Y, 0, TimeSpan.FromMilliseconds(EnterMillis))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(rise, EnterTranslate);
        Storyboard.SetTarget(rise, RootBorder);
        Storyboard.SetTargetProperty(rise, new PropertyPath("RenderTransform.Y"));
        var fade = new DoubleAnimation(Opacity, 1, TimeSpan.FromMilliseconds(EnterMillis));
        Storyboard.SetTarget(fade, this);
        Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
        storyboard.Children.Add(rise);
        storyboard.Children.Add(fade);
        _transitionStoryboard = storyboard;
        storyboard.Begin();
    }

    private void PlayExitTransition(int generation)
    {
        _transitionStoryboard?.Stop();
        if (Math.Abs(Opacity) < 0.01)
        {
            if (_transition.ShouldFinish(generation, open: false))
            {
                Hide();
            }

            return;
        }

        var storyboard = new Storyboard();
        var dip = new DoubleAnimation(EnterTranslate.Y, ExitDipDip, TimeSpan.FromMilliseconds(ExitMillis))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(dip, EnterTranslate);
        // Target the element with a composite path: a storyboard child aimed
        // directly at the Freezable transform silently never attaches its
        // clock (verified at runtime), while element targets attach normally.
        Storyboard.SetTarget(dip, RootBorder);
        Storyboard.SetTargetProperty(dip, new PropertyPath("RenderTransform.Y"));
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(ExitMillis));
        Storyboard.SetTarget(fade, this);
        Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
        storyboard.Children.Add(dip);
        storyboard.Children.Add(fade);
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (ViewModel.IsSeekPreviewing)
            {
                CancelSeekInteraction();
                e.Handled = true;
                return;
            }

            if (ViewModel.Queue.CloseFromEscape())
            {
                e.Handled = true;
                return;
            }

            ViewModel.Collapse(restoreForeground: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space && IsSeekSliderSource(e.OriginalSource))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space && ViewModel.PlayPauseCommand.CanExecute(null))
        {
            ViewModel.PlayPauseCommand.Execute(null);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void WireSeekSlider()
    {
        SeekSlider.ApplyTemplate();
        SeekSlider.ValueChanged += SeekSlider_OnValueChanged;
        SeekSlider.MouseEnter += (_, _) => AnimateThumb(active: true);
        SeekSlider.MouseLeave += (_, _) =>
        {
            if (!_seekInteracting && !_keyboardSeeking)
            {
                AnimateThumb(active: SeekSlider.IsKeyboardFocused);
            }
        };
        SeekSlider.GotKeyboardFocus += (_, _) => AnimateThumb(active: true);
        SeekSlider.LostKeyboardFocus += (_, _) =>
        {
            if (!_seekInteracting && !_keyboardSeeking)
            {
                AnimateThumb(active: SeekSlider.IsMouseOver);
            }
        };
        if (SeekSlider.Template.FindName("PART_Track", SeekSlider) is Track track && track.Thumb is { } thumb)
        {
            thumb.DragStarted += SeekThumb_OnDragStarted;
            thumb.DragCompleted += SeekThumb_OnDragCompleted;
        }
    }

    private void Panel_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.Queue.IsOpen && !IsWithinQueueTrigger(e.OriginalSource as DependencyObject))
        {
            ViewModel.Queue.NotifyPanelPointerDown();
        }

        // Single owner during pointer gestures: us. Handling the event stops
        // the native Slider/Track/Thumb before it can jump, capture or drag,
        // so the pointer delta is applied exactly once, by our mapping below.
        // (Native-only was tried first: the Slider jumps to a track click but
        // does not keep dragging, which regressed press-and-drag to click-only.)
        if (TryBeginPointerSeekAt(e.GetPosition(SeekSlider)))
        {
            e.Handled = true;
        }
    }

    internal bool TryBeginPointerSeekAt(System.Windows.Point sliderPosition)
    {
        if (!ViewModel.CanSeek || SeekSlider.Visibility != Visibility.Visible || SeekSlider.ActualWidth <= 0)
        {
            return false;
        }

        if (sliderPosition.X < 0 || sliderPosition.X > SeekSlider.ActualWidth ||
            sliderPosition.Y < -2 || sliderPosition.Y > SeekSlider.ActualHeight + 2)
        {
            return false;
        }

        if (_seekInteracting)
        {
            return true;
        }

        _seekInteracting = true;
        _thumbDragStarted = false;
        _grabOffset = GrabOffsetAt(sliderPosition.X);
        ViewModel.BeginSeekPreview(FractionFromTrackX(sliderPosition.X + _grabOffset));
        if (PresentationSource.FromVisual(SeekSlider) is not null)
        {
            SeekSlider.Focus();
            if (!SeekSlider.IsMouseCaptured)
            {
                SeekSlider.CaptureMouse();
            }
        }

        SyncSliderFromViewModel();
        UpdateSeekHint();
        AnimateThumb(active: true);
        return true;
    }

    private void Panel_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_seekInteracting || e.LeftButton != MouseButtonState.Pressed || _completingSeek)
        {
            return;
        }

        UpdatePointerPreviewAt(e.GetPosition(SeekSlider));
    }

    internal void SeekSlider_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_seekInteracting || _completingSeek)
        {
            return;
        }

        if (_thumbDragStarted)
        {
            // A native thumb drag slipped past us (safety net only): its
            // DragCompleted owns completion.
            return;
        }

        if (!ViewModel.IsSeekPreviewing)
        {
            // The preview died mid-gesture (track/session change, support
            // withdrawn, panel dismissed): clean up without sending a command.
            ReleaseSeekCapture();
            _seekInteracting = false;
            SyncSliderFromViewModel();
            UpdateSeekHint();
            AnimateThumb(active: SeekSlider.IsMouseOver || SeekSlider.IsKeyboardFocused);
            return;
        }

        // One last sample at the release point: it can be ahead of the most
        // recent move event on fast flicks.
        CompletePointerSeekAt(e.GetPosition(SeekSlider));
    }

    private void SeekThumb_OnDragStarted(object sender, DragStartedEventArgs e)
    {
        _thumbDragStarted = true;
        _seekInteracting = true;
        if (!ViewModel.IsSeekPreviewing)
        {
            ViewModel.BeginSeekPreview(SeekSlider.Value);
        }
        else
        {
            ViewModel.UpdateSeekPreview(SeekSlider.Value);
        }

        UpdateSeekHint();
        AnimateThumb(active: true);
    }

    private void SeekThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _completingSeek = true;
        try
        {
            if (e.Canceled)
            {
                CancelSeekInteraction();
                return;
            }

            if (_seekInteracting)
            {
                CompletePointerSeekAt(null);
            }
        }
        finally
        {
            _thumbDragStarted = false;
            _completingSeek = false;
            UpdateSeekHint();
            AnimateThumb(active: SeekSlider.IsMouseOver || SeekSlider.IsKeyboardFocused);
        }
    }

    private void SeekSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSlider || !_seekInteracting)
        {
            return;
        }

        // Safety net only: while our manual gesture runs, Value is written by
        // us (suppressed). An unsuppressed change here means native input
        // slipped past the handled press; follow it rather than fight it.
        ViewModel.UpdateSeekPreview(SeekSlider.Value);
        UpdateSeekHint();
    }

    internal void SeekSlider_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            e.Handled = true;
            _keyboardSeeking = true;
            if (e.Key == Key.Home)
            {
                ViewModel.BeginSeekPreview(0);
            }
            else if (e.Key == Key.End)
            {
                ViewModel.BeginSeekPreview(1);
            }
            else
            {
                ViewModel.NudgeSeek(e.Key == Key.Left ? -1 : 1);
            }

            SyncSliderFromViewModel();
            UpdateSeekHint();
            AnimateThumb(active: true);
        }
    }

    internal void SeekSlider_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_keyboardSeeking || e.Key is not (Key.Left or Key.Right or Key.Home or Key.End))
        {
            return;
        }

        _keyboardSeeking = false;
        ViewModel.EndSeekKeyboard();
        UpdateSeekHint();
        AnimateThumb(active: SeekSlider.IsKeyboardFocused || SeekSlider.IsMouseOver);
    }

    private void SeekSlider_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_completingSeek || !_seekInteracting)
        {
            return;
        }

        var captured = Mouse.Captured as DependencyObject;
        if (captured is not null && IsWithinSeekSlider(captured))
        {
            return;
        }

        CancelSeekInteraction();
    }

    internal void CompletePointerSeekAt(System.Windows.Point? sliderPosition)
    {
        _completingSeek = true;
        try
        {
            if (sliderPosition.HasValue)
            {
                UpdatePointerPreviewAt(sliderPosition.Value);
            }

            ViewModel.CommitSeek();
        }
        finally
        {
            ReleaseSeekCapture();
            _seekInteracting = false;
            _thumbDragStarted = false;
            _grabOffset = 0;
            _completingSeek = false;
        }

        UpdateSeekHint();
        AnimateThumb(active: SeekSlider.IsMouseOver || SeekSlider.IsKeyboardFocused);
    }

    internal void CancelSeekInteraction()
    {
        if (TryCancelNativeThumbDrag())
        {
            // Thumb.CancelDrag raises DragCompleted(Canceled), which finishes
            // the cancellation through the guarded path below.
            return;
        }

        FinishCancelSeek();
    }

    private bool TryCancelNativeThumbDrag()
    {
        if (_completingSeek || !_thumbDragStarted)
        {
            return false;
        }

        if (SeekSlider.Template?.FindName("PART_Track", SeekSlider) is Track track &&
            track.Thumb is { } thumb && thumb.IsDragging)
        {
            thumb.CancelDrag();
            return true;
        }

        return false;
    }

    private void FinishCancelSeek()
    {
        // Flags first: releasing capture raises LostMouseCapture synchronously,
        // which must observe the finished state instead of recursing.
        _seekInteracting = false;
        _keyboardSeeking = false;
        _thumbDragStarted = false;
        _grabOffset = 0;
        ReleaseSeekCapture();
        ViewModel.CancelSeek();
        SyncSliderFromViewModel();
        UpdateSeekHint();
        AnimateThumb(active: false);
    }

    internal void UpdatePointerPreviewAt(System.Windows.Point sliderPosition)
    {
        if (!ViewModel.CanSeek || SeekSlider.ActualWidth <= 0)
        {
            return;
        }

        ViewModel.UpdateSeekPreview(FractionFromTrackX(sliderPosition.X + _grabOffset));
        SyncSliderFromViewModel();
        UpdateSeekHint();
    }

    private double GrabOffsetAt(double sliderX)
    {
        // Grabbing the thumb keeps the finger offset (no snap); pressing the
        // bare track jumps the thumb center to the pointer (offset zero).
        if (SeekSlider.Template?.FindName("PART_Track", SeekSlider) is Track track &&
            track.Thumb is { } thumb && thumb.ActualWidth > 0)
        {
            var center = ThumbCenterX();
            if (Math.Abs(sliderX - center) <= thumb.ActualWidth / 2 + 2)
            {
                return center - sliderX;
            }
        }

        return 0;
    }

    private double FractionFromTrackX(double x)
    {
        if (SeekSlider.Template?.FindName("PART_Track", SeekSlider) is Track track &&
            track.Thumb is { } thumb && track.ActualWidth > 0)
        {
            double trackOffset = 0;
            try
            {
                trackOffset = track.TransformToAncestor(SeekSlider).Transform(new System.Windows.Point(0, 0)).X;
            }
            catch (InvalidOperationException)
            {
                trackOffset = 0;
            }

            return FractionFromTrack(x - trackOffset, track.ActualWidth, thumb.ActualWidth);
        }

        return Math.Clamp(x / Math.Max(1, SeekSlider.ActualWidth), 0, 1);
    }

    // Inverse of the native thumb layout: the thumb center travels the usable
    // track (full width minus thumb width), starting at half a thumb.
    internal static double FractionFromTrack(double x, double trackWidth, double thumbWidth)
    {
        var usable = trackWidth - thumbWidth;
        if (usable <= 0)
        {
            return x <= 0 ? 0 : 1;
        }

        return Math.Clamp((x - thumbWidth / 2) / usable, 0, 1);
    }

    private void ReleaseSeekCapture()
    {
        if (SeekSlider.IsMouseCaptured)
        {
            SeekSlider.ReleaseMouseCapture();
        }
    }

    internal void QueueTrigger_OnMouseEnter(object sender, MouseEventArgs e) =>
        ViewModel.Queue.SetTriggerPointer(true);

    internal void QueueTrigger_OnMouseLeave(object sender, MouseEventArgs e) =>
        ViewModel.Queue.SetTriggerPointer(false);

    internal static bool IsWithinQueueTrigger(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is Button button && button.Name == "QueueTrigger")
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private static bool IsWithinSeekSlider(DependencyObject current)
    {
        while (current is not null)
        {
            if (current is Slider slider && slider.Name == "SeekSlider")
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!ViewModel.IsExpanded)
            {
                return;
            }

            if (ShouldStayOpen?.Invoke() == true)
            {
                return;
            }

            ViewModel.Collapse(restoreForeground: false);
        }, DispatcherPriority.Input);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CapsuleViewModel.TrackKey))
        {
            var key = ViewModel.TrackKey;
            var first = string.IsNullOrEmpty(_lastTrackKey);
            _lastTrackKey = key;
            if (!first && !string.IsNullOrEmpty(key) && AreAnimationsEnabled())
            {
                AnimateTrackChange();
            }
        }

        if (e.PropertyName is nameof(CapsuleViewModel.Progress)
            or nameof(CapsuleViewModel.CanSeek)
            or nameof(CapsuleViewModel.IsSeekPreviewing)
            or nameof(CapsuleViewModel.SeekHintText)
            or nameof(CapsuleViewModel.ShowSeekThumb))
        {
            if (!_seekInteracting && !_keyboardSeeking)
            {
                SyncSliderFromViewModel();
            }

            UpdateSeekHint();
            UpdateThumbPresence();
        }
    }

    private void SyncSliderFromViewModel()
    {
        _suppressSlider = true;
        try
        {
            SeekSlider.Value = ViewModel.Progress;
            // Track only repositions the thumb on arrange; a Value change
            // alone does not invalidate it.
            SeekSlider.InvalidateArrange();
        }
        finally
        {
            _suppressSlider = false;
        }
    }

    private void UpdateSeekHint()
    {
        SeekHintPopup.IsOpen = ViewModel.IsSeekPreviewing && !string.IsNullOrEmpty(ViewModel.SeekHintText);
        if (!SeekHintPopup.IsOpen || SeekSlider.ActualWidth <= 0)
        {
            return;
        }

        SeekHintPopup.HorizontalOffset = ThumbCenterX() - 18;
        SeekHintPopup.VerticalOffset = -4;
    }

    private double ThumbCenterX()
    {
        var fraction = Math.Clamp(ViewModel.Progress, 0, 1);
        if (SeekSlider.Template?.FindName("PART_Track", SeekSlider) is Track track &&
            track.Thumb is { } thumb &&
            track.ActualWidth > 0 && thumb.ActualWidth >= 0)
        {
            var usable = Math.Max(0, track.ActualWidth - thumb.ActualWidth);
            var trackOffset = 0.0;
            try
            {
                trackOffset = track.TransformToAncestor(SeekSlider).Transform(new System.Windows.Point(0, 0)).X;
            }
            catch (InvalidOperationException)
            {
                trackOffset = 0;
            }

            return trackOffset + (usable * fraction) + (thumb.ActualWidth / 2);
        }

        return fraction * SeekSlider.ActualWidth;
    }

    internal void AnimateTrackChange()
    {
        // Cover + info only: controls and slider stay put.
        _trackStoryboard?.Stop();
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(230))));
        Storyboard.SetTarget(fade, HeaderGrid);
        Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
        var rise = new DoubleAnimationUsingKeyFrames();
        rise.KeyFrames.Add(new LinearDoubleKeyFrame(3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        rise.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(230))));
        Storyboard.SetTarget(rise, HeaderGrid);
        Storyboard.SetTargetProperty(rise, new PropertyPath("RenderTransform.Y"));
        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Children.Add(rise);
        _trackStoryboard = storyboard;
        storyboard.Begin();
    }

    private void ApplyButtonStyles()
    {
        // The animated style is the XAML default; fall back to the instant one
        // when the system prefers reduced motion.
        if (AreAnimationsEnabled())
        {
            return;
        }

        if (FindResource("IconButtonStyle") is Style instant)
        {
            PrevButton.Style = instant;
            PlayPauseButton.Style = instant;
            NextButton.Style = instant;
        }
    }

    private void UpdateThumbPresence()
    {
        if (SeekSlider.Template?.FindName("SeekThumb", SeekSlider) is not Thumb thumb)
        {
            return;
        }

        thumb.Visibility = ViewModel.ShowSeekThumb ? Visibility.Visible : Visibility.Collapsed;
        thumb.IsHitTestVisible = ViewModel.ShowSeekThumb;
    }

    private void AnimateThumb(bool active)
    {
        if (SeekSlider.Template?.FindName("SeekThumb", SeekSlider) is not Thumb thumb ||
            thumb.Template?.FindName("DotScale", thumb) is not ScaleTransform scale)
        {
            return;
        }

        var target = active ? 1.67 : 1;
        if (!AreAnimationsEnabled())
        {
            scale.ScaleX = target;
            scale.ScaleY = target;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(150);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, duration));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, duration));
    }

    private static bool IsSeekSliderSource(object originalSource) =>
        originalSource is DependencyObject current && IsWithinSeekSlider(current);

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
            // Fall back to WPF.
        }

        return SystemParameters.ClientAreaAnimation;
    }
}
