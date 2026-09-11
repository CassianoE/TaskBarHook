using TaskBarHook.Queue;

namespace TaskBarHook.Tests;

public sealed class SpotifyAuthTests
{
    [Fact]
    public void Pkce_forms_never_include_a_client_secret()
    {
        var redirect = SpotifyOptions.DefaultRedirect;
        var codeForm = SpotifyPkce.AuthorizationCodeForm("client", "code", redirect, "verifier");
        var refreshForm = SpotifyPkce.RefreshForm("client", "refresh");
        Assert.False(SpotifyPkce.ContainsClientSecret(codeForm));
        Assert.False(SpotifyPkce.ContainsClientSecret(refreshForm));
        Assert.DoesNotContain(codeForm, pair => pair.Key.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(codeForm, pair => pair.Key == "code_verifier");
        Assert.Contains(refreshForm, pair => pair.Key == "client_id");
    }

    [Fact]
    public void Authorize_url_uses_loopback_and_least_privilege_scopes()
    {
        var url = SpotifyPkce.BuildAuthorizeUrl(
            "client",
            SpotifyOptions.DefaultRedirect,
            SpotifyOptions.RequiredScopes,
            "challenge",
            "state");
        Assert.Contains("127.0.0.1", url.ToString(), StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", url.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("client_secret", url.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(SpotifyOptions.CurrentlyPlayingScope, url.Query);
        Assert.Contains(SpotifyOptions.PlaybackStateScope, url.Query);
    }

    [Fact]
    public void Missing_client_id_is_named_exactly()
    {
        var options = new SpotifyOptions(null, SpotifyOptions.DefaultRedirect, SpotifyOptions.RequiredScopes);
        Assert.False(options.HasClientId);
        Assert.Equal("TASKBARHOOK_SPOTIFY_CLIENT_ID", SpotifyOptions.ClientIdVariable);
    }
}
