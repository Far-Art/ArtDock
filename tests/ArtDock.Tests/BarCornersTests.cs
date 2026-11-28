using System.Text.Json;
using System.Windows;
using ArtDock.Dock;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers the choice of corner shape: how it is stored, and the bar's outline, which the dock
/// and the sheet behind it both draw from.
/// </summary>
public class BarCornersTests
{
    private static readonly Rect Bar = new(100, 60, 400, 56);

    /// <summary>Areas measured to a thousandth of a DIP, not WPF's default quarter.</summary>
    private const double Fine = 0.001;

    private const System.Windows.Media.ToleranceType Exact = System.Windows.Media.ToleranceType.Absolute;

    private static BarOutline.Corner[] Corners(Rect bar, double radius, BarCorners shape)
    {
        var corners = new BarOutline.Corner[4];
        BarOutline.Corners(bar, radius, shape, corners);
        return corners;
    }

    /// <summary>Every shape.</summary>
    public static TheoryData<BarCorners> Outlined() => [.. Enum.GetValues<BarCorners>()];

    /// <summary>The shapes drawn in one cubic per corner: all but round.</summary>
    public static TheoryData<BarCorners> SingleCurve() =>
        [.. Enum.GetValues<BarCorners>().Where(shape => shape != BarCorners.Round)];

    /// <summary>An outline as traced: where it starts, then each piece, a line being a cubic with its handles on its ends.</summary>
    private sealed class Recorder : IOutlineSink
    {
        public Point Start { get; private set; }

        public List<(Point From, Point Handle1, Point Handle2, Point To, bool Line)> Pieces { get; } = [];

        private Point _at;

        public void Begin(Point start) => Start = _at = start;

        public void LineTo(Point end)
        {
            Pieces.Add((_at, _at, end, end, true));
            _at = end;
        }

        public void BezierTo(Point handle1, Point handle2, Point end)
        {
            Pieces.Add((_at, handle1, handle2, end, false));
            _at = end;
        }

        public IEnumerable<Point> Points =>
            Pieces.SelectMany(piece => new[] { piece.From, piece.Handle1, piece.Handle2, piece.To });
    }

    private static Recorder Traced(Rect bar, double radius, BarCorners shape)
    {
        var recorder = new Recorder();
        BarOutline.Trace(bar, radius, shape, ref recorder);
        return recorder;
    }

    [Fact]
    public void ANewDockHasRoundCorners()
    {
        var settings = new DockSettings();

        Assert.Equal(BarCorners.Round, settings.CornerShape);
        Assert.Equal(BarCorners.Round, settings.Metrics.Corners);
    }

    [Theory]
    [InlineData("Beveled", BarCorners.Beveled)]
    [InlineData("beveled", BarCorners.Beveled)]
    [InlineData("Scooped", BarCorners.Round)]
    [InlineData("Round", BarCorners.Round)]
    [InlineData("Squircle", BarCorners.Round)]
    [InlineData("Smooth", BarCorners.Round)]
    [InlineData("Leaf", BarCorners.Round)]
    [InlineData("Hexagon", BarCorners.Round)]
    [InlineData("7", BarCorners.Round)]
    [InlineData("", BarCorners.Round)]
    public void TheStoredNameIsReadForgivingly(string stored, BarCorners expected) =>
        Assert.Equal(expected, new DockSettings { BarCorners = stored }.CornerShape);

    [Fact]
    public void TheShapeSurvivesASaveAndAClone()
    {
        var settings = new DockSettings { BarCorners = nameof(BarCorners.Beveled) };

        var json = JsonSerializer.Serialize(settings);
        Assert.Contains("\"BarCorners\":\"Beveled\"", json);
        Assert.Equal(BarCorners.Beveled, JsonSerializer.Deserialize<DockSettings>(json)!.CornerShape);
        Assert.Equal(BarCorners.Beveled, settings.Clone().CornerShape);
    }

    [Fact]
    public void ChangingTheShapeChangesTheGeometry() =>
        Assert.NotEqual(
            new DockSettings().Metrics,
            new DockSettings { BarCorners = nameof(BarCorners.Beveled) }.Metrics);

    [Theory]
    [MemberData(nameof(Outlined))]
    public void EveryPointLiesOnOrInsideTheBar(BarCorners shape)
    {
        foreach (var point in Traced(Bar, 20, shape).Points)
        {
            Assert.InRange(point.X, Bar.Left - 1e-9, Bar.Right + 1e-9);
            Assert.InRange(point.Y, Bar.Top - 1e-9, Bar.Bottom + 1e-9);
        }
    }

    [Theory]
    [MemberData(nameof(Outlined))]
    public void TheOutlineClosesWhereItBegan(BarCorners shape)
    {
        var traced = Traced(Bar, 20, shape);

        // The last piece ends on the left edge, from where the figure closes straight up to its start.
        var end = traced.Pieces[^1].To;
        Assert.Equal(traced.Start.X, end.X, 6);
        Assert.True(end.Y >= traced.Start.Y - 1e-9);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(14.8)]
    [InlineData(22)]
    [InlineData(28)]
    public void ARoundOutlineNeverBends(double radius)
    {
        // Continuous curvature implies continuous direction: wherever one piece hands over to
        // the next, the two leave the joint along the same line.
        var pieces = Traced(Bar, radius, BarCorners.Round).Pieces;
        for (var i = 0; i < pieces.Count; i++)
        {
            var into = pieces[i];
            var outOf = pieces[(i + 1) % pieces.Count];
            var arriving = Direction(into.Line ? into.From : Last(into), into.To);
            var leaving = Direction(outOf.From, outOf.Line ? outOf.To : First(outOf));
            if (arriving.Length < 1e-9 || leaving.Length < 1e-9)
            {
                continue;
            }

            Assert.True(Vector.CrossProduct(arriving, leaving) is > -1e-6 and < 1e-6, $"bend at piece {i}");
            Assert.True(arriving * leaving > 0, $"turn-back at piece {i}");
        }

        static Point Last((Point From, Point Handle1, Point Handle2, Point To, bool Line) piece) =>
            (piece.To - piece.Handle2).Length > 1e-9 ? piece.Handle2 : piece.Handle1;

        static Point First((Point From, Point Handle1, Point Handle2, Point To, bool Line) piece) =>
            (piece.Handle1 - piece.From).Length > 1e-9 ? piece.Handle1 : piece.Handle2;

        static Vector Direction(Point from, Point to)
        {
            var vector = to - from;
            if (vector.Length > 1e-9)
            {
                vector.Normalize();
            }

            return vector;
        }
    }

    [Fact]
    public void RoundCornersStartFurtherOutThanTheirRadius()
    {
        // The easing: a corner of radius r starts (1 + smoothing)·r from its tip.
        var traced = Traced(Bar, 10, BarCorners.Round);

        Assert.Equal(Bar.Top + (10 * (1 + BarOutline.Smoothing)), traced.Start.Y, 6);
    }

    [Fact]
    public void WithNoSmoothingTheCornersAreTrueQuarterCircles()
    {
        // The same construction with no easing is a rounded rectangle, to within the cubic's
        // approximation of a circle.
        var recorder = new Recorder();
        BarOutline.TraceRounded(Bar, 20, smoothing: 0, ref recorder);
        var traced = new System.Windows.Media.PathGeometry(
        [
            new System.Windows.Media.PathFigure(
                recorder.Start,
                recorder.Pieces.Select(piece => (System.Windows.Media.PathSegment)new System.Windows.Media.BezierSegment(
                    piece.Handle1, piece.Handle2, piece.To, isStroked: true)),
                closed: true)
        ]);

        var round = new System.Windows.Media.RectangleGeometry(Bar, 20, 20);
        // A cubic stands a hair outside the quarter circle it stands in for: 0.03% of the radius at most.
        Assert.Equal(round.GetArea(Fine, Exact), traced.GetArea(Fine, Exact), tolerance: 1);
    }

    [Fact]
    public void AtFullRoundnessARoundBarIsAPill()
    {
        // No room is left on the ends for the easing, so it gives way and the ends close in
        // half circles.
        var metrics = new DockSettings { BarRoundness = 1 }.Metrics;
        var bar = new Rect(0, 0, 400, metrics.BarHeight);

        var smooth = BarOutline.Geometry(bar, metrics.BarRadius, BarCorners.Round).GetArea(Fine, Exact);
        var pill = new System.Windows.Media.RectangleGeometry(bar, metrics.BarRadius, metrics.BarRadius).GetArea(Fine, Exact);
        Assert.Equal(pill, smooth, tolerance: 2);
    }

    [Fact]
    public void RoundCornersAreAsTallAsTheyAreLong()
    {
        // Not stretched: the curve reaches as far down the end as along the bar.
        var metrics = new DockSettings { BarRoundness = 0.6 }.Metrics;
        var bar = new Rect(0, 0, 400, metrics.BarHeight);
        var traced = Traced(bar, metrics.BarRadius, BarCorners.Round);

        var down = traced.Start.Y - bar.Top;
        var along = traced.Pieces[2].To.X - bar.Left;
        Assert.Equal(down, along, 6);
    }

    public static TheoryData<BarCorners, double> ShapesAndRoundness()
    {
        var data = new TheoryData<BarCorners, double>();
        foreach (var shape in Enum.GetValues<BarCorners>().Where(shape => shape != BarCorners.Round))
        {
            foreach (var roundness in new[] { 0, 0.25, 0.4, 0.6, 0.8, 1 })
            {
                data.Add(shape, roundness);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ShapesAndRoundness))]
    public void NoCornerIsStretched(BarCorners shape, double roundness)
    {
        // Found on 2026-11-28: past about half the slider the corners went on growing along the
        // bar after they had run out of height, and the ends came out stretched.
        var metrics = new DockSettings { BarRoundness = roundness, BarCorners = shape.ToString() }.Metrics;
        var bar = new Rect(0, 0, 400, metrics.BarHeight);
        foreach (var corner in Corners(bar, metrics.BarRadius, shape))
        {
            var along = Math.Abs(corner.End.X - corner.Start.X);
            var down = Math.Abs(corner.End.Y - corner.Start.Y);
            Assert.Equal(along, down, 6);
        }
    }

    [Theory]
    [MemberData(nameof(Outlined))]
    public void EveryStepOfTheSliderChangesTheCorners(BarCorners shape)
    {
        // Measured by what the corners take out of the bar, which every shape must go on
        // taking more of all the way along the slider.
        var previous = double.MaxValue;
        for (var step = 0; step <= 50; step++)
        {
            var metrics = new DockSettings { BarRoundness = step / 50.0 }.Metrics;
            var area = BarOutline.Geometry(new Rect(0, 0, 400, metrics.BarHeight), metrics.BarRadius, shape).GetArea();
            Assert.True(area < previous - 0.01, $"step {step} of the slider did not change the corners");
            previous = area;
        }
    }

    [Fact]
    public void AtFullRoundnessABevelComesToAPoint()
    {
        // The cuts meet in the middle of each end: the end is a triangle's point.
        var corners = Corners(Bar, Bar.Height / 2, BarCorners.Beveled);
        var middle = Bar.Top + (Bar.Height / 2);

        Assert.Equal(corners[1].End, corners[2].Start);
        Assert.Equal(new Point(Bar.Right, middle), corners[1].End);
        Assert.Equal(new Point(Bar.Left, middle), corners[3].End);
    }

    [Theory]
    [MemberData(nameof(SingleCurve))]
    public void ABarTooShortForItsCornersIsNeverTurnedInsideOut(BarCorners shape)
    {
        var narrow = new Rect(0, 0, 30, 56);
        var corners = Corners(narrow, 28, shape);

        // The top runs right from the first corner to the second, the bottom left from the
        // third to the fourth: neither may run backwards.
        Assert.True(corners[0].End.X <= corners[1].Start.X);
        Assert.True(corners[3].Start.X <= corners[2].End.X);
    }

    [Fact]
    public void ARoundBarIsSmoothedNotPlainlyRounded()
    {
        // Round corners ease into their arcs, so they take a little more out of the corners
        // than plain quarter circles of the same radius do: about a square DIP each at 20.
        var smoothed = BarOutline.Geometry(Bar, 20, BarCorners.Round).GetArea(Fine, Exact);
        var plain = new System.Windows.Media.RectangleGeometry(Bar, 20, 20).GetArea(Fine, Exact);

        Assert.True(plain - smoothed > 2, $"smoothed {smoothed:0.0}, plain {plain:0.0}");
    }

    [Theory]
    [MemberData(nameof(Outlined))]
    public void EveryShapeCoversTheBarAndNotItsCorners(BarCorners shape)
    {
        var geometry = BarOutline.Geometry(Bar, 20, shape);

        Assert.True(geometry.FillContains(new Point(Bar.Left + (Bar.Width / 2), Bar.Top + (Bar.Height / 2))));
        Assert.True(geometry.FillContains(new Point(Bar.Left + 1, Bar.Top + (Bar.Height / 2))));
        Assert.False(geometry.FillContains(new Point(Bar.Left + 0.5, Bar.Top + 0.5)));
        Assert.False(geometry.FillContains(new Point(Bar.Right - 0.5, Bar.Bottom - 0.5)));
    }

    public static TheoryData<BarCorners, double, bool> EveryShapeRoundnessAndWay()
    {
        var data = new TheoryData<BarCorners, double, bool>();
        foreach (var shape in Enum.GetValues<BarCorners>())
        {
            foreach (var roundness in new[] { 0, 0.25, 0.4, 0.6, 0.8, 1 })
            {
                data.Add(shape, roundness, false);
                data.Add(shape, roundness, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryShapeRoundnessAndWay))]
    public void TheRoundedShapeKeptInsideTheBarIsInsideIt(BarCorners shape, double roundness, bool upright)
    {
        // What the shadow's rings and the fallback blur are drawn from: no part of it may lie
        // outside the bar, or the shadow fills in a corner that has been cut away — the whole
        // triangle at a pointed bevel's end, which no rounding as wide as the bar fits inside.
        var metrics = new DockSettings { BarRoundness = roundness, BarCorners = shape.ToString() }.Metrics;
        var bar = upright ? new Rect(0, 0, metrics.BarHeight, 400) : new Rect(0, 0, 400, metrics.BarHeight);
        var outline = BarOutline.Geometry(bar, metrics.BarRadius, shape);
        var (bounds, radius) = BarOutline.RoundedInside(bar, metrics.BarRadius, shape);
        var round = new System.Windows.Media.RectangleGeometry(bounds, radius, radius);

        var outside = System.Windows.Media.Geometry.Combine(
            round, outline, System.Windows.Media.GeometryCombineMode.Exclude, null).GetArea(Fine, Exact);
        Assert.True(outside < 0.5, $"{outside:0.00} square DIPs of it lie outside");

        // And it is no smaller than it has to be: it reaches the bar's sides, and runs most of
        // its length.
        var (across, along) = upright ? (bounds.Width, bounds.Height) : (bounds.Height, bounds.Width);
        Assert.Equal(metrics.BarHeight, across, 6);
        Assert.True(along > 400 - metrics.BarHeight, $"drawn in to {along:0.0} of 400");
    }

    [Fact]
    public void ABevelIsStraight()
    {
        foreach (var corner in Corners(Bar, 20, BarCorners.Beveled))
        {
            Assert.Equal(corner.Start, corner.Handle1);
            Assert.Equal(corner.End, corner.Handle2);
        }
    }

    [Fact]
    public void EveryShapeHasALabel()
    {
        Assert.Equal(Enum.GetValues<BarCorners>().Length, ArtDock.Views.SettingsWindow.CornerLabels.Length);
        Assert.All(ArtDock.Views.SettingsWindow.CornerLabels, key =>
            Assert.NotEqual(key, ArtDock.Localization.Localizer.Get(key)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.4)]
    [InlineData(1)]
    public void RoundCornersKeepTheUsualRoomAtTheEnds(double roundness) =>
        Assert.Equal(new DockMetrics().PaddingX, new DockSettings { BarRoundness = roundness }.Metrics.PaddingX);

    [Fact]
    public void AShallowBevelKeepsTheUsualRoomAtTheEnds() =>
        Assert.Equal(
            new DockMetrics().PaddingX,
            new DockSettings { BarRoundness = 0, BarCorners = nameof(BarCorners.Beveled) }.Metrics.PaddingX);

    [Fact]
    public void TheRoomAtTheEndsGrowsWithTheBevel()
    {
        var previous = 0.0;
        for (var step = 0; step <= 50; step++)
        {
            var padding = new DockSettings { BarRoundness = step / 50.0, BarCorners = nameof(BarCorners.Beveled) }.Metrics.PaddingX;
            Assert.True(padding >= previous, $"step {step} of the slider took room away");
            previous = padding;
        }

        Assert.True(previous > new DockMetrics().PaddingX + 10, $"only {previous:0.0} at full roundness");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    [InlineData(0.4)]
    [InlineData(0.6)]
    [InlineData(0.8)]
    [InlineData(1)]
    public void TheEndIconsKeepClearOfTheBevel(double roundness)
    {
        // Every point within PaddingY of the end icons — as near as the icons come to the bar's
        // top and bottom — lies inside the bar: the slant comes no closer to them than its edges.
        var metrics = new DockSettings { BarRoundness = roundness, BarCorners = nameof(BarCorners.Beveled) }.Metrics;
        var width = (metrics.PaddingX * 2) + (3 * metrics.BaseSize) + (2 * metrics.Gap);
        var bar = new Rect(0, 0, width, metrics.BarHeight);
        var outline = BarOutline.Geometry(bar, metrics.BarRadius, BarCorners.Beveled);

        var clearance = metrics.PaddingY - 0.05;
        foreach (var left in new[] { metrics.PaddingX, width - metrics.PaddingX - metrics.BaseSize })
        {
            var icon = new Rect(left, metrics.PaddingY, metrics.BaseSize, metrics.BaseSize);
            icon.Inflate(clearance, clearance);
            var around = new System.Windows.Media.RectangleGeometry(icon, clearance, clearance);

            var outside = System.Windows.Media.Geometry.Combine(
                around, outline, System.Windows.Media.GeometryCombineMode.Exclude, null).GetArea(Fine, Exact);
            Assert.True(outside < 0.01, $"{outside:0.00} square DIPs round the icon at {left:0.0} lie past the bevel");
        }
    }
}
