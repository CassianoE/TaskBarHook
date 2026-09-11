using System.Text.Json;

namespace TaskBarHook.Queue;

public sealed record SpotifyTokenSet(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt);

public static class SpotifyTokenParser
{
    public static SpotifyTokenSet Parse(string json, DateTimeOffset now, string? previousRefresh = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var access = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Token sem access_token.");
        var refresh = root.TryGetProperty("refresh_token", out var refreshElement)
            ? refreshElement.GetString()
            : previousRefresh;
        var expiresIn = root.TryGetProperty("expires_in", out var expiresElement) && expiresElement.TryGetInt32(out var seconds)
            ? seconds
            : 3600;
        return new SpotifyTokenSet(access, refresh, now.AddSeconds(Math.Max(30, expiresIn - 60)));
    }
}
