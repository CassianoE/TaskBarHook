namespace TaskBarHook.Presentation;

public sealed class QueueFlyoutCoordinator
{
    public static readonly TimeSpan HoverOpenDelay = TimeSpan.FromMilliseconds(280);
    public static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(160);

    private bool _overTrigger;
    private bool _overFlyout;
    private DateTimeOffset? _openAt;
    private DateTimeOffset? _closeAt;

    public bool IsOpen { get; private set; }

    public bool WantsFocus { get; private set; }

    public bool PointerOverSet => _overTrigger || _overFlyout;

    public void SetTrigger(bool over, DateTimeOffset now) => SetOver(ref _overTrigger, over, now);

    public void SetFlyout(bool over, DateTimeOffset now) => SetOver(ref _overFlyout, over, now);

    public void Tick(DateTimeOffset now)
    {
        if (_openAt is { } openAt && now >= openAt)
        {
            _openAt = null;
            IsOpen = true;
            WantsFocus = false;
        }

        if (_closeAt is { } closeAt && now >= closeAt)
        {
            _closeAt = null;
            IsOpen = false;
            WantsFocus = false;
        }
    }

    public void OpenExplicit(bool stealFocus)
    {
        _openAt = null;
        _closeAt = null;
        IsOpen = true;
        WantsFocus = stealFocus;
    }

    public void ToggleExplicit()
    {
        if (IsOpen)
        {
            CloseImmediate();
            return;
        }

        OpenExplicit(stealFocus: true);
    }

    public bool CloseFromEscape()
    {
        if (!IsOpen && _openAt is null)
        {
            return false;
        }

        CloseImmediate();
        return true;
    }

    public void CloseImmediate()
    {
        _openAt = null;
        _closeAt = null;
        IsOpen = false;
        WantsFocus = false;
    }

    public TimeSpan? NextDue(DateTimeOffset now)
    {
        DateTimeOffset? due = null;
        if (_openAt is { } openAt && openAt > now)
        {
            due = openAt;
        }

        if (_closeAt is { } closeAt && closeAt > now && (due is null || closeAt < due))
        {
            due = closeAt;
        }

        return due is { } instant ? instant - now : null;
    }

    private void SetOver(ref bool field, bool over, DateTimeOffset now)
    {
        field = over;
        if (PointerOverSet)
        {
            _closeAt = null;
            if (!IsOpen && _openAt is null)
            {
                _openAt = now + HoverOpenDelay;
            }

            return;
        }

        _openAt = null;
        if (IsOpen)
        {
            _closeAt = now + CloseDelay;
        }
    }
}
