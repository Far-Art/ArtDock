namespace ArtDock.Dock;

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
/// The rules for what the dock does about the window in front of its display — decided here,
/// apart from the windows they move, so they can be tested.
/// </summary>
/// <remarks>
/// <para>
/// Asked on 2026-09-30, and what they replaced: the dock went under only a program on the
/// Exclusions page and floated over every other fullscreen window. Now the dock hides, as
/// auto-hide would, for anything that fills its display — fullscreen, or maximized with the
/// taskbar showing — and slides back up over it when the pointer is held against the bottom
/// edge; the Exclusions page's programs it does not come up over at all.
/// </para>
/// <para>
/// The same day, the dock went <em>under</em> such a window at first, and a rule here kept a
/// dock the pointer had lifted over it from being put straight back under by the next look.
/// Hiding made that rule unnecessary: while the dock hides, auto-hide's own keeping of a
/// revealed dock under the pointer is what keeps it up.
/// </para>
/// <para>
/// Since 2026-11-22 any window that fills the dock's display counts, not only the one in
/// front: a window fullscreen or maximized there had the dock back over it at a click on the
/// other display, or with a smaller window in front of it (<c>ForegroundApp.AnyFilling</c>).
/// </para>
/// <para>
/// What the rules are fed is the window system's to answer — <c>DockWindow</c> reads the
/// windows on the dock's display — and nothing here touches a window.
/// </para>
/// </remarks>
public static class DockFront
{
    /// <summary>What the dock does about the window in front of its display.</summary>
    /// <param name="excludedInFront">
    /// A program on the Exclusions page is in front and fills the display, maximized or not.
    /// </param>
    /// <param name="filled">
    /// A window covers the dock's display, or all of it the taskbar leaves — fullscreen, or
    /// maximized — whichever program it is.
    /// </param>
    public static FrontAction Decide(bool excludedInFront, bool filled) =>
        excludedInFront ? FrontAction.HideOutranked
        : filled ? FrontAction.Hide
        : FrontAction.Show;
}
