using System.Net.Http;
using TaskBarHook.Logging;
using TaskBarHook.Media;
using TaskBarHook.Models;

namespace TaskBarHook.Queue;

public sealed class SpotifyPlaybackQueueService : IPlaybackQueueService
{
    public const string AccessKey = "spotify.access";
    public const string RefreshKey = "spotify.refresh";
    public const string ExpiryKey = "spotify.expiry";

    private readonly SpotifyOptions _options;
    private readonly ISecretStore _secrets;
    private readonly ISpotifyApiClient _api;
    private readonly ISpotifyInteractiveAuth _auth;
    private readonly IClock _clock;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SpotifyPlaybackQueueService(
        SpotifyOptions options,
        ISecretStore secrets,
        ISpotifyApiClient api,
        ISpotifyInteractiveAuth auth,
        IClock clock,
        IAppLogger logger)
    {
        _options = options;
        _secrets = secrets;
        _api = api;
        _auth = auth;
        _clock = clock;
        _logger = logger;
    }

    public PlaybackQueueSnapshot Current { get; private set; } = PlaybackQueueSnapshot.Idle;

    public event EventHandler<PlaybackQueueSnapshot>? QueueChanged;

    public bool CanAuthorize => _options.HasClientId;

    public async Task RefreshAsync(MediaSnapshot session, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await LoadAsync(session, cancellationToken);
            Publish(snapshot);
            if (snapshot.Items.Any(item => !string.IsNullOrWhiteSpace(item.ImageUrl) && item.ArtworkBytes is null))
            {
                _ = HydrateAfterAsync(snapshot, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AuthorizeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_options.HasClientId)
            {
                Publish(PlaybackQueueSnapshot.Missing(SpotifyOptions.ClientIdVariable));
                return;
            }

            var verifier = SpotifyPkce.CreateVerifier();
            var state = SpotifyPkce.CreateState();
            var authorize = SpotifyPkce.BuildAuthorizeUrl(
                _options.ClientId!,
                _options.RedirectUri,
                _options.Scopes,
                SpotifyPkce.ChallengeS256(verifier),
                state);
            var code = await _auth.GetAuthorizationCodeAsync(authorize, _options.RedirectUri, state, cancellationToken);
            var tokens = await _api.ExchangeCodeAsync(
                _options.ClientId!,
                code,
                _options.RedirectUri,
                verifier,
                _clock.UtcNow,
                cancellationToken);
            Store(tokens);
            _logger.Info("Spotify PKCE authorization stored.");
        }
        catch (Exception ex) when (ex is SpotifyAuthException or TimeoutException or HttpRequestException)
        {
            _logger.Warn($"Spotify authorization failed: {ex.Message}");
            Publish(PlaybackQueueSnapshot.Failed(ex.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        (_api as IDisposable)?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<PlaybackQueueSnapshot> LoadAsync(MediaSnapshot session, CancellationToken cancellationToken)
    {
        if (!session.HasLiveSession)
        {
            return PlaybackQueueSnapshot.Idle;
        }

        if (!SourceAppId.IsSpotify(session.SourceAppId))
        {
            return PlaybackQueueSnapshot.Unsupported();
        }

        if (!_options.HasClientId)
        {
            return PlaybackQueueSnapshot.Missing(SpotifyOptions.ClientIdVariable);
        }

        var access = _secrets.Get(AccessKey);
        var refresh = _secrets.Get(RefreshKey);
        if (string.IsNullOrWhiteSpace(access) && string.IsNullOrWhiteSpace(refresh))
        {
            return PlaybackQueueSnapshot.NeedsAuth();
        }

        try
        {
            access = await EnsureAccessAsync(access, refresh, cancellationToken);
            if (string.IsNullOrWhiteSpace(access))
            {
                return PlaybackQueueSnapshot.NeedsAuth();
            }

            var json = await _api.GetQueueJsonAsync(access, cancellationToken);
            var parsed = SpotifyQueueParser.Parse(json);
            var matches = SpotifyQueueParser.MatchesLocal(
                session.Track.Title,
                session.Track.Artist,
                parsed);
            var items = parsed.Queue;
            if (!matches)
            {
                return PlaybackQueueSnapshot.Mismatched(items, parsed.CurrentlyPlayingTitle);
            }

            return items.Count == 0
                ? PlaybackQueueSnapshot.Empty(PlaybackQueueSource.Spotify)
                : PlaybackQueueSnapshot.Ready(items, PlaybackQueueSource.Spotify, true, parsed.CurrentlyPlayingTitle);
        }
        catch (SpotifyAuthException)
        {
            return PlaybackQueueSnapshot.NeedsAuth();
        }
        catch (SpotifyRestrictedException ex)
        {
            return PlaybackQueueSnapshot.Failed(ex.Message);
        }
        catch (SpotifyRateLimitException ex)
        {
            return PlaybackQueueSnapshot.Failed(ex.Message);
        }
        catch (HttpRequestException)
        {
            return PlaybackQueueSnapshot.Disconnected();
        }
        catch (TaskCanceledException)
        {
            return PlaybackQueueSnapshot.Disconnected();
        }
        catch (Exception ex)
        {
            _logger.Error("Spotify queue request failed.", ex);
            return PlaybackQueueSnapshot.Failed("Não foi possível ler a fila");
        }
    }

    private async Task<string?> EnsureAccessAsync(string? access, string? refresh, CancellationToken cancellationToken)
    {
        var expiryRaw = _secrets.Get(ExpiryKey);
        var expired = !DateTimeOffset.TryParse(expiryRaw, out var expiry) || expiry <= _clock.UtcNow;
        if (!string.IsNullOrWhiteSpace(access) && !expired)
        {
            return access;
        }

        if (string.IsNullOrWhiteSpace(refresh) || !_options.HasClientId)
        {
            return null;
        }

        var tokens = await _api.RefreshAsync(_options.ClientId!, refresh, _clock.UtcNow, cancellationToken);
        Store(tokens);
        return tokens.AccessToken;
    }

    private async Task HydrateAfterAsync(PlaybackQueueSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            var items = await HydrateArtworkAsync(snapshot.Items, cancellationToken);
            if (!ReferenceEquals(Current, snapshot) && Current.Identity != snapshot.Identity)
            {
                return;
            }

            Publish(snapshot with { Items = items });
        }
        catch (Exception ex)
        {
            _logger.Warn($"Queue artwork skipped: {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<QueueTrack>> HydrateArtworkAsync(
        IReadOnlyList<QueueTrack> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var hydrated = new QueueTrack[items.Count];
        var cache = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (string.IsNullOrWhiteSpace(item.ImageUrl))
            {
                hydrated[i] = item;
                continue;
            }

            if (!cache.TryGetValue(item.ImageUrl, out var bytes))
            {
                bytes = await _api.GetBytesAsync(item.ImageUrl, cancellationToken);
                if (bytes is { Length: > 0 })
                {
                    cache[item.ImageUrl] = bytes;
                }
            }

            hydrated[i] = bytes is { Length: > 0 }
                ? item with { ArtworkBytes = bytes }
                : item;
        }

        return hydrated;
    }

    private void Store(SpotifyTokenSet tokens)
    {
        _secrets.Set(AccessKey, tokens.AccessToken);
        if (!string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            _secrets.Set(RefreshKey, tokens.RefreshToken);
        }

        _secrets.Set(ExpiryKey, tokens.ExpiresAt.ToString("O"));
    }

    private void Publish(PlaybackQueueSnapshot snapshot)
    {
        Current = snapshot;
        QueueChanged?.Invoke(this, snapshot);
    }
}
