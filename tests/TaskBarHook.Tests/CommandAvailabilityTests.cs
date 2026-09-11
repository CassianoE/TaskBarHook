using TaskBarHook.Logging;
using TaskBarHook.Media;
using TaskBarHook.Models;
using TaskBarHook.Presentation;

namespace TaskBarHook.Tests;

public sealed class CommandAvailabilityTests
{
    [Fact]
    public void Commands_cannot_execute_when_the_session_disables_them()
    {
        var media = new FakeMedia();
        var viewModel = new CapsuleViewModel(media, new SystemClock(), new NullLogger());
        viewModel.ApplySnapshot(new MediaSnapshot(
            MediaPlaybackStatus.Paused,
            new TrackInfo("Song", "Artist", null, null, "Song|Artist|"),
            TimelineInfo.Unknown,
            CommandAvailability.None,
            "s",
            "Spotify.exe"));

        Assert.False(viewModel.CanPlayPause);
        Assert.False(viewModel.CanSkipNext);
        Assert.False(viewModel.CanSkipPrevious);
        Assert.False(viewModel.PlayPauseCommand.CanExecute(null));
        Assert.False(viewModel.SkipNextCommand.CanExecute(null));
        Assert.False(viewModel.SkipPreviousCommand.CanExecute(null));
        viewModel.Dispose();
    }

    [Fact]
    public void Play_pause_is_available_when_the_session_allows_it()
    {
        var media = new FakeMedia();
        var viewModel = new CapsuleViewModel(media, new SystemClock(), new NullLogger());
        viewModel.ApplySnapshot(new MediaSnapshot(
            MediaPlaybackStatus.Playing,
            new TrackInfo("Song", "Artist", null, null, "Song|Artist|"),
            TimelineInfo.Unknown,
            new CommandAvailability(true, true, false),
            "s",
            "Spotify.exe"));

        Assert.True(viewModel.PlayPauseCommand.CanExecute(null));
        Assert.True(viewModel.SkipNextCommand.CanExecute(null));
        Assert.False(viewModel.SkipPreviousCommand.CanExecute(null));
        viewModel.Dispose();
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
