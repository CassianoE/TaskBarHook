using System.Windows;
using System.Windows.Media.Animation;
using TaskBarHook.Logging;
using TaskBarHook.Media;
using TaskBarHook.Models;
using TaskBarHook.Presentation;
using TaskBarHook.Views;

namespace TaskBarHook.Tests;

// A track change must briefly transition the cover + info only. Animating the
// whole window (as 0.3.x did) would also fade the controls and the slider.
[Collection("WPF")]
public sealed class TrackTransitionTests
{
    [Fact]
    public void Track_change_animates_header_only_and_skips_the_initial_bind()
    {
        WpfStaRunner.Run(() =>
        {
            var media = new FakeMedia("key-a");
            var vm = new CapsuleViewModel(media, new SystemClock(), new TestLogger());
            var window = new PanelWindow(vm);

            vm.ApplySnapshot(media.Current);
            Assert.Null(window._trackStoryboard);

            media.SetTrack("key-b", "Nova faixa", "Novo artista");
            vm.ApplySnapshot(media.Current);

            if (!AnimationsOn())
            {
                Assert.Null(window._trackStoryboard);
                return;
            }

            Assert.NotNull(window._trackStoryboard);
            Assert.NotEmpty(window._trackStoryboard.Children);
            foreach (var timeline in window._trackStoryboard.Children)
            {
                Assert.NotSame(window, Storyboard.GetTarget(timeline));
            }

            Assert.Contains(
                window._trackStoryboard.Children,
                timeline => ReferenceEquals(Storyboard.GetTarget(timeline), window.HeaderGrid));
            Assert.Equal(1.0, window.Opacity);
        });
    }

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

        public FakeMedia(string artworkKey)
        {
            SetTrack(artworkKey, "Faixa", "Artista");
        }

        public MediaSnapshot Current { get; private set; } = MediaSnapshot.Empty;

        public event EventHandler<MediaSnapshot>? SnapshotChanged;

        public void SetTrack(string artworkKey, string title, string artist)
        {
            Current = new MediaSnapshot(
                MediaPlaybackStatus.Playing,
                new TrackInfo(title, artist, null, null, artworkKey),
                new TimelineInfo(TimeSpan.FromSeconds(10), _duration, DateTimeOffset.UtcNow, 1, true)
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
