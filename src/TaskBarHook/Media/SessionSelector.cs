using TaskBarHook.Models;

namespace TaskBarHook.Media;

public sealed class SessionSelector
{
    public SessionCandidate? Select(
        string? currentSessionId,
        IReadOnlyList<SessionCandidate> candidates,
        string? systemCurrentId)
    {
        var eligible = candidates.Where(candidate => candidate.IsEligible).ToList();
        var spotify = eligible.Where(candidate => SourceAppId.IsSpotify(candidate.SourceAppId)).ToList();

        if (spotify.Count > 0)
        {
            if (currentSessionId is not null)
            {
                var kept = spotify.FirstOrDefault(candidate => candidate.Id == currentSessionId);
                if (kept is not null)
                {
                    return kept;
                }
            }

            return spotify.FirstOrDefault(candidate => candidate.IsPlaying) ?? spotify[0];
        }

        if (systemCurrentId is not null)
        {
            var systemCurrent = eligible.FirstOrDefault(candidate => candidate.Id == systemCurrentId);
            if (systemCurrent is not null)
            {
                return systemCurrent;
            }
        }

        return eligible.Count > 0 ? eligible[0] : null;
    }
}
