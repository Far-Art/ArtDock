namespace ArtDock.Dock;

/// <summary>Where a dragged icon stands, as <see cref="DragOff"/> sees it.</summary>
public enum DragOffState
{
    /// <summary>On the bar: the drag is a reorder, and letting go puts the icon in its slot.</summary>
    OnBar,

    /// <summary>
    /// Pulled clear of the dock, but not yet far enough: letting go drops the icon back where it
    /// came from, as if it had never been moved.
    /// </summary>
    Lifted,

    /// <summary>Far enough from the dock that letting go takes it off the dock.</summary>
    Armed,
}

/// <summary>
/// When a dragged icon has left the dock, and when letting go of it removes it, as the macOS
/// dock does: pulled away from the edge, far enough that <i>Remove</i> shows, then let go.
/// </summary>
/// <remarks>
/// <para>
/// The gesture was here once and was taken away, because it fired on a drag that wandered
/// rather than on one that meant it. Asked for again on 2026-11-20, and made harder to do by
/// accident: only pulling <b>away from the edge</b> counts — past the ends of the bar the icon
/// stays on it, held at the end slot (<c>DockLayout.ClampIconToBar</c>) — and only by distance.
/// A little past the space the magnified icons fill (<see cref="LiftDistance"/>) the icon leaves
/// the row and follows the pointer; a clear distance further (<see cref="RemoveDistance"/>)
/// <i>Remove</i> shows, and letting go there removes it. Let go nearer the dock and it drops back
/// where it came from; come back into the dock's space and it is a reorder again. A first build
/// had it held off the dock for 0.7 s before it could go; replaced the same day, on request, by
/// distance alone, as macOS does it. A locked dock does not drag at all, so nothing is removed
/// this way either.
/// </para>
/// <para>
/// Fed the pointer's distance from the dock on every frame of the drag, and pure, so every rule
/// can be tested.
/// </para>
/// </remarks>
public sealed class DragOff
{
    /// <summary>
    /// How far past the top of the dock's space — the bar raised by a fully magnified icon,
    /// <c>DockBar.HoverTop</c> — the pointer has to be before the icon leaves the row, in the
    /// dock's units. The dock sets it to half an icon.
    /// </summary>
    public double LiftDistance { get; set; } = 18;

    /// <summary>
    /// How far past the same line the pointer has to be before letting go removes the icon. The
    /// dock sets it to three icons: an icon and a half at first, doubled on 2026-11-20 on request.
    /// </summary>
    public double RemoveDistance { get; set; } = 54;

    /// <summary>
    /// How far back towards the dock an armed icon may come before <i>Remove</i> goes again, so a
    /// hand resting at the line does not have it flicker.
    /// </summary>
    public double Slack { get; set; } = 9;

    public DragOffState State { get; private set; }

    /// <summary>True while the icon is clear of the dock, armed or not.</summary>
    public bool IsLifted => State != DragOffState.OnBar;

    /// <summary>Takes a look at the pointer and says where the icon stands now.</summary>
    /// <param name="beyond">
    /// How far the pointer is past the top of the dock's space, away from the edge the dock is
    /// on; negative while it is inside it.
    /// </param>
    public DragOffState Look(double beyond)
    {
        if (double.IsNaN(beyond))
        {
            return State;
        }

        State = State switch
        {
            DragOffState.OnBar => beyond >= RemoveDistance ? DragOffState.Armed
                : beyond >= LiftDistance ? DragOffState.Lifted
                : DragOffState.OnBar,

            // Back into the dock's own space, not merely below the line it was lifted at: between
            // the two the icon stays where it is, for the same reason as the slack.
            DragOffState.Lifted => beyond < 0 ? DragOffState.OnBar
                : beyond >= RemoveDistance ? DragOffState.Armed
                : DragOffState.Lifted,

            _ => beyond < 0 ? DragOffState.OnBar
                : beyond < RemoveDistance - Slack ? DragOffState.Lifted
                : DragOffState.Armed,
        };

        return State;
    }

    /// <summary>Starts again, for a new drag.</summary>
    public void Reset() => State = DragOffState.OnBar;
}
