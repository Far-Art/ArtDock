using System.Windows;
using System.Windows.Media;

namespace ArtDock.Dock;

/// <summary>The shape of the bar's corners. The order is the settings picker's.</summary>
public enum BarCorners
{
    /// <summary>
    /// A rounded rectangle with continuous curvature: each corner eases off the straight edge,
    /// runs through a circular arc of the radius, and eases back on — no seam where edge meets
    /// arc, as a plain quarter circle leaves. A stadium at full roundness.
    /// </summary>
    /// <remarks>
    /// Plain quarter circles until 2026-11-28, when the smooth corner, tried as a shape of its
    /// own, was liked better and took round's place under its name.
    /// </remarks>
    Round,

    /// <summary>
    /// Each corner cut off in a straight line. At full roundness the cuts meet in the middle of
    /// each end, and the end comes to a point.
    /// </summary>
    Beveled
}

/// <summary>What an outline is traced into: WPF's geometry, or a Direct2D path for the compositor.</summary>
public interface IOutlineSink
{
    void Begin(Point start);

    void LineTo(Point end);

    void BezierTo(Point handle1, Point handle2, Point end);
}

/// <summary>
/// The bar's outline, as a closed figure of four straight edges and four corners of cubic
/// curves — described once, for the dock to draw with WPF and for the sheet behind it to draw
/// with the compositor, which must agree.
/// </summary>
/// <remarks>
/// <para>
/// Neither side's own rounded rectangle will do even for round corners, which are smoothed and
/// not plain circles. A round corner is three cubics (<see cref="TraceRounded"/>); a bevel is
/// one straight cut per corner (<see cref="Corners"/>), drawn as a cubic whose handles sit on
/// its ends.
/// </para>
/// <para>
/// Every corner reaches as far along one side as along the other (<see cref="Extent"/>). They
/// used to reach the radius times a shape's own factor along each side, cut off at what that
/// side had room for — and since the bar is far wider than it is tall, at high roundness the
/// corners went on growing along the bar after they had run out of height, and came out
/// stretched sideways: the squircle and scooped corners the picker offered for a while came out
/// as a long blunt nose and a shallow ellipse.
/// </para>
/// </remarks>
public static class BarOutline
{
    /// <summary>
    /// How much of a round corner is eased rather than circular, 0 to 1: Figma's "corner
    /// smoothing", at the 60% that is the usual match for the continuous corners Apple draws.
    /// </summary>
    public const double Smoothing = 0.6;

    /// <summary>One corner: the curve's start, its two handles and its end.</summary>
    public readonly record struct Corner(Point Start, Point Handle1, Point Handle2, Point End);

    /// <summary>
    /// Traces the bar's outline, clockwise from the left end of the top-left corner, as one
    /// closed figure: the caller closes it.
    /// </summary>
    public static void Trace<TSink>(Rect bounds, double radius, BarCorners shape, ref TSink sink)
        where TSink : IOutlineSink
    {
        if (shape == BarCorners.Round)
        {
            TraceRounded(bounds, radius, Smoothing, ref sink);
            return;
        }

        Span<Corner> corners = stackalloc Corner[4];
        Corners(bounds, radius, shape, corners);

        sink.Begin(corners[0].Start);
        for (var i = 0; i < 4; i++)
        {
            var corner = corners[i];
            if (i > 0)
            {
                sink.LineTo(corner.Start);
            }

            sink.BezierTo(corner.Handle1, corner.Handle2, corner.End);
        }
    }

    /// <summary>
    /// Traces rounded corners, smoothed by <paramref name="smoothing"/>: each a cubic that eases
    /// off the straight edge, a circular arc of the radius, and a cubic that eases onto the next
    /// edge. With no smoothing, the corners are plain quarter circles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Figma's construction (<c>figma-squircle</c>), which is public and the usual stand-in for
    /// Apple's own. A corner of radius r with smoothing ξ starts <c>(1 + ξ)·r</c> from the tip
    /// rather than r: the extra length is where the curvature ramps up from nothing to the arc's.
    /// The arc itself is shortened to <c>90°·(1 - ξ)</c>, and the easing cubics take the rest of
    /// the turn either side of it.
    /// </para>
    /// <para>
    /// Where the side has no room for the extra length — half the bar's short side, which the
    /// radius reaches at full roundness — the smoothing gives way, rather than the corner
    /// stretching along the bar: so the bar grows into a plain stadium at full roundness.
    /// </para>
    /// </remarks>
    public static void TraceRounded<TSink>(Rect bounds, double radius, double smoothing, ref TSink sink)
        where TSink : IOutlineSink
    {
        var width = Math.Max(0, bounds.Width);
        var height = Math.Max(0, bounds.Height);
        var budget = Math.Min(width, height) / 2;
        var r = Math.Clamp(radius, 0, budget);
        var (a, b, c, d, p, arc, eased) = SmoothCorner(r, smoothing, budget);

        var left = bounds.X;
        var top = bounds.Y;
        var right = left + width;
        var bottom = top + height;

        // The tip of each corner, the way along the edge into it, and the way along the edge out.
        ReadOnlySpan<(Point Tip, Vector In, Vector Out)> tips =
        [
            (new Point(left, top), new Vector(0, -1), new Vector(1, 0)),
            (new Point(right, top), new Vector(1, 0), new Vector(0, 1)),
            (new Point(right, bottom), new Vector(0, 1), new Vector(-1, 0)),
            (new Point(left, bottom), new Vector(-1, 0), new Vector(0, -1))
        ];

        // The arc as one cubic, from the tangent the first easing leaves it to the one the second
        // takes up: handles of 4/3·tan(θ/4) of the radius, θ being the turn it makes — with the
        // smoothing the side had room for, which near full roundness is less than was asked.
        var turn = (Math.PI / 2) * (1 - eased);
        var reach = 4.0 / 3.0 * Math.Tan(turn / 4) * r;
        var tangent = c + d > 1e-9 ? new Vector(c, d) / Math.Sqrt((c * c) + (d * d)) : new Vector(1, 0);

        for (var i = 0; i < 4; i++)
        {
            var (tip, into, onto) = tips[i];
            var start = tip - (p * into);
            Point At(double along, double across) => start + (along * into) + (across * onto);

            if (i == 0)
            {
                sink.Begin(start);
            }
            else
            {
                sink.LineTo(start);
            }

            // Easing on: straight along the edge, curving towards the arc.
            sink.BezierTo(At(a, 0), At(a + b, 0), At(a + b + c, d));

            // The arc: from its start, tangent (c, d), to its end, tangent (d, c).
            var arcStart = At(a + b + c, d);
            var arcEnd = At(a + b + c + arc, d + arc);
            sink.BezierTo(
                arcStart + (reach * ((tangent.X * into) + (tangent.Y * onto))),
                arcEnd - (reach * ((tangent.Y * into) + (tangent.X * onto))),
                arcEnd);

            // Easing off, onto the next edge.
            var along = a + b + c + arc + d;
            sink.BezierTo(At(along, d + arc + c), At(along, d + arc + b + c), At(along, p));
        }
    }

    /// <summary>
    /// The lengths a smooth corner is built from, as Figma names them: the easing cubics' a, b,
    /// c and d, how far from the tip the corner starts (p), the arc's run along each side, and
    /// the smoothing the side had room for.
    /// </summary>
    private static (double A, double B, double C, double D, double P, double Arc, double Smoothing) SmoothCorner(
        double radius, double smoothing, double budget)
    {
        if (radius <= 0)
        {
            return default;
        }

        // No more smoothing than the side has room for.
        smoothing = Math.Clamp(Math.Min(smoothing, (budget / radius) - 1), 0, 1);
        var p = Math.Min((1 + smoothing) * radius, budget);

        static double Radians(double degrees) => degrees * Math.PI / 180;

        // The arc's run along each side: its chord, 2r·sin(θ/2), lies on the diagonal.
        var arcMeasure = 90 * (1 - smoothing);
        var arc = Math.Sin(Radians(arcMeasure / 2)) * radius * Math.Sqrt(2);
        var alpha = (90 - arcMeasure) / 2;
        var p3ToP4 = radius * Math.Tan(Radians(alpha / 2));
        var beta = 45 * smoothing;
        var c = p3ToP4 * Math.Cos(Radians(beta));
        var d = c * Math.Tan(Radians(beta));

        var b = (p - arc - c - d) / 3;
        return (2 * b, b, c, d, p, arc, smoothing);
    }

    /// <summary>
    /// The four corners of a beveled bar, clockwise from the top-left, each a straight cut drawn as
    /// a cubic. Each edge runs straight from one corner's <see cref="Corner.End"/> to the next
    /// one's <see cref="Corner.Start"/>. Round corners are three curves each, which
    /// <see cref="Trace"/> draws.
    /// </summary>
    public static void Corners(Rect bounds, double radius, BarCorners shape, Span<Corner> corners)
    {
        if (shape == BarCorners.Round)
        {
            throw new ArgumentException("Round corners are three curves each: trace them.", nameof(shape));
        }

        var width = Math.Max(0, bounds.Width);
        var height = Math.Max(0, bounds.Height);
        radius = Math.Max(0, radius);

        var left = bounds.X;
        var top = bounds.Y;
        var right = left + width;
        var bottom = top + height;

        // Each corner is drawn in its own frame: from a point on one edge, round to a point on
        // the next, measured from the corner's tip. (sx, sy) points from the tip into the bar.
        ReadOnlySpan<(double X, double Y, double Sx, double Sy, bool Vertical)> tips =
        [
            (left, top, 1, 1, true),      // top-left: from the left edge onto the top
            (right, top, -1, 1, false),   // top-right: from the top down onto the right
            (right, bottom, -1, -1, true), // bottom-right: from the right onto the bottom
            (left, bottom, 1, -1, false)  // bottom-left: from the bottom up onto the left
        ];

        var ex = Extent(radius, shape, width, height);
        var ey = ex;

        for (var i = 0; i < 4; i++)
        {
            var (tx, ty, sx, sy, vertical) = tips[i];

            // In the corner's frame: from the point on the edge it comes from to the one on the
            // edge it goes to, the handles on its ends.
            Point At(double x, double y) => new(tx + (sx * x), ty + (sy * y));

            var start = vertical ? At(0, ey) : At(ex, 0);
            var end = vertical ? At(ex, 0) : At(0, ey);
            corners[i] = new Corner(start, start, end, end);
        }
    }

    /// <summary>
    /// The room to keep between the bar's ends and its end icons: the metrics' own, or more for a
    /// bevel deep enough to cut towards the icons.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Enough that the end icon's nearer corner sits as far from the bevel's slant as the icons
    /// sit from the bar's top and bottom (<see cref="DockMetrics.PaddingY"/>). The corner is
    /// PaddingY in from the long side, where a cut reaching e along it has come
    /// <c>e - PaddingY</c> in from the end; keeping a perpendicular PaddingY from a 45° slant
    /// takes <c>√2·PaddingY</c> more, so <c>e + (√2 - 1)·PaddingY</c> in all.
    /// </para>
    /// <para>
    /// Asked for on 2026-11-28: the end icons looked crowded against the slant as the bevel
    /// deepened, and at full roundness their corners crossed it. Below about a quarter of the
    /// roundness slider the bevel is too shallow to need any more than the usual room.
    /// </para>
    /// </remarks>
    public static double EndPadding(DockMetrics metrics)
    {
        if (metrics.Corners != BarCorners.Beveled)
        {
            return metrics.PaddingX;
        }

        var cut = Extent(metrics.BarRadius, BarCorners.Beveled, double.PositiveInfinity, metrics.BarHeight);
        return Math.Max(metrics.PaddingX, cut + ((Math.Sqrt(2) - 1) * metrics.PaddingY));
    }

    /// <summary>How far each corner reaches along both of its sides, for a bar of the given size.</summary>
    /// <remarks>
    /// A bevel reaches the radius itself, up to half the bar's short side at full roundness,
    /// where each end comes to a point. A round corner reaches further than its radius, by the
    /// length it eases in over (<see cref="TraceRounded"/>).
    /// </remarks>
    public static double Extent(double radius, BarCorners shape, double width, double height)
    {
        var half = Math.Max(0, Math.Min(width, height) / 2);
        if (half <= 0)
        {
            return 0;
        }

        // A round corner eases its own way into the side's room — see TraceRounded.
        if (shape == BarCorners.Round)
        {
            return SmoothCorner(Math.Clamp(radius, 0, half), Smoothing, half).P;
        }

        return Math.Clamp(radius, 0, half);
    }

    /// <summary>The bar's outline for WPF, frozen.</summary>
    public static Geometry Geometry(Rect bounds, double radius, BarCorners shape)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var context = geometry.Open())
        {
            var sink = new WpfSink(context);
            Trace(bounds, radius, shape, ref sink);
        }

        geometry.Freeze();
        return geometry;
    }

    private readonly struct WpfSink(StreamGeometryContext context) : IOutlineSink
    {
        public void Begin(Point start) => context.BeginFigure(start, isFilled: true, isClosed: true);

        public void LineTo(Point end) => context.LineTo(end, isStroked: true, isSmoothJoin: false);

        public void BezierTo(Point handle1, Point handle2, Point end) =>
            context.BezierTo(handle1, handle2, end, isStroked: true, isSmoothJoin: false);
    }

    /// <summary>
    /// A plain rounded rectangle that lies wholly inside the bar's outline: for what can only be
    /// rounded and must not poke past the bar — the fallback blur kept inside it, and the
    /// shadow's rings, which would otherwise fill in a corner that has been cut away.
    /// </summary>
    /// <returns>The rectangle — the bar's own, or the bar drawn in from its ends — and its radius.</returns>
    /// <remarks>
    /// <para>
    /// A round corner of radius ρ cuts <c>(√2 - 1)·ρ</c> in along the diagonal and starts ρ along
    /// each edge, so it is inside the bar's corner once it starts no sooner and cuts at least as
    /// deep: a smoothed round corner holds the circle of its own extent, and a bevel of extent e
    /// cuts <c>e/√2</c> deep.
    /// </para>
    /// <para>
    /// No rounding can go past half the bar's short side, R, and a deep bevel wants more: an end
    /// coming to a point leaves no room for a half circle as wide as the bar. The rectangle is
    /// drawn in from the ends instead, until a half circle of R fits — its centre far enough in to
    /// clear the bevel's line, <c>e - (2 - √2)·R</c> further.
    /// </para>
    /// </remarks>
    public static (Rect Bounds, double Radius) RoundedInside(Rect bar, double radius, BarCorners shape)
    {
        var half = Math.Max(0, Math.Min(bar.Width, bar.Height) / 2);
        var extent = Extent(radius, shape, bar.Width, bar.Height);
        var round = shape == BarCorners.Beveled ? extent / Math.Sqrt(2) / (Math.Sqrt(2) - 1) : extent;

        if (round <= half)
        {
            return (bar, round);
        }

        // Only a bevel gets here: a round corner never wants more than R. Drawn in along the bar's
        // length, whichever way it runs.
        var inset = Math.Max(0, extent - ((2 - Math.Sqrt(2)) * half));
        var inside = bar.Width >= bar.Height
            ? new Rect(bar.X + inset, bar.Y, Math.Max(0, bar.Width - (2 * inset)), bar.Height)
            : new Rect(bar.X, bar.Y + inset, bar.Width, Math.Max(0, bar.Height - (2 * inset)));
        return (inside, half);
    }
}
