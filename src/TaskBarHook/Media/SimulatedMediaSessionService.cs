using TaskBarHook.Logging;
using TaskBarHook.Models;

namespace TaskBarHook.Media;

public sealed class SimulatedMediaSessionService : IMediaSessionService
{
    private static readonly byte[] SampleArtwork = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly IClock _clock;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly SimulatedTrack[] _tracks;
    private int _index;
    private bool _playing = true;
    private bool _hasSession = true;
    private TimeSpan _position;
    private DateTimeOffset _originUtc;
    private int _sessionSerial = 1;
    private int _trackGeneration = 1;

    public SimulatedMediaSessionService(IClock clock, IAppLogger logger)
    {
        _clock = clock;
        _logger = logger;
        _originUtc = clock.UtcNow;
        _tracks =
        [
            new SimulatedTrack("Faixa longa simulada", "TaskBarHook", TimeSpan.FromMinutes(12), TimeSpan.Zero, true, SampleArtwork),
            new SimulatedTrack("Início deslocado", "Artista simulado", TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(10), true, SampleArtwork),
            new SimulatedTrack("Sem capa", "Artista simulado", TimeSpan.FromMinutes(3), TimeSpan.Zero, true, null),
            new SimulatedTrack("Sem duração", "Artista simulado", null, TimeSpan.Zero, false, SampleArtwork),
            new SimulatedTrack("Um título propositalmente muito longo para conferir o truncamento estável do painel", "Artista com nome também razoavelmente extenso", TimeSpan.FromMinutes(5), TimeSpan.Zero, true, SampleArtwork),
            new SimulatedTrack("Sem artista nem capa", "", TimeSpan.FromMinutes(3), TimeSpan.Zero, true, null),
            new SimulatedTrack("Seek sempre falha", "Artista simulado", TimeSpan.FromMinutes(3), TimeSpan.Zero, true, SampleArtwork, FailSeek: true)
        ];
        _position = CurrentTrack.StartTime;
    }

    public TimeSpan SeekDelay { get; set; }

    public bool FailNextSeek { get; set; }

    public bool ChangeTrackOnSeek { get; set; }

    public bool SeekEnabled { get; set; } = true;

    public MediaSnapshot Current { get; private set; } = MediaSnapshot.Empty;

    public event EventHandler<MediaSnapshot>? SnapshotChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info("Simulated media source started. This is not a real player session.");
        Publish();
        return Task.CompletedTask;
    }

    public Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info("Simulated media source reconnected.");
        Publish();
        return Task.CompletedTask;
    }

    public Task<bool> PlayPauseAsync() => RunAsync(() =>
    {
        if (!_hasSession)
        {
            return false;
        }

        CapturePosition();
        _playing = !_playing;
        _originUtc = _clock.UtcNow;
        Publish();
        return true;
    });

    public Task<bool> SkipNextAsync() => RunAsync(() =>
    {
        if (!_hasSession)
        {
            return false;
        }

        if (_index >= _tracks.Length - 1)
        {
            _hasSession = false;
            _playing = false;
            _position = TimeSpan.Zero;
            Current = MediaSnapshot.Empty;
            SnapshotChanged?.Invoke(this, Current);
            _logger.Info("Simulated session disappeared.");
            return true;
        }

        _index++;
        ResetTrack();
        Publish();
        return true;
    });

    public Task<bool> SkipPreviousAsync() => RunAsync(() =>
    {
        if (!_hasSession)
        {
            _hasSession = true;
            _index = 0;
            _playing = true;
            _sessionSerial++;
            ResetTrack();
            Publish();
            return true;
        }

        if (_index > 0)
        {
            _index--;
        }

        ResetTrack();
        Publish();
        return true;
    });

    public async Task<bool> SeekAsync(long positionTicks)
    {
        await _commandGate.WaitAsync();
        try
        {
            if (SeekDelay > TimeSpan.Zero)
            {
                await Task.Delay(SeekDelay);
            }

            if (!_hasSession || !SeekEnabled || !CurrentTrack.CanSeek || CurrentTrack.FailSeek)
            {
                return false;
            }

            if (ChangeTrackOnSeek)
            {
                ChangeTrackOnSeek = false;
                if (_index < _tracks.Length - 1)
                {
                    _index++;
                    ResetTrack();
                    Publish();
                }

                return true;
            }

            if (FailNextSeek)
            {
                FailNextSeek = false;
                Publish();
                return false;
            }

            var range = SeekMapping.From(BuildTimeline(), true);
            var requested = SeekMapping.ClampSession(TimeSpan.FromTicks(positionTicks), range);
            CapturePosition();
            _position = requested;
            _originUtc = _clock.UtcNow;
            Publish();
            return true;
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _commandGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<bool> RunAsync(Func<bool> action)
    {
        await _commandGate.WaitAsync();
        try
        {
            return action();
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private SimulatedTrack CurrentTrack => _tracks[_index];

    private void ResetTrack()
    {
        _trackGeneration++;
        _position = CurrentTrack.StartTime;
        _originUtc = _clock.UtcNow;
        _playing = true;
    }

    private void CapturePosition()
    {
        var interpolated = ProgressInterpolator.Interpolate(BuildTimeline(), _clock.UtcNow);
        _position = interpolated ?? CurrentTrack.StartTime;
    }

    private void Publish()
    {
        if (!_hasSession)
        {
            Current = MediaSnapshot.Empty;
            SnapshotChanged?.Invoke(this, Current);
            return;
        }

        var track = CurrentTrack;
        Current = new MediaSnapshot(
            _playing ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused,
            new TrackInfo(track.Title, track.Artist, "Simulação", track.Artwork, $"{track.Title}|{track.Artist}"),
            BuildTimeline(),
            new CommandAvailability(true, true, true, SeekEnabled && track.CanSeek),
            "simulate:taskbarhook",
            "TaskBarHook.Simulate",
            $"simulate:taskbarhook#{_sessionSerial}",
            _trackGeneration);
        SnapshotChanged?.Invoke(this, Current);
    }

    private TimelineInfo BuildTimeline()
    {
        var track = CurrentTrack;
        TimeSpan? end = track.Duration is { } duration ? track.StartTime + duration : null;
        return new TimelineInfo(
            _position,
            track.Duration,
            _originUtc,
            1,
            _playing && _hasSession,
            track.StartTime,
            end,
            track.CanSeek ? track.StartTime : null,
            track.CanSeek ? end : null);
    }

    private sealed record SimulatedTrack(
        string Title,
        string Artist,
        TimeSpan? Duration,
        TimeSpan StartTime,
        bool CanSeek,
        byte[]? Artwork,
        bool FailSeek = false);
}
