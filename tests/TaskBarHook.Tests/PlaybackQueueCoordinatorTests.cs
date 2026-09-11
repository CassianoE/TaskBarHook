using TaskBarHook.Queue;

namespace TaskBarHook.Tests;

public sealed class PlaybackQueueCoordinatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Does_not_fetch_while_closed_or_on_repeated_ticks()
    {
        var coordinator = new PlaybackQueueCoordinator();
        Assert.False(coordinator.ShouldFetch(false, T0, "s", force: false));
        Assert.True(coordinator.ShouldFetch(true, T0, "s", force: false));
        coordinator.Begin("s", T0);
        Assert.False(coordinator.ShouldFetch(true, T0 + TimeSpan.FromMilliseconds(16), "s", force: false));
        coordinator.End();
        Assert.False(coordinator.ShouldFetch(true, T0 + TimeSpan.FromSeconds(1), "s", force: false));
        Assert.True(coordinator.ShouldFetch(true, T0 + PlaybackQueueCoordinator.MinRefreshInterval, "s", force: false));
    }

    [Fact]
    public void Track_change_invalidates_the_cache()
    {
        var coordinator = new PlaybackQueueCoordinator();
        coordinator.Begin("a", T0);
        coordinator.End();
        Assert.True(coordinator.ShouldFetch(true, T0 + TimeSpan.FromMilliseconds(50), "b", force: false));
    }

    [Fact]
    public void Force_refreshes_even_inside_the_interval()
    {
        var coordinator = new PlaybackQueueCoordinator();
        coordinator.Begin("s", T0);
        coordinator.End();
        Assert.True(coordinator.ShouldFetch(true, T0 + TimeSpan.FromSeconds(1), "s", force: true));
    }
}
