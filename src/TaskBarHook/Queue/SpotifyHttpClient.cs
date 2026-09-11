using System.Net.Http.Headers;
using System.Text.Json;
using System.Net.Http;

namespace TaskBarHook.Queue;

public sealed class SpotifyHttpClient : ISpotifyApiClient, IDisposable
{
    public const string QueueEndpoint = "https://api.spotify.com/v1/me/player/queue";
    public const string TokenEndpoint = "https://accounts.spotify.com/api/token";

    private readonly HttpClient _http;

    public SpotifyHttpClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    public async Task<SpotifyTokenSet> ExchangeCodeAsync(
        string clientId,
        string code,
        Uri redirectUri,
        string verifier,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var form = SpotifyPkce.AuthorizationCodeForm(clientId, code, redirectUri, verifier);
        if (SpotifyPkce.ContainsClientSecret(form))
        {
            throw new InvalidOperationException("PKCE must not send a client secret.");
        }

        return await RequestTokenAsync(form, now, previousRefresh: null, cancellationToken);
    }

    public async Task<SpotifyTokenSet> RefreshAsync(
        string clientId,
        string refreshToken,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var form = SpotifyPkce.RefreshForm(clientId, refreshToken);
        if (SpotifyPkce.ContainsClientSecret(form))
        {
            throw new InvalidOperationException("PKCE must not send a client secret.");
        }

        return await RequestTokenAsync(form, now, refreshToken, cancellationToken);
    }

    public async Task<string> GetQueueJsonAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, QueueEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((int)response.StatusCode == 401)
        {
            throw new SpotifyAuthException("Token expirado ou revogado.");
        }

        if ((int)response.StatusCode == 403)
        {
            throw new SpotifyRestrictedException(
                $"Acesso restrito à fila (escopos {SpotifyOptions.CurrentlyPlayingScope} e {SpotifyOptions.PlaybackStateScope}).");
        }

        if ((int)response.StatusCode == 429)
        {
            throw new SpotifyRateLimitException("Limite de pedidos. Tente de novo em instantes.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Fila Spotify HTTP {(int)response.StatusCode}.");
        }

        return string.IsNullOrWhiteSpace(body) ? "{}" : body;
    }

    public async Task<byte[]?> GetBytesAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return bytes.Length is > 0 and < 512_000 ? bytes : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();

    private async Task<SpotifyTokenSet> RequestTokenAsync(
        IReadOnlyList<KeyValuePair<string, string>> form,
        DateTimeOffset now,
        string? previousRefresh,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(TokenEndpoint, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var description = TryReadError(body) ?? $"Token HTTP {(int)response.StatusCode}.";
            throw new SpotifyAuthException(description);
        }

        return SpotifyTokenParser.Parse(body, now, previousRefresh);
    }

    private static string? TryReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error_description", out var description))
            {
                return description.GetString();
            }

            if (document.RootElement.TryGetProperty("error", out var error))
            {
                return error.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}

public sealed class SpotifyAuthException : Exception
{
    public SpotifyAuthException(string message) : base(message)
    {
    }
}

public sealed class SpotifyRestrictedException : Exception
{
    public SpotifyRestrictedException(string message) : base(message)
    {
    }
}

public sealed class SpotifyRateLimitException : Exception
{
    public SpotifyRateLimitException(string message) : base(message)
    {
    }
}
