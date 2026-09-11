using TaskBarHook.Queue;

namespace TaskBarHook.Tests;

public sealed class SpotifyQueueParserTests
{
    private const string Sample = """
        {
          "currently_playing": {
            "type": "track",
            "id": "current",
            "name": "Faixa atual",
            "artists": [{ "name": "Artista" }],
            "album": { "images": [{ "url": "https://img/current", "width": 64 }] }
          },
          "queue": [
            {
              "type": "track",
              "id": "q1",
              "name": "Próxima",
              "artists": [{ "name": "A" }],
              "album": { "images": [
                { "url": "https://img/wide", "width": 300 },
                { "url": "https://img/small", "width": 32 }
              ]}
            },
            {
              "type": "track",
              "id": "q1",
              "name": "Próxima",
              "artists": [{ "name": "A" }],
              "album": { "images": [] }
            },
            {
              "type": "episode",
              "id": "ep1",
              "name": "Episódio",
              "images": [{ "url": "https://img/ep", "width": 64 }]
            }
          ],
          "recently_played": [{ "id": "history" }],
          "recommendations": [{ "id": "rec" }],
          "items": [{ "id": "playlist" }]
        }
        """;

    [Fact]
    public void Reads_only_the_queue_array_and_keeps_repeats()
    {
        var parsed = SpotifyQueueParser.Parse(Sample);
        Assert.Equal("Faixa atual", parsed.CurrentlyPlayingTitle);
        Assert.Equal("Artista", parsed.CurrentlyPlayingArtist);
        Assert.Equal(3, parsed.Queue.Count);
        Assert.Equal("q1", parsed.Queue[0].Id);
        Assert.Equal("q1", parsed.Queue[1].Id);
        Assert.Equal("Episódio", parsed.Queue[2].Title);
        Assert.Equal("https://img/small", parsed.Queue[0].ImageUrl);
        Assert.DoesNotContain(parsed.Queue, item => item.Id is "history" or "rec" or "playlist" or "current");
    }

    [Fact]
    public void Matches_local_smtc_title_and_artist()
    {
        var parsed = SpotifyQueueParser.Parse(Sample);
        Assert.True(SpotifyQueueParser.MatchesLocal("Faixa atual", "Artista", parsed));
        Assert.False(SpotifyQueueParser.MatchesLocal("Outra faixa", "Artista", parsed));
        Assert.False(SpotifyQueueParser.MatchesLocal("Faixa atual", "Outro", parsed));
    }

    [Fact]
    public void Empty_queue_is_empty_not_invented()
    {
        var parsed = SpotifyQueueParser.Parse("""{ "currently_playing": null, "queue": [] }""");
        Assert.Empty(parsed.Queue);
        Assert.Null(parsed.CurrentlyPlayingTitle);
    }
}
