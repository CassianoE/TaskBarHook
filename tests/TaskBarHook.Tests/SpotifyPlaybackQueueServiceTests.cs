using System.Net.Http;
using TaskBarHook.Logging;
using TaskBarHook.Media;
using TaskBarHook.Models;
using TaskBarHook.Queue;

namespace TaskBarHook.Tests;

public sealed class SpotifyPlaybackQueueServiceTests
{
    [Fact]
    public async Task Missing_client_id_is_reported_without_inventing_tracks()
    {
        var service = Create(clientId: null, session: SpotifySession());
        await service.RefreshAsync(SpotifySession());
        Assert.Equal(PlaybackQueueKind.MissingCredential, service.Current.Kind);
        Assert.Equal(SpotifyOptions.ClientIdVariable, service.Current.MissingCredential);
        Assert.Empty(service.Current.Items);
    }

    [Fact]
    public async Task Unsupported_player_does_not_call_spotify()
    {
        var api = new FakeApi();
        var service = Create(clientId: "abc", api: api, session: BraveSession());
        await service.RefreshAsync(BraveSession());
        Assert.Equal(PlaybackQueueKind.UnsupportedPlayer, service.Current.Kind);
        Assert.Equal(0, api.QueueCalls);
        Assert.Empty(service.Current.Items);
    }

    [Fact]
    public async Task Needs_authorization_when_tokens_are_missing()
    {
        var service = Create(clientId: "abc", session: SpotifySession());
        await service.RefreshAsync(SpotifySession());
        Assert.Equal(PlaybackQueueKind.NeedsAuthorization, service.Current.Kind);
        Assert.True(service.CanAuthorize);
    }

    [Fact]
    public async Task Mismatch_is_not_presented_as_this_session()
    {
        var api = new FakeApi
        {
            QueueJson = """{ "currently_playing": { "type":"track", "id":"x", "name":"Outra", "artists":[{"name":"Z"}] }, "queue": [{ "type":"track", "id":"q", "name":"Depois" }] }"""
        };
        var secrets = new MemorySecretStore();
        secrets.Set(SpotifyPlaybackQueueService.AccessKey, "token");
        secrets.Set(SpotifyPlaybackQueueService.ExpiryKey, DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var service = Create(clientId: "abc", api: api, secrets: secrets, session: SpotifySession());
        await service.RefreshAsync(SpotifySession());
        Assert.Equal(PlaybackQueueKind.MismatchedSession, service.Current.Kind);
        Assert.False(service.Current.MatchesLocalSession);
        Assert.Equal("Depois", service.Current.Items[0].Title);
    }

    [Fact]
    public async Task Matching_queue_preserves_order()
    {
        var api = new FakeApi
        {
            QueueJson = """{ "currently_playing": { "type":"track", "id":"c", "name":"Song", "artists":[{"name":"Artist"}] }, "queue": [{ "type":"track", "id":"1", "name":"A" }, { "type":"track", "id":"2", "name":"B" }] }"""
        };
        var secrets = new MemorySecretStore();
        secrets.Set(SpotifyPlaybackQueueService.AccessKey, "token");
        secrets.Set(SpotifyPlaybackQueueService.ExpiryKey, DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var service = Create(clientId: "abc", api: api, secrets: secrets, session: SpotifySession());
        await service.RefreshAsync(SpotifySession());
        Assert.Equal(PlaybackQueueKind.Ready, service.Current.Kind);
        Assert.True(service.Current.MatchesLocalSession);
        Assert.Equal(new[] { "A", "B" }, service.Current.Items.Select(item => item.Title).ToArray());
        Assert.Equal(1, api.QueueCalls);
    }

    [Fact]
    public async Task Network_failure_is_disconnected()
    {
        var api = new FakeApi { ThrowNetwork = true };
        var secrets = new MemorySecretStore();
        secrets.Set(SpotifyPlaybackQueueService.AccessKey, "token");
        secrets.Set(SpotifyPlaybackQueueService.ExpiryKey, DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var service = Create(clientId: "abc", api: api, secrets: secrets, session: SpotifySession());
        await service.RefreshAsync(SpotifySession());
        Assert.Equal(PlaybackQueueKind.Disconnected, service.Current.Kind);
    }

    private static SpotifyPlaybackQueueService Create(
        string? clientId,
        MediaSnapshot session,
        FakeApi? api = null,
        MemorySecretStore? secrets = null)
    {
        return new SpotifyPlaybackQueueService(
            new SpotifyOptions(clientId, SpotifyOptions.DefaultRedirect, SpotifyOptions.RequiredScopes),
            secrets ?? new MemorySecretStore(),
            api ?? new FakeApi(),
            new FakeAuth(),
            new SystemClock(),
            new NullLogger());
    }

    private static MediaSnapshot SpotifySession() => new(
        MediaPlaybackStatus.Playing,
        new TrackInfo("Song", "Artist", null, null, "k"),
        TimelineInfo.Unknown,
        new CommandAvailability(true, true, true, true),
        "s",
        "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify");

    private static MediaSnapshot BraveSession() => new(
        MediaPlaybackStatus.Playing,
        new TrackInfo("Video", "Channel", null, null, "k"),
        TimelineInfo.Unknown,
        new CommandAvailability(true, true, true, true),
        "s",
        "Brave");

    private sealed class FakeApi : ISpotifyApiClient
    {
        public string QueueJson { get; set; } = "{}";
        public bool ThrowNetwork { get; set; }
        public int QueueCalls { get; private set; }

        public Task<SpotifyTokenSet> ExchangeCodeAsync(
            string clientId, string code, Uri redirectUri, string verifier, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(new SpotifyTokenSet("a", "r", now.AddHours(1)));

        public Task<SpotifyTokenSet> RefreshAsync(
            string clientId, string refreshToken, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(new SpotifyTokenSet("a", refreshToken, now.AddHours(1)));

        public Task<string> GetQueueJsonAsync(string accessToken, CancellationToken cancellationToken)
        {
            QueueCalls++;
            if (ThrowNetwork)
            {
                throw new HttpRequestException("offline");
            }

            return Task.FromResult(QueueJson);
        }

        public Task<byte[]?> GetBytesAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult<byte[]?>(null);
    }

    private sealed class FakeAuth : ISpotifyInteractiveAuth
    {
        public Task<string> GetAuthorizationCodeAsync(
            Uri authorizeUrl, Uri redirectUri, string expectedState, CancellationToken cancellationToken) =>
            Task.FromResult("code");
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
