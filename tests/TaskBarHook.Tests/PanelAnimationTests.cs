using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TaskBarHook.Logging;
using TaskBarHook.Media;
using TaskBarHook.Models;
using TaskBarHook.Presentation;
using TaskBarHook.Views;

namespace TaskBarHook.Tests;

// Real-animation regressions: a real offscreen window (never activated, so no
// focus steal) with real-time dispatcher pumping. Property sampling alone is
// not trusted: every movement assertion also checks the rendered offset, so a
// storyboard that silently never attaches (the 0.4.0 defect) fails here.
[Collection("WPF")]
public sealed class PanelAnimationTests
{
    [Fact]
    public void Enter_moves_and_settles_at_zero()
    {
        WpfStaRunner.Run(() =>
        {
            var window = CreateOffscreenPanel(out _);
            try
            {
                if (!AnimationsOn())
                {
                    AssertRendered(window, 0);
                    return;
                }

                Assert.True(PumpUntil(() => IsStrictlyBetween(window.EnterTranslate.Y, 0, 8)),
                    "Enter displacement never moved mid-flight.");
                var midY = window.EnterTranslate.Y;
                Assert.InRange(window.Opacity, 0.01, 0.99);
                AssertRendered(window, midY);

                Pump(TimeSpan.FromMilliseconds(400));
                Assert.Equal(0, window.EnterTranslate.Y, 2);
                Assert.Equal(1, window.Opacity, 2);
                AssertRendered(window, 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Exit_runs_from_visible_and_hides()
    {
        WpfStaRunner.Run(() =>
        {
            var window = CreateOffscreenPanel(out _);
            try
            {
                Pump(TimeSpan.FromMilliseconds(400));
                window.HidePanel();
                if (!AnimationsOn())
                {
                    Assert.False(window.IsVisible);
                    return;
                }

                Assert.True(window.IsVisible);
                Assert.True(PumpUntil(() => IsStrictlyBetween(window.EnterTranslate.Y, 0, 6)),
                    "Exit displacement never moved mid-flight.");
                var midY = window.EnterTranslate.Y;
                Assert.InRange(window.Opacity, 0.01, 0.99);
                AssertRendered(window, midY);

                Pump(TimeSpan.FromMilliseconds(400));
                Assert.False(window.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Reverse_mid_enter_continues_without_jumping_then_hides()
    {
        WpfStaRunner.Run(() =>
        {
            var window = CreateOffscreenPanel(out _);
            try
            {
                if (!AnimationsOn())
                {
                    window.HidePanel();
                    Assert.False(window.IsVisible);
                    return;
                }

                Assert.True(PumpUntil(() => IsStrictlyBetween(window.EnterTranslate.Y, 0, 8)),
                    "Enter displacement never moved mid-flight.");
                var before = window.EnterTranslate.Y;

                window.HidePanel();
                Pump(TimeSpan.FromMilliseconds(15));
                Assert.True(Math.Abs(window.EnterTranslate.Y - before) < 1.5,
                    $"Jumped on reverse: {before:F2} -> {window.EnterTranslate.Y:F2}.");

                Pump(TimeSpan.FromMilliseconds(400));
                Assert.False(window.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Reverse_mid_exit_continues_without_jumping_then_shows()
    {
        WpfStaRunner.Run(() =>
        {
            var window = CreateOffscreenPanel(out _);
            try
            {
                Pump(TimeSpan.FromMilliseconds(400));
                window.HidePanel();
                if (!AnimationsOn())
                {
                    window.ShowCore(activate: false);
                    Assert.True(window.IsVisible);
                    return;
                }

                Assert.True(PumpUntil(() => IsStrictlyBetween(window.EnterTranslate.Y, 0, 6)),
                    "Exit displacement never moved mid-flight.");
                var before = window.EnterTranslate.Y;
                window.ShowCore(activate: false);
                Pump(TimeSpan.FromMilliseconds(15));
                Assert.True(Math.Abs(window.EnterTranslate.Y - before) < 1.5,
                    $"Jumped on reverse: {before:F2} -> {window.EnterTranslate.Y:F2}.");

                Pump(TimeSpan.FromMilliseconds(400));
                Assert.True(window.IsVisible);
                Assert.Equal(0, window.EnterTranslate.Y, 2);
                Assert.Equal(1, window.Opacity, 2);
                AssertRendered(window, 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Rapid_toggles_end_in_the_last_intent_with_clean_values()
    {
        WpfStaRunner.Run(() =>
        {
            var window = CreateOffscreenPanel(out _);
            try
            {
                for (var i = 0; i < 3; i++)
                {
                    window.HidePanel();
                    Pump(TimeSpan.FromMilliseconds(30));
                    window.ShowCore(activate: false);
                    Pump(TimeSpan.FromMilliseconds(30));
                }

                Pump(TimeSpan.FromMilliseconds(500));
                Assert.True(window.IsVisible);
                Assert.Equal(0, window.EnterTranslate.Y, 2);
                Assert.Equal(1, window.Opacity, 2);
                AssertRendered(window, 0);

                window.HidePanel();
                Pump(TimeSpan.FromMilliseconds(30));
                window.ShowCore(activate: false);
                Pump(TimeSpan.FromMilliseconds(30));
                window.HidePanel();
                Pump(TimeSpan.FromMilliseconds(500));
                Assert.False(window.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Track_change_dips_header_only_and_returns_to_zero()
    {
        WpfStaRunner.Run(() =>
        {
            var window = CreateOffscreenPanel(out var vm, out var media);
            try
            {
                Pump(TimeSpan.FromMilliseconds(400));
                var rest = RenderedHeaderOffset(window);

                media.SetTrack("key-b", "Nova faixa", "Novo artista");
                vm.ApplySnapshot(media.Current);
                if (!AnimationsOn())
                {
                    Assert.Null(window._trackStoryboard);
                    return;
                }

                Assert.NotNull(window._trackStoryboard);
                Assert.True(PumpUntil(() => IsStrictlyBetween(window.HeaderTranslate.Y, 0, 3)),
                    "Header displacement never moved mid-flight.");
                Assert.InRange(window.HeaderGrid.Opacity, 0.01, 0.99);
                Assert.True(RenderedHeaderOffset(window) - rest > 0.2,
                    "Header did not move on screen.");
                Assert.Equal(1.0, window.PlayPauseButton.Opacity);
                Assert.Equal(1.0, window.SeekSlider.Opacity);

                Pump(TimeSpan.FromMilliseconds(400));
                Assert.Equal(0, window.HeaderTranslate.Y, 2);
                Assert.Equal(1, window.HeaderGrid.Opacity, 2);
                Assert.True(Math.Abs(RenderedHeaderOffset(window) - rest) < 0.5,
                    "Header did not return to rest.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void AssertRendered(PanelWindow window, double expectedY)
    {
        var rendered = window.RootBorder.TransformToAncestor(window).Transform(new Point(0, 0));
        Assert.True(Math.Abs(rendered.Y - expectedY) < 0.5,
            $"Rendered offset {rendered.Y:F2} != property {expectedY:F2}.");
    }

    private static double RenderedHeaderOffset(PanelWindow window) =>
        window.HeaderGrid.TransformToAncestor(window.ContentGrid).Transform(new Point(0, 0)).Y;

    private static PanelWindow CreateOffscreenPanel(out CapsuleViewModel vm) =>
        CreateOffscreenPanel(out vm, out _);

    private static PanelWindow CreateOffscreenPanel(out CapsuleViewModel vm, out FakeMedia media)
    {
        var innerMedia = new FakeMedia("key-a", "Faixa", "Artista");
        var innerVm = new CapsuleViewModel(innerMedia, new SystemClock(), new TestLogger());
        var window = new PanelWindow(innerVm);
        innerVm.ApplySnapshot(innerMedia.Current);
        window.Left = -10000;
        window.Top = -10000;
        window.ShowCore(activate: false);
        vm = innerVm;
        media = innerMedia;
        return window;
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static bool PumpUntil(Func<bool> condition, int sliceMs = 20, int maxMs = 1500)
    {
        var waited = 0;
        while (!condition() && waited < maxMs)
        {
            Pump(TimeSpan.FromMilliseconds(sliceMs));
            waited += sliceMs;
        }

        return condition();
    }

    private static bool IsStrictlyBetween(double value, double low, double high) =>
        value > low && value < high;

    private static bool AnimationsOn()
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
            // Fall back to WPF, mirroring the window.
        }

        return SystemParameters.ClientAreaAnimation;
    }

    private sealed class FakeMedia : IMediaSessionService
    {
        private readonly TimeSpan _duration = TimeSpan.FromMinutes(3);

        public FakeMedia(string artworkKey, string title, string artist)
        {
            SetTrack(artworkKey, title, artist);
        }

        public MediaSnapshot Current { get; private set; } = MediaSnapshot.Empty;

        public event EventHandler<MediaSnapshot>? SnapshotChanged;

        public void SetTrack(string artworkKey, string title, string artist)
        {
            Current = new MediaSnapshot(
                MediaPlaybackStatus.Paused,
                new TrackInfo(title, artist, null, null, artworkKey),
                new TimelineInfo(TimeSpan.FromSeconds(10), _duration, DateTimeOffset.UtcNow, 1, false)
                {
                    StartTime = TimeSpan.Zero,
                    EndTime = _duration,
                    MinSeekTime = TimeSpan.Zero,
                    MaxSeekTime = _duration
                },
                new CommandAvailability(PlayPause: true, Next: true, Previous: true, Seek: true),
                "test-session",
                "test-app",
                "test-session#1",
                1);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> PlayPauseAsync() => Task.FromResult(true);

        public Task<bool> SkipNextAsync() => Task.FromResult(false);

        public Task<bool> SkipPreviousAsync() => Task.FromResult(false);

        public Task<bool> SeekAsync(long positionTicks) => Task.FromResult(true);
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
