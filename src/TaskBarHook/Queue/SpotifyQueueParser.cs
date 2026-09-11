using System.Text.Json;

namespace TaskBarHook.Queue;

public sealed record SpotifyQueueParseResult(
    string? CurrentlyPlayingId,
    string? CurrentlyPlayingTitle,
    string? CurrentlyPlayingArtist,
    IReadOnlyList<QueueTrack> Queue);

public static class SpotifyQueueParser
{
    public static SpotifyQueueParseResult Parse(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var root = document.RootElement;
        var playing = ReadItem(root.TryGetProperty("currently_playing", out var playingElement) ? playingElement : default);
        var queue = new List<QueueTrack>();
        if (root.TryGetProperty("queue", out var queueElement) && queueElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in queueElement.EnumerateArray())
            {
                var parsed = ReadItem(item);
                if (parsed is not null)
                {
                    queue.Add(parsed.Value.Track);
                }
            }
        }

        return new SpotifyQueueParseResult(
            playing?.Track.Id,
            playing?.Track.Title,
            playing?.Artist,
            queue);
    }

    public static bool MatchesLocal(string? localTitle, string? localArtist, SpotifyQueueParseResult parsed)
    {
        if (string.IsNullOrWhiteSpace(parsed.CurrentlyPlayingTitle) || string.IsNullOrWhiteSpace(localTitle))
        {
            return false;
        }

        if (!TitlesMatch(localTitle, parsed.CurrentlyPlayingTitle))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(localArtist) || string.IsNullOrWhiteSpace(parsed.CurrentlyPlayingArtist))
        {
            return true;
        }

        return TitlesMatch(localArtist, parsed.CurrentlyPlayingArtist);
    }

    private static bool TitlesMatch(string left, string right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        return a == b || a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal);
    }

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant();

    private static ParsedItem? ReadItem(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        var type = ReadString(element, "type") ?? "track";
        var name = ReadString(element, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var id = ReadString(element, "id") ?? ReadString(element, "uri") ?? name;
        var artist = ReadArtist(element);
        var image = ReadImage(element, type);
        var track = new QueueTrack(id, name, image, null);
        return new ParsedItem(track, artist);
    }

    private static string? ReadArtist(JsonElement element)
    {
        if (element.TryGetProperty("artists", out var artists) &&
            artists.ValueKind == JsonValueKind.Array &&
            artists.GetArrayLength() > 0)
        {
            return ReadString(artists[0], "name");
        }

        if (element.TryGetProperty("show", out var show))
        {
            return ReadString(show, "name");
        }

        return null;
    }

    private static string? ReadImage(JsonElement element, string type)
    {
        if (type == "episode" &&
            element.TryGetProperty("images", out var episodeImages) &&
            PickImage(episodeImages) is { } episodeUrl)
        {
            return episodeUrl;
        }

        if (element.TryGetProperty("album", out var album) &&
            album.TryGetProperty("images", out var albumImages) &&
            PickImage(albumImages) is { } albumUrl)
        {
            return albumUrl;
        }

        if (element.TryGetProperty("images", out var images))
        {
            return PickImage(images);
        }

        return null;
    }

    private static string? PickImage(JsonElement images)
    {
        if (images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? smallest = null;
        var smallestWidth = int.MaxValue;
        string? first = null;
        foreach (var image in images.EnumerateArray())
        {
            var url = ReadString(image, "url");
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            first ??= url;
            var width = image.TryGetProperty("width", out var widthElement) && widthElement.TryGetInt32(out var widthValue)
                ? widthValue
                : int.MaxValue;
            if (width < smallestWidth)
            {
                smallestWidth = width;
                smallest = url;
            }
        }

        return smallest ?? first;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private readonly record struct ParsedItem(QueueTrack Track, string? Artist);
}
