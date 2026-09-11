using TaskBarHook.Desktop;
using TaskBarHook.Logging;

namespace TaskBarHook.Tests;

// Sustained browser fullscreen used to flap: a single false assessment
// popped the compact back over the video. Entry and exit both require
// sustained samples now. No STA/dispatcher pumping needed: RefreshFullscreen
// is synchronous and the scripted environment drives it directly.
public sealed class FullscreenDebounceTests
{
    [Fact]
    public void Default_delays_hide_fast_and_return_without_lingering()
    {
        // Locks the 0.4.4/0.4.5 tuning: entry must feel instant, exit must
        // neither flap on transients nor keep the user waiting ~1s.
        Assert.True(DesktopShellMonitor.DefaultEntryDelay <= TimeSpan.FromMilliseconds(250));
        Assert.True(DesktopShellMonitor.DefaultExitDelay >= TimeSpan.FromMilliseconds(200));
        Assert.True(DesktopShellMonitor.DefaultExitDelay <= TimeSpan.FromMilliseconds(500));
        Assert.True(DesktopShellMonitor.DefaultEntryDelay < DesktopShellMonitor.DefaultExitDelay);
    }

    [Fact]
    public void Entry_fires_without_delay_when_configured()
    {
        var monitor = CreateMonitor(out var events, out _,
            new FullscreenAssessment(true, "exclusive"));
        monitor.RefreshFullscreen(); // true

        Assert.Equal(new[] { true }, events);
    }

    [Fact]
    public void Single_false_sample_does_not_end_sustained_fullscreen()
    {
        var monitor = CreateMonitor(out var events, out _,
            new FullscreenAssessment(true, "exclusive"),
            new FullscreenAssessment(false, "transient"),
            new FullscreenAssessment(true, "exclusive"));

        monitor.RefreshFullscreen();
        monitor.RefreshFullscreen();
        monitor.RefreshFullscreen();

        Assert.Equal(new[] { true }, events);
    }

    [Fact]
    public void Sustained_false_ends_after_the_exit_delay()
    {
        var monitor = CreateMonitor(out var events, out _,
            new FullscreenAssessment(true, "exclusive"),
            new FullscreenAssessment(false, "gone"),
            new FullscreenAssessment(false, "gone"));

        monitor.RefreshFullscreen();
        Assert.Equal(new[] { true }, events);

        monitor.RefreshFullscreen(); // false starts the exit candidate
        Assert.Equal(new[] { true }, events);

        // Timing is one-directional: the sleep can only push elapsed time
        // further past the 30ms test delay, never short of it.
        Thread.Sleep(60);
        monitor.RefreshFullscreen(); // false, sustained
        Assert.Equal(new[] { true, false }, events);
    }

    [Fact]
    public void Rapid_flapping_never_fires_exit_until_it_settles()
    {
        var assessments = new List<FullscreenAssessment> { new(true, "exclusive") };
        for (var i = 0; i < 10; i++)
        {
            assessments.Add(new FullscreenAssessment(false, "transient"));
            assessments.Add(new FullscreenAssessment(true, "exclusive"));
        }

        var monitor = CreateMonitor(out var events, out _, assessments.ToArray());
        foreach (var _ in assessments)
        {
            monitor.RefreshFullscreen();
        }

        Assert.Equal(new[] { true }, events);
    }

    [Fact]
    public void Steady_false_fires_a_single_exit()
    {
        var monitor = CreateMonitor(out var events, out _,
            new FullscreenAssessment(false, "none"),
            new FullscreenAssessment(false, "none"),
            new FullscreenAssessment(false, "none"));

        monitor.RefreshFullscreen();
        monitor.RefreshFullscreen();
        monitor.RefreshFullscreen();

        Assert.Single(events);
        Assert.False(events[0]);
    }

    [Fact]
    public void Exit_log_names_the_false_cause()
    {
        var monitor = CreateMonitor(out _, out var logger,
            new FullscreenAssessment(true, "exclusive"),
            new FullscreenAssessment(false, "maximized class=Foo"),
            new FullscreenAssessment(false, "maximized class=Foo"));

        monitor.RefreshFullscreen();
        monitor.RefreshFullscreen();
        Thread.Sleep(60);
        monitor.RefreshFullscreen();

        Assert.Contains(
            logger.Messages,
            m => m == "Fullscreen ended (maximized class=Foo).");
    }

    private static DesktopShellMonitor CreateMonitor(
        out List<bool> events,
        out RecordingLogger logger,
        params FullscreenAssessment[] script)
    {
        var collected = new List<bool>();
        var environment = new ScriptedEnvironment(script);
        var recording = new RecordingLogger();
        var monitor = new DesktopShellMonitor(
            environment,
            recording,
            entryDelay: TimeSpan.Zero,
            exitDelay: TimeSpan.FromMilliseconds(30));
        monitor.FullscreenChanged += (_, fullscreen) => collected.Add(fullscreen);
        events = collected;
        logger = recording;
        return monitor;
    }

    private sealed class ScriptedEnvironment : IDesktopEnvironment
    {
        private readonly Queue<FullscreenAssessment> _script;
        private FullscreenAssessment _last = new(false, "none");

        public ScriptedEnvironment(FullscreenAssessment[] script)
        {
            _script = new Queue<FullscreenAssessment>(script);
        }

        public MonitorGeometry GetPrimaryMonitor() => new(0, 0, 1920, 1080, 0, 0, 1920, 1032);

        public TaskbarGeometry GetTaskbar() => new(TaskbarEdge.Bottom, 0, 1032, 1920, 1080, false, true);

        public FullscreenAssessment AssessFullscreen(IReadOnlyList<nint> excludeHwnds)
        {
            if (_script.Count > 0)
            {
                _last = _script.Dequeue();
            }

            return _last;
        }

        public double GetScale(nint hwnd) => 1;
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Messages { get; } = new();

        public void Info(string message) => Messages.Add(message);

        public void Warn(string message) => Messages.Add(message);

        public void Error(string message, Exception? exception = null) =>
            Messages.Add(message + (exception is null ? "" : ": " + exception.GetType().Name));
    }
}
