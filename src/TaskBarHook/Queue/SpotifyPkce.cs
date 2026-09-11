using System.Security.Cryptography;
using System.Text;

namespace TaskBarHook.Queue;

public static class SpotifyPkce
{
    public static string CreateVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url(bytes);
    }

    public static string ChallengeS256(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url(hash);
    }

    public static string CreateState()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Base64Url(bytes);
    }

    public static Uri BuildAuthorizeUrl(
        string clientId,
        Uri redirectUri,
        string scopes,
        string challenge,
        string state)
    {
        var query = new StringBuilder()
            .Append("client_id=").Append(Uri.EscapeDataString(clientId))
            .Append("&response_type=code")
            .Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectUri.ToString()))
            .Append("&scope=").Append(Uri.EscapeDataString(scopes))
            .Append("&code_challenge_method=S256")
            .Append("&code_challenge=").Append(Uri.EscapeDataString(challenge))
            .Append("&state=").Append(Uri.EscapeDataString(state));
        return new Uri("https://accounts.spotify.com/authorize?" + query);
    }

    public static IReadOnlyList<KeyValuePair<string, string>> AuthorizationCodeForm(
        string clientId,
        string code,
        Uri redirectUri,
        string verifier) =>
    [
        new("grant_type", "authorization_code"),
        new("code", code),
        new("redirect_uri", redirectUri.ToString()),
        new("client_id", clientId),
        new("code_verifier", verifier)
    ];

    public static IReadOnlyList<KeyValuePair<string, string>> RefreshForm(string clientId, string refreshToken) =>
    [
        new("grant_type", "refresh_token"),
        new("refresh_token", refreshToken),
        new("client_id", clientId)
    ];

    public static bool ContainsClientSecret(IEnumerable<KeyValuePair<string, string>> form) =>
        form.Any(pair => string.Equals(pair.Key, "client_secret", StringComparison.OrdinalIgnoreCase));

    public static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
