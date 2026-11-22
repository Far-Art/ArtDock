using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers what the dock does about the window in front of its display.
/// </summary>
/// <remarks>
/// The rules asked for on 2026-09-30. The dock hides, as auto-hide would, while anything fills
/// its display — fullscreen, or maximized with the taskbar showing — whatever <em>Always on
/// top</em> says, and slides back up over it when the pointer is held against the bottom edge;
/// the Exclusions page's programs outrank that, and the dock does not come up over them at all.
/// Keeping a dock the pointer has brought up from being sent straight away again is not tested
/// here: that is auto-hide's own keeping of a revealed dock under the pointer, which the dock
/// hiding for a window reuses whole.
/// </remarks>
public class DockFrontTests
{
    // ---- what the dock does ---------------------------------------------------------------

    [Fact]
    public void Nothing_filling_the_display_has_the_dock_where_the_settings_have_it()
    {
        Assert.Equal(FrontAction.Show, DockFront.Decide(false, false));
    }

    /// <summary>
    /// Fullscreen, or maximized with the taskbar showing — both fill the part of the display the
    /// dock sits in, and the dock hides for both (<c>ForegroundApp.Filling</c>).
    /// </summary>
    [Fact]
    public void A_window_filling_the_display_in_front_has_the_dock_hide()
    {
        Assert.Equal(FrontAction.Hide, DockFront.Decide(false, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_program_on_the_Exclusions_page_outranks_everything(bool filled)
    {
        Assert.Equal(FrontAction.HideOutranked, DockFront.Decide(true, filled));
    }
}
