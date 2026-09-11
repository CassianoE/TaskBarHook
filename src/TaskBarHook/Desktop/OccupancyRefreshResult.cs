namespace TaskBarHook.Desktop;

public sealed record OccupancyRefreshResult(int Generation, TaskbarOccupancy Occupancy);

public static class OccupancyApplyGate
{
    public static bool ShouldApply(
        OccupancyRefreshResult result,
        int currentGeneration,
        int lastAppliedGeneration)
    {
        if (result.Generation < lastAppliedGeneration)
        {
            return false;
        }

        if (result.Occupancy.Status is OccupancyReadStatus.Complete or OccupancyReadStatus.Incomplete
            && result.Generation != currentGeneration)
        {
            return false;
        }

        return true;
    }
}
