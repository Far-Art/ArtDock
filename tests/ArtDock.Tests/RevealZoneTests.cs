using System.Windows;
using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers where across the screen the edge brings the dock up, and the strip below a dock that is
/// up keeps it there: under the bar at rest, and on the dock's own display.
/// </summary>
/// <remarks>
/// <para>
/// Asked for on 2026-09-30. The zone was the dock's whole window and 80 DIPs more at each end —
/// the window keeps room beside the bar for the wave to grow into, and for the slot a drop opens —
/// which came out at about half as wide again as the dock.
/// </para>
/// <para>
/// In physical pixels, as the cursor and the bar are both read, against this machine's two
/// displays: the main one at 100%, and the 4K one to its left at 150%, from (-3840,8).
/// </para>
/// </remarks>
public class RevealZoneTests
{
    private static readonly Rect Main = new(0, 0, 2560, 1440);

    /// <summary>A bar of about ten icons in the middle of the main display.</summary>
    private static readonly Rect Bar = new(980, 1318, 600, 62);

    [Theory]
    [InlineData(980)]
    [InlineData(1280)]
    [InlineData(1579)]
    public void Across_the_bar_is_under_the_dock(double x)
    {
        Assert.True(AutoHideController.IsUnderDock(x, Bar, Main));
    }

    /// <summary>
    /// Beside the bar — a pixel past either end, and out in the room the window keeps for the
    /// wave, where the edge used to answer.
    /// </summary>
    [Theory]
    [InlineData(979)]
    [InlineData(1580)]
    [InlineData(900)]
    [InlineData(1700)]
    public void Beside_the_bar_is_not(double x)
    {
        Assert.False(AutoHideController.IsUnderDock(x, Bar, Main));
    }

    /// <summary>
    /// The edge and the handle line up where the handle is as wide as the dock: both are the
    /// bar, measured the same way.
    /// </summary>
    [Fact]
    public void A_handle_as_wide_as_the_dock_spans_the_zone_exactly()
    {
        var handle = DockHandle.Place(Bar.Left, Bar.Right, 1392, 1, matchDock: true, width: 140);

        Assert.True(AutoHideController.IsUnderDock(handle.Left, Bar, Main));
        Assert.True(AutoHideController.IsUnderDock(handle.Right - 1, Bar, Main));
        Assert.False(AutoHideController.IsUnderDock(handle.Left - 1, Bar, Main));
        Assert.False(AutoHideController.IsUnderDock(handle.Right, Bar, Main));
    }

    /// <summary>
    /// A bar with more icons than its display is wide reaches past the sides, and a pointer on
    /// the display next door — at whatever height this one's bottom falls — is not under it.
    /// </summary>
    [Fact]
    public void A_bar_wider_than_its_display_is_cut_at_the_display_s_sides()
    {
        var wide = new Rect(-100, 1318, 2760, 62);

        Assert.False(AutoHideController.IsUnderDock(-1, wide, Main));
        Assert.True(AutoHideController.IsUnderDock(0, wide, Main));
        Assert.True(AutoHideController.IsUnderDock(2559, wide, Main));
        Assert.False(AutoHideController.IsUnderDock(2560, wide, Main));
    }

    /// <summary>
    /// On the 4K display the bar is half as wide again in pixels as in DIPs, and the main display
    /// beside it is not under it.
    /// </summary>
    [Fact]
    public void On_the_display_to_the_left_the_bar_is_measured_where_it_is()
    {
        var wide = new Rect(-3840, 8, 3840, 2160);
        var bar = new Rect(-2370, 1980, 900, 93);

        Assert.True(AutoHideController.IsUnderDock(-1920, bar, wide));
        Assert.False(AutoHideController.IsUnderDock(-2371, bar, wide));
        Assert.False(AutoHideController.IsUnderDock(-1470, bar, wide));
        Assert.False(AutoHideController.IsUnderDock(100, bar, wide));
    }

    [Fact]
    public void Nothing_is_under_a_dock_not_yet_laid_out_or_placed()
    {
        Assert.False(AutoHideController.IsUnderDock(1280, Rect.Empty, Main));
        Assert.False(AutoHideController.IsUnderDock(1280, Bar, Rect.Empty));
    }
}
