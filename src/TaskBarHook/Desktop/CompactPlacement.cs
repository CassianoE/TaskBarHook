namespace TaskBarHook.Desktop;

public enum CompactFit
{
    Full,
    Mini,
    Hidden
}

public sealed record CompactPlacementRequest(
    PixelRect Taskbar,
    IReadOnlyList<PixelRect> Occupied,
    PixelRect? CurrentSlot,
    int FullWidth,
    int MiniWidth,
    int Height,
    int HorizontalPadding,
    int VerticalInset,
    bool OccupancyKnown,
    bool LayoutSupported);

public sealed record CompactPlacementResult(
    CompactFit Fit,
    PixelRect? Slot,
    bool PreservedPrevious,
    string Reason)
{
    public static CompactPlacementResult Hide(string reason) =>
        new(CompactFit.Hidden, null, false, reason);
}
