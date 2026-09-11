using TaskBarHook.Logging;
using TaskBarHook.Media;
using TaskBarHook.Models;
using TaskBarHook.Presentation;

namespace TaskBarHook.Tests;

public sealed class FullscreenPanelTests
{
    [Fact]
    public void Fullscreen_round_trip_does_not_reopen_the_panel()
    {
        var media = new FakeMedia();
        using var viewModel = new CapsuleViewModel(media, new SystemClock(), new NullLogger());
        viewModel.ApplySnapshot(new MediaSnapshot(
            MediaPlaybackStatus.Paused,
            new TrackInfo("Song", "Artist", null, null, "Song|Artist|"),
            TimelineInfo.Unknown,
            new CommandAvailability(true, true, true),
            "s",
            "Spotify.exe"));

        viewModel.ToggleExpandedCommand.Execute(null);
        viewModel.Queue.ToggleCommand.Execute(null);
        Assert.True(viewModel.IsExpanded);
        Assert.True(viewModel.Queue.IsOpen);

        viewModel.SetFullscreen(true);
        Assert.False(viewModel.IsExpanded);
        Assert.False(viewModel.Queue.IsOpen);

        viewModel.SetFullscreen(false);
        Assert.False(viewModel.IsExpanded);
    }

    [Fact]
    public void Panel_cannot_open_while_fullscreen()
    {
        var media = new FakeMedia();
        using var viewModel = new CapsuleViewModel(media, new SystemClock(), new NullLogger());
        viewModel.ApplySnapshot(new MediaSnapshot(
            MediaPlaybackStatus.Paused,
            new TrackInfo("Song", "Artist", null, null, "Song|Artist|"),
            TimelineInfo.Unknown,
            new CommandAvailability(true, true, true),
            "s",
            "Spotify.exe"));

        viewModel.SetFullscreen(true);
        viewModel.ToggleExpandedCommand.Execute(null);
        viewModel.ShowPanelFromTray();

        Assert.False(viewModel.IsExpanded);
    }

    private sealed class FakeMedia : IMediaSessionService
    {
        public MediaSnapshot Current { get; } = MediaSnapshot.Empty;

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

    private sealed class NullLogger : IAppLogger
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
