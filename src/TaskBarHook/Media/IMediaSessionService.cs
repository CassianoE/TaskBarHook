using TaskBarHook.Models;

namespace TaskBarHook.Media;

public interface IMediaSessionService : IAsyncDisposable
{
    MediaSnapshot Current { get; }

    event EventHandler<MediaSnapshot>? SnapshotChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task ReconnectAsync(CancellationToken cancellationToken = default);

    Task<bool> PlayPauseAsync();

    Task<bool> SkipNextAsync();

    Task<bool> SkipPreviousAsync();

    Task<bool> SeekAsync(long positionTicks);
}
