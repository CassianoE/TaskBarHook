using System.Windows;

namespace TaskBarHook.Presentation;

public enum QueueFlyoutEdge
{
    Right,
    Left,
    Above
}

public readonly record struct QueueFlyoutRect(
    double Left,
    double Top,
    double Width,
    double Height,
    QueueFlyoutEdge Edge);

public static class QueueFlyoutPlacement
{
    public const double GapDip = 8;
    public const double WidthDip = 260;
    public const double RowHeightDip = 40;
    public const int VisibleRows = 5;
    public const double HeaderHeightDip = 32;
    public const double StatusHeightDip = 48;
    public const double PaddingDip = 8;

    public static double HeightFor(int itemCount, bool listVisible)
    {
        if (!listVisible)
        {
            return HeaderHeightDip + StatusHeightDip + PaddingDip;
        }

        var rows = Math.Clamp(itemCount, 1, VisibleRows);
        return HeaderHeightDip + (rows * RowHeightDip) + PaddingDip;
    }

    public static QueueFlyoutRect Choose(Rect panel, Rect work, System.Windows.Size size)
    {
        var width = size.Width;
        var height = size.Height;
        var rightLeft = panel.Right + GapDip;
        if (rightLeft + width <= work.Right + 0.5)
        {
            return new QueueFlyoutRect(rightLeft, AlignVertical(panel, height, work), width, height, QueueFlyoutEdge.Right);
        }

        var leftLeft = panel.Left - GapDip - width;
        if (leftLeft >= work.Left - 0.5)
        {
            return new QueueFlyoutRect(leftLeft, AlignVertical(panel, height, work), width, height, QueueFlyoutEdge.Left);
        }

        var aboveTop = panel.Top - GapDip - height;
        var top = Math.Max(work.Top, Math.Min(aboveTop, work.Bottom - height));
        var left = Math.Clamp(panel.Left, work.Left, Math.Max(work.Left, work.Right - width));
        return new QueueFlyoutRect(left, top, width, height, QueueFlyoutEdge.Above);
    }

    private static double AlignVertical(Rect panel, double height, Rect work)
    {
        var top = panel.Top;
        var maxTop = work.Bottom - height;
        return Math.Clamp(top, work.Top, Math.Max(work.Top, maxTop));
    }
}
