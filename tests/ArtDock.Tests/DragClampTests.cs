using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers a dragged icon being held inside the bar.
/// </summary>
/// <remarks>
/// The reorder drag snaps the icon to the pointer so that it tracks the cursor exactly, and
/// that let a drag which wandered carry the icon clean off the bar. The slot it would land in
/// was always clamped, so the row still reordered sensibly — it was only what the eye follows
/// that escaped, leaving an icon floating outside the dock, over the desktop, still driving a
/// reorder. Dragging an icon off does not unpin it either, so there was nothing the excursion
/// could ever amount to.
/// </remarks>
public class DragClampTests
{
    private static DockLayout Layout() =>
        new(new DockMetrics { BaseSize = 50, MaxSize = 70, Gap = 8, PaddingX = 14 });

    // A bar of five resting icons, positioned at x = 100.
    private const double BarLeft = 100;
    private static double BarWidth() => Layout().RestingWidth(5);

    [Fact]
    public void AnIconInsideTheBarIsLeftWhereItIs()
    {
        var layout = Layout();
        var wanted = BarLeft + 120;

        Assert.Equal(wanted, layout.ClampIconToBar(wanted, 50, BarLeft, BarWidth()), 6);
    }

    [Fact]
    public void DraggingPastTheLeftEndStopsAtTheFirstSlot()
    {
        var layout = Layout();

        // Far off the left of the screen, which is where a wandering drag ends up.
        var clamped = layout.ClampIconToBar(-500, 50, BarLeft, BarWidth());

        Assert.Equal(BarLeft + 14, clamped, 6);
    }

    [Fact]
    public void DraggingPastTheRightEndStopsAtTheLastSlot()
    {
        var layout = Layout();
        var width = BarWidth();

        var clamped = layout.ClampIconToBar(BarLeft + 5000, 50, BarLeft, width);

        Assert.Equal(BarLeft + width - 14 - 50, clamped, 6);
    }

    [Fact]
    public void AMagnifiedIconIsHeldByItsOwnWidth()
    {
        // The dragged icon is the magnified one, so the right-hand bound moves with its size
        // rather than with the resting pitch.
        var layout = Layout();
        var width = BarWidth();

        var clamped = layout.ClampIconToBar(BarLeft + 5000, 70, BarLeft, width);

        Assert.Equal(BarLeft + width - 14 - 70, clamped, 6);
    }

    [Fact]
    public void TheIconNeverEscapesTheBarAtAnyPointerPosition()
    {
        var layout = Layout();
        var width = BarWidth();

        for (var x = -1000d; x <= 1000; x += 7)
        {
            var left = layout.ClampIconToBar(x, 50, BarLeft, width);

            Assert.True(left >= BarLeft - 0.000001, $"pointer {x} put the icon's left edge at {left}");
            Assert.True(
                left + 50 <= BarLeft + width + 0.000001,
                $"pointer {x} put the icon's right edge at {left + 50}, past the bar's {BarLeft + width}");
        }
    }

    [Fact]
    public void ABarNarrowerThanTheIconDoesNotThrow()
    {
        // Reachable for a frame while the bar is still opening over an icon that has just
        // arrived. Math.Clamp throws outright when its bounds cross, so this is not academic.
        var layout = Layout();

        var clamped = layout.ClampIconToBar(500, 90, BarLeft, 40);

        Assert.Equal(BarLeft + 14, clamped, 6);
    }
}
