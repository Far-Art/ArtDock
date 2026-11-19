namespace ArtDock.Dock;

/// <summary>
/// How auto-hide's slide moves: how long each way takes, and the curve it follows.
/// </summary>
/// <remarks>
/// <para>
/// Until 2026-11-20 both ways were 220 ms of cubic ease-out, run by a WPF
/// <c>DoubleAnimation</c> on the window's <c>Top</c> — asked to be slower and smoother. The cubic
/// set off at three times its average speed and spent its last third crawling, so the dock
/// seemed to jump and then settle; the sines below set off at half that pace or from rest. And a
/// WPF animation's clock runs at 60 frames a second unless told otherwise, so on the 120 Hz
/// display the window moved on every other frame — the slide is driven a frame at a time now
/// (<c>AutoHideController.Slide</c>), at whatever rate the display has.
/// </para>
/// <para>
/// The reveal is the quicker of the two, being an answer to the pointer; the dock going away can
/// take its time.
/// </para>
/// </remarks>
public static class DockSlide
{
    /// <summary>The slide back on screen, at the stock pace.</summary>
    public static readonly TimeSpan RevealDuration = TimeSpan.FromMilliseconds(280);

    /// <summary>
    /// The slide away, at the stock pace. The handle fades in over the same time, as the dock goes.
    /// </summary>
    public static readonly TimeSpan HideDuration = TimeSpan.FromMilliseconds(320);

    /// <summary>The slowest the Behaviour page offers, as a percentage of the stock pace.</summary>
    public const int SlowestSpeed = 50;

    /// <summary>The fastest the Behaviour page offers, as a percentage of the stock pace.</summary>
    public const int FastestSpeed = 200;

    /// <summary>
    /// The duration of the slide either way, at <paramref name="speed"/> percent of the stock
    /// pace (<c>DockSettings.SlideSpeed</c>), held to the slider's range — a hand-edited 0 would
    /// otherwise never arrive.
    /// </summary>
    public static TimeSpan Duration(bool reveal, int speed = 100) =>
        (reveal ? RevealDuration : HideDuration) * (100.0 / Math.Clamp(speed, SlowestSpeed, FastestSpeed));

    /// <summary>
    /// How far along the way the dock is, 0 to 1, at <paramref name="t"/>, the share of the
    /// slide's time gone, 0 to 1.
    /// </summary>
    /// <remarks>
    /// Coming up, a sine ease-out: on its way at once, at about one and a half times its average
    /// speed, and coming to rest without a jolt. Going away, a sine ease-in-out: from rest, so
    /// nothing lurches as the pointer leaves, and out at the same gentle pace — the end of it is
    /// mostly off the screen.
    /// </remarks>
    public static double Progress(bool reveal, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return reveal
            ? Math.Sin(t * Math.PI / 2)
            : (1 - Math.Cos(t * Math.PI)) / 2;
    }
}
