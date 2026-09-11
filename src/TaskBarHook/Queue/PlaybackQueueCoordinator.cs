namespace TaskBarHook.Queue;

public sealed class PlaybackQueueCoordinator
{
    public static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(8);

    private DateTimeOffset _lastFetch = DateTimeOffset.MinValue;
    private bool _inFlight;
    private string? _sessionKey;

    public bool ShouldFetch(bool isOpen, DateTimeOffset now, string sessionKey, bool force)
    {
        if (!isOpen || _inFlight)
        {
            return false;
        }

        if (force)
        {
            return true;
        }

        if (!string.Equals(sessionKey, _sessionKey, StringComparison.Ordinal))
        {
            return true;
        }

        return now - _lastFetch >= MinRefreshInterval;
    }

    public void Begin(string sessionKey, DateTimeOffset now)
    {
        _inFlight = true;
        _sessionKey = sessionKey;
        _lastFetch = now;
    }

    public void End() => _inFlight = false;

    public static string SessionKey(string? sourceAppId, string? title, string? artist) =>
        $"{sourceAppId}|{title}|{artist}";
}
