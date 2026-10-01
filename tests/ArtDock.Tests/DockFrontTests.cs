using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers what the dock does about the window in front of its display, and when the handle
/// marks it.
/// </summary>
/// <remarks>
/// <para>
/// The rules asked for on 2026-09-30. The dock hides, as auto-hide would, while anything in
/// front fills its display — fullscreen, or maximized with the taskbar showing — whatever
/// <em>Always on top</em> says, and slides back up over it when the pointer is held against the
/// bottom edge; the Exclusions page's programs outrank that, and the dock does not come up over
/// them at all. The handle marks the dock whenever it is out of sight, auto-hide or not, over
/// everything but those programs — a mark only, which the pointer cannot use. Since 2026-10-01
/// not over anything fullscreen either, so that it does not sit over a video.
/// </para>
/// <para>
/// What matters most: the handle never marks a dock the edge will not bring up — not over a
/// listed program, and not for a dock put away from the tray — and never sits over a picture
/// that has the whole display. Keeping a dock the pointer has
/// brought up from being sent straight away again is not tested here: that is auto-hide's own
/// keeping of a revealed dock under the pointer, which the dock hiding for a window reuses
/// whole.
/// </para>
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

    // ---- when the handle marks it ---------------------------------------------------------

    /// <summary>
    /// Hiding itself — for auto-hide, or for a window in front that fills the display.
    /// </summary>
    [Theory]
    [InlineData(DockVisibility.Hiding)]
    [InlineData(DockVisibility.Hidden)]
    public void A_dock_that_hides_itself_leaves_a_handle_as_it_goes(DockVisibility visibility)
    {
        Assert.True(Marks(hides: true, visibility: visibility));
    }

    [Fact]
    public void The_handle_goes_as_the_dock_comes_back_even_from_under_a_window()
    {
        Assert.False(Marks(hides: true, visibility: DockVisibility.Revealing, outOfSight: true));
        Assert.False(Marks(hides: false, visibility: DockVisibility.Revealing, outOfSight: true));
    }

    /// <summary>
    /// The half of the change that was asked for as "regardless of auto-hide": a dock on screen
    /// but under the windows in front is out of sight, and marked, whether it hides itself or not.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_dock_under_the_windows_in_front_is_marked_hiding_or_not(bool hides)
    {
        Assert.True(Marks(hides: hides, visibility: DockVisibility.Shown, outOfSight: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_dock_that_can_be_seen_is_not_marked(bool hides)
    {
        Assert.False(Marks(hides: hides, visibility: DockVisibility.Shown, outOfSight: false));
    }

    /// <summary>
    /// Put away from the tray, the dock comes back only from the tray, and a mark would promise a
    /// dock that the edge will not bring.
    /// </summary>
    [Theory]
    [InlineData(DockVisibility.Hiding)]
    [InlineData(DockVisibility.Hidden)]
    public void A_dock_put_away_from_the_tray_is_not_marked(DockVisibility visibility)
    {
        Assert.False(Marks(hides: false, visibility: visibility));
    }

    [Theory]
    [InlineData(true, DockVisibility.Hidden, false, false)]
    [InlineData(false, DockVisibility.Shown, true, false)]
    [InlineData(false, DockVisibility.Shown, false, true)]
    public void Turned_off_the_handle_marks_nothing(
        bool hides, DockVisibility visibility, bool outOfSight, bool previewing)
    {
        Assert.False(Marks(
            showHandle: false,
            hides: hides,
            visibility: visibility,
            outOfSight: outOfSight,
            previewing: previewing));
    }

    /// <summary>
    /// A window that has the whole display — a video, a game, a presentation, a program on the
    /// Exclusions page, which counts only when it fills the display — has nothing drawn over it:
    /// not for a dock that is hiding itself, and not with the settings dialog open.
    /// </summary>
    [Theory]
    [InlineData(true, DockVisibility.Hidden, false, false)]
    [InlineData(true, DockVisibility.Hiding, false, false)]
    [InlineData(false, DockVisibility.Shown, true, false)]
    [InlineData(true, DockVisibility.Shown, false, true)]
    public void No_handle_is_drawn_over_a_fullscreen_window(
        bool hides, DockVisibility visibility, bool outOfSight, bool previewing)
    {
        Assert.False(Marks(
            fullscreenInFront: true,
            hides: hides,
            visibility: visibility,
            outOfSight: outOfSight,
            previewing: previewing));
    }

    /// <summary>
    /// What was asked on 2026-10-01: the dock hides for both alike, and only the one that has
    /// the whole display loses the mark — so a browser maximized keeps it, and the same browser
    /// showing a video fullscreen does not.
    /// </summary>
    [Fact]
    public void A_dock_hidden_for_a_maximized_window_is_marked_and_for_a_fullscreen_one_is_not()
    {
        Assert.True(Marks(fullscreenInFront: false, hides: true, visibility: DockVisibility.Hidden));
        Assert.False(Marks(fullscreenInFront: true, hides: true, visibility: DockVisibility.Hidden));
    }

    [Fact]
    public void The_settings_dialog_shows_the_handle_under_a_dock_in_plain_sight()
    {
        Assert.True(Marks(previewing: true, hides: false, visibility: DockVisibility.Shown));
    }

    private static bool Marks(
        bool showHandle = true,
        bool fullscreenInFront = false,
        bool previewing = false,
        bool hides = false,
        DockVisibility visibility = DockVisibility.Shown,
        bool outOfSight = false) =>
        DockFront.Marks(showHandle, fullscreenInFront, previewing, hides, visibility, outOfSight);
}
