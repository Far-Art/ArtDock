namespace ArtDock.Dock;

/// <summary>How much of the dock's display the window in front has taken.</summary>
public enum FrontFill
{
    /// <summary>Less than all of the work area — or the window in front is the desktop.</summary>
    None,

    /// <summary>
    /// All of the work area, as a maximized window does with the taskbar showing: the dock
    /// hides, and the handle marks it.
    /// </summary>
    Maximized,

    /// <summary>
    /// The whole display, taken for itself — a video, a game, a presentation: the dock hides,
    /// and no handle is drawn over it.
    /// </summary>
    Fullscreen
}

/// <summary>What the dock does about the window in front of its display.</summary>
public enum FrontAction
{
    /// <summary>
    /// Nothing in front fills the display — maximized or fullscreen: the dock is back where the
    /// settings have it, unless something else has it hidden.
    /// </summary>
    Show,

    /// <summary>
    /// Something in front fills the display — maximized or fullscreen: the dock hides as
    /// auto-hide would, and the pointer held against the bottom edge brings it back.
    /// </summary>
    Hide,

    /// <summary>
    /// A program on the Exclusions page fills the display: the dock goes under it and hides,
    /// and the edge does not bring it back.
    /// </summary>
    HideOutranked
}

/// <summary>
/// The rules for what the dock does about the window in front of its display, and for when
/// the handle marks it — decided here, apart from the windows they move, so they can be tested.
/// </summary>
/// <remarks>
/// <para>
/// Asked on 2026-09-30, and what they replaced: the dock went under only a program on the
/// Exclusions page and floated over every other fullscreen window, and the handle marked only
/// a dock that auto-hide had put away, stepping aside for anything that filled the display.
/// Now the dock hides, as auto-hide would, for anything in front that fills its display —
/// fullscreen, or maximized with the taskbar showing — and slides back up over it when the
/// pointer is held against the bottom edge; the handle marks the dock whenever it is out of
/// sight, over everything but the Exclusions page's programs, which the dock does not come up
/// over at all. The handle is a mark only — resting the pointer on it does nothing.
/// </para>
/// <para>
/// On 2026-10-01 the handle was taken off fullscreen windows as well, so that it does not sit
/// over a video: it marks a dock hidden for a maximized window, and not one hidden for a window
/// that has the whole display. The dock hides for both alike, and the edge brings it back over
/// both.
/// </para>
/// <para>
/// The same day, the dock went <em>under</em> such a window at first, and a rule here kept a
/// dock the pointer had lifted over it from being put straight back under by the next look.
/// Hiding made that rule unnecessary: while the dock hides, auto-hide's own keeping of a
/// revealed dock under the pointer is what keeps it up.
/// </para>
/// <para>
/// What the rules are fed is the window system's to answer — <c>DockWindow</c> reads the window
/// in front and walks the windows over the dock — and nothing here touches a window.
/// </para>
/// </remarks>
public static class DockFront
{
    /// <summary>What the dock does about the window in front of its display.</summary>
    /// <param name="excludedInFront">
    /// A program on the Exclusions page is in front and fills the display, maximized or not.
    /// </param>
    /// <param name="filledInFront">
    /// The window in front covers the dock's display, or all of it the taskbar leaves —
    /// fullscreen, or maximized — whichever program it is.
    /// </param>
    public static FrontAction Decide(bool excludedInFront, bool filledInFront) =>
        excludedInFront ? FrontAction.HideOutranked
        : filledInFront ? FrontAction.Hide
        : FrontAction.Show;

    /// <summary>Whether the handle marks the dock.</summary>
    /// <param name="showHandle">The handle is asked for.</param>
    /// <param name="fullscreenInFront">
    /// The window in front has the whole of the dock's display — fullscreen, or a program on
    /// the Exclusions page filling it.
    /// </param>
    /// <param name="previewing">The settings dialog is open, showing the dock off.</param>
    /// <param name="hides">
    /// The dock hides itself — auto-hide is on, or a window in front fills its display — and
    /// was not put away from the tray.
    /// </param>
    /// <param name="visibility">Where the dock is in its hide and reveal.</param>
    /// <param name="outOfSight">
    /// The dock is on screen and none of its bar can be seen, for the windows over it.
    /// </param>
    /// <remarks>
    /// <para>
    /// Never over a window that has the whole display: what is shown that way — a video, a
    /// game, a presentation — wants nothing drawn over it, and a mark there sits on the picture.
    /// Asked on 2026-10-01; before, it held only for a program on the Exclusions page, which
    /// counts only when it fills the display and so is one of these. Over a listed program there
    /// is a second reason: the dock does not come up over one, and a mark would promise a dock
    /// the edge will not bring. For that reason too, not for a dock put away from the tray,
    /// which only the tray brings back.
    /// </para>
    /// <para>
    /// A dock on its way somewhere is marked by where it is going: leaving as it hides, the
    /// handle arrives as it goes; coming back, the handle goes as it arrives. Only a dock that
    /// has arrived is asked whether it can be seen.
    /// </para>
    /// <para>
    /// While the settings dialog is open the handle is shown whatever else, so its width can be
    /// seen while it is set — the dock is held up then, and the handle would otherwise never be
    /// on screen at the same time as the slider that sizes it.
    /// </para>
    /// </remarks>
    public static bool Marks(
        bool showHandle,
        bool fullscreenInFront,
        bool previewing,
        bool hides,
        DockVisibility visibility,
        bool outOfSight) =>
        showHandle
        && !fullscreenInFront
        && (previewing
            || (hides && visibility is DockVisibility.Hiding or DockVisibility.Hidden)
            || (visibility is DockVisibility.Shown && outOfSight));
}
