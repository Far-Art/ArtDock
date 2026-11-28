namespace ArtDock.Dock;

/// <summary>
/// Which of the display's frames the dock's frame loop works on, under a cap on its frame rate
/// (<c>DockSettings.Performance</c>).
/// </summary>
/// <remarks>
/// <para>
/// Measured 2026-11-22 on a 120 Hz display, with the dock sweeping its own wave: the full rate
/// cost about 15% of a core in the dock and 20% in DWM, 60 frames a second about 9% and 13%, and
/// 30 about 6% and 9%, with the graphics card's share falling the same way. Neither turning
/// the blur off nor the icons' shadows saved anything measurable, so the frame rate is the only
/// thing the setting changes until magnification goes altogether.
/// </para>
/// <para>
/// Meant as a whole number of the display's frames each time, with half a frame's grace either
/// way; the cap is what it is near, not under: at 144 Hz, 60 is every second frame, 72 a second.
/// </para>
/// <para>
/// It is not even on the screen, though (measured 2026-11-28, 120 Hz, the machine in use): about
/// 55 a second, the frames two and three of the display's apart by turns. With nothing drawn on
/// a frame let go by, WPF raises <c>Rendering</c> next 12 to 26 ms on, at no frame in particular.
/// Tried and dropped: running the loop from <see cref="Interop.CompositorClock"/> (51 a second,
/// the same mix — the clock itself delivered only about 91 of the 120 frames), and counting
/// frames by DWM's own record (it was seen counting backwards). Each step goes by the time it
/// is given, so the wave keeps its speed; only its steps are uneven.
/// </para>
/// </remarks>
public static class FramePacing
{
    /// <summary>
    /// The time between the frames worked on, in ms, for at most <paramref name="maxFrameRate"/>
    /// a second on a display refreshing every <paramref name="refreshMs"/>; 0 for every frame.
    /// </summary>
    public static double IntervalMs(double refreshMs, int maxFrameRate)
    {
        if (maxFrameRate <= 0 || refreshMs <= 0 || double.IsNaN(refreshMs))
        {
            return 0;
        }

        var frames = Math.Max(1, (int)Math.Round(1000.0 / maxFrameRate / refreshMs));
        return frames == 1 ? 0 : frames * refreshMs;
    }

    /// <summary>
    /// Whether a frame <paramref name="sinceLastMs"/> after the last one worked on comes too soon
    /// for <paramref name="intervalMs"/>, and is let go by.
    /// </summary>
    public static bool Skips(double sinceLastMs, double intervalMs, double refreshMs) =>
        intervalMs > 0 && sinceLastMs < intervalMs - (refreshMs / 2);
}
