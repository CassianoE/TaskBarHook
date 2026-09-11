using TaskBarHook.Logging;
using TaskBarHook.Media;
using TaskBarHook.Queue;

namespace TaskBarHook.Tests;

public sealed class SimulatedQueueTests
{
    [Fact]
    public async Task Simulated_queue_is_the_remaining_tracks_and_marked_as_simulation()
    {
        var media = new SimulatedMediaSessionService(new SystemClock(), new NullLogger());
        await media.StartAsync();
        await media.RefreshAsync(media.Current);
        Assert.Equal(PlaybackQueueSource.Simulated, media.CurrentQueue.Source);
        Assert.NotEmpty(media.CurrentQueue.Items);
        Assert.Equal("Início deslocado", media.CurrentQueue.Items[0].Title);
        Assert.DoesNotContain(media.CurrentQueue.Items, item => item.Title == media.Current.Track.Title);
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
