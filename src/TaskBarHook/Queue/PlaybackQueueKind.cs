namespace TaskBarHook.Queue;

public enum PlaybackQueueKind
{
    Idle,
    Loading,
    Ready,
    Empty,
    UnsupportedPlayer,
    MissingCredential,
    NeedsAuthorization,
    Disconnected,
    Error,
    MismatchedSession
}

public enum PlaybackQueueSource
{
    None,
    Simulated,
    Spotify
}
