using TaskBarHook.Models;

namespace TaskBarHook.Queue;

public interface IPlaybackQueueService : IAsyncDisposable
{
    PlaybackQueueSnapshot Current { get; }

    event EventHandler<PlaybackQueueSnapshot>? QueueChanged;

    bool CanAuthorize { get; }

    Task RefreshAsync(MediaSnapshot session, CancellationToken cancellationToken = default);

    Task AuthorizeAsync(CancellationToken cancellationToken = default);
}
