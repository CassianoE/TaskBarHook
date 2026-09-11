namespace TaskBarHook.Queue;

public sealed record PlaybackQueueSnapshot(
    PlaybackQueueKind Kind,
    PlaybackQueueSource Source,
    string Message,
    string? MissingCredential,
    IReadOnlyList<QueueTrack> Items,
    string? CurrentlyPlayingTitle,
    bool MatchesLocalSession)
{
    public static PlaybackQueueSnapshot Idle { get; } = new(
        PlaybackQueueKind.Idle,
        PlaybackQueueSource.None,
        "",
        null,
        [],
        null,
        false);

    public static PlaybackQueueSnapshot Loading(PlaybackQueueSource source) =>
        new(PlaybackQueueKind.Loading, source, "Carregando fila…", null, [], null, true);

    public static PlaybackQueueSnapshot Ready(
        IReadOnlyList<QueueTrack> items,
        PlaybackQueueSource source,
        bool matchesLocal,
        string? currentlyPlaying) =>
        new(PlaybackQueueKind.Ready, source, "", null, items, currentlyPlaying, matchesLocal);

    public static PlaybackQueueSnapshot Empty(PlaybackQueueSource source, bool matchesLocal = true) =>
        new(PlaybackQueueKind.Empty, source, "Nada a seguir", null, [], null, matchesLocal);

    public static PlaybackQueueSnapshot Unsupported() =>
        new(
            PlaybackQueueKind.UnsupportedPlayer,
            PlaybackQueueSource.None,
            "Fila só no Spotify",
            null,
            [],
            null,
            false);

    public static PlaybackQueueSnapshot Missing(string credentialName) =>
        new(
            PlaybackQueueKind.MissingCredential,
            PlaybackQueueSource.Spotify,
            $"Credencial ausente: {credentialName}",
            credentialName,
            [],
            null,
            false);

    public static PlaybackQueueSnapshot NeedsAuth() =>
        new(
            PlaybackQueueKind.NeedsAuthorization,
            PlaybackQueueSource.Spotify,
            "Autorize o Spotify para ver a fila",
            null,
            [],
            null,
            false);

    public static PlaybackQueueSnapshot Disconnected() =>
        new(
            PlaybackQueueKind.Disconnected,
            PlaybackQueueSource.Spotify,
            "Sem conexão",
            null,
            [],
            null,
            false);

    public static PlaybackQueueSnapshot Failed(string message) =>
        new(PlaybackQueueKind.Error, PlaybackQueueSource.Spotify, message, null, [], null, false);

    public static PlaybackQueueSnapshot Mismatched(
        IReadOnlyList<QueueTrack> items,
        string? currentlyPlaying) =>
        new(
            PlaybackQueueKind.MismatchedSession,
            PlaybackQueueSource.Spotify,
            "Fila de outro dispositivo",
            null,
            items,
            currentlyPlaying,
            false);

    public string Identity =>
        $"{Kind}|{Source}|{string.Join('|', Items.Select(item => item.Id))}";
}
