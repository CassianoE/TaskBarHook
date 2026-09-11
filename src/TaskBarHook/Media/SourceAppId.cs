namespace TaskBarHook.Media;

public static class SourceAppId
{
    public static bool IsSpotify(string? sourceAppId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppId))
        {
            return false;
        }

        return sourceAppId.Contains("spotify", StringComparison.OrdinalIgnoreCase);
    }
}
