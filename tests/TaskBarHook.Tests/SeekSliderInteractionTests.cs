using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TaskBarHook.Logging;
using TaskBarHook.Media;
using TaskBarHook.Models;
using TaskBarHook.Presentation;
using TaskBarHook.Theming;
using TaskBarHook.Views;

namespace TaskBarHook.Tests;

// Exercises the real Slider/Thumb routed events and the PanelWindow wiring.
// The pointer gesture has a single manual owner: the press is handled before
// the native Slider sees it, every move maps through the usable track, and
// release commits. SeekCoordinator-only tests cannot catch double-owner jumps
// or track-mapping mistakes.
public sealed class SeekSliderInteractionTests
{
    private static readonly TimeSpan TrackLength = TimeSpan.FromMinutes(12);

    [Fact]
    public void Fast_backward_drag_tracks_without_visiting_zero()
    {
        var (window, vm, media) = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            Assert.True(BeginAt(window, 0.7));
            Assert.Equal(0.7, vm.Progress, 4);

            // Fast pointer path towards 0.25: every sample must follow the
            // pointer exactly, never excurs to 0.
            foreach (var value in new[] { 0.6, 0.45, 0.3, 0.25 })
            {
                MoveAt(window, value);
                Assert.Equal(value, vm.Progress, 4);
            }

            Assert.Empty(media.SeekTicks);
            CompleteAt(window, 0.25);

            Assert.Single(media.SeekTicks);
            Assert.InRange(
                TimeSpan.FromTicks(media.SeekTicks[0]),
                TimeSpan.FromSeconds(179),
                TimeSpan.FromSeconds(181));
            Assert.Equal(MediaPlaybackStatus.Paused, media.Current.Status);
        });
    }

    [Fact]
    public void Fast_forward_drag_tracks_without_visiting_the_end()
    {
        var (window, vm, media) = CreateSeekablePanel(TimeSpan.FromMinutes(2.4));
        WpfStaRunner.Run(() =>
        {
            Assert.True(BeginAt(window, 0.2));
            foreach (var value in new[] { 0.35, 0.55, 0.75 })
            {
                MoveAt(window, value);
                Assert.Equal(value, vm.Progress, 4);
            }

            CompleteAt(window, 0.75);

            Assert.Single(media.SeekTicks);
            Assert.InRange(
                TimeSpan.FromTicks(media.SeekTicks[0]),
                TimeSpan.FromSeconds(539),
                TimeSpan.FromSeconds(541));
        });
    }

    [Fact]
    public void Reversal_without_release_commits_only_the_final_point()
    {
        var (window, vm, media) = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            Assert.True(BeginAt(window, 0.5));
            foreach (var value in new[] { 0.8, 0.3, 0.6 })
            {
                MoveAt(window, value);
                Assert.Equal(value, vm.Progress, 4);
            }

            CompleteAt(window, 0.6);

            Assert.Single(media.SeekTicks);
            Assert.InRange(
                TimeSpan.FromTicks(media.SeekTicks[0]),
                TimeSpan.FromSeconds(431),
                TimeSpan.FromSeconds(433));
        });
    }

    [Fact]
    public void Distant_track_click_jumps_to_the_pointer()
    {
        var (window, vm, media) = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            Assert.True(BeginAt(window, 0.9));
            Assert.Equal(0.9, vm.Progress, 4);

            CompleteAt(window, 0.9);
            Assert.Single(media.SeekTicks);
        });
    }

    [Fact]
    public void Thumb_grab_keeps_its_offset_without_snapping()
    {
        var (window, vm, _) = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            var slider = window.SeekSlider;
            var track = (Track)slider.Template.FindName("PART_Track", slider)!;
            var thumb = (Thumb)track.Thumb!;
            var center = PointFor(window, 0.5);

            // Press 2 DIP off the thumb center: the preview must stay exactly
            // at the live position instead of snapping to the pointer.
            Assert.True(window.TryBeginPointerSeekAt(new Point(center.X + 2, center.Y)));
            Assert.Equal(0.5, vm.Progress, 5);

            // Moving keeps the 2 DIP offset.
            window.UpdatePointerPreviewAt(new Point(center.X + 22, center.Y));
            var expected = PanelWindow.FractionFromTrack(
                center.X + 22 - 2, track.ActualWidth, thumb.ActualWidth);
            Assert.Equal(expected, vm.Progress, 5);
        });
    }

    [Fact]
    public void Moves_and_release_outside_clamp_to_the_limits()
    {
        var start = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            Assert.True(BeginAt(start.Window, 0.5));
            // Way past the left end: clamps to zero, single command.
            start.Window.UpdatePointerPreviewAt(new Point(-40, 11));
            Assert.Equal(0, start.Vm.Progress, 5);
            start.Window.CompletePointerSeekAt(new Point(-40, 11));
            Assert.Single(start.Media.SeekTicks);
            Assert.Equal(TimeSpan.Zero, TimeSpan.FromTicks(start.Media.SeekTicks[0]));
        });

        var end = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            var slider = end.Window.SeekSlider;
            Assert.True(BeginAt(end.Window, 0.5));
            end.Window.UpdatePointerPreviewAt(new Point(slider.ActualWidth + 60, 11));
            Assert.Equal(1, end.Vm.Progress, 5);
            end.Window.CompletePointerSeekAt(null);
            Assert.Single(end.Media.SeekTicks);
            Assert.InRange(
                TimeSpan.FromTicks(end.Media.SeekTicks[0]),
                TrackLength - TimeSpan.FromSeconds(1),
                TrackLength);
        });
    }

    [Fact]
    public void Real_pointer_move_event_keeps_a_valid_preview()
    {
        var (window, vm, _) = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            Assert.True(BeginAt(window, 0.5));
            window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
            {
                RoutedEvent = UIElement.PreviewMouseMoveEvent
            });

            // The synthetic event carries the live cursor position, so only
            // validity (clamped, still previewing) is asserted here. Exact
            // tracking is pinned by the point-based tests above.
            Assert.True(vm.IsSeekPreviewing);
            Assert.InRange(vm.Progress, 0, 1);
        });
    }

    [Fact]
    public void Real_release_event_sends_a_single_valid_command()
    {
        var (window, _, media) = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            Assert.True(BeginAt(window, 0.5));
            window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent
            });

            Assert.Single(media.SeekTicks);
            Assert.InRange(
                TimeSpan.FromTicks(media.SeekTicks[0]),
                TimeSpan.Zero,
                TrackLength);
        });
    }

    [Fact]
    public void Escape_during_preview_cancels_without_a_command()
    {
        var (window, vm, media) = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            var slider = window.SeekSlider;
            Assert.True(BeginAt(window, 0.5));
            MoveAt(window, 0.3);
            Assert.True(vm.IsSeekPreviewing);

            RaiseKey(slider, Key.Escape, UIElement.KeyDownEvent);

            Assert.False(vm.IsSeekPreviewing);
            Assert.Empty(media.SeekTicks);
            Assert.Equal(0.5, vm.Progress, 3);
            Assert.Equal(0.5, slider.Value, 3);
        });
    }

    [Fact]
    public void Thumb_drag_completed_commits_and_canceled_aborts()
    {
        var committed = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            var thumb = FindThumb(committed.Window);
            Assert.True(BeginAt(committed.Window, 0.5));
            thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            MoveAt(committed.Window, 0.7);
            thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });

            Assert.Single(committed.Media.SeekTicks);
            Assert.False(committed.Vm.IsSeekPreviewing);
        });

        var aborted = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            var thumb = FindThumb(aborted.Window);
            Assert.True(BeginAt(aborted.Window, 0.5));
            thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            MoveAt(aborted.Window, 0.7);
            thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, true) { RoutedEvent = Thumb.DragCompletedEvent });

            Assert.Empty(aborted.Media.SeekTicks);
            Assert.False(aborted.Vm.IsSeekPreviewing);
        });
    }

    [Fact]
    public void Keyboard_repeats_group_into_a_single_command()
    {
        var (window, vm, media) = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            var slider = window.SeekSlider;
            RaiseKey(slider, Key.Right, UIElement.PreviewKeyDownEvent);
            RaiseKey(slider, Key.Right, UIElement.PreviewKeyDownEvent);
            Assert.True(vm.IsSeekPreviewing);
            Assert.Empty(media.SeekTicks);

            // Two 5s nudges from 6:00 -> 6:10.
            Assert.Equal(370.0 / 720.0, vm.Progress, 4);
            Assert.Equal(vm.Progress, slider.Value, 5);

            RaiseKey(slider, Key.Right, UIElement.PreviewKeyUpEvent);
            Assert.Single(media.SeekTicks);
            Assert.InRange(
                TimeSpan.FromTicks(media.SeekTicks[0]),
                TimeSpan.FromSeconds(369),
                TimeSpan.FromSeconds(371));
        });
    }

    [Fact]
    public void Space_on_the_slider_does_not_toggle_playback()
    {
        var (window, _, media) = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            var slider = window.SeekSlider;
            var args = RaiseKey(slider, Key.Space, UIElement.KeyDownEvent);

            Assert.True(args.Handled);
            Assert.Equal(0, media.PlayPauseCount);

            var outside = RaiseKey(window, Key.Space, UIElement.KeyDownEvent);
            Assert.Equal(1, media.PlayPauseCount);
            _ = outside;
        });
    }

    [Fact]
    public void Press_outside_the_slider_or_without_seek_does_nothing()
    {
        var (window, vm, _) = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            Assert.False(window.TryBeginPointerSeekAt(new System.Windows.Point(-5, 11)));
            Assert.False(vm.IsSeekPreviewing);
        });

        var (plain, plainVm, _) = CreatePanel(paused: true, seek: false, TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            Assert.False(plainVm.CanSeek);
            Assert.Equal(Visibility.Collapsed, plain.SeekSlider.Visibility);
            Assert.False(plain.TryBeginPointerSeekAt(new System.Windows.Point(10, 11)));
        });
    }

    [Theory]
    [InlineData(0, 206, 12, 0)]
    [InlineData(206, 206, 12, 1)]
    [InlineData(-30, 206, 12, 0)]
    [InlineData(300, 206, 12, 1)]
    [InlineData(6, 206, 12, 0)]
    [InlineData(200, 206, 12, 1)]
    [InlineData(103, 206, 12, 0.5)]
    public void Track_mapping_accounts_for_the_thumb(double x, double track, double thumb, double expected)
    {
        Assert.Equal(expected, PanelWindow.FractionFromTrack(x, track, thumb), 5);
    }

    [Fact]
    public void Gesture_path_renders_the_thumb_center_at_the_pointer()
    {
        // Full path per sample: pointer point -> preview -> Slider.Value ->
        // arrange -> thumb center back at the pointer. Catches a mapping that
        // disagrees with the native thumb layout, and a thumb that never
        // re-renders from programmatic Value changes.
        var (window, _, _) = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            var slider = window.SeekSlider;
            var track = (Track)slider.Template.FindName("PART_Track", slider)!;
            var thumb = (Thumb)track.Thumb!;
            var content = (FrameworkElement)window.Content;
            Assert.True(BeginAt(window, 0.5));
            foreach (var fraction in new[] { 0.0, 0.25, 0.5, 0.8, 1.0 })
            {
                var point = PointFor(window, fraction);
                window.UpdatePointerPreviewAt(point);
                // Process the real layout queue, like the render pump does.
                content.UpdateLayout();
                var center = thumb.TranslatePoint(
                    new Point(thumb.ActualWidth / 2, 0), slider).X;
                Assert.True(
                    Math.Abs(point.X - center) < 1.0,
                    $"fraction={fraction} slider.Value={slider.Value} track.Value={track.Value} pointerX={point.X} thumbCenter={center}");
            }
        });
    }

    [Fact]
    public void Theme_switch_mid_gesture_keeps_preview_slider_and_state()
    {
        var (window, vm, media) = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            Assert.True(BeginAt(window, 0.5));
            MoveAt(window, 0.7);
            Assert.True(vm.IsSeekPreviewing);

            var manager = new ThemeManager(System.Windows.Application.Current.Resources);
            manager.EnsureRegistered();
            var dark = ThemeResolver.Resolve(new ThemeInputs(
                SystemLight: false, AppsLight: false,
                Accent: System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4), HighContrast: false));
            var light = ThemeResolver.Resolve(new ThemeInputs(
                SystemLight: true, AppsLight: true,
                Accent: System.Windows.Media.Color.FromRgb(0xD8, 0x3B, 0x01), HighContrast: false));
            Assert.True(manager.Apply(dark));
            Assert.True(manager.Apply(light));

            Assert.True(vm.IsSeekPreviewing);
            Assert.Equal(0.7, vm.Progress, 4);
            Assert.Equal(0.7, window.SeekSlider.Value, 4);
            Assert.Empty(media.SeekTicks);
            CompleteAt(window, 0.7);
            Assert.Single(media.SeekTicks);
        });
    }

    [Fact]
    public void HideImmediate_during_gesture_cancels_preview_popup_and_pending_release()
    {
        var (window, vm, media) = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            Assert.True(BeginAt(window, 0.5));
            MoveAt(window, 0.7);
            Assert.True(vm.IsSeekPreviewing);

            window.HideImmediate();

            Assert.False(vm.IsSeekPreviewing);
            Assert.False(window.SeekHintPopup.IsOpen);
            Assert.Equal(0.5, window.SeekSlider.Value, 3);
            window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent
            });
            Assert.Empty(media.SeekTicks);
        });
    }

    [Fact]
    public void HidePanel_during_gesture_cancels_before_the_exit_fade()
    {
        var (window, vm, media) = CreateSeekablePanel(TimeSpan.FromMinutes(6));
        WpfStaRunner.Run(() =>
        {
            Assert.True(BeginAt(window, 0.5));
            MoveAt(window, 0.7);

            window.HidePanel();

            Assert.False(vm.IsSeekPreviewing);
            Assert.False(window.SeekHintPopup.IsOpen);
            Assert.Empty(media.SeekTicks);
        });
    }

    private static (PanelWindow Window, CapsuleViewModel Vm, FakeMedia Media) CreateSeekablePanel(TimeSpan position) =>
        CreatePanel(paused: true, seek: true, position);

    private static (PanelWindow Window, CapsuleViewModel Vm, FakeMedia Media) CreatePanel(
        bool paused, bool seek, TimeSpan position)
    {
        return WpfStaRunner.Run(() =>
        {
            var media = new FakeMedia(paused, seek, position);
            var vm = new CapsuleViewModel(media, new SystemClock(), new TestLogger());
            vm.ApplySnapshot(media.Current);
            var window = new PanelWindow(vm);
            // A Window without an HWND never measures; lay out its content at
            // the exact client size instead (borderless window: client == window).
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(PanelWindow.PanelWidth, PanelWindow.PanelHeight));
            content.Arrange(new Rect(0, 0, PanelWindow.PanelWidth, PanelWindow.PanelHeight));
            content.UpdateLayout();
            return (window, vm, media);
        });
    }

    private static bool BeginAt(PanelWindow window, double fraction) =>
        window.TryBeginPointerSeekAt(PointFor(window, fraction));

    private static void MoveAt(PanelWindow window, double fraction) =>
        window.UpdatePointerPreviewAt(PointFor(window, fraction));

    private static void CompleteAt(PanelWindow window, double fraction) =>
        window.CompletePointerSeekAt(PointFor(window, fraction));

    private static Point PointFor(PanelWindow window, double fraction)
    {
        var slider = window.SeekSlider;
        var track = (Track)slider.Template.FindName("PART_Track", slider)!;
        var thumb = (Thumb)track.Thumb!;
        return new Point(
            thumb.ActualWidth / 2 + fraction * (track.ActualWidth - thumb.ActualWidth),
            slider.ActualHeight / 2);
    }

    private static KeyEventArgs RaiseKey(Visual target, Key key, RoutedEvent routed)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, TestPresentationSource.Instance, 0, key)
        {
            RoutedEvent = routed
        };
        ((UIElement)target).RaiseEvent(args);
        return args;
    }

    private static Thumb FindThumb(PanelWindow window)
    {
        var slider = window.SeekSlider;
        var track = (Track)slider.Template.FindName("PART_Track", slider)!;
        return (Thumb)track.Thumb!;
    }

    // KeyEventArgs requires a non-null source, but our windows are never
    // shown. Routing only walks the visual tree, so a stub suffices.
    private sealed class TestPresentationSource : PresentationSource
    {
        public static readonly TestPresentationSource Instance = new();

        public override Visual RootVisual { get; set; } = new Grid();

        public override bool IsDisposed => false;

        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    private sealed class FakeMedia : IMediaSessionService
    {
        public FakeMedia(bool paused, bool seek, TimeSpan position)
        {
            var duration = TrackLength;
            Current = new MediaSnapshot(
                paused ? MediaPlaybackStatus.Paused : MediaPlaybackStatus.Playing,
                new TrackInfo("Faixa de teste", "Artista de teste", null, null, "test"),
                new TimelineInfo(position, duration, DateTimeOffset.UtcNow, 1, !paused)
                {
                    StartTime = TimeSpan.Zero,
                    EndTime = duration,
                    MinSeekTime = TimeSpan.Zero,
                    MaxSeekTime = duration
                },
                new CommandAvailability(PlayPause: true, Next: true, Previous: true, Seek: seek),
                "test-session",
                "test-app",
                "test-session#1",
                1);
        }

        public MediaSnapshot Current { get; private set; }

        public List<long> SeekTicks { get; } = new();

        public int PlayPauseCount { get; private set; }

        public bool FailSeek { get; set; }

        public event EventHandler<MediaSnapshot>? SnapshotChanged;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> PlayPauseAsync()
        {
            PlayPauseCount++;
            return Task.FromResult(true);
        }

        public Task<bool> SkipNextAsync() => Task.FromResult(false);

        public Task<bool> SkipPreviousAsync() => Task.FromResult(false);

        public Task<bool> SeekAsync(long positionTicks)
        {
            SeekTicks.Add(positionTicks);
            if (!FailSeek)
            {
                var position = TimeSpan.FromTicks(positionTicks);
                Current = Current with
                {
                    Timeline = Current.Timeline with { Position = position, LastUpdatedUtc = DateTimeOffset.UtcNow }
                };
                SnapshotChanged?.Invoke(this, Current);
            }

            return Task.FromResult(!FailSeek);
        }
    }

    private sealed class TestLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
