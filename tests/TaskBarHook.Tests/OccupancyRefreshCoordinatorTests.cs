using TaskBarHook.Desktop;

namespace TaskBarHook.Tests;

public sealed class OccupancyRefreshCoordinatorTests
{
    [Fact]
    public async Task Timeout_hides_without_starting_another_read()
    {
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var source = new ScriptedSource(_ =>
        {
            entered.Set();
            release.Wait();
            return Complete("late");
        });
        var applied = new List<TaskbarOccupancy>();
        using var coordinator = new OccupancyRefreshCoordinator(source, TimeSpan.FromMilliseconds(40));
        coordinator.OccupancyReady += (_, result) => applied.Add(result.Occupancy);

        coordinator.Request();
        Assert.True(entered.Wait(1000));
        await WaitUntil(() => applied.Count >= 1, TimeSpan.FromSeconds(2));
        Assert.Equal(1, source.Calls);
        Assert.Equal(OccupancyReadStatus.Unavailable, applied[0].Status);
        Assert.Equal("occupancy-timeout", applied[0].Error);

        release.Set();
        await WaitUntil(() => applied.Count >= 2, TimeSpan.FromSeconds(2));
        Assert.Equal(1, source.Calls);
        Assert.True(applied[^1].Success);
        Assert.Equal("late", applied[^1].Error);
    }

    [Fact]
    public async Task Newer_request_discards_the_older_result()
    {
        var firstEntered = new ManualResetEventSlim();
        var firstRelease = new ManualResetEventSlim();
        var secondEntered = new ManualResetEventSlim();
        var secondRelease = new ManualResetEventSlim();
        var source = new ScriptedSource(call =>
        {
            if (call == 1)
            {
                firstEntered.Set();
                firstRelease.Wait();
                return Complete("old");
            }

            secondEntered.Set();
            secondRelease.Wait();
            return Complete("new");
        });
        var applied = new List<string?>();
        using var coordinator = new OccupancyRefreshCoordinator(source, TimeSpan.FromSeconds(5));
        coordinator.OccupancyReady += (_, result) => applied.Add(result.Occupancy.Error);

        coordinator.Request();
        Assert.True(firstEntered.Wait(1000));
        coordinator.Request();
        firstRelease.Set();
        Assert.True(secondEntered.Wait(2000));
        Assert.DoesNotContain("old", applied);
        secondRelease.Set();
        await WaitUntil(() => applied.Contains("new"), TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { "new" }, applied);
    }

    [Fact]
    public async Task Timeout_invalidates_even_when_a_newer_request_is_waiting()
    {
        var firstEntered = new ManualResetEventSlim();
        var firstRelease = new ManualResetEventSlim();
        var secondEntered = new ManualResetEventSlim();
        var secondRelease = new ManualResetEventSlim();
        var source = new ScriptedSource(call =>
        {
            if (call == 1)
            {
                firstEntered.Set();
                firstRelease.Wait();
                return Complete("old");
            }

            secondEntered.Set();
            secondRelease.Wait();
            return Complete("new");
        });
        var applied = new List<TaskbarOccupancy>();
        using var coordinator = new OccupancyRefreshCoordinator(source, TimeSpan.FromMilliseconds(40));
        coordinator.OccupancyReady += (_, result) => applied.Add(result.Occupancy);

        coordinator.Request();
        Assert.True(firstEntered.Wait(1000));
        coordinator.Request();
        await WaitUntil(
            () => applied.Exists(item => item.Error == "occupancy-timeout"),
            TimeSpan.FromSeconds(2));
        Assert.Equal(1, source.Calls);
        Assert.False(secondEntered.IsSet);
        Assert.DoesNotContain(applied, item => item.Error == "old");

        firstRelease.Set();
        Assert.True(secondEntered.Wait(2000));
        Assert.Equal(2, source.Calls);
        Assert.DoesNotContain(applied, item => item.Error == "old");

        secondRelease.Set();
        await WaitUntil(() => applied.Exists(item => item.Error == "new"), TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(applied, item => item.Error == "old");
        Assert.Equal("new", applied[^1].Error);
    }

    [Fact]
    public void Stale_complete_is_not_applied_after_reaching_the_visual_queue()
    {
        var stale = new OccupancyRefreshResult(1, Complete("old"));
        var timeout = new OccupancyRefreshResult(1, TaskbarOccupancy.Unavailable("occupancy-timeout"));
        var fresh = new OccupancyRefreshResult(2, Complete("new"));

        Assert.False(OccupancyApplyGate.ShouldApply(stale, currentGeneration: 2, lastAppliedGeneration: 0));
        Assert.True(OccupancyApplyGate.ShouldApply(timeout, currentGeneration: 2, lastAppliedGeneration: 0));
        Assert.False(OccupancyApplyGate.ShouldApply(timeout, currentGeneration: 2, lastAppliedGeneration: 2));
        Assert.True(OccupancyApplyGate.ShouldApply(fresh, currentGeneration: 2, lastAppliedGeneration: 1));
        Assert.False(OccupancyApplyGate.ShouldApply(stale, currentGeneration: 2, lastAppliedGeneration: 2));
    }

    [Fact]
    public async Task Read_error_is_reported_as_unavailable()
    {
        var source = new ScriptedSource(_ => throw new InvalidOperationException("boom"));
        var applied = new List<TaskbarOccupancy>();
        using var coordinator = new OccupancyRefreshCoordinator(source, TimeSpan.FromSeconds(2));
        coordinator.OccupancyReady += (_, result) => applied.Add(result.Occupancy);

        coordinator.Request();
        await WaitUntil(() => applied.Count == 1, TimeSpan.FromSeconds(2));
        Assert.Equal(OccupancyReadStatus.Unavailable, applied[0].Status);
        Assert.Equal(nameof(InvalidOperationException), applied[0].Error);
    }

    [Fact]
    public async Task Caller_and_other_work_stay_responsive_during_a_slow_read()
    {
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var source = new ScriptedSource(_ =>
        {
            entered.Set();
            release.Wait();
            return Complete("ok");
        });
        using var coordinator = new OccupancyRefreshCoordinator(source, TimeSpan.FromSeconds(5));

        var requestWatch = System.Diagnostics.Stopwatch.StartNew();
        coordinator.Request();
        requestWatch.Stop();
        Assert.True(entered.Wait(1000));
        Assert.True(requestWatch.Elapsed < TimeSpan.FromMilliseconds(250));

        var command = Task.Run(() => 42);
        var completed = await Task.WhenAny(command, Task.Delay(200));
        Assert.Same(command, completed);
        Assert.Equal(42, await command);

        release.Set();
    }

    private static TaskbarOccupancy Complete(string tag) =>
        new(OccupancyReadStatus.Complete, true, default, default, 1, [], tag);

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - start > timeout)
            {
                throw new TimeoutException("Condition was not met.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class ScriptedSource : ITaskbarOccupancySource
    {
        private readonly Func<int, TaskbarOccupancy> _read;

        public ScriptedSource(Func<int, TaskbarOccupancy> read)
        {
            _read = read;
        }

        public int Calls { get; private set; }

        public TaskbarOccupancy Read()
        {
            Calls++;
            return _read(Calls);
        }
    }
}
