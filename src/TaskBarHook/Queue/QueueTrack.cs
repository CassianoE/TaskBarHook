namespace TaskBarHook.Queue;

public sealed record QueueTrack(
    string Id,
    string Title,
    string? ImageUrl,
    byte[]? ArtworkBytes)
{
    public string ArtworkKey => string.IsNullOrEmpty(Id) ? Title : Id;
}
