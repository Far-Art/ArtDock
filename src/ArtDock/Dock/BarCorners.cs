using System.Windows;
using System.Windows.Media;

namespace ArtDock.Dock;

/// <summary>The shape of the bar's corners. The order is the settings picker's.</summary>
public enum BarCorners
{
    /// <summary>Quarter circles: the bar is a rounded rectangle, a stadium at full roundness.</summary>
    Round,

    /// <summary>
    /// Quarters of a superellipse: the curve starts gently off each straight edge and tightens
    /// through the corner, with no visible seam where edge and corner meet.
    /// </summary>
    Squircle,

    /// <summary>Each corner cut off in a straight line.</summary>
    Beveled,

    /// <summary>Each corner scooped out: a quarter circle curving into the bar.</summary>
    Scooped,

    /// <summary>The top-left and bottom-right corners round, the other two barely rounded.</summary>
    Leaf
}

/// <summary>
/// The bar's outline for every corner shape but <see cref="BarCorners.Round"/>, as a closed
/// figure of four straight edges and four cubic curves — described once, for the dock to draw
/// with WPF and for the sheet behind it to draw with the compositor, which must agree.
/// </summary>
/// <remarks>
/// <para>
/// Round corners are left to each side's own rounded rectangle, which both have. Every other
/// shape is one cubic per corner: a straight cut is a cubic whose handles sit on its ends.
/// </para>
/// <para>
/// A squircle corner approximates a quarter of the superellipse <c>|x|⁴ + |y|⁴ = 1</c>
/// stretched over the corner's extent. Its handles lie along the edges it leaves, so it meets
/// them without a bend, and their length is chosen so the curve passes through the
/// superellipse's own point at 45°. Its extent is the radius times <see cref="SquircleReach"/>:
/// a squircle bites less deep than a circle of the same size, so at the same radius it would read
/// as sharper. Stretched, it takes about as much out of the corner, and the roundness slider
/// means much the same at either shape.
/// </para>
/// </remarks>
public static class BarOutline
{
    /// <summary>How much further along each edge a squircle's corner reaches than a circle of the same radius.</summary>
    public const double SquircleReach = 1.6;

    /// <summary>
    /// How far a scoop may reach along a side, as a share of half that side: short of meeting
    /// the next one, so the bar keeps a flat to stand on at each end instead of coming to a point.
    /// </summary>
    public const double ScoopLimit = 0.6;

    /// <summary>The leaf's two tight corners, as a share of the radius of its two round ones.</summary>
    public const double LeafTight = 0.25;

    /// <summary>A circle's handle length, as a share of its radius, for a quarter of it in one cubic.</summary>
    private const double CircleHandle = 0.5523;

    /// <summary>
    /// How far along its edge a squircle's handle reaches, as a share of the corner's extent:
    /// the value that puts the curve's midpoint at the superellipse's, <c>8/3 · (2^-¼ - ½)</c>.
    /// (At an exponent of 2 the same formula gives the circle's 0.5523.)
    /// </summary>
    private static readonly double SquircleHandle = 8.0 / 3.0 * (Math.Pow(2, -0.25) - 0.5);

    /// <summary>One corner: the curve's start, its two handles and its end.</summary>
    public readonly record struct Corner(Point Start, Point Handle1, Point Handle2, Point End);

    /// <summary>
    /// The four corners of a bar, clockwise from the top-left. Each edge runs straight from one
    /// corner's <see cref="Corner.End"/> to the next one's <see cref="Corner.Start"/>.
    /// </summary>
    public static void Corners(Rect bounds, double radius, BarCorners shape, Span<Corner> corners)
    {
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

        for (var i = 0; i < 4; i++)
        {
            var (tx, ty, sx, sy, vertical) = tips[i];
            var (ex, ey) = Extent(shape, i, radius, width, height);

            // In the corner's frame: a is the point on the edge it comes from, b the one on the
            // edge it goes to, each with its handle.
            Point At(double x, double y) => new(tx + (sx * x), ty + (sy * y));

            Point start, handle1, handle2, end;
            switch (shape)
            {
                case BarCorners.Beveled:
                    start = handle1 = vertical ? At(0, ey) : At(ex, 0);
                    end = handle2 = vertical ? At(ex, 0) : At(0, ey);
                    break;

                case BarCorners.Scooped:
                {
                    // A quarter circle about the tip itself, so it bows into the bar.
                    var hx = ex * CircleHandle;
                    var hy = ey * CircleHandle;
                    start = vertical ? At(0, ey) : At(ex, 0);
                    handle1 = vertical ? At(hx, ey) : At(ex, hy);
                    handle2 = vertical ? At(ex, hy) : At(hx, ey);
                    end = vertical ? At(ex, 0) : At(0, ey);
                    break;
                }

                default:
                {
                    var share = shape == BarCorners.Squircle ? SquircleHandle : CircleHandle;
                    var hx = ex * share;
                    var hy = ey * share;
                    start = vertical ? At(0, ey) : At(ex, 0);
                    handle1 = vertical ? At(0, ey - hy) : At(ex - hx, 0);
                    handle2 = vertical ? At(ex - hx, 0) : At(0, ey - hy);
                    end = vertical ? At(ex, 0) : At(0, ey);
                    break;
                }
            }

            corners[i] = new Corner(start, handle1, handle2, end);
        }
    }

    /// <summary>How far corner <paramref name="index"/> reaches along each side, never past half of it.</summary>
    private static (double X, double Y) Extent(BarCorners shape, int index, double radius, double width, double height)
    {
        var reach = shape switch
        {
            BarCorners.Squircle => radius * SquircleReach,
            BarCorners.Leaf when index is 1 or 3 => radius * LeafTight,
            _ => radius
        };

        var limit = shape == BarCorners.Scooped ? ScoopLimit : 1;
        return (Math.Min(reach, limit * width / 2), Math.Min(reach, limit * height / 2));
    }

    /// <summary>The bar's outline for WPF, frozen: a plain rounded rectangle for round corners.</summary>
    public static Geometry Geometry(Rect bounds, double radius, BarCorners shape)
    {
        if (shape == BarCorners.Round)
        {
            var round = new RectangleGeometry(bounds, radius, radius);
            round.Freeze();
            return round;
        }

        Span<Corner> corners = stackalloc Corner[4];
        Corners(bounds, radius, shape, corners);

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var context = geometry.Open())
        {
            context.BeginFigure(corners[0].Start, isFilled: true, isClosed: true);
            for (var i = 0; i < 4; i++)
            {
                var corner = corners[i];
                if (i > 0)
                {
                    context.LineTo(corner.Start, isStroked: true, isSmoothJoin: false);
                }

                context.BezierTo(corner.Handle1, corner.Handle2, corner.End, isStroked: true, isSmoothJoin: false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// The radius of a round corner that lies wholly inside the bar's own: for what can only be
    /// rounded and must not poke past the bar — the fallback blur kept inside it, and the
    /// shadow's rings, which would otherwise fill in a corner that has been cut away.
    /// </summary>
    /// <remarks>
    /// A round corner of radius ρ cuts <c>(√2 - 1)·ρ</c> in along the diagonal and starts ρ along
    /// each edge, so it is inside the bar's corner once it starts no sooner and cuts at least as
    /// deep: a superellipse holds the circle of its own extent; a bevel of extent e cuts
    /// <c>e/√2</c> deep, and a scoop of radius e cuts e deep.
    /// </remarks>
    public static double EnclosedBy(double radius, BarCorners shape) => shape switch
    {
        BarCorners.Squircle => radius * SquircleReach,
        BarCorners.Beveled => radius / Math.Sqrt(2) / (Math.Sqrt(2) - 1),
        BarCorners.Scooped => radius / (Math.Sqrt(2) - 1),
        _ => radius
    };
}
