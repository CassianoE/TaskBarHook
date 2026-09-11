namespace TaskBarHook.Models;

public sealed record SessionCandidate(
    string Id,
    string? SourceAppId,
    bool IsPlaying,
    bool IsEligible);
