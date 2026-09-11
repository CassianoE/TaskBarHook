using TaskBarHook.Media;
using TaskBarHook.Models;

namespace TaskBarHook.Queue;

public sealed class NullPlaybackQueueService : IPlaybackQueueService
{
    public PlaybackQueueSnapshot Current { get; private set; } = PlaybackQueueSnapshot.Idle;

    public event EventHandler<PlaybackQueueSnapshot>? QueueChanged
    {
        add { }
        remove { }
    }

    public bool CanAuthorize => false;

    public Task RefreshAsync(MediaSnapshot session, CancellationToken cancellationToken = default)
    {
        Current = session.HasLiveSession && !SourceAppId.IsSpotify(session.SourceAppId)
            ? PlaybackQueueSnapshot.Unsupported()
            : PlaybackQueueSnapshot.Idle;
        return Task.CompletedTask;
    }

    public Task AuthorizeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
