namespace TaskBarHook.Desktop;

public static class CompactPlacementPolicy
{
    public static CompactPlacementResult Choose(CompactPlacementRequest request)
    {
        if (!request.OccupancyKnown)
        {
            return CompactPlacementResult.Hide("occupancy-unknown");
        }

        if (!request.LayoutSupported || request.Taskbar.IsEmpty)
        {
            return CompactPlacementResult.Hide("unsupported-layout");
        }

        var inner = Inset(request.Taskbar, request.VerticalInset);
        if (inner.IsEmpty || inner.Height < request.Height)
        {
            return CompactPlacementResult.Hide("taskbar-too-short");
        }

        var height = Math.Min(request.Height, inner.Height);
        var occupied = Merge(Clip(request.Occupied, request.Taskbar));
        var gaps = Gaps(request.Taskbar, occupied, request.HorizontalPadding);
        if (gaps.Count == 0)
        {
            return CompactPlacementResult.Hide("no-gap");
        }

        if (request.CurrentSlot is { } current &&
            TryPreserve(current, gaps, inner, height, request.FullWidth, request.MiniWidth) is { } preserved)
        {
            return preserved;
        }

        if (TryPlace(gaps, inner, height, request.FullWidth, CompactFit.Full) is { } full)
        {
            return full;
        }

        if (TryPlace(gaps, inner, height, request.MiniWidth, CompactFit.Mini) is { } mini)
        {
            return mini;
        }

        return CompactPlacementResult.Hide("no-fit");
    }

    private static CompactPlacementResult? TryPreserve(
        PixelRect current,
        IReadOnlyList<PixelRect> gaps,
        PixelRect inner,
        int height,
        int fullWidth,
        int miniWidth)
    {
        foreach (var gap in gaps)
        {
            if (current.Right <= gap.Left || current.Left >= gap.Right)
            {
                continue;
            }

            if (gap.Width >= fullWidth)
            {
                var left = Math.Clamp(current.Left, gap.Left, gap.Right - fullWidth);
                return new CompactPlacementResult(
                    CompactFit.Full,
                    Slot(left, fullWidth, inner, height),
                    true,
                    current.Width >= fullWidth ? "preserve-full" : "recover-full");
            }

            if (gap.Width >= miniWidth)
            {
                var left = Math.Clamp(current.Left, gap.Left, gap.Right - miniWidth);
                return new CompactPlacementResult(
                    CompactFit.Mini,
                    Slot(left, miniWidth, inner, height),
                    true,
                    "preserve-mini");
            }
        }

        return null;
    }

    private static CompactPlacementResult? TryPlace(
        IReadOnlyList<PixelRect> gaps,
        PixelRect inner,
        int height,
        int width,
        CompactFit fit)
    {
        PixelRect? best = null;
        foreach (var gap in gaps)
        {
            if (gap.Width < width)
            {
                continue;
            }

            if (best is null || gap.Width > best.Value.Width)
            {
                best = gap;
            }
        }

        if (best is null)
        {
            return null;
        }

        var chosen = best.Value;
        var left = PlaceInGap(chosen, width, inner);
        return new CompactPlacementResult(fit, Slot(left, width, inner, height), false, fit == CompactFit.Full ? "full" : "mini");
    }

    private static int PlaceInGap(PixelRect gap, int width, PixelRect inner)
    {
        var maxLeft = gap.Right - width;
        var minLeft = gap.Left;
        if (maxLeft < minLeft)
        {
            return minLeft;
        }

        var innerCenter = inner.CenterX;
        if (gap.Right <= innerCenter)
        {
            return maxLeft;
        }

        if (gap.Left >= innerCenter)
        {
            return minLeft;
        }

        var centered = innerCenter - (width / 2);
        return Math.Clamp(centered, minLeft, maxLeft);
    }

    private static PixelRect Slot(int left, int width, PixelRect inner, int height)
    {
        var top = inner.Top + ((inner.Height - height) / 2);
        return new PixelRect(left, top, left + width, top + height);
    }

    private static PixelRect Inset(PixelRect taskbar, int verticalInset)
    {
        var inset = Math.Max(0, verticalInset);
        return new PixelRect(taskbar.Left, taskbar.Top + inset, taskbar.Right, taskbar.Bottom - inset);
    }

    private static List<PixelRect> Clip(IReadOnlyList<PixelRect> occupied, PixelRect taskbar)
    {
        var list = new List<PixelRect>();
        foreach (var rect in occupied)
        {
            var clipped = rect.Intersect(taskbar);
            if (!clipped.IsEmpty)
            {
                list.Add(clipped);
            }
        }

        return list;
    }

    private static List<PixelRect> Merge(List<PixelRect> rects)
    {
        if (rects.Count == 0)
        {
            return rects;
        }

        var ordered = rects.OrderBy(rect => rect.Left).ToList();
        var merged = new List<PixelRect> { ordered[0] };
        for (var i = 1; i < ordered.Count; i++)
        {
            var last = merged[^1];
            var next = ordered[i];
            if (next.Left <= last.Right)
            {
                merged[^1] = new PixelRect(last.Left, Math.Min(last.Top, next.Top), Math.Max(last.Right, next.Right), Math.Max(last.Bottom, next.Bottom));
            }
            else
            {
                merged.Add(next);
            }
        }

        return merged;
    }

    private static List<PixelRect> Gaps(PixelRect taskbar, IReadOnlyList<PixelRect> occupied, int padding)
    {
        var gaps = new List<PixelRect>();
        var cursor = taskbar.Left;
        foreach (var block in occupied.OrderBy(rect => rect.Left))
        {
            var blockLeft = block.Left - padding;
            if (blockLeft > cursor)
            {
                gaps.Add(new PixelRect(cursor, taskbar.Top, blockLeft, taskbar.Bottom));
            }

            cursor = Math.Max(cursor, block.Right + padding);
        }

        if (cursor < taskbar.Right)
        {
            gaps.Add(new PixelRect(cursor, taskbar.Top, taskbar.Right, taskbar.Bottom));
        }

        return gaps.Where(gap => !gap.IsEmpty).ToList();
    }
}
