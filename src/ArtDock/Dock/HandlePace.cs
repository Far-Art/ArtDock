namespace ArtDock.Dock;

/// <summary>
/// When the handle next reads what is behind it: fifteen times a second while that keeps still,
/// and every frame while it moves.
/// </summary>
/// <remarks>
/// <para>
/// A read is cheap for the dock and not for DWM, which copies its composed frame back for every
/// read of the screen. Measured on 2026-09-30, reads alternated with none on an idle machine:
/// DWM went from 12.2% of one core to 13.9 at fifteen reads a second and 23.3 at every frame of
/// the main display's 120 Hz — about 0.09 ms of DWM a read. The handle is up all day under a
/// dock that is hidden all day, and what is behind it is usually a status bar or a scroll bar
/// that does not move, so reading every frame is kept for when something does.
/// </para>
/// <para>
/// Fifteen a second alone was what the handle did until then, and it was reported as not
/// following what moved behind it: over a scrolling page or a video it changed in steps of a
/// fifteenth of a second, each shown — by working it out, since the handle cannot be captured —
/// about 60 ms late on average and up to 100.
/// </para>
/// <para>
/// Moving is two changes within <see cref="Settle"/> of each other, and it lasts until what is
/// behind has kept still for that long. A single change — a caret blinking, a clock ticking over,
/// a status bar's text — gets one read straight after it, to see whether it is the start of
/// something, and no more: a caret blinking under the handle would otherwise have it reading
/// every frame nearly all the time. So the first change of something that moves is shown at
/// the quiet pace, up to a fifteenth of a second late, and everything after it at the next
/// frame.
/// </para>
/// <para>
/// Pure, and fed the time, so it can be tested without a screen. Not thread-safe: one reading
/// thread owns one.
/// </para>
/// </remarks>
public sealed class HandlePace
{
    /// <summary>The wait between reads while what is behind keeps still: about fifteen reads a second.</summary>
    public static readonly TimeSpan StillInterval = TimeSpan.FromMilliseconds(66);

    /// <summary>
    /// The shortest time from one read to the next while what is behind moves — a frame at 120 Hz.
    /// </summary>
    /// <remarks>
    /// A read waits for DWM's next frame, so reads made back to back are already one a frame, and
    /// this is not usually waited for. It is a floor for a read that did not wait — in a remote
    /// session, say — which would otherwise have the thread spinning; and it keeps a display
    /// faster than 120 Hz from doubling the cost.
    /// </remarks>
    public static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(8);

    /// <summary>
    /// How close two changes have to be to count as movement, and how long it has to keep still
    /// before the reads slow down again.
    /// </summary>
    /// <remarks>
    /// Longer than the gap between the frames of anything that moves smoothly enough to matter —
    /// a 24 fps video changes every 42 ms, a 10 fps animation every 100 — and shorter than a
    /// caret's blink, about half a second.
    /// </remarks>
    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(200);

    private TimeSpan? _latest;
    private TimeSpan? _before;

    /// <summary>How long to wait before the next read.</summary>
    /// <param name="now">When the read just made finished, on a clock that only goes forward.</param>
    /// <param name="changed">Whether that read differed from the one before it.</param>
    /// <param name="took">How long that read took.</param>
    public TimeSpan Next(TimeSpan now, bool changed, TimeSpan took)
    {
        if (changed)
        {
            _before = _latest;
            _latest = now;
        }

        var moving = changed
            || (_latest is { } latest && _before is { } before
                && latest - before <= Settle && now - latest <= Settle);

        if (!moving)
        {
            return StillInterval;
        }

        var rest = FrameInterval - took;
        return rest > TimeSpan.Zero ? rest : TimeSpan.Zero;
    }
}
