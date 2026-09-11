namespace TaskBarHook.Queue;

public sealed record SpotifyOptions(string? ClientId, Uri RedirectUri, string Scopes)
{
    public const string ClientIdVariable = "TASKBARHOOK_SPOTIFY_CLIENT_ID";
    public const string CurrentlyPlayingScope = "user-read-currently-playing";
    public const string PlaybackStateScope = "user-read-playback-state";
    public const string RequiredScopes = CurrentlyPlayingScope + " " + PlaybackStateScope;
    public const int LoopbackPort = 47823;

    public static Uri DefaultRedirect { get; } = new($"http://127.0.0.1:{LoopbackPort}/callback");

    public static SpotifyOptions FromEnvironment() =>
        new(Environment.GetEnvironmentVariable(ClientIdVariable)?.Trim(), DefaultRedirect, RequiredScopes);

    public bool HasClientId => !string.IsNullOrWhiteSpace(ClientId);
}
