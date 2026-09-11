using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskBarHook.Desktop;
using TaskBarHook.Logging;
using TaskBarHook.Media;
using TaskBarHook.Models;

namespace TaskBarHook.Presentation;

public partial class CapsuleViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan NoMediaGrace = TimeSpan.FromMilliseconds(900);

    private readonly IMediaSessionService _media;
    private readonly IClock _clock;
    private readonly IAppLogger _logger;
    private readonly SeekCoordinator _seek = new();
    private readonly DispatcherTimer _progressTimer;
    private readonly DispatcherTimer _graceTimer;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly List<double> _seekTrace = new();

    private static readonly bool SeekTraceEnabled = string.Equals(
        Environment.GetEnvironmentVariable("TASKBARHOOK_SEEK_TRACE"), "1", StringComparison.Ordinal);

    private MediaSnapshot _snapshot = MediaSnapshot.Empty;
    private TimelineInfo _timeline = TimelineInfo.Unknown;
    private bool _fullscreen;
    private bool _withinGrace;
    private bool _hasTaskbarSlot;
    private bool _occupancyKnown;
    private bool _layoutSupported = true;
    private bool _shellConflict;
    private bool _disposed;
    private bool _seekSending;

    public CapsuleViewModel(IMediaSessionService media, IClock clock, IAppLogger logger)
    {
        _media = media;
        _clock = clock;
        _logger = logger;
        _media.SnapshotChanged += OnSnapshotChanged;

        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _progressTimer.Tick += (_, _) => UpdateProgress();

        _graceTimer = new DispatcherTimer { Interval = NoMediaGrace };
        _graceTimer.Tick += (_, _) => OnGraceElapsed();
    }

    public event EventHandler? ActivateRequested;

    public event EventHandler? VisibilityChanged;

    [ObservableProperty] private string _title = "Nenhuma mídia";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private bool _hasArtist;
    [ObservableProperty] private ImageSource? _artwork;
    [ObservableProperty] private bool _hasArtwork;
    [ObservableProperty] private bool _hasPresentation;
    [ObservableProperty] private bool _hasLiveSession;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isEmptyState;
    [ObservableProperty] private bool _canPlayPause;
    [ObservableProperty] private bool _canSkipNext;
    [ObservableProperty] private bool _canSkipPrevious;
    [ObservableProperty] private bool _canSeek;
    [ObservableProperty] private bool _showSeekThumb;
    [ObservableProperty] private bool _isSeekPreviewing;
    [ObservableProperty] private string? _seekHintText;
    [ObservableProperty] private string? _seekFailureText;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _hasTimeline;
    [ObservableProperty] private string _positionText = "";
    [ObservableProperty] private string _durationText = "";
    [ObservableProperty] private string _trackKey = "";
    [ObservableProperty] private bool _isCapsuleVisible;
    [ObservableProperty] private bool _showCompact;
    [ObservableProperty] private bool _showPanel;
    [ObservableProperty] private CompactFit _compactFit = CompactFit.Hidden;
    [ObservableProperty] private HideReason _hideReason = HideReason.NoMedia;
    [ObservableProperty] private MediaPlaybackStatus _status = MediaPlaybackStatus.None;
    [ObservableProperty] private string _playPauseGlyph = "\uE768";
    [ObservableProperty] private string _playPauseLabel = "Reproduzir";

    public bool UserHidden { get; private set; }

    public bool UserRequestedEmptyPanel { get; private set; }

    public void ApplySnapshot(MediaSnapshot snapshot)
    {
        Status = snapshot.Status;
        var live = snapshot.HasLiveSession && snapshot.Status is not MediaPlaybackStatus.Unavailable;
        if (live)
        {
            _withinGrace = false;
            _graceTimer.Stop();
            HasLiveSession = true;
            UserRequestedEmptyPanel = false;
            try
            {
                ApplyPresentation(snapshot);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to apply media presentation.", ex);
            }
        }
        else if (HasLiveSession)
        {
            HasLiveSession = false;
            CanPlayPause = false;
            CanSkipNext = false;
            CanSkipPrevious = false;
            CanSeek = false;
            ShowSeekThumb = false;
            IsPlaying = false;
            _seek.Cancel();
            _withinGrace = true;
            _graceTimer.Stop();
            _graceTimer.Start();
        }
        else
        {
            HasLiveSession = false;
        }

        RecalculateVisibility();
    }

    public void SetFullscreen(bool fullscreen)
    {
        if (_fullscreen == fullscreen)
        {
            return;
        }

        if (fullscreen)
        {
            IsExpanded = false;
        }

        _fullscreen = fullscreen;
        RecalculateVisibility();
    }

    public void ShowPanelFromTray()
    {
        if (_fullscreen)
        {
            return;
        }

        UserHidden = false;
        UserRequestedEmptyPanel = !HasLiveSession;
        IsExpanded = true;
        RecalculateVisibility();
        ActivateRequested?.Invoke(this, EventArgs.Empty);
        _logger.Info("Panel opened from tray.");
    }

    public void ToggleUserHidden()
    {
        UserHidden = !UserHidden;
        if (UserHidden)
        {
            IsExpanded = false;
            UserRequestedEmptyPanel = false;
            _logger.Info("Capsule hidden by user.");
        }
        else
        {
            _logger.Info("Capsule shown by user.");
        }

        RecalculateVisibility();
    }

    public void Collapse(bool restoreForeground)
    {
        if (!IsExpanded)
        {
            return;
        }

        CancelSeek();
        IsExpanded = false;
        UserRequestedEmptyPanel = false;
        RecalculateVisibility();
        if (restoreForeground)
        {
            RestoreForegroundRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? RestoreForegroundRequested;

    [RelayCommand]
    private void ToggleExpanded()
    {
        if (!HasLiveSession && !UserRequestedEmptyPanel && !HasPresentation)
        {
            return;
        }

        if (_fullscreen && !IsExpanded)
        {
            return;
        }

        IsExpanded = !IsExpanded;
        _logger.Info(IsExpanded ? "Capsule expanded." : "Capsule collapsed.");
        if (IsExpanded)
        {
            ActivateRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            CancelSeek();
            UserRequestedEmptyPanel = false;
            RecalculateVisibility();
        }
    }

    public void UpdateDesktopState(
        bool hasTaskbarSlot,
        bool occupancyKnown,
        bool layoutSupported,
        bool shellConflict,
        CompactFit fit)
    {
        _hasTaskbarSlot = hasTaskbarSlot;
        _occupancyKnown = occupancyKnown;
        _layoutSupported = layoutSupported;
        _shellConflict = shellConflict;
        CompactFit = fit;
        RecalculateVisibility();
    }

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private async Task PlayPauseAsync()
    {
        if (_seek.IsPreviewing)
        {
            CancelSeek();
        }

        await RunCommandAsync(_media.PlayPauseAsync);
    }

    [RelayCommand(CanExecute = nameof(CanSkipNext))]
    private async Task SkipNextAsync()
    {
        if (_seek.IsPreviewing)
        {
            CancelSeek();
        }

        await RunCommandAsync(_media.SkipNextAsync);
    }

    [RelayCommand(CanExecute = nameof(CanSkipPrevious))]
    private async Task SkipPreviousAsync()
    {
        if (_seek.IsPreviewing)
        {
            CancelSeek();
        }

        await RunCommandAsync(_media.SkipPreviousAsync);
    }

    public bool BeginSeekPreview(double fraction)
    {
        var range = CurrentRange();
        var identity = SeekIdentity.From(_snapshot);
        if (!_seek.BeginPreview(identity, range, SeekMapping.FromFraction(fraction, range)))
        {
            return false;
        }

        TraceSeekSample(fraction, starting: true);
        UpdateProgress();
        return true;
    }

    public void UpdateSeekPreview(double fraction)
    {
        _seek.UpdatePreview(SeekMapping.FromFraction(fraction, CurrentRange()));
        TraceSeekSample(fraction, starting: false);
        UpdateProgress();
    }

    public void CommitSeek()
    {
        var request = _seek.Commit(_clock.UtcNow);
        if (request is not null)
        {
            _logger.Info($"Seek requested ticks={request.PositionTicks} display={request.DisplayPosition}.");
        }

        FlushSeekTrace(canceled: false);
        UpdateProgress();
        _ = PumpSeekAsync();
    }

    public void CancelSeek()
    {
        if (!_seek.IsPreviewing && !_seek.IsPending && SeekFailureText is null)
        {
            return;
        }

        _seek.Cancel();
        FlushSeekTrace(canceled: true);
        UpdateProgress();
    }

    public void NudgeSeek(int direction)
    {
        var range = CurrentRange();
        var identity = SeekIdentity.From(_snapshot);
        var current = _seek.Present(_clock.UtcNow).DisplayPosition ?? TimeSpan.Zero;
        var step = TimeSpan.FromTicks(SeekCoordinator.KeyboardStep.Ticks * Math.Sign(direction == 0 ? 1 : direction));
        if (direction < 0)
        {
            step = -SeekCoordinator.KeyboardStep;
        }
        else if (direction > 0)
        {
            step = SeekCoordinator.KeyboardStep;
        }

        if (_seek.BeginOrContinueKeyboard(identity, range, current, step))
        {
            UpdateProgress();
        }
    }

    public void EndSeekKeyboard()
    {
        var request = _seek.EndKeyboard(_clock.UtcNow);
        if (request is not null)
        {
            _logger.Info($"Seek requested ticks={request.PositionTicks} display={request.DisplayPosition}.");
        }

        UpdateProgress();
        _ = PumpSeekAsync();
    }

    partial void OnCanPlayPauseChanged(bool value) => PlayPauseCommand.NotifyCanExecuteChanged();

    partial void OnCanSkipNextChanged(bool value) => SkipNextCommand.NotifyCanExecuteChanged();

    partial void OnCanSkipPreviousChanged(bool value) => SkipPreviousCommand.NotifyCanExecuteChanged();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _media.SnapshotChanged -= OnSnapshotChanged;
        _progressTimer.Stop();
        _graceTimer.Stop();
        _commandGate.Dispose();
    }

    private async Task RunCommandAsync(Func<Task<bool>> command)
    {
        await _commandGate.WaitAsync();
        try
        {
            await command();
        }
        catch (Exception ex)
        {
            _logger.Error("Presentation command failed.", ex);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async Task PumpSeekAsync()
    {
        if (_seekSending)
        {
            return;
        }

        _seekSending = true;
        try
        {
            while (_seek.TakeOutbound() is { } request)
            {
                var ok = false;
                try
                {
                    ok = await _media.SeekAsync(request.PositionTicks);
                }
                catch (Exception ex)
                {
                    _logger.Error("Seek command failed.", ex);
                }

                _seek.CompleteCommand(request.Token, ok, _clock.UtcNow);
                UpdateProgress();
            }
        }
        finally
        {
            _seekSending = false;
        }
    }

    private void OnSnapshotChanged(object? sender, MediaSnapshot snapshot)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher &&
            !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ApplySnapshot(snapshot));
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplyPresentation(MediaSnapshot snapshot)
    {
        var key = $"{snapshot.SessionId}|{snapshot.Track.ArtworkKey}|{snapshot.Track.Title}|{snapshot.Track.Artist}";
        if (!string.Equals(key, TrackKey, StringComparison.Ordinal))
        {
            TrackKey = key;
        }

        Title = string.IsNullOrWhiteSpace(snapshot.Track.Title) ? "Sem título" : snapshot.Track.Title;
        Artist = snapshot.Track.Artist ?? "";
        HasArtist = !string.IsNullOrWhiteSpace(snapshot.Track.Artist);
        HasPresentation = true;
        IsPlaying = snapshot.Status == MediaPlaybackStatus.Playing;
        CanPlayPause = snapshot.Commands.PlayPause;
        CanSkipNext = snapshot.Commands.Next;
        CanSkipPrevious = snapshot.Commands.Previous;
        PlayPauseGlyph = IsPlaying ? "\uE769" : "\uE768";
        PlayPauseLabel = IsPlaying ? "Pausar" : "Reproduzir";
        UpdateArtwork(snapshot.Track);
        _snapshot = snapshot;
        _timeline = snapshot.Timeline;
        UpdateProgress();
    }

    private void ClearPresentation()
    {
        Title = "Nenhuma mídia";
        Artist = "";
        HasArtist = false;
        Artwork = null;
        HasArtwork = false;
        HasPresentation = false;
        TrackKey = "";
        IsPlaying = false;
        CanPlayPause = false;
        CanSkipNext = false;
        CanSkipPrevious = false;
        CanSeek = false;
        ShowSeekThumb = false;
        IsSeekPreviewing = false;
        SeekHintText = null;
        SeekFailureText = null;
        PlayPauseGlyph = "\uE768";
        PlayPauseLabel = "Reproduzir";
        HasTimeline = false;
        Progress = 0;
        PositionText = "";
        DurationText = "";
        _timeline = TimelineInfo.Unknown;
        _snapshot = MediaSnapshot.Empty;
        _seek.Cancel();
    }

    private void UpdateArtwork(TrackInfo track)
    {
        if (track.ArtworkBytes is not { Length: > 0 })
        {
            Artwork = null;
            HasArtwork = false;
            return;
        }

        try
        {
            using var stream = new MemoryStream(track.ArtworkBytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            Artwork = image;
            HasArtwork = true;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to decode artwork.", ex);
            Artwork = null;
            HasArtwork = false;
        }
    }

    private void UpdateProgress()
    {
        try
        {
            var now = _clock.UtcNow;
            var interpolated = ProgressInterpolator.Interpolate(_timeline, now);
            _seek.Sync(_snapshot, interpolated, now);
            var presentation = _seek.Present(now);
            HasTimeline = presentation.DisplayPosition is not null || presentation.DisplayDuration is not null;
            Progress = presentation.Progress;
            PositionText = presentation.DisplayPosition is null ? "--:--" : Format(presentation.DisplayPosition.Value);
            DurationText = presentation.DisplayDuration is null ? "--:--" : Format(presentation.DisplayDuration.Value);
            CanSeek = presentation.CanSeek;
            ShowSeekThumb = presentation.ShowThumb;
            IsSeekPreviewing = presentation.IsPreviewing;
            SeekHintText = presentation.HintText;
            SeekFailureText = presentation.FailureText;
            SyncProgressTimer();
        }
        catch (Exception ex)
        {
            _logger.Error("Progress update failed.", ex);
        }
    }

    private SeekRange CurrentRange() => SeekMapping.From(_timeline, _snapshot.Commands.Seek);

    private void TraceSeekSample(double fraction, bool starting)
    {
        if (!SeekTraceEnabled || !_seek.IsPreviewing)
        {
            return;
        }

        if (starting)
        {
            _seekTrace.Clear();
        }

        if (_seekTrace.Count < 200)
        {
            _seekTrace.Add(Math.Clamp(fraction, 0, 1));
        }
    }

    private void FlushSeekTrace(bool canceled)
    {
        if (!SeekTraceEnabled || _seekTrace.Count == 0)
        {
            _seekTrace.Clear();
            return;
        }

        var min = _seekTrace.Min();
        var max = _seekTrace.Max();
        var path = string.Join(",", _seekTrace.Select(f => f.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));
        _logger.Info($"Seek gesture {(canceled ? "canceled" : "commit")} samples={_seekTrace.Count} min={min:F3} max={max:F3} path={path}.");
        _seekTrace.Clear();
    }

    private static string Format(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"m\:ss");
    }

    private void OnGraceElapsed()
    {
        _graceTimer.Stop();
        _withinGrace = false;
        if (!HasLiveSession)
        {
            ClearPresentation();
        }

        RecalculateVisibility();
    }

    private void RecalculateVisibility()
    {
        var decision = VisibilityPolicy.Evaluate(new VisibilityInput(
            UserHidden,
            HasLiveSession,
            _fullscreen,
            UserRequestedEmptyPanel,
            _withinGrace,
            IsExpanded,
            _hasTaskbarSlot,
            _occupancyKnown,
            _layoutSupported,
            _shellConflict));

        var compactChanged = ShowCompact != decision.ShowCompact;
        var panelChanged = ShowPanel != decision.ShowPanel;
        var reasonChanged = HideReason != decision.Reason;
        ShowCompact = decision.ShowCompact;
        ShowPanel = decision.ShowPanel;
        IsCapsuleVisible = decision.ShowCompact || decision.ShowPanel;
        HideReason = decision.Reason;
        IsEmptyState = !HasLiveSession && decision.ShowPanel && UserRequestedEmptyPanel;
        if (!ShowPanel && _seek.IsPreviewing)
        {
            _seek.Cancel();
        }

        SyncProgressTimer();

        if (compactChanged || panelChanged || reasonChanged)
        {
            _logger.Info($"Compact={ShowCompact} panel={ShowPanel} reason={HideReason} slot={_hasTaskbarSlot} fit={CompactFit} live={HasLiveSession} occ={_occupancyKnown}.");
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SyncProgressTimer()
    {
        var animations = AreAnimationsEnabled();
        var interpolate = ShowPanel && IsPlaying && _timeline.HasUsablePosition && !_seek.IsPreviewing && !_seek.IsPending;
        var watchPending = ShowPanel && _seek.IsPending;
        var shouldRun = interpolate || watchPending;
        _progressTimer.Interval = interpolate && animations
            ? TimeSpan.FromMilliseconds(33)
            : TimeSpan.FromMilliseconds(250);
        if (shouldRun && !_progressTimer.IsEnabled)
        {
            _progressTimer.Start();
        }
        else if (!shouldRun && _progressTimer.IsEnabled)
        {
            _progressTimer.Stop();
        }
    }

    private static bool AreAnimationsEnabled()
    {
        try
        {
            if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
            {
                return false;
            }
        }
        catch
        {
            // Fall back to WPF.
        }

        return System.Windows.SystemParameters.ClientAreaAnimation;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value)
        {
            RecalculateVisibility();
        }
    }
}
