using System.Windows;
using TaskBarHook.Presentation;

namespace TaskBarHook.Tests;

public sealed class QueueFlyoutPlacementTests
{
    [Fact]
    public void Prefers_the_right_side_when_there_is_room()
    {
        var panel = new Rect(100, 800, 320, 192);
        var work = new Rect(0, 0, 1920, 1032);
        var size = new Size(260, 240);
        var placed = QueueFlyoutPlacement.Choose(panel, work, size);

        Assert.Equal(QueueFlyoutEdge.Right, placed.Edge);
        Assert.Equal(panel.Right + QueueFlyoutPlacement.GapDip, placed.Left, 3);
        Assert.True(placed.Left + placed.Width <= work.Right + 0.5);
    }

    [Fact]
    public void Uses_the_left_side_near_the_right_edge()
    {
        var panel = new Rect(1600, 800, 320, 192);
        var work = new Rect(0, 0, 1920, 1032);
        var size = new Size(260, 240);
        var placed = QueueFlyoutPlacement.Choose(panel, work, size);

        Assert.Equal(QueueFlyoutEdge.Left, placed.Edge);
        Assert.True(placed.Left >= work.Left - 0.5);
        Assert.True(placed.Left + placed.Width <= panel.Left + 0.5);
    }

    [Fact]
    public void Falls_back_above_when_neither_side_fits()
    {
        var panel = new Rect(40, 400, 700, 192);
        var work = new Rect(0, 0, 800, 600);
        var size = new Size(260, 240);
        var placed = QueueFlyoutPlacement.Choose(panel, work, size);

        Assert.Equal(QueueFlyoutEdge.Above, placed.Edge);
        Assert.True(placed.Top + placed.Height <= panel.Top + 0.5);
        Assert.True(placed.Top >= work.Top - 0.5);
    }

    [Fact]
    public void Caps_height_to_five_rows()
    {
        Assert.Equal(
            QueueFlyoutPlacement.HeightFor(5, true),
            QueueFlyoutPlacement.HeightFor(40, true));
        Assert.True(QueueFlyoutPlacement.HeightFor(1, false) < QueueFlyoutPlacement.HeightFor(5, true));
    }
}
