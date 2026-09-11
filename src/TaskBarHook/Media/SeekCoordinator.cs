using TaskBarHook.Models;

namespace TaskBarHook.Media;

public sealed record SeekRequest(
    int Token,
    SeekIdentity Identity,
    long PositionTicks,
    TimeSpan SessionPosition,
    TimeSpan DisplayPosition);

public sealed record SeekPresentation(
    double Progress,
    TimeSpan? DisplayPosition,
    TimeSpan? DisplayDuration,
    bool CanSeek,
    bool ShowThumb,
    bool IsPreviewing,
    bool IsPending,
    string? HintText,
    string? FailureText);

public sealed class SeekCoordinator
{
    public static readonly TimeSpan DefaultReconcileTimeout = TimeSpan.FromSeconds(2.5);
    public static readonly TimeSpan KeyboardStep = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan FailureHintDuration = TimeSpan.FromSeconds(2.5);

    private readonly TimeSpan _reconcileTimeout;
    private readonly TimeSpan _failureHintDuration;

    private SeekIdentity _identity = new("", 0);
    private SeekRange _range = SeekRange.None;
    private TimeSpan? _liveDisplay;
    private TimeSpan? _heldDisplay;
    private bool _previewing;
    private PendingSeek? _pending;
    private SeekRequest? _outbound;
    private int _token;
    private string? _failure;
    private DateTimeOffset? _failureUntil;

    public SeekCoordinator(TimeSpan? reconcileTimeout = null, TimeSpan? failureHintDuration = null)
    {
        _reconcileTimeout = reconcileTimeout ?? DefaultReconcileTimeout;
        _failureHintDuration = failureHintDuration ?? FailureHintDuration;
    }

    public bool IsPreviewing => _previewing;

    public bool IsPending => _pending is not null;

    public void Sync(MediaSnapshot snapshot, TimeSpan? interpolatedSessionPosition, DateTimeOffset now)
    {
        ClearExpiredFailure(now);
        var identity = SeekIdentity.From(snapshot);
        var range = SeekMapping.From(snapshot.Timeline, snapshot.Commands.Seek);
        if (!_identity.Equals(identity) && (!_identity.IsEmpty || !identity.IsEmpty))
        {
            Cancel();
        }

        _identity = identity;
        _range = range;
        if (_previewing && !range.CanSeek)
        {
            Cancel();
        }

        var live = interpolatedSessionPosition is { } session
            ? SeekMapping.ToDisplay(session, range)
            : (TimeSpan?)null;

        if (_previewing)
        {
            _liveDisplay = live;
            return;
        }

        if (_pending is { } pending)
        {
            if (now - pending.StartedUtc >= _reconcileTimeout)
            {
                Fail("Não foi possível alterar a posição.", now);
                _liveDisplay = live;
                return;
            }

            if (ShouldIgnoreLive(live, pending, snapshot.Timeline))
            {
                _liveDisplay = live;
                return;
            }

            _pending = null;
            _heldDisplay = null;
            _liveDisplay = live;
            return;
        }

        _liveDisplay = live;
    }

    public SeekPresentation Present(DateTimeOffset now)
    {
        ClearExpiredFailure(now);
        var display = ShownDisplay();
        var duration = _range.HasDuration ? _range.DisplayDuration : (TimeSpan?)null;
        var fraction = SeekMapping.ToFraction(display, duration) ?? 0;
        var interactive = _range.CanSeek && !_identity.IsEmpty;
        return new SeekPresentation(
            fraction,
            display,
            duration,
            interactive,
            interactive,
            _previewing,
            _pending is not null,
            _previewing || _pending is not null ? Format(display) : null,
            _failure);
    }

    public bool BeginPreview(SeekIdentity identity, SeekRange range, TimeSpan displayPosition)
    {
        if (!range.CanSeek || identity.IsEmpty)
        {
            return false;
        }

        if (!_identity.Equals(identity) && !_identity.IsEmpty)
        {
            Cancel();
        }

        _identity = identity;
        _range = range;
        _previewing = true;
        _pending = null;
        _heldDisplay = ClampDisplay(displayPosition);
        _failure = null;
        _failureUntil = null;
        return true;
    }

    public void UpdatePreview(TimeSpan displayPosition)
    {
        if (!_previewing || !_range.CanSeek)
        {
            return;
        }

        _heldDisplay = ClampDisplay(displayPosition);
    }

    public SeekRequest? Commit(DateTimeOffset now)
    {
        if (!_previewing || !_range.CanSeek || _identity.IsEmpty)
        {
            _previewing = false;
            return null;
        }

        var display = ClampDisplay(_heldDisplay ?? _liveDisplay ?? TimeSpan.Zero);
        var session = SeekMapping.ToSession(display, _range);
        _previewing = false;
        _heldDisplay = display;
        var request = new SeekRequest(
            ++_token,
            _identity,
            SeekMapping.ToTicks(session),
            session,
            display);
        _pending = new PendingSeek(request.Token, request.Identity, display, _liveDisplay ?? display, now);
        _outbound = request;
        return request;
    }

    public void Cancel()
    {
        _previewing = false;
        _pending = null;
        _heldDisplay = null;
        _outbound = null;
    }

    public bool BeginOrContinueKeyboard(SeekIdentity identity, SeekRange range, TimeSpan currentDisplay, TimeSpan delta)
    {
        if (!_previewing)
        {
            if (!BeginPreview(identity, range, currentDisplay))
            {
                return false;
            }
        }

        UpdatePreview(ClampDisplay((_heldDisplay ?? currentDisplay) + delta));
        return true;
    }

    public SeekRequest? EndKeyboard(DateTimeOffset now) => Commit(now);

    public void CompleteCommand(int token, bool success, DateTimeOffset now)
    {
        if (_pending is not { } pending || pending.Token != token)
        {
            return;
        }

        if (success)
        {
            return;
        }

        Fail("Não foi possível alterar a posição.", now);
    }

    public SeekRequest? TakeOutbound()
    {
        var request = _outbound;
        _outbound = null;
        return request;
    }

    private TimeSpan? ShownDisplay() =>
        _previewing || _pending is not null ? _heldDisplay ?? _liveDisplay : _liveDisplay;

    private TimeSpan ClampDisplay(TimeSpan display)
    {
        var session = SeekMapping.ToSession(display, _range);
        return SeekMapping.ToDisplay(session, _range);
    }

    private static bool ShouldIgnoreLive(TimeSpan? live, PendingSeek pending, TimelineInfo timeline)
    {
        if (timeline.LastUpdatedUtc is { } updated && updated < pending.StartedUtc)
        {
            return true;
        }

        if (live is null)
        {
            return true;
        }

        var toTarget = (live.Value - pending.DisplayPosition).Duration();
        if (toTarget <= TimeSpan.FromSeconds(1.25))
        {
            return false;
        }

        var toOrigin = (live.Value - pending.OriginDisplay).Duration();
        return toOrigin <= TimeSpan.FromSeconds(1.5);
    }

    private void Fail(string message, DateTimeOffset now)
    {
        _pending = null;
        _heldDisplay = null;
        _previewing = false;
        _failure = message;
        _failureUntil = now + _failureHintDuration;
    }

    private void ClearExpiredFailure(DateTimeOffset now)
    {
        if (_failureUntil is { } until && now >= until)
        {
            _failure = null;
            _failureUntil = null;
        }
    }

    private static string? Format(TimeSpan? value)
    {
        if (value is null)
        {
            return null;
        }

        var display = value.Value < TimeSpan.Zero ? TimeSpan.Zero : value.Value;
        return display.TotalHours >= 1
            ? display.ToString(@"h\:mm\:ss")
            : display.ToString(@"m\:ss");
    }

    private sealed record PendingSeek(
        int Token,
        SeekIdentity Identity,
        TimeSpan DisplayPosition,
        TimeSpan OriginDisplay,
        DateTimeOffset StartedUtc);
}
