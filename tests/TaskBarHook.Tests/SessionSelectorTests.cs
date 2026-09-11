using TaskBarHook.Media;
using TaskBarHook.Models;

namespace TaskBarHook.Tests;

public sealed class SessionSelectorTests
{
    private readonly SessionSelector _selector = new();

    [Fact]
    public void Prefers_spotify_even_when_paused_over_playing_browser()
    {
        var spotify = new SessionCandidate("spotify", "Spotify.exe", IsPlaying: false, IsEligible: true);
        var browser = new SessionCandidate("edge", "msedge.exe", IsPlaying: true, IsEligible: true);

        var selected = _selector.Select(null, [browser, spotify], systemCurrentId: "edge");

        Assert.Equal("spotify", selected?.Id);
    }

    [Fact]
    public void Recognizes_store_spotify_app_user_model_id()
    {
        var spotify = new SessionCandidate(
            "store",
            "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify",
            IsPlaying: false,
            IsEligible: true);
        var browser = new SessionCandidate("chrome", "Chrome.exe", IsPlaying: true, IsEligible: true);

        var selected = _selector.Select(null, [browser, spotify], "chrome");

        Assert.Equal("store", selected?.Id);
    }

    [Fact]
    public void Does_not_identify_spotify_from_song_title()
    {
        var youtube = new SessionCandidate("yt", "msedge.exe", IsPlaying: true, IsEligible: true);

        var selected = _selector.Select(null, [youtube], "yt");

        Assert.Equal("yt", selected?.Id);
        Assert.False(SourceAppId.IsSpotify("msedge.exe"));
        Assert.False(SourceAppId.IsSpotify(youtube.SourceAppId));
    }

    [Fact]
    public void Keeps_current_spotify_when_another_spotify_candidate_appears()
    {
        var first = new SessionCandidate("desktop", "Spotify.exe", IsPlaying: false, IsEligible: true);
        var second = new SessionCandidate("store", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", IsPlaying: true, IsEligible: true);

        var selected = _selector.Select("desktop", [first, second], "store");

        Assert.Equal("desktop", selected?.Id);
    }

    [Fact]
    public void Picks_playing_spotify_on_initial_selection()
    {
        var paused = new SessionCandidate("paused", "Spotify.exe", IsPlaying: false, IsEligible: true);
        var playing = new SessionCandidate("playing", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", IsPlaying: true, IsEligible: true);

        var selected = _selector.Select(null, [paused, playing], "paused");

        Assert.Equal("playing", selected?.Id);
    }

    [Fact]
    public void Falls_back_to_system_current_when_spotify_disappears()
    {
        var browser = new SessionCandidate("edge", "msedge.exe", IsPlaying: true, IsEligible: true);

        var selected = _selector.Select("spotify", [browser], "edge");

        Assert.Equal("edge", selected?.Id);
    }

    [Fact]
    public void Returns_null_when_no_eligible_session_exists()
    {
        var closed = new SessionCandidate("gone", "Spotify.exe", IsPlaying: false, IsEligible: false);

        var selected = _selector.Select("gone", [closed], "gone");

        Assert.Null(selected);
    }
}
