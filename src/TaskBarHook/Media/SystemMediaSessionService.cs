using System.IO;
using TaskBarHook.Logging;
using TaskBarHook.Models;
using Windows.Media.Control;

namespace TaskBarHook.Media;

public sealed class SystemMediaSessionService : IMediaSessionService
{
    private readonly IAppLogger _logger;
    private readonly SessionSelector _selector = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly SemaphoreSlim _bindGate = new(1, 1);
    private readonly SnapshotUpdateCoordinator _snapshots = new();
    private readonly SynchronizationContext? _sync;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _boundId;
    private string? _sessionInstanceId;
    private int _sessionSerial;
    private bool _disposed;

    public SystemMediaSessionService(IAppLogger logger)
    {
        _logger = logger;
        _sync = SynchronizationContext.Current;
    }

    public MediaSnapshot Current => _snapshots.Current;

    public event EventHandler<MediaSnapshot>? SnapshotChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken);
        _manager.SessionsChanged += OnSessionsChanged;
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        await RefreshBindingAsync();
        _logger.Info($"SMTC started. Sessions={_manager.GetSessions().Count}.");
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _bindGate.WaitAsync(cancellationToken);
        try
        {
            UnhookManager();
            UnbindSession();
        }
        finally
        {
            _bindGate.Release();
        }

        await StartAsync(cancellationToken);
    }

    public Task<bool> PlayPauseAsync() => ExecuteCommandAsync(async session =>
    {
        var info = session.GetPlaybackInfo();
        var controls = info.Controls;
        if (info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing &&
            controls.IsPauseEnabled)
        {
            return await session.TryPauseAsync().AsTask();
        }

        if (info.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing &&
            controls.IsPlayEnabled)
        {
            return await session.TryPlayAsync().AsTask();
        }

        return await session.TryTogglePlayPauseAsync().AsTask();
    }, "PlayPause");

    public Task<bool> SkipNextAsync() =>
        ExecuteCommandAsync(session => session.TrySkipNextAsync().AsTask(), "SkipNext");

    public Task<bool> SkipPreviousAsync() =>
        ExecuteCommandAsync(session => session.TrySkipPreviousAsync().AsTask(), "SkipPrevious");

    public Task<bool> SeekAsync(long positionTicks) => ExecuteCommandAsync(session =>
    {
        var playback = session.GetPlaybackInfo();
        if (!playback.Controls.IsPlaybackPositionEnabled)
        {
            return Task.FromResult(false);
        }

        var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var range = SeekMapping.From(ReadTimeline(session, playing), true);
        if (!range.CanSeek)
        {
            return Task.FromResult(false);
        }

        var requested = SeekMapping.ClampSession(TimeSpan.FromTicks(positionTicks), range);
        return session.TryChangePlaybackPositionAsync(requested.Ticks).AsTask();
    }, "Seek");

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _bindGate.WaitAsync();
        try
        {
            UnhookManager();
            UnbindSession();
        }
        finally
        {
            _bindGate.Release();
            _bindGate.Dispose();
            _commandGate.Dispose();
        }
    }

    private async Task<bool> ExecuteCommandAsync(
        Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command,
        string name)
    {
        if (_session is null)
        {
            return false;
        }

        await _commandGate.WaitAsync();
        try
        {
            var ok = await command(_session);
            if (!ok)
            {
                _logger.Warn($"Media command {name} returned false.");
            }

            return ok;
        }
        catch (Exception ex)
        {
            _logger.Error($"Media command {name} failed.", ex);
            return false;
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        _ = RefreshBindingAsync();
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        _ = RefreshBindingAsync();
    }

    private async Task RefreshBindingAsync()
    {
        if (_disposed || _manager is null)
        {
            return;
        }

        await _bindGate.WaitAsync();
        try
        {
            if (_disposed || _manager is null)
            {
                return;
            }

            var sessions = _manager.GetSessions();
            var candidates = new List<SessionCandidate>(sessions.Count);
            foreach (var session in sessions)
            {
                candidates.Add(ToCandidate(session));
            }

            var systemCurrent = _manager.GetCurrentSession();
            var selected = _selector.Select(_boundId, candidates, GetId(systemCurrent));
            if (selected is null)
            {
                if (_session is not null)
                {
                    _logger.Info("No eligible media session.");
                }

                UnbindSession();
                _snapshots.Clear();
                Publish(_snapshots.Current);
                return;
            }

            var match = sessions.FirstOrDefault(session => GetId(session) == selected.Id && selected.IsPlaying == IsPlaying(session))
                        ?? sessions.FirstOrDefault(session => GetId(session) == selected.Id);
            if (match is null)
            {
                UnbindSession();
                _snapshots.Clear();
                Publish(_snapshots.Current);
                return;
            }

            if (ReferenceEquals(_session, match) && _boundId == selected.Id)
            {
                return;
            }

            BindSession(match, selected.Id);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to refresh media session binding.", ex);
        }
        finally
        {
            _bindGate.Release();
        }
    }

    private void BindSession(GlobalSystemMediaTransportControlsSession session, string id)
    {
        UnbindSession();
        _session = session;
        _boundId = id;
        _sessionSerial++;
        _sessionInstanceId = $"{id}#{_sessionSerial}";
        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        _logger.Info($"Bound media session '{id}' ({session.SourceAppUserModelId}).");
        _ = LoadTrackAsync();
    }

    private void UnbindSession()
    {
        if (_session is null)
        {
            return;
        }

        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        _session = null;
        _boundId = null;
        _sessionInstanceId = null;
        _snapshots.BeginTrackUpdate();
    }

    private void UnhookManager()
    {
        if (_manager is null)
        {
            return;
        }

        _manager.SessionsChanged -= OnSessionsChanged;
        _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        _manager = null;
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        _ = LoadTrackAsync();
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            var playback = _session.GetPlaybackInfo();
            var status = MapStatus(playback.PlaybackStatus);
            _snapshots.ApplyPlayback(status, ReadCommands(playback), ReadTimeline(_session, status == MediaPlaybackStatus.Playing));
            Publish(_snapshots.Current);
        }
        catch (Exception ex)
        {
            _logger.Error("Playback update failed.", ex);
        }
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            var status = MapStatus(_session.GetPlaybackInfo().PlaybackStatus);
            _snapshots.ApplyTimeline(ReadTimeline(_session, status == MediaPlaybackStatus.Playing));
            Publish(_snapshots.Current);
        }
        catch (Exception ex)
        {
            _logger.Error("Timeline update failed.", ex);
        }
    }

    private async Task LoadTrackAsync()
    {
        var token = _snapshots.BeginTrackUpdate();
        var session = _session;
        if (session is null)
        {
            _snapshots.Clear();
            Publish(_snapshots.Current);
            return;
        }

        GlobalSystemMediaTransportControlsSessionMediaProperties? properties = null;
        try
        {
            properties = await session.TryGetMediaPropertiesAsync().AsTask();
        }
        catch (Exception ex)
        {
            _logger.Error("TryGetMediaPropertiesAsync failed.", ex);
        }

        if (!_snapshots.IsTrackCurrent(token))
        {
            return;
        }

        var pending = SnapshotUpdateCoordinator.TrackWithoutStaleArtwork(
            _snapshots.Current.Track,
            properties?.Title,
            properties?.Artist,
            properties?.AlbumTitle);
        try
        {
            var playback = session.GetPlaybackInfo();
            var status = MapStatus(playback.PlaybackStatus);
            _snapshots.TryApplyTrack(
                token,
                pending,
                status,
                ReadTimeline(session, status == MediaPlaybackStatus.Playing),
                ReadCommands(playback),
                GetId(session),
                session.SourceAppUserModelId,
                _sessionInstanceId);
            Publish(_snapshots.Current);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to apply pending track.", ex);
        }

        byte[]? artwork = null;
        if (properties?.Thumbnail is not null)
        {
            try
            {
                artwork = await ReadThumbnailAsync(properties);
            }
            catch (Exception ex)
            {
                _logger.Error("Thumbnail read failed.", ex);
            }
        }

        if (!_snapshots.IsTrackCurrent(token))
        {
            return;
        }

        var track = new TrackInfo(pending.Title, pending.Artist, pending.Album, artwork, pending.ArtworkKey);
        try
        {
            var playback = session.GetPlaybackInfo();
            var status = MapStatus(playback.PlaybackStatus);
            if (_snapshots.TryApplyTrack(
                    token,
                    track,
                    status,
                    ReadTimeline(session, status == MediaPlaybackStatus.Playing),
                    ReadCommands(playback),
                    GetId(session),
                    session.SourceAppUserModelId,
                    _sessionInstanceId))
            {
                Publish(_snapshots.Current);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to apply track artwork.", ex);
        }
    }

    private static async Task<byte[]?> ReadThumbnailAsync(GlobalSystemMediaTransportControlsSessionMediaProperties properties)
    {
        if (properties.Thumbnail is null)
        {
            return null;
        }

        using var stream = await properties.Thumbnail.OpenReadAsync();
        if (stream.Size is 0 or > 8 * 1024 * 1024)
        {
            return null;
        }

        await using var input = stream.AsStreamForRead();
        using var output = new MemoryStream();
        await input.CopyToAsync(output);
        return output.ToArray();
    }

    private static TimelineInfo ReadTimeline(GlobalSystemMediaTransportControlsSession session, bool isPlaying)
    {
        var timeline = session.GetTimelineProperties();
        var start = timeline.StartTime;
        var end = timeline.EndTime;
        TimeSpan? duration = end > start ? end - start : null;
        TimeSpan? position = timeline.Position >= TimeSpan.Zero ? timeline.Position : null;
        if (duration is { } value && value <= TimeSpan.Zero)
        {
            duration = null;
        }

        DateTimeOffset updated = timeline.LastUpdatedTime.ToUniversalTime();
        if (updated == default)
        {
            updated = DateTimeOffset.UtcNow;
        }

        var rate = session.GetPlaybackInfo().PlaybackRate is { } playbackRate and > 0 ? playbackRate : 1;
        TimeSpan? minSeek = timeline.MinSeekTime >= TimeSpan.Zero ? timeline.MinSeekTime : null;
        TimeSpan? maxSeek = timeline.MaxSeekTime > timeline.MinSeekTime ? timeline.MaxSeekTime : null;
        return new TimelineInfo(position, duration, updated, rate, isPlaying, start, end > start ? end : null, minSeek, maxSeek);
    }

    private static CommandAvailability ReadCommands(GlobalSystemMediaTransportControlsSessionPlaybackInfo playback)
    {
        var controls = playback.Controls;
        var playPause = controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled;
        return new CommandAvailability(playPause, controls.IsNextEnabled, controls.IsPreviousEnabled, controls.IsPlaybackPositionEnabled);
    }

    private static MediaPlaybackStatus MapStatus(GlobalSystemMediaTransportControlsSessionPlaybackStatus status)
    {
        return status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackStatus.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackStatus.Paused,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackStatus.Paused,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaPlaybackStatus.Loading,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => MediaPlaybackStatus.Loading,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => MediaPlaybackStatus.Unavailable,
            _ => MediaPlaybackStatus.Unavailable
        };
    }

    private static SessionCandidate ToCandidate(GlobalSystemMediaTransportControlsSession session)
    {
        var status = session.GetPlaybackInfo().PlaybackStatus;
        var eligible = status is not GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed;
        return new SessionCandidate(GetId(session), session.SourceAppUserModelId, IsPlaying(session), eligible);
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession session) =>
        session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    private static string GetId(GlobalSystemMediaTransportControlsSession? session)
    {
        if (session is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(session.SourceAppUserModelId)
            ? "session:unknown"
            : session.SourceAppUserModelId;
    }

    private void Publish(MediaSnapshot snapshot)
    {
        void Raise() => SnapshotChanged?.Invoke(this, snapshot);
        if (_sync is not null)
        {
            _sync.Post(_ => Raise(), null);
        }
        else
        {
            Raise();
        }
    }
}
