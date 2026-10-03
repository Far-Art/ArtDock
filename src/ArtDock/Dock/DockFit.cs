namespace ArtDock.Dock;

/// <summary>
/// How the dock fits its display: the size its icons rest at, and how many of them are on the
/// bar rather than behind the overflow item.
/// </summary>
/// <param name="IconSize">The resting icon size, in DIPs.</param>
/// <param name="Visible">How many items, from the start of the row, are on the bar.</param>
/// <param name="Hidden">How many, from the end, are behind the overflow item.</param>
/// <param name="Preferred">The size the dock is set to, which <paramref name="IconSize"/> may be below.</param>
public sealed record DockFitResult(double IconSize, int Visible, int Hidden, double Preferred)
{
    /// <summary>True when some items are behind the overflow item.</summary>
    public bool Overflows => Hidden > 0;

    /// <summary>True when the icons are smaller than the dock is set to, to fit.</summary>
    public bool Shrunk => IconSize < Preferred;
}

/// <summary>
/// Keeps the resting bar inside its display: first by shrinking the icons, as far as a floor,
/// then by putting the last items behind an overflow item, and at the last by refusing more.
/// </summary>
/// <remarks>
/// <para>
/// The bar is fitted with room for its wave beside it (<c>waveFraction</c>, from
/// <see cref="WaveRoom"/>), so the icons at the ends stay on the display, whole, when the pointer
/// is on them. Fitted at first with the resting bar alone, a full dock's wave carried the end
/// icons past the display's sides, cut off there, and they were awkward to click — reported on
/// 2026-10-03; the whole wave's room was then too much. Nothing is kept with the wave turned off.
/// Whatever still reaches past the display — the slot a drop opens on a full dock — is cut off
/// there (<c>DockBar.OnScreen</c>).
/// </para>
/// <para>
/// The size is a whole number of DIPs, so a dock that has to shrink rests at the same size
/// every time it is fitted for the same contents, rather than at a fraction that a rounding
/// somewhere could move by a hair.
/// </para>
/// <para>
/// Pure: the room is the caller's to measure — the display's work area in its own DIPs, less
/// the margin the bar keeps from the sides, the same as from the bottom.
/// </para>
/// </remarks>
public static class DockFit
{
    /// <summary>
    /// The most items the overflow item holds before the dock refuses more. A menu longer than
    /// this is a list to scroll, not a dock.
    /// </summary>
    public const int OverflowLimit = 30;

    /// <summary>Fits <paramref name="count"/> items into <paramref name="room"/>.</summary>
    /// <param name="count">Every item on the dock, separators included: each takes a slot.</param>
    /// <param name="preferred">The size the dock is set to.</param>
    /// <param name="floor">The smallest the icons may be made; above <paramref name="preferred"/> it is taken as that.</param>
    /// <param name="gapFraction">The gap between icons, as a fraction of their size.</param>
    /// <param name="paddingX">The bar's padding at each end, which does not scale.</param>
    /// <param name="room">How wide the bar may be, in DIPs.</param>
    /// <param name="waveFraction">
    /// How much wider the widest wave makes the bar, both ends together, as a fraction of the
    /// icon size — twice <see cref="DockLayout.WaveReach"/> over the size, which it is in
    /// proportion to; 0 for a dock that does not magnify.
    /// </param>
    public static DockFitResult Compute(
        int count, double preferred, double floor, double gapFraction, double paddingX, double room,
        double waveFraction = 0)
    {
        floor = Math.Min(floor, preferred);
        if (count <= 0 || !double.IsFinite(room))
        {
            return new DockFitResult(preferred, Math.Max(0, count), 0, preferred);
        }

        var largest = LargestSize(count, gapFraction, paddingX, room, waveFraction);
        if (largest >= preferred)
        {
            return new DockFitResult(preferred, count, 0, preferred);
        }

        if (largest >= floor)
        {
            return new DockFitResult(largest, count, 0, preferred);
        }

        // At the floor, one slot goes to the overflow item, and the items that fit beside it
        // are the first ones: the row keeps its order.
        var visible = Math.Clamp(Slots(floor, gapFraction, paddingX, room, waveFraction) - 1, 0, count);
        return new DockFitResult(floor, visible, count - visible, preferred);
    }

    /// <summary>The most items the dock takes in this room: what fits at the floor, and <see cref="OverflowLimit"/> behind the overflow item.</summary>
    public static int Capacity(
        double floor, double gapFraction, double paddingX, double room, double waveFraction = 0)
    {
        if (!double.IsFinite(room))
        {
            return int.MaxValue;
        }

        var slots = Slots(floor, gapFraction, paddingX, room, waveFraction);
        return Math.Max(slots, Math.Max(0, slots - 1) + OverflowLimit);
    }

    /// <summary>How many more items may be added to a dock holding <paramref name="count"/>; never below 0.</summary>
    public static int RoomFor(
        int count, double floor, double gapFraction, double paddingX, double room, double waveFraction = 0)
    {
        var capacity = Capacity(floor, gapFraction, paddingX, room, waveFraction);
        return capacity == int.MaxValue ? int.MaxValue : Math.Max(0, capacity - Math.Max(0, count));
    }

    /// <summary>
    /// The resting bar's width for <paramref name="count"/> icons of <paramref name="size"/> —
    /// <see cref="DockLayout.RestingWidth"/>, with the gap a fraction of the size.
    /// </summary>
    public static double RestingWidth(int count, double size, double gapFraction, double paddingX) =>
        count <= 0 ? 0 : (2 * paddingX) + (count * size) + ((count - 1) * size * gapFraction);

    /// <summary>
    /// The room, in DIPs, to keep beside a fitted bar for its wave: enough that the icon at
    /// either end is whole and the bar keeps its margin while that icon is pointed at, and that
    /// the widest wave — the pointer in the middle — never takes the bar off the display, the
    /// margin <paramref name="margin"/> taking up what it can of that.
    /// </summary>
    /// <remarks>
    /// The whole wave's room (twice <see cref="DockLayout.WaveReach"/>) was kept at first, the same
    /// evening the end icons were found cut off, and was more than needed: the user asked for the
    /// dock to be let a little bigger again. With the pointer on an end icon the bar grows by only
    /// <see cref="DockLayout.EndLift"/>, and the rest of the reach falls on the margin — at the
    /// stock magnification wholly, so the bar stays on the display everywhere.
    /// </remarks>
    public static double WaveRoom(DockLayout layout, double margin) =>
        Math.Max(layout.EndLift, (2 * layout.WaveReach) - (2 * Math.Max(0, margin)));

    /// <summary>The widest the bar gets for <paramref name="count"/> icons of <paramref name="size"/>: at rest, and the wave beside it.</summary>
    public static double WidestWidth(int count, double size, double gapFraction, double paddingX, double waveFraction) =>
        count <= 0 ? 0 : RestingWidth(count, size, gapFraction, paddingX) + (size * Math.Max(0, waveFraction));

    /// <summary>The largest whole size at which <paramref name="count"/> icons fit; 0 when none does.</summary>
    private static double LargestSize(int count, double gapFraction, double paddingX, double room, double waveFraction)
    {
        var span = count + ((count - 1) * gapFraction) + Math.Max(0, waveFraction);
        var size = Math.Floor(((room - (2 * paddingX)) / span) + 1e-9);
        return Math.Max(0, size);
    }

    /// <summary>How many icons of <paramref name="size"/> fit across the room.</summary>
    private static int Slots(double size, double gapFraction, double paddingX, double room, double waveFraction)
    {
        if (size <= 0)
        {
            return 0;
        }

        // 2p + n·s + (n − 1)·s·g + s·w ≤ room, solved for n.
        var pitch = size * (1 + gapFraction);
        var wave = size * Math.Max(0, waveFraction);
        var slots = Math.Floor(((room - (2 * paddingX) + (size * gapFraction) - wave) / pitch) + 1e-9);
        return slots <= 0 ? 0 : (int)Math.Min(slots, int.MaxValue / 2);
    }
}
