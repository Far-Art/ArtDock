namespace ArtDock.Dock;

/// <summary>
/// Which edge of a display the dock lives on.
/// </summary>
/// <remarks>
/// The bottom is the default and the shape everything was built around: a horizontal row of
/// icons with labels above them. The two sides turn the same row on its end — the layout is
/// one-dimensional either way, so what changes is only which screen axis "along the bar"
/// means, and which way the icons grow as they magnify.
/// </remarks>
public enum DockEdge
{
    Bottom,
    Left,
    Right
}

/// <summary>Helpers for the two axes a dock has, whichever edge it is on.</summary>
public static class DockEdges
{
    /// <summary>True when the bar runs down the screen rather than across it.</summary>
    public static bool IsVertical(this DockEdge edge) => edge is DockEdge.Left or DockEdge.Right;

    /// <summary>
    /// How far the dock is turned from its natural, horizontal shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dock draws itself horizontally and is rotated into place, rather than being
    /// taught to lay out along either axis. Every part of it that is hard about this dock —
    /// the magnification wave, the drag-to-reorder, the hover zone, the drop preview — is
    /// arithmetic along one axis with the pointer's position on that axis as its only input,
    /// and turning the whole control leaves all of it exactly as it was. WPF maps the cursor
    /// through the transform on the way in, so none of that code ever learns which edge it
    /// is on.
    /// </para>
    /// <para>
    /// The parts that do have to know are the ones with a natural up: the icons and the
    /// labels, which are turned back the other way so they read the right way round.
    /// </para>
    /// </remarks>
    public static double Rotation(this DockEdge edge) => edge switch
    {
        // The bar's outer edge is the bottom in the dock's own frame. Turning it a quarter
        // clockwise puts that edge on the left of the screen; anticlockwise, on the right.
        DockEdge.Left => 90,
        DockEdge.Right => -90,
        _ => 0
    };
}
