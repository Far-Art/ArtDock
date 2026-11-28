using System.Windows;
using System.Windows.Media;
using ArtDock.Controls;
using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers the shape of the bar's shadow, which two things draw and must agree on: the dock
/// itself while the blur is off, and the acrylic sheet behind it while the blur is on.
/// </summary>
public class BarShadowTests
{
    private static readonly Rect Bar = new(100, 60, 400, 70);
    private const double Radius = 28;

    private static BarShadow.Ring[] Rings(Rect bar, double radius, double scale)
    {
        var rings = new BarShadow.Ring[BarShadow.RingCount];
        BarShadow.Rings(bar, radius, scale, rings);
        return rings;
    }

    [Fact]
    public void ThereIsARingForEveryLayerOfEveryPass()
    {
        Assert.Equal(BarShadow.Passes.Sum(pass => pass.Layers), BarShadow.RingCount);

        var rings = Rings(Bar, Radius, 1);
        for (var pass = 0; pass < BarShadow.Passes.Count; pass++)
        {
            Assert.Equal(BarShadow.Passes[pass].Layers, rings.Count(ring => ring.Pass == pass));
        }
    }

    [Fact]
    public void EachPassIsPaintedFromItsOutermostRingInward()
    {
        var rings = Rings(Bar, Radius, 1);
        for (var i = 1; i < rings.Length; i++)
        {
            if (rings[i].Pass == rings[i - 1].Pass)
            {
                Assert.True(rings[i].Bounds.Width < rings[i - 1].Bounds.Width, $"ring {i} is wider than the one before it");
            }
        }
    }

    /// <summary>
    /// The sheet is the same size as the dock's window and draws the shadow while the blur is
    /// on, so the room the window keeps around the bar is all the room the shadow has: any of
    /// it past the window's edge would be cut off in a hard line.
    /// </summary>
    [Fact]
    public void TheWindowsSlack_HoldsTheWholeShadow()
    {
        foreach (var ring in Rings(Bar, Radius, 1))
        {
            Assert.True(ring.Bounds.Left >= Bar.Left - DockBar.SideInset, $"a ring reaches {Bar.Left - ring.Bounds.Left} past the bar's left end");
            Assert.True(ring.Bounds.Right <= Bar.Right + DockBar.SideInset, $"a ring reaches {ring.Bounds.Right - Bar.Right} past the bar's right end");
            Assert.True(ring.Bounds.Bottom <= Bar.Bottom + DockBar.BottomInset, $"a ring reaches {ring.Bounds.Bottom - Bar.Bottom} below the bar");
            Assert.True(ring.Bounds.Bottom <= Bar.Bottom + BarShadow.Reach);
        }
    }

    /// <summary>
    /// What the dock paints, invisibly, while the sheet draws the bar is the bar and the
    /// outermost ring of each pass. That is the whole of what the shadow covered when the dock
    /// drew it — the area that took clicks and drops then, and must go on taking them.
    /// </summary>
    [Fact]
    public void TheOutermostRings_CoverEveryRingOfTheirPass()
    {
        foreach (var ring in Rings(Bar, Radius, 1))
        {
            var outermost = BarShadow.Outermost(Bar, Radius, ring.Pass);
            Assert.True(outermost.Bounds.Contains(ring.Bounds), $"a ring of pass {ring.Pass} lies outside that pass's outermost");
            Assert.True(outermost.Radius >= ring.Radius);
        }
    }

    [Fact]
    public void TheOutermostRing_IsTheFirstPaintedOfItsPass()
    {
        var rings = Rings(Bar, Radius, 1);
        for (var pass = 0; pass < BarShadow.Passes.Count; pass++)
        {
            var first = rings.First(ring => ring.Pass == pass);
            var outermost = BarShadow.Outermost(Bar, Radius, pass);
            Assert.Equal(outermost.Bounds, first.Bounds);
            Assert.Equal(outermost.Radius, first.Radius, 9);
        }
    }

    /// <summary>
    /// The sheet draws in device pixels, the dock in DIPs; at 150% the sheet's shadow has to be
    /// the dock's, one and a half times the size, and nothing else.
    /// </summary>
    [Fact]
    public void InDevicePixels_TheShadowIsTheSameShapeScaled()
    {
        const double scale = 1.5;
        var dips = Rings(Bar, Radius, 1);
        var pixels = Rings(new Rect(Bar.X * scale, Bar.Y * scale, Bar.Width * scale, Bar.Height * scale), Radius * scale, scale);

        for (var i = 0; i < dips.Length; i++)
        {
            Assert.Equal(dips[i].Bounds.X * scale, pixels[i].Bounds.X, 9);
            Assert.Equal(dips[i].Bounds.Y * scale, pixels[i].Bounds.Y, 9);
            Assert.Equal(dips[i].Bounds.Width * scale, pixels[i].Bounds.Width, 9);
            Assert.Equal(dips[i].Bounds.Height * scale, pixels[i].Bounds.Height, 9);
            Assert.Equal(dips[i].Radius * scale, pixels[i].Radius, 9);
        }
    }

    /// <summary>
    /// How dark the stack of rings makes a point, at full strength: every ring that holds it,
    /// each at its own alpha, laid over one another.
    /// </summary>
    private static double Coverage(BarShadow.Ring[] rings, Point point)
    {
        var clear = 1.0;
        foreach (var ring in rings)
        {
            if (new RectangleGeometry(ring.Bounds, ring.Radius, ring.Radius).FillContains(point))
            {
                clear *= 1 - (ring.Alpha / 255.0);
            }
        }

        return 1 - clear;
    }

    /// <summary>
    /// What a real blurred shadow would make the point straight below the bar's middle, at
    /// <paramref name="below"/> DIPs under its bottom edge: each pass a Gaussian blur of the bar
    /// drawn in by its spread and pushed down by its offset, the two laid over one another.
    /// </summary>
    private static double Blurred(double below)
    {
        var clear = 1.0;
        foreach (var pass in BarShadow.Passes)
        {
            var past = below - pass.Spread - pass.OffsetY;
            clear *= 1 - (pass.Peak * BarShadow.Normal(-past / pass.Sigma));
        }

        return 1 - clear;
    }

    /// <summary>
    /// The point of the rings: below the bar, where the shadow is seen most, the stack is the
    /// blur it stands for, to within the step one ring's band can be off by.
    /// </summary>
    [Fact]
    public void BelowTheBar_TheRingsAreTheBlur()
    {
        var rings = Rings(Bar, Radius, 1);
        for (var below = 0.25; below < BarShadow.Reach; below += 0.5)
        {
            var point = new Point(Bar.X + (Bar.Width / 2), Bar.Bottom + below);
            var expected = Blurred(below);
            var actual = Coverage(rings, point);

            // Past where the rings begin, the blur is under two steps of alpha and is let go.
            Assert.True(Math.Abs(expected - actual) <= Math.Max(0.02, expected * 0.1) || expected < 2.5 / 255,
                $"{below} DIP below the bar the rings make {actual:F3} where the blur is {expected:F3}");
        }
    }

    /// <summary>
    /// Lit from above: darkest below the bar, lighter beside it, faintest above — the shadow
    /// has a direction, rather than being a halo the same all round.
    /// </summary>
    [Fact]
    public void TheShadowFallsBelow_MoreThanBeside_MoreThanAbove()
    {
        var rings = Rings(Bar, Radius, 1);
        foreach (var away in new[] { 1.0, 3, 6, 10 })
        {
            var below = Coverage(rings, new Point(Bar.X + (Bar.Width / 2), Bar.Bottom + away));
            var beside = Coverage(rings, new Point(Bar.Right + away, Bar.Y + (Bar.Height / 2)));
            var above = Coverage(rings, new Point(Bar.X + (Bar.Width / 2), Bar.Top - away));

            Assert.True(below > beside, $"{away} DIP out, below is {below:F3} and beside {beside:F3}");
            Assert.True(beside > above, $"{away} DIP out, beside is {beside:F3} and above {above:F3}");
        }
    }

    /// <summary>
    /// No ring lies wholly under the bar, where it would be drawn on every frame of the wave
    /// for nothing: each reaches out from behind it somewhere, below at least.
    /// </summary>
    [Fact]
    public void EveryRing_ShowsBelowTheBar()
    {
        foreach (var ring in Rings(Bar, Radius, 1))
        {
            Assert.True(ring.Bounds.Bottom > Bar.Bottom, $"a ring of pass {ring.Pass} ends {Bar.Bottom - ring.Bounds.Bottom} above the bar's bottom");
            Assert.True(ring.Alpha > 0);
        }
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(0.0228)]
    [InlineData(0.5)]
    [InlineData(0.8413)]
    [InlineData(0.999)]
    public void TheInverseNormal_UndoesTheNormal(double share) =>
        Assert.Equal(share, BarShadow.Normal(BarShadow.InverseNormal(share)), 6);
}
