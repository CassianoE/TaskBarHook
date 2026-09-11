namespace TaskBarHook.Queue;

public interface ISpotifyApiClient
{
    Task<SpotifyTokenSet> ExchangeCodeAsync(
        string clientId,
        string code,
        Uri redirectUri,
        string verifier,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<SpotifyTokenSet> RefreshAsync(
        string clientId,
        string refreshToken,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<string> GetQueueJsonAsync(string accessToken, CancellationToken cancellationToken);

    Task<byte[]?> GetBytesAsync(string url, CancellationToken cancellationToken);
}

public interface ISpotifyInteractiveAuth
{
    Task<string> GetAuthorizationCodeAsync(
        Uri authorizeUrl,
        Uri redirectUri,
        string expectedState,
        CancellationToken cancellationToken);
}
