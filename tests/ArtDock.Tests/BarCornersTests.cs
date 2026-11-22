using System.Text.Json;
using System.Windows;
using ArtDock.Dock;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers the choice of corner shape: how it is stored, and the squircle's outline, which the
/// dock and the sheet behind it both draw from.
/// </summary>
public class BarCornersTests
{
    private static readonly Rect Bar = new(100, 60, 400, 56);

    private static BarOutline.Corner[] Corners(Rect bar, double radius, BarCorners shape = BarCorners.Squircle)
    {
        var corners = new BarOutline.Corner[4];
        BarOutline.Corners(bar, radius, shape, corners);
        return corners;
    }

    /// <summary>Every shape the outline draws: all but round, which is a plain rounded rectangle.</summary>
    public static TheoryData<BarCorners> Outlined() =>
        [.. Enum.GetValues<BarCorners>().Where(shape => shape != BarCorners.Round)];

    [Fact]
    public void ANewDockHasRoundCorners()
    {
        var settings = new DockSettings();

        Assert.Equal(BarCorners.Round, settings.CornerShape);
        Assert.Equal(BarCorners.Round, settings.Metrics.Corners);
    }

    [Theory]
    [InlineData("Squircle", BarCorners.Squircle)]
    [InlineData("squircle", BarCorners.Squircle)]
    [InlineData("Round", BarCorners.Round)]
    [InlineData("Hexagon", BarCorners.Round)]
    [InlineData("7", BarCorners.Round)]
    [InlineData("", BarCorners.Round)]
    public void TheStoredNameIsReadForgivingly(string stored, BarCorners expected) =>
        Assert.Equal(expected, new DockSettings { BarCorners = stored }.CornerShape);

    [Fact]
    public void TheShapeSurvivesASaveAndAClone()
    {
        var settings = new DockSettings { BarCorners = nameof(BarCorners.Squircle) };

        var json = JsonSerializer.Serialize(settings);
        Assert.Contains("\"BarCorners\":\"Squircle\"", json);
        Assert.Equal(BarCorners.Squircle, JsonSerializer.Deserialize<DockSettings>(json)!.CornerShape);
        Assert.Equal(BarCorners.Squircle, settings.Clone().CornerShape);
    }

    [Fact]
    public void ChangingTheShapeChangesTheGeometry() =>
        Assert.NotEqual(
            new DockSettings().Metrics,
            new DockSettings { BarCorners = nameof(BarCorners.Squircle) }.Metrics);

    [Theory]
    [MemberData(nameof(Outlined))]
    public void EveryPointLiesOnOrInsideTheBar(BarCorners shape)
    {
        foreach (var corner in Corners(Bar, 20, shape))
        {
            foreach (var point in new[] { corner.Start, corner.Handle1, corner.Handle2, corner.End })
            {
                Assert.InRange(point.X, Bar.Left, Bar.Right);
                Assert.InRange(point.Y, Bar.Top, Bar.Bottom);
            }
        }
    }

    [Fact]
    public void EachCurveLeavesAndMeetsItsEdgesWithoutABend()
    {
        var corners = Corners(Bar, 20);

        // Top-left: the first handle straight up the left edge, the second straight along the top.
        Assert.Equal(corners[0].Start.X, corners[0].Handle1.X, 6);
        Assert.Equal(corners[0].End.Y, corners[0].Handle2.Y, 6);

        // And so on round: each curve's handles are in line with the edges either side of it.
        Assert.Equal(corners[1].Start.Y, corners[1].Handle1.Y, 6);
        Assert.Equal(corners[1].End.X, corners[1].Handle2.X, 6);
        Assert.Equal(corners[2].Start.X, corners[2].Handle1.X, 6);
        Assert.Equal(corners[2].End.Y, corners[2].Handle2.Y, 6);
        Assert.Equal(corners[3].Start.Y, corners[3].Handle1.Y, 6);
        Assert.Equal(corners[3].End.X, corners[3].Handle2.X, 6);
    }

    [Fact]
    public void TheCornersReachFurtherThanACircleOfTheSameRadius()
    {
        var corners = Corners(Bar, 12);

        Assert.Equal(Bar.Left + (12 * BarOutline.SquircleReach), corners[0].End.X, 6);
        Assert.Equal(Bar.Top + (12 * BarOutline.SquircleReach), corners[0].Start.Y, 6);
    }

    [Fact]
    public void AtFullRoundnessTheEndsCloseWithNoStraightSide()
    {
        // A radius of half the height, as Roundness 1 gives: the curves down each end meet in
        // the middle of it.
        var corners = Corners(Bar, Bar.Height / 2);

        Assert.Equal(corners[1].End, corners[2].Start);
        Assert.Equal(corners[3].End, corners[0].Start);
        Assert.Equal(Bar.Top + (Bar.Height / 2), corners[0].Start.Y, 6);
    }

    [Theory]
    [MemberData(nameof(Outlined))]
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
    public void TheCurvePassesThroughTheSuperellipseAt45Degrees()
    {
        var corners = Corners(Bar, 12);
        var c = corners[0];

        // The Bézier's midpoint.
        var x = (c.Start.X + (3 * c.Handle1.X) + (3 * c.Handle2.X) + c.End.X) / 8;
        var y = (c.Start.Y + (3 * c.Handle1.Y) + (3 * c.Handle2.Y) + c.End.Y) / 8;

        // |u|⁴ + |v|⁴ = 1, measured from the corner's centre in units of its extent.
        var extent = 12 * BarOutline.SquircleReach;
        var u = (Bar.Left + extent - x) / extent;
        var v = (Bar.Top + extent - y) / extent;
        Assert.Equal(1, Math.Pow(u, 4) + Math.Pow(v, 4), 3);
    }

    [Fact]
    public void ARoundBarIsAPlainRoundedRectangle()
    {
        var geometry = BarOutline.Geometry(Bar, 20, BarCorners.Round);

        var round = Assert.IsType<System.Windows.Media.RectangleGeometry>(geometry);
        Assert.Equal(20, round.RadiusX);
        Assert.Equal(Bar, round.Rect);
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

    [Fact]
    public void TheFallbackBlurIsKeptInsideASquircle() =>
        Assert.Equal(20 * BarOutline.SquircleReach, BarOutline.EnclosedBy(20, BarCorners.Squircle), 6);

    [Theory]
    [MemberData(nameof(Outlined))]
    public void TheRoundCornerKeptInsideEachShapeIsInsideIt(BarCorners shape)
    {
        // What the shadow's rings and the fallback blur are rounded to: no point of it may lie
        // outside the bar, or the shadow fills in a corner that has been cut away.
        var bar = new Rect(0, 0, 400, 200);
        var outline = BarOutline.Geometry(bar, 20, shape);
        var inside = BarOutline.EnclosedBy(20, shape);
        var round = new System.Windows.Media.RectangleGeometry(bar, inside, inside);

        var outside = System.Windows.Media.Geometry.Combine(
            round, outline, System.Windows.Media.GeometryCombineMode.Exclude, null);
        Assert.True(outside.GetArea() < 0.5, $"{outside.GetArea():0.00} square DIPs of the round corner lie outside");
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
    public void AScoopBowsIntoTheBar()
    {
        var c = Corners(Bar, 12, BarCorners.Scooped)[0];

        // A quarter circle about the corner's tip: its midpoint is as far from the tip as its ends.
        var x = (c.Start.X + (3 * c.Handle1.X) + (3 * c.Handle2.X) + c.End.X) / 8;
        var y = (c.Start.Y + (3 * c.Handle1.Y) + (3 * c.Handle2.Y) + c.End.Y) / 8;
        Assert.Equal(12, Math.Sqrt(Math.Pow(x - Bar.Left, 2) + Math.Pow(y - Bar.Top, 2)), 1);
    }

    [Fact]
    public void AScoopStopsShortOfTheMiddleOfTheEnd()
    {
        var corners = Corners(Bar, Bar.Height / 2, BarCorners.Scooped);

        Assert.True(corners[1].End.Y < corners[2].Start.Y, "the scoops at the right end met");
    }

    [Fact]
    public void ALeafIsRoundOnOneDiagonalAndTightOnTheOther()
    {
        var corners = Corners(Bar, 20, BarCorners.Leaf);

        Assert.Equal(20, corners[0].End.X - Bar.Left, 6);
        Assert.Equal(20, Bar.Right - corners[2].End.X, 6);
        Assert.Equal(20 * BarOutline.LeafTight, Bar.Right - corners[1].Start.X, 6);
        Assert.Equal(20 * BarOutline.LeafTight, corners[3].Start.X - Bar.Left, 6);
    }

    [Fact]
    public void EveryShapeHasALabel()
    {
        Assert.Equal(Enum.GetValues<BarCorners>().Length, ArtDock.Views.SettingsWindow.CornerLabels.Length);
        Assert.All(ArtDock.Views.SettingsWindow.CornerLabels, key =>
            Assert.NotEqual(key, ArtDock.Localization.Localizer.Get(key)));
    }
}
