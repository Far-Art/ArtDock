using System.Windows;
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
}
