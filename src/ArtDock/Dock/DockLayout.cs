namespace ArtDock.Dock;

/// <summary>
/// Turns a pointer position into icon sizes and positions.
///
/// An icon's size comes from how near the pointer is to its <b>resting</b> centre; its
/// position comes from a running total over the magnified widths, so growing icons push
/// their neighbours aside and the row spreads under the pointer.
/// </summary>
/// <remarks>
/// <para>
/// Sizes are measured from resting centres, never from live ones. Feeding an icon's current
/// position back into the function that decides its size is what makes this effect wobble;
/// the web original has to snapshot resting centres and freeze a coordinate anchor to avoid
/// exactly that. Computing them arithmetically means there is nothing to snapshot.
/// </para>
/// <para>
/// The spreading is the point, but the dock changing size is not. What keeps the two apart is
/// the falloff: a raised cosine spanning whole icon pitches sums to a constant across
/// neighbours, so the widths always total the same however the pointer moves between icons.
/// </para>
/// <para>
/// All x-coordinates are relative to the <b>resting</b> bar's left edge — except those of
/// the helpers at the end, which place that edge in the first place.
/// </para>
/// </remarks>
public sealed class DockLayout(DockMetrics metrics)
{
    public DockMetrics Metrics { get; } = metrics;

    /// <summary>Centre of the icon at <paramref name="index"/> while the dock is at rest.</summary>
    public double RestingCentre(int index) =>
        Metrics.PaddingX + (index * (Metrics.BaseSize + Metrics.Gap)) + (Metrics.BaseSize / 2);

    /// <summary>Width of the bar with every icon at its resting size.</summary>
    public double RestingWidth(int count) =>
        count <= 0
            ? 0
            : (Metrics.PaddingX * 2) + (count * Metrics.BaseSize) + ((count - 1) * Metrics.Gap);

    /// <summary>
    /// Magnified size of every icon, in visual order.
    /// </summary>
    /// <param name="count">Number of icons.</param>
    /// <param name="pointerX">
    /// Pointer position in the resting frame, or <see langword="null"/> when the pointer is away.
    /// </param>
    /// <param name="progress">
    /// Wave amplitude from 0 (flat) to 1 (full). This is the entry/exit ramp: only the
    /// amplitude is animated, never the pointer position, so moving the cursor mid-ramp
    /// neither restarts nor stalls the animation.
    /// </param>
    public double[] Sizes(int count, double? pointerX, double progress = 1)
    {
        var sizes = new double[Math.Max(0, count)];
        if (sizes.Length == 0)
        {
            return sizes;
        }

        if (pointerX is not { } pointer || progress <= 0)
        {
            Array.Fill(sizes, Metrics.BaseSize);
            return sizes;
        }

        var clamped = Math.Clamp(progress, 0, 1);
        for (var i = 0; i < sizes.Length; i++)
        {
            var target = DockMagnify.MagnifiedSize(
                Math.Abs(pointer - RestingCentre(i)),
                Metrics.EffectiveInfluenceRange,
                Metrics.BaseSize,
                Metrics.MaxSize);
            sizes[i] = Metrics.BaseSize + ((target - Metrics.BaseSize) * clamped);
        }

        return sizes;
    }

    /// <summary>
    /// Left edge of every icon, given their magnified sizes — a running total, so icons
    /// spread apart as their neighbours grow.
    /// </summary>
    /// <remarks>
    /// The spreading is the effect, not a side effect of it: an icon growing pushes its
    /// neighbours aside, which is what makes the row read as a wave rather than as icons
    /// inflating in place. What must not vary is the *total*, and that is the falloff's
    /// job — see <see cref="DockMagnify.MagnifiedSize"/>. With a raised cosine spanning
    /// whole pitches the widths sum to a constant, so the row spreads without the dock
    /// changing size.
    /// </remarks>
    public double[] Offsets(IReadOnlyList<double> sizes)
    {
        var offsets = new double[sizes.Count];
        var x = Metrics.PaddingX;
        for (var i = 0; i < sizes.Count; i++)
        {
            offsets[i] = x;
            x += sizes[i] + Metrics.Gap;
        }

        return offsets;
    }

    /// <summary>Width the icons occupy for the given sizes, gaps included.</summary>
    public double IconBlockWidth(IReadOnlyList<double> sizes)
    {
        if (sizes.Count == 0)
        {
            return 0;
        }

        var total = 0d;
        for (var i = 0; i < sizes.Count; i++)
        {
            total += sizes[i];
        }

        return total + ((sizes.Count - 1) * Metrics.Gap);
    }

    /// <summary>
    /// Memo for <see cref="MaxLift"/>: finding it sweeps the pointer across the bar, and
    /// it is read every frame, but the metrics are fixed for this instance's lifetime.
    /// </summary>
    private (int Count, double Lift)? _maxLift;

    /// <summary>
    /// The widest the bar ever gets for this many icons. The window is sized to this once,
    /// so a magnifying wave never has to resize it.
    /// </summary>
    public double MaxBarWidth(int count) => RestingWidth(count) + MaxLift(count);

    /// <summary>
    /// The most total width magnification ever adds, over any pointer position.
    /// </summary>
    /// <remarks>
    /// Used only to size the window, which must be wide enough for the widest the bar will
    /// ever be. The bar's live width tracks the icons instead — see <see cref="BarWidth"/>.
    /// </remarks>
    public double MaxLift(int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        if (_maxLift is { } cached && cached.Count == count)
        {
            return cached.Lift;
        }

        var resting = count * Metrics.BaseSize;
        var widest = 0d;
        var span = RestingWidth(count);
        for (var pointer = 0d; pointer <= span; pointer += 1)
        {
            var sizes = Sizes(count, pointer);
            var total = 0d;
            foreach (var size in sizes)
            {
                total += size;
            }

            widest = Math.Max(widest, total - resting);
        }

        _maxLift = (count, widest);
        return widest;
    }

    /// <summary>
    /// The bar's width for the icons it currently holds — padding plus whatever they
    /// occupy, so the bar tracks them as they grow.
    /// </summary>
    /// <remarks>
    /// Safe to derive from the icons only because the raised-cosine falloff sums to a
    /// constant across the middle of the dock: the bar therefore holds steady as the
    /// pointer moves between icons, rather than pulsing at the icon spacing the way it
    /// did under a plain cosine. Near the ends it does narrow slightly — an icon there has
    /// fewer neighbours to lift, so the row really is narrower.
    ///
    /// The dock no longer sizes its bar from this, for that last reason: see
    /// <see cref="SteadyBarWidth"/>. This remains what the icons actually occupy, which is
    /// what they are centred within.
    /// </remarks>
    public double BarWidth(IReadOnlyList<double> sizes) =>
        sizes.Count == 0 ? 0 : (Metrics.PaddingX * 2) + IconBlockWidth(sizes);

    /// <summary>
    /// The bar's width for a wave of the given amplitude, whatever the pointer is doing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Depends on the amplitude and not on the pointer, which is the whole point: the width
    /// then changes only during the entry and exit ramps and holds perfectly still for as
    /// long as the wave is up.
    /// </para>
    /// <para>
    /// <see cref="BarWidth"/> — the bar wrapped tightly around whatever the icons occupy
    /// right now — is nearly this already, because the falloff sums to a constant across the
    /// middle of the dock. Nearly is the problem. Near the ends it narrows by a few pixels
    /// over a few hundred milliseconds, which is under a pixel per frame, and the acrylic
    /// sheet behind the bar is a separate window that can only be moved in whole pixels. A
    /// sub-pixel drift rendered in whole pixels is a visible stutter, and it was the one
    /// part of the dock that did not move smoothly.
    /// </para>
    /// <para>
    /// The cost is a few pixels of extra padding at the ends of the row, where the icons no
    /// longer quite fill the bar. That is far less noticeable than the alternative, and it
    /// leaves the sheet behind the bar completely still while the wave runs.
    /// </para>
    /// </remarks>
    public double SteadyBarWidth(int count, double amplitude) =>
        count <= 0
            ? 0
            : RestingWidth(count) + (MaxLift(count) * Math.Clamp(amplitude, 0, 1));

    /// <summary>
    /// Where an icon's middle is drawn while a full wave is parked on it, from the resting bar's
    /// left edge.
    /// </summary>
    /// <remarks>
    /// Its resting centre in the middle of a long row, where the wave lifts as much on one side
    /// as the other. Towards an end it is not: the icon has fewer neighbours on that side to push
    /// away, the row grows mostly the other way, and growing about its middle carries the icon
    /// outward, by up to half its own growth. What the window previews are centred on, so they
    /// stand over the icon as it is drawn rather than over where it rests.
    /// </remarks>
    public double HeldCentre(int count, int index)
    {
        if (count <= 0 || index < 0 || index >= count)
        {
            return 0;
        }

        var sizes = Sizes(count, RestingCentre(index));
        var offsets = Offsets(sizes);
        var lift = BarWidth(sizes) - RestingWidth(count);
        return offsets[index] + (sizes[index] / 2) - (lift / 2);
    }

    /// <summary>
    /// Holds a dragged icon's left edge inside the bar.
    /// </summary>
    /// <remarks>
    /// The reorder drag snaps the icon to the pointer rather than easing it, so that it
    /// tracks the cursor exactly — and that let a drag which wandered carry the icon clean
    /// off the bar and out of the dock. It was never a gesture: dragging an icon off no
    /// longer unpins it, so an icon outside the bar is only an icon somewhere it cannot be
    /// dropped, still reordering a row it is no longer over. The slot it would land in was
    /// already clamped; this clamps what the eye follows to match.
    ///
    /// The bounds are the bar's own padding, so the icon comes to rest exactly where the end
    /// slot sits rather than flush against the bar's edge.
    /// </remarks>
    /// <param name="wantedLeft">Where the pointer would put the icon's left edge.</param>
    /// <param name="iconSize">The icon's current, possibly magnified, width.</param>
    public double ClampIconToBar(double wantedLeft, double iconSize, double barLeft, double barWidth)
    {
        var min = barLeft + Metrics.PaddingX;
        var max = barLeft + barWidth - Metrics.PaddingX - iconSize;

        // A bar narrower than the icon it holds has no range to clamp into, and Math.Clamp
        // throws outright when its bounds cross. Only reachable for a frame while the bar is
        // still opening over an icon that has just arrived, but a throw either way.
        return max >= min ? Math.Clamp(wantedLeft, min, max) : min;
    }

    /// <summary>
    /// The horizontal span that counts as "on the dock": the bar at this amplitude, grown
    /// about the resting bar's middle, with half a gap of slack at each end.
    /// </summary>
    /// <param name="restingLeft">
    /// Where the resting bar starts, in whatever frame the span is wanted in — see
    /// <see cref="RestingLeft"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// Takes no pointer, and that is the whole of it. The bar's live width —
    /// <see cref="BarWidth"/> — narrows near the ends of a wave, because an icon there has
    /// fewer neighbours to lift, so a zone measured from it moves whenever the wave moves.
    /// A stationary pointer near either end then falls in and out of the dock as the wave
    /// comes and goes: it captures the wave, the bar narrows out from under it, it is
    /// outside again, and whatever else was driving the wave takes over and widens the bar
    /// back over it. With the settings dialog up — where the wave is driven by the
    /// demonstration sweep, or parked on the middle icon — that ran at a few hertz, and on
    /// alternate frames for the parked case.
    /// </para>
    /// <para>
    /// <see cref="SteadyBarWidth"/> depends on the amplitude alone, and the amplitude only
    /// rises while the pointer is already inside, so the boundary can no longer be moved out
    /// from under it. It is also never narrower than the icons occupy, so the span always
    /// covers the bar as it is drawn.
    /// </para>
    /// </remarks>
    public (double Left, double Right) HoverSpan(int count, double amplitude, double restingLeft)
    {
        var width = SteadyBarWidth(count, amplitude);

        // The wave grows the bar about the resting one's middle, wherever along its edge the
        // dock has been put — so half of what it adds lies past each end.
        var left = restingLeft - ((width - RestingWidth(count)) / 2);
        var slack = Metrics.Gap / 2;

        return (left - slack, left + width + slack);
    }

    // ---- where the row sits --------------------------------------------------

    /// <summary>
    /// How far a full wave carries each end of the bar past where it rests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Half of what a wave adds to a row long enough to hold all of it. The raised cosine
    /// sums to a constant over whole pitches, so that is exactly the growth of the icon under
    /// the pointer times the number of pitches the influence spans — no sweep needed. A
    /// shorter row lifts by less, never by more, so this bounds every dock whatever it holds.
    /// </para>
    /// <para>
    /// It is the room a dock pushed to one end of its edge keeps at that end: see
    /// <see cref="RestingLeft"/> for why the wave is given room rather than being held back.
    /// Closed-form rather than <see cref="MaxLift"/> of the dock's own count on purpose.
    /// <see cref="MaxLift"/> grows with the count until the row can hold a whole wave, and a
    /// room that grew with it would move a pushed dock every time a short row gained an icon.
    /// </para>
    /// </remarks>
    public double WaveReach
    {
        get
        {
            var pitch = Metrics.Pitch;
            if (pitch <= 0)
            {
                return 0;
            }

            var pitches = Metrics.EffectiveInfluenceRange / pitch;
            return Math.Max(0, Metrics.MaxSize - Metrics.BaseSize) * pitches / 2;
        }
    }

    /// <summary>
    /// How much a full wave widens the bar with the pointer on the icon at one end of a row long
    /// enough to hold it — less than twice <see cref="WaveReach"/>, since the end icon has
    /// neighbours to lift on one side only.
    /// </summary>
    /// <remarks>
    /// The bar grows about its middle, so half of this is how far past its resting end the bar
    /// goes while that end's icon is the one being pointed at — what has to stay on the display
    /// for the icon to be whole when it is clicked (<see cref="DockFit.WaveRoom"/>).
    /// </remarks>
    public double EndLift
    {
        get
        {
            var pitch = Metrics.Pitch;
            if (pitch <= 0)
            {
                return 0;
            }

            var count = (2 * (int)Math.Ceiling(Metrics.EffectiveInfluenceRange / pitch)) + 2;
            var sizes = Sizes(count, RestingCentre(0));
            var total = 0d;
            foreach (var size in sizes)
            {
                total += size;
            }

            return Math.Max(0, total - (count * Metrics.BaseSize));
        }
    }

    /// <summary>
    /// Where something <paramref name="width"/> wide starts when it is placed
    /// <paramref name="alignment"/> of the way across the free room in a span.
    /// </summary>
    /// <remarks>
    /// 0 is flush with the span's start, 1 flush with its end and ½ centred. Placing a box
    /// inside a box this way composes: a row aligned within the window, and the window aligned
    /// the same way within the screen, puts the row exactly where aligning it within the
    /// screen directly would — whatever size the window is. That is what keeps a dock that
    /// has been moved along its edge still while its window grows underneath it, which the
    /// settings dialog makes it do on purpose.
    /// </remarks>
    public static double Align(double start, double length, double width, double alignment) =>
        start + (Math.Clamp(alignment, 0, 1) * (length - width));

    /// <summary>
    /// Where the resting bar starts, for a row <paramref name="restingWidth"/> wide placed
    /// <paramref name="alignment"/> of the way along a span.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The row is anchored rather than centred: whatever changes its width — an icon added or
    /// removed, the slot a drop preview opens, the bar easing between the two — moves its ends
    /// in the proportion the alignment sets. Centred, that is half each way, which is the dock
    /// as it always was. Pushed to one end of the edge, the row grows away from that end
    /// rather than into it, so the end stays where it was put and nothing arrives off the
    /// screen.
    /// </para>
    /// <para>
    /// The wave is not anchored, and must not be. It grows about the resting row's middle
    /// exactly as it does on a centred dock, because a wave held against one end has to push
    /// the icon under the pointer away from it — by half the wave's growth in the middle of
    /// the row, which at a strong magnification is more than half an icon, and puts the
    /// pointer over a different icon from the one magnified. So the row keeps
    /// <see cref="WaveReach"/> of room at each end, and it is the row with that room that is
    /// aligned. A dock pushed all the way along therefore rests the reach short of the end,
    /// and its widest wave just meets it.
    /// </para>
    /// <para>
    /// Unless that would take the resting row out of its <paramref name="room"/> — the width the
    /// display leaves the bar, which a dock fitted to it (<see cref="DockFit"/>) all but fills.
    /// The reach then gives way, as far as nothing: the resting row is what must stay on the
    /// display, and the wave is cut off at its edge. It is given way at both ends alike, so
    /// placing the row within the window and the window within the screen still composes.
    /// </para>
    /// </remarks>
    /// <param name="room">
    /// How wide the resting row may be, in the same units — unlimited when not given.
    /// </param>
    public double RestingLeft(
        double restingWidth, double start, double length, double alignment,
        double room = double.PositiveInfinity)
    {
        var reach = ReachWithin(restingWidth, room);
        return Align(start, length, restingWidth + (2 * reach), alignment) + reach;
    }

    /// <summary>
    /// The room kept at each end for the wave, for a resting row of
    /// <paramref name="restingWidth"/> that must stay inside <paramref name="room"/>:
    /// <see cref="WaveReach"/>, or as much of it as the room leaves.
    /// </summary>
    public double ReachWithin(double restingWidth, double room) =>
        double.IsNaN(room)
            ? WaveReach
            : Math.Clamp((room - restingWidth) / 2, 0, WaveReach);
}
