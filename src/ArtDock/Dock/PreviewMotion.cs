using System.Windows;

namespace ArtDock.Dock;

/// <summary>A CSS-style cubic Bézier easing: the curve from (0, 0) to (1, 1) through two control points.</summary>
/// <param name="X1">The first control point's time, 0 to 1.</param>
/// <param name="Y1">The first control point's progress.</param>
/// <param name="X2">The second control point's time, 0 to 1.</param>
/// <param name="Y2">The second control point's progress.</param>
public readonly record struct CubicBezier(double X1, double Y1, double X2, double Y2)
{
    /// <summary>The progress at a time, both 0 to 1; outside that the ends.</summary>
    public double Ease(double t)
    {
        if (t <= 0)
        {
            return 0;
        }

        if (t >= 1)
        {
            return 1;
        }

        // The curve's parameter for this time: Newton's method from the time itself, which
        // converges in a few steps on any curve whose time runs forward, and halving the interval
        // where the slope is too flat for it to.
        var s = t;
        for (var i = 0; i < 8; i++)
        {
            var error = Coordinate(s, X1, X2) - t;
            if (Math.Abs(error) < 1e-7)
            {
                return Coordinate(s, Y1, Y2);
            }

            var slope = Slope(s, X1, X2);
            if (Math.Abs(slope) < 1e-6)
            {
                break;
            }

            s -= error / slope;
        }

        double low = 0, high = 1;
        s = t;
        for (var i = 0; i < 40; i++)
        {
            var x = Coordinate(s, X1, X2);
            if (Math.Abs(x - t) < 1e-7)
            {
                break;
            }

            if (x < t)
            {
                low = s;
            }
            else
            {
                high = s;
            }

            s = (low + high) / 2;
        }

        return Coordinate(s, Y1, Y2);
    }

    private static double Coordinate(double s, double p1, double p2)
    {
        var u = 1 - s;
        return (3 * u * u * s * p1) + (3 * u * s * s * p2) + (s * s * s);
    }

    private static double Slope(double s, double p1, double p2)
    {
        var u = 1 - s;
        return (3 * u * u * p1) + (6 * u * s * (p2 - p1)) + (3 * s * s * (1 - p2));
    }
}

/// <summary>A window's picture cut to what can be seen of it: where it goes and which part of the window it shows.</summary>
/// <param name="Destination">The part of the picture's rectangle that shows, in the panel's pixels.</param>
/// <param name="Source">The part of the window that shows there, in the window's own pixels.</param>
public readonly record struct PictureCut(Int32Rect Destination, Int32Rect Source);

/// <summary>
/// How the window previews move: the card gliding and reshaping from one app's windows to
/// another's, as Windows 11's taskbar moves its own, and rising and fading in as it opens and
/// fading out as it closes.
/// </summary>
/// <remarks>
/// <para>
/// The glide is measured, not remembered: the taskbar's previews recorded at 120 Hz, every frame,
/// on 2026-11-20, and the curve fitted to where the card was on each (TODO.md item 25). It is the
/// same length whatever the distance — 47 pixels or 309 took the same time — and moves every edge
/// of the card on the one curve, so it changes shape as it goes, its bottom staying where it is.
/// The card's contents cross-fade as it glides.
/// </para>
/// <para>
/// Opening and closing are not the taskbar's, which slides its card out from behind the taskbar
/// and back: see <see cref="RiseDips"/>.
/// </para>
/// <para>
/// Pure, and in physical pixels like <see cref="PreviewLayout"/>, so it can be tested with a
/// clock that is only a number.
/// </para>
/// </remarks>
public static class PreviewMotion
{
    /// <summary>The glide from one card to another: a slow start, a fast middle and a long slow end.</summary>
    public static CubicBezier Glide { get; } = new(0.56, 0, 0, 1);

    /// <summary>How long the glide takes, in milliseconds, however far it goes.</summary>
    public const double GlideMs = 350;

    /// <summary>The rise into place as the previews open, and their fade in with it: fast, and settling slowly.</summary>
    public static CubicBezier Rise { get; } = new(0, 0, 0.1, 1);

    /// <summary>How long the rise and the fade in take.</summary>
    public const double RiseMs = 250;

    /// <summary>
    /// How far below its place the card starts its rise, in DIPs: the gap it keeps above the
    /// dock (<see cref="PreviewMetrics.Lift"/>), and no further, so nothing of it is ever cut off.
    /// </summary>
    /// <remarks>
    /// The taskbar's rise from 120 pixels down, from behind the taskbar, was tried on 2026-11-20
    /// and seen: over the dock there is nothing for the card to come out from behind, and it
    /// rose out of, and sank into, a line in mid-air. So it fades in and out, as it did before.
    /// </remarks>
    public const double RiseDips = 12;

    /// <summary>
    /// The fade out as they close, and the glide up and away from the dock that goes with it, by
    /// <see cref="RiseDips"/>: quick to start, and gone. Asked for on 2026-11-20, once seen.
    /// </summary>
    public static CubicBezier FadeOut { get; } = new(0, 0, 0.58, 1);

    /// <summary>How long the fade out takes from fully shown.</summary>
    public const double FadeOutMs = 150;

    /// <summary>How far through an animation of <paramref name="durationMs"/> it is after <paramref name="elapsedMs"/>, 0 to 1.</summary>
    public static double Progress(double elapsedMs, double durationMs) =>
        durationMs <= 0 ? 1 : Math.Clamp(elapsedMs / durationMs, 0, 1);

    /// <summary>
    /// A rectangle part of the way from one to another, each edge moved on its own and rounded to
    /// the pixel — so an edge both share stays exactly where it is.
    /// </summary>
    public static Int32Rect Between(Int32Rect from, Int32Rect to, double eased)
    {
        if (eased <= 0)
        {
            return from;
        }

        if (eased >= 1)
        {
            return to;
        }

        var left = Lerp(from.X, to.X, eased);
        var top = Lerp(from.Y, to.Y, eased);
        var right = Lerp(from.X + from.Width, to.X + to.Width, eased);
        var bottom = Lerp(from.Y + from.Height, to.Y + to.Height, eased);
        return new Int32Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>
    /// Where a set of cards laid out for a panel of <paramref name="width"/> by <paramref name="height"/>
    /// goes inside a card that may be another size: centred across it and standing on its bottom,
    /// as the card's bottom stands still while it changes shape. A set laid out for the card fills it exactly.
    /// </summary>
    public static (int X, int Y) Anchor(Int32Rect card, int width, int height) =>
        (card.X + (int)Math.Round((card.Width - width) / 2.0, MidpointRounding.AwayFromZero), card.Y + card.Height - height);

    /// <summary>
    /// A window's picture cut to a rectangle: the part of <paramref name="destination"/> inside
    /// <paramref name="clip"/>, and the part of the window that shows there. Null when none shows.
    /// </summary>
    /// <param name="destination">Where the whole picture would go.</param>
    /// <param name="clip">What can be seen.</param>
    /// <param name="source">The window's size, in its own pixels.</param>
    /// <remarks>
    /// Needed because nothing else cuts a DWM thumbnail: not the window's region, not anything the
    /// compositor draws (TODO.md item 25). The whole window, not only its client area — the size
    /// DWM reports is of the whole.
    /// </remarks>
    public static PictureCut? Cut(Int32Rect destination, Int32Rect clip, Size source)
    {
        if (destination.Width <= 0 || destination.Height <= 0 || source.Width <= 0 || source.Height <= 0)
        {
            return null;
        }

        var left = Math.Max(destination.X, clip.X);
        var top = Math.Max(destination.Y, clip.Y);
        var right = Math.Min(destination.X + destination.Width, clip.X + clip.Width);
        var bottom = Math.Min(destination.Y + destination.Height, clip.Y + clip.Height);
        if (right <= left || bottom <= top)
        {
            return null;
        }

        var shown = new Int32Rect(left, top, right - left, bottom - top);
        if (shown == destination)
        {
            return new PictureCut(shown, new Int32Rect(0, 0, (int)Math.Round(source.Width), (int)Math.Round(source.Height)));
        }

        var scaleX = source.Width / destination.Width;
        var scaleY = source.Height / destination.Height;
        var sourceLeft = (int)Math.Round((left - destination.X) * scaleX);
        var sourceTop = (int)Math.Round((top - destination.Y) * scaleY);
        var sourceRight = (int)Math.Round((right - destination.X) * scaleX);
        var sourceBottom = (int)Math.Round((bottom - destination.Y) * scaleY);
        if (sourceRight <= sourceLeft || sourceBottom <= sourceTop)
        {
            return null;
        }

        return new PictureCut(shown, new Int32Rect(sourceLeft, sourceTop, sourceRight - sourceLeft, sourceBottom - sourceTop));
    }

    private static int Lerp(int from, int to, double t) =>
        (int)Math.Round(from + ((to - from) * t), MidpointRounding.AwayFromZero);
}
