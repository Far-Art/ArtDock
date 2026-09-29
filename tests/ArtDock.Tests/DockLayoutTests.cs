using ArtDock.Dock;
using ArtDock.Services;

namespace ArtDock.Tests;

public class DockLayoutTests
{
    private static readonly DockMetrics Metrics = new()
    {
        BaseSize = 36,
        MaxSize = 46.8,
        InfluenceRange = 80,
        Gap = 8,
        PaddingX = 14,
        PaddingY = 10
    };

    private static DockLayout Layout() => new(Metrics);

    [Fact]
    public void RestingCentre_IsEvenlySpaced()
    {
        var layout = Layout();
        Assert.Equal(14 + 18, layout.RestingCentre(0), 6);
        Assert.Equal(14 + 44 + 18, layout.RestingCentre(1), 6);
        Assert.Equal(
            layout.RestingCentre(1) - layout.RestingCentre(0),
            layout.RestingCentre(2) - layout.RestingCentre(1),
            6);
    }

    [Fact]
    public void NoPointer_LeavesEveryIconAtBaseSize()
    {
        var sizes = Layout().Sizes(9, pointerX: null);
        Assert.All(sizes, size => Assert.Equal(Metrics.BaseSize, size, 6));
    }

    [Fact]
    public void ZeroProgress_LeavesEveryIconAtBaseSize()
    {
        var layout = Layout();
        var sizes = layout.Sizes(9, layout.RestingCentre(4), progress: 0);
        Assert.All(sizes, size => Assert.Equal(Metrics.BaseSize, size, 6));
    }

    [Fact]
    public void PointerOnIcon_MagnifiesThatIconMost()
    {
        var layout = Layout();
        var sizes = layout.Sizes(9, layout.RestingCentre(4));

        Assert.Equal(Metrics.MaxSize, sizes[4], 6);
        Assert.True(sizes[3] < sizes[4]);
        Assert.True(sizes[5] < sizes[4]);
        Assert.Equal(sizes[3], sizes[5], 6);
    }

    [Fact]
    public void HalfProgress_HalvesTheLift()
    {
        var layout = Layout();
        var full = layout.Sizes(9, layout.RestingCentre(4));
        var half = layout.Sizes(9, layout.RestingCentre(4), progress: 0.5);

        for (var i = 0; i < full.Length; i++)
        {
            var expected = Metrics.BaseSize + ((full[i] - Metrics.BaseSize) * 0.5);
            Assert.Equal(expected, half[i], 6);
        }
    }




    [Fact]
    public void TotalIconWidthIsConstantAcrossTheMiddleOfTheDock()
    {
        var layout = Layout();
        var pitch = Metrics.BaseSize + Metrics.Gap;

        // The raised-cosine falloff sums to a constant over evenly spaced neighbours, so
        // sweeping the pointer must not change the total width the icons occupy — that is
        // what lets the row spread without the dock breathing. Only the middle is checked:
        // near the ends an icon genuinely has fewer neighbours to lift.
        var range = layout.Metrics.EffectiveInfluenceRange;
        var first = layout.RestingCentre(0) + range;
        var last = layout.RestingCentre(11) - range;

        var reference = layout.IconBlockWidth(layout.Sizes(12, first));
        for (var pointer = first; pointer <= last; pointer += 1)
        {
            Assert.Equal(reference, layout.IconBlockWidth(layout.Sizes(12, pointer)), 3);
        }
    }

    [Fact]
    public void IconsSpreadApartAsNeighboursGrow()
    {
        var layout = Layout();
        var atRest = layout.Offsets(layout.Sizes(9, pointerX: null));
        var magnified = layout.Offsets(layout.Sizes(9, layout.RestingCentre(4)));

        // Icons to the right of the hovered one are pushed further right, and the row
        // spreads — this is the effect, not an artefact of it.
        Assert.True(magnified[6] > atRest[6]);
        Assert.True(magnified[8] > atRest[8]);
    }

    [Fact]
    public void InfluenceRangeSnapsToWholePitches()
    {
        // The constant-total property only holds when the range spans whole pitches.
        var metrics = Metrics with { InfluenceRange = 70 };
        var pitch = metrics.BaseSize + metrics.Gap;

        Assert.Equal(0, metrics.EffectiveInfluenceRange % pitch, 6);
        Assert.True(metrics.EffectiveInfluenceRange >= pitch);
    }

    [Fact]
    public void BarWidth_HoldsSteadyAcrossTheMiddleOfTheDock()
    {
        var layout = Layout();
        var width = layout.BarWidth(layout.Sizes(9, layout.RestingCentre(4)));

        // The bar wraps the icons, so this only holds because the raised-cosine falloff
        // sums to a constant: a pointer on an icon and a pointer between two icons lift
        // the same total. Under a plain cosine they did not, and the dock visibly
        // contracted as the pointer moved between icons. Checked across the middle only —
        // near the ends the row really is narrower, and the bar should follow it.
        var range = layout.Metrics.EffectiveInfluenceRange;
        for (var pointer = layout.RestingCentre(0) + range;
             pointer <= layout.RestingCentre(8) - range;
             pointer += 1)
        {
            Assert.Equal(width, layout.BarWidth(layout.Sizes(9, pointer)), 3);
        }
    }

    [Fact]
    public void BarWidth_GrowsMonotonicallyWithAmplitude()
    {
        var layout = Layout();
        var previous = 0d;
        var pointer = layout.RestingCentre(4);

        for (var progress = 0d; progress <= 1; progress += 0.1)
        {
            var width = layout.BarWidth(layout.Sizes(9, pointer, progress));
            Assert.True(width >= previous, $"width shrank at progress {progress}");
            previous = width;
        }

        Assert.Equal(
            layout.RestingWidth(9),
            layout.BarWidth(layout.Sizes(9, pointer, progress: 0)),
            6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void EmptyDock_HasNoGeometry(int count)
    {
        var layout = Layout();
        var sizes = layout.Sizes(count, pointerX: 0);
        Assert.Empty(sizes);
        Assert.Equal(0, layout.BarWidth(sizes), 6);
        Assert.Equal(0, layout.RestingWidth(count), 6);
    }

    [Fact]
    public void SingleIcon_HasNoGapInItsWidth()
    {
        var layout = Layout();
        Assert.Equal(
            (Metrics.PaddingX * 2) + Metrics.BaseSize,
            layout.BarWidth(layout.Sizes(1, pointerX: null)),
            6);
    }

    [Fact]
    public void SteadyBarWidth_DoesNotMoveWhileTheWaveIsUp()
    {
        var layout = new DockLayout(new DockMetrics());

        // The width the bar holds at full amplitude, whatever the pointer is doing — which
        // is the property that keeps the sheet behind it from stuttering.
        var width = layout.SteadyBarWidth(9, amplitude: 1);

        Assert.True(width > layout.RestingWidth(9));
        Assert.Equal(layout.RestingWidth(9), layout.SteadyBarWidth(9, amplitude: 0), 6);
    }

    [Fact]
    public void SteadyBarWidth_IsNeverNarrowerThanTheIconsNeed()
    {
        var layout = new DockLayout(new DockMetrics());
        var held = layout.SteadyBarWidth(9, amplitude: 1);

        // Sweeping the pointer across the dock, the icons must always fit inside the bar.
        for (var pointer = 0d; pointer <= layout.RestingWidth(9); pointer += 3)
        {
            Assert.True(layout.BarWidth(layout.Sizes(9, pointer)) <= held + 0.001);
        }
    }

    /// <summary>
    /// The bar's drawn ends really do move as the wave passes — which is why the hover zone
    /// is not allowed to be measured from them.
    /// </summary>
    /// <remarks>
    /// A wave at the end of the row has half its neighbours missing, so it lifts the total
    /// width by less than one in the middle does and the bar is narrower. The wave grows the
    /// bar about its middle, so half of that difference is how far each end travels while the
    /// wave crosses the dock, and it is a boundary a stationary pointer can find itself on
    /// both sides of.
    /// </remarks>
    [Fact]
    public void BarWidth_MovesTheBarsEndsAsTheWaveCrossesIt()
    {
        var layout = new DockLayout(new DockSettings { InfluenceIcons = 3 }.Metrics);

        var middle = layout.BarWidth(layout.Sizes(9, layout.RestingCentre(4)));
        var end = layout.BarWidth(layout.Sizes(9, layout.RestingCentre(0)));

        Assert.True(middle - end > 2, $"the bar only narrowed by {middle - end} at the end");
    }

    [Fact]
    public void HoverSpan_CoversTheDrawnBarWhereverTheWaveIs()
    {
        var layout = new DockLayout(new DockSettings { InfluenceIcons = 3 }.Metrics);
        const double container = 1200;

        var (left, right) = layout.HoverSpan(
            9, amplitude: 1, restingLeft: (container - layout.RestingWidth(9)) / 2);

        // Whatever the wave is doing, the bar as drawn is inside the zone: no part of the
        // dock the user can see is unhoverable, and — the point of the exercise — the
        // boundary does not move when the wave does.
        for (var pointer = 0d; pointer <= layout.RestingWidth(9); pointer += 3)
        {
            var width = layout.BarWidth(layout.Sizes(9, pointer));
            var barLeft = (container - width) / 2;

            Assert.True(left <= barLeft + 0.001, $"bar starts left of the zone at {pointer}");
            Assert.True(right >= barLeft + width - 0.001, $"bar ends right of the zone at {pointer}");
        }
    }

    [Fact]
    public void HoverSpan_IsTheRestingBarAndItsSlackWhenTheWaveIsDown()
    {
        var layout = new DockLayout(new DockMetrics());
        const double container = 1200;

        var resting = layout.RestingWidth(9);
        var (left, right) = layout.HoverSpan(
            9, amplitude: 0, restingLeft: (container - resting) / 2);
        var slack = new DockMetrics().Gap / 2;

        Assert.Equal(((container - resting) / 2) - slack, left, 6);
        Assert.Equal(((container + resting) / 2) + slack, right, 6);
    }

    [Fact]
    public void InfluenceInIcons_KeepsTheWaveIdenticalAtAnyIconSize()
    {
        // The same dock tuned identically, at two very different icon sizes. Expressing the
        // influence range in icons rather than pixels is what makes these agree: in pixels
        // it covered a different number of neighbours at each size, so changing the icon
        // size quietly changed the wave.
        var small = new DockSettings
        {
            BaseSize = 32, Gap = 8, MaxScale = 1.6, InfluenceIcons = 2
        }.Metrics;

        var large = new DockSettings
        {
            BaseSize = 72, Gap = 18, MaxScale = 1.6, InfluenceIcons = 2
        }.Metrics;

        var smallLayout = new DockLayout(small);
        var largeLayout = new DockLayout(large);

        var smallSizes = smallLayout.Sizes(9, smallLayout.RestingCentre(4));
        var largeSizes = largeLayout.Sizes(9, largeLayout.RestingCentre(4));

        // Every icon is magnified by the same proportion of its own resting size.
        for (var i = 0; i < 9; i++)
        {
            Assert.Equal(smallSizes[i] / small.BaseSize, largeSizes[i] / large.BaseSize, 6);
        }
    }

    [Fact]
    public void InfluenceInPixels_IsCarriedOverAsACount()
    {
        // A settings file written before the change says 80 pixels; at that icon size and
        // gap the dock was already rounding it to two icons, and that is what it becomes.
        var carried = new DockSettings { BaseSize = 60, Gap = 12, InfluenceRange = 80 };

        Assert.Equal(2, carried.Neighbours);
        Assert.Equal(2 * (60 + 12), carried.Metrics.InfluenceRange, 6);
    }

    [Fact]
    public void GapAsAShare_KeepsItsProportionsAtAnyIconSize()
    {
        // A fifth of an icon, whatever an icon happens to be.
        var small = new DockSettings { BaseSize = 30, GapRatio = 0.2 }.Metrics;
        var large = new DockSettings { BaseSize = 90, GapRatio = 0.2 }.Metrics;

        Assert.Equal(6, small.Gap, 6);
        Assert.Equal(18, large.Gap, 6);
        Assert.Equal(small.Gap / small.BaseSize, large.Gap / large.BaseSize, 6);
    }

    [Fact]
    public void GapInPixels_IsCarriedOverAsAShare()
    {
        // A settings file written before the change: twelve pixels against sixty-pixel
        // icons is a fifth, and stays a fifth when the icons change size.
        var carried = new DockSettings { BaseSize = 60, Gap = 12 };

        Assert.Equal(0.2, carried.GapFraction, 6);
        Assert.Equal(12, carried.Metrics.Gap, 6);
    }

    [Fact]
    public void FreshDefaults_AreTheOnesTheDockShips()
    {
        var fresh = new DockSettings();

        Assert.Equal(50, fresh.BaseSize, 6);
        Assert.Equal(1.4, fresh.MaxScale, 6);
        Assert.Equal(3, fresh.Neighbours, 6);

        // Nothing to migrate from, so the defaults stand rather than being derived from a
        // pixel value the settings have never carried.
        Assert.Null(fresh.InfluenceRange);
        Assert.Null(fresh.Gap);
    }
}
