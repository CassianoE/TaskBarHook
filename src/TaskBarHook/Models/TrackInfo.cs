namespace TaskBarHook.Models;

public sealed record TrackInfo(
    string? Title,
    string? Artist,
    string? Album,
    byte[]? ArtworkBytes,
    string ArtworkKey)
{
    public static TrackInfo Empty { get; } = new(null, null, null, null, "");
}
