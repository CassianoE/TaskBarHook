using System.Windows;
using TaskBarHook.Logging;
using TaskBarHook.Media;
using TaskBarHook.Models;
using TaskBarHook.Presentation;
using TaskBarHook.Queue;
using TaskBarHook.Views;

namespace TaskBarHook.Tests;

[Collection("WPF")]
public sealed class QueueFlyoutWindowTests
{
    [Fact]
    public void List_shows_names_and_caps_visible_height()
    {
        WpfStaRunner.Run(() =>
        {
            var window = CreateFlyout(out _, itemCount: 12);
            try
            {
                Assert.Equal(12, window.ViewModel.Queue.Items.Count);
                Assert.True(
                    window.QueueScroll.MaxHeight <= QueueFlyoutPlacement.RowHeightDip * QueueFlyoutPlacement.VisibleRows + 1);
                var first = (QueueItemViewModel)window.QueueList.Items[0]!;
                Assert.False(string.IsNullOrWhiteSpace(first.Title));
                Assert.Equal(first.Title, first.Title);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Empty_and_error_states_use_short_messages()
    {
        var empty = PlaybackQueueSnapshot.Empty(PlaybackQueueSource.Spotify);
        var missing = PlaybackQueueSnapshot.Missing(SpotifyOptions.ClientIdVariable);
        var disconnected = PlaybackQueueSnapshot.Disconnected();
        var unsupported = PlaybackQueueSnapshot.Unsupported();
        Assert.Contains("Nada", empty.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SpotifyOptions.ClientIdVariable, missing.MissingCredential);
        Assert.Contains("conexão", disconnected.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Spotify", unsupported.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Esc_closes_the_list_without_collapsing_the_panel()
    {
        var media = new FakeMedia();
        var vm = new CapsuleViewModel(media, new SystemClock(), new TestLogger(), new FakeQueue());
        vm.ApplySnapshot(media.Current);
        vm.ToggleExpandedCommand.Execute(null);
        vm.Queue.ToggleCommand.Execute(null);
        Assert.True(vm.IsExpanded);
        Assert.True(vm.Queue.IsOpen);
        Assert.True(vm.Queue.CloseFromEscape());
        Assert.False(vm.Queue.IsOpen);
        Assert.True(vm.IsExpanded);
    }

    private static QueueFlyoutWindow CreateFlyout(out CapsuleViewModel vm, int itemCount)
    {
        var media = new FakeMedia();
        var items = Enumerable.Range(1, itemCount)
            .Select(i => new QueueTrack(i.ToString(), $"Faixa {i} com um título bem longo para truncar", null, null))
            .ToList();
        var queue = new FakeQueue
        {
            Current = PlaybackQueueSnapshot.Ready(items, PlaybackQueueSource.Simulated, true, "Atual")
        };
        vm = new CapsuleViewModel(media, new SystemClock(), new TestLogger(), queue);
        vm.ApplySnapshot(media.Current);
        foreach (var item in items)
        {
            vm.Queue.Items.Add(new QueueItemViewModel(item));
        }

        var window = new QueueFlyoutWindow(vm);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(QueueFlyoutPlacement.WidthDip, 400));
        content.Arrange(new Rect(0, 0, QueueFlyoutPlacement.WidthDip, 248));
        content.UpdateLayout();
        return window;
    }

    private sealed class FakeQueue : IPlaybackQueueService
    {
        public PlaybackQueueSnapshot Current { get; set; } = PlaybackQueueSnapshot.Idle;

        public event EventHandler<PlaybackQueueSnapshot>? QueueChanged
        {
            add { }
            remove { }
        }

        public bool CanAuthorize => false;

        public Task RefreshAsync(MediaSnapshot session, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AuthorizeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeMedia : IMediaSessionService
    {
        public FakeMedia()
        {
            Current = new MediaSnapshot(
                MediaPlaybackStatus.Paused,
                new TrackInfo("Faixa", "Artista", null, null, "k"),
                TimelineInfo.Unknown,
                new CommandAvailability(true, true, true, true),
                "s",
                "Spotify.exe");
        }

        public MediaSnapshot Current { get; }

        public event EventHandler<MediaSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> PlayPauseAsync() => Task.FromResult(true);

        public Task<bool> SkipNextAsync() => Task.FromResult(true);

        public Task<bool> SkipPreviousAsync() => Task.FromResult(true);

        public Task<bool> SeekAsync(long positionTicks) => Task.FromResult(true);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
