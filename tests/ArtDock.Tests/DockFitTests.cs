using ArtDock.Dock;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers keeping the resting bar on its display: the icons at the dock's size while they
/// fit, smaller as far as the floor when they do not, and past that the last items behind the
/// overflow item — up to <see cref="DockFit.OverflowLimit"/> of them, and no more.
/// </summary>
public class DockFitTests
{
    // The stock dock: 50 DIP icons, the gap 16% of them, 14 DIP of padding at each end.
    private const double Size = 50;
    private const double Floor = 32;
    private const double Gap = 0.16;
    private const double Padding = 14;

    // The main display here: 2560 wide at 100%, less the 12 DIP margin at each side.
    private const double Room = 2560 - 24;

    private static DockFitResult Fit(int count, double room = Room, double size = Size, double floor = Floor) =>
        DockFit.Compute(count, size, floor, Gap, Padding, room);

    private static double Width(int count, double size) => DockFit.RestingWidth(count, size, Gap, Padding);

    [Fact]
    public void TheRestingWidth_IsTheLayouts()
    {
        // The fit's arithmetic is the layout's, with the gap a share of the size.
        var metrics = new DockSettings { BaseSize = 44, GapRatio = Gap }.Metrics;
        var layout = new DockLayout(metrics);

        foreach (var count in new[] { 1, 2, 7, 40 })
        {
            Assert.Equal(layout.RestingWidth(count), Width(count, 44), 6);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(43)]
    public void WhatFits_IsLeftAtTheDocksSize(int count)
    {
        var fit = Fit(count);

        Assert.Equal(Size, fit.IconSize);
        Assert.Equal(count, fit.Visible);
        Assert.False(fit.Shrunk);
        Assert.False(fit.Overflows);
    }

    [Theory]
    [InlineData(44)]
    [InlineData(50)]
    [InlineData(60)]
    [InlineData(67)]
    public void WhatDoesNot_IsShrunkToTheLargestWholeSizeThatFits(int count)
    {
        var fit = Fit(count);

        Assert.True(fit.Shrunk);
        Assert.False(fit.Overflows);
        Assert.Equal(count, fit.Visible);
        Assert.Equal(Math.Floor(fit.IconSize), fit.IconSize);
        Assert.InRange(fit.IconSize, Floor, Size - 1);

        // The largest: it fits, and a pixel more would not.
        Assert.True(Width(count, fit.IconSize) <= Room);
        Assert.True(Width(count, fit.IconSize + 1) > Room);
    }

    [Fact]
    public void ShrinkingGoesBackAsItemsGo()
    {
        // Nothing is remembered: the size is the contents' and the room's, each time.
        Assert.True(Fit(60).Shrunk);
        Assert.Equal(Size, Fit(20).IconSize);
    }

    [Theory]
    [InlineData(70)]
    [InlineData(90)]
    [InlineData(200)]
    public void PastTheFloor_TheLastItemsGoBehindTheOverflowItem(int count)
    {
        var fit = Fit(count);

        Assert.Equal(Floor, fit.IconSize);
        Assert.True(fit.Overflows);
        Assert.Equal(count, fit.Visible + fit.Hidden);

        // The items on the bar and the overflow item fit; one more would not.
        Assert.True(Width(fit.Visible + 1, Floor) <= Room);
        Assert.True(Width(fit.Visible + 2, Floor) > Room);
    }

    [Fact]
    public void AFloorAboveTheDocksSize_IsTheDocksSize()
    {
        // A small dock is never made bigger to fit, and overflows at its own size.
        var fit = Fit(200, size: 24, floor: 40);

        Assert.Equal(24, fit.IconSize);
        Assert.True(fit.Overflows);
        Assert.Equal(24, new DockSettings { BaseSize = 24, MinIconSize = 40 }.IconFloor);
    }

    [Fact]
    public void TheFloorSetting_IsHeldToTheSliders()
    {
        Assert.Equal(DockSettings.IconSizeMin, new DockSettings { MinIconSize = 3 }.IconFloor);
        Assert.Equal(DockSettings.DefaultMinIconSize, new DockSettings { MinIconSize = double.NaN }.IconFloor);
        Assert.Equal(DockSettings.DefaultMinIconSize, new DockSettings().IconFloor);
    }

    [Fact]
    public void ARoomTooSmallForAnything_ShowsOnlyTheOverflowItem()
    {
        var fit = Fit(5, room: 40);

        Assert.Equal(0, fit.Visible);
        Assert.Equal(5, fit.Hidden);
    }

    [Fact]
    public void NoRoomMeasured_FitsEverything()
    {
        var fit = Fit(500, room: double.PositiveInfinity);

        Assert.Equal(Size, fit.IconSize);
        Assert.Equal(500, fit.Visible);
        Assert.Equal(int.MaxValue, DockFit.RoomFor(500, Floor, Gap, Padding, double.PositiveInfinity));
    }

    [Fact]
    public void TheOverflowItemHoldsAtMostItsLimit()
    {
        var capacity = DockFit.Capacity(Floor, Gap, Padding, Room);

        // At capacity, exactly the limit is behind the overflow item, and nothing more is let in.
        var full = Fit(capacity);
        Assert.Equal(DockFit.OverflowLimit, full.Hidden);
        Assert.Equal(0, DockFit.RoomFor(capacity, Floor, Gap, Padding, Room));
        Assert.Equal(1, DockFit.RoomFor(capacity - 1, Floor, Gap, Padding, Room));

        // A dock already past it — moved to a smaller display — is left as it is, and takes no more.
        Assert.Equal(0, DockFit.RoomFor(capacity + 10, Floor, Gap, Padding, Room));
        Assert.Equal(capacity + 10, Fit(capacity + 10).Visible + Fit(capacity + 10).Hidden);
    }

    // ---- room for the wave ----------------------------------------------------------

    /// <summary>The stock dock's wave, both ends together, over its icon size — what DockWindow passes.</summary>
    private static double StockWave()
    {
        var settings = new DockSettings { BaseSize = Size, GapRatio = Gap };
        return 2 * new DockLayout(settings.MetricsAt(Size)).WaveReach / Size;
    }

    [Theory]
    [InlineData(40)]
    [InlineData(44)]
    [InlineData(60)]
    public void WithTheWave_TheWidestBarFitsAndOnePixelMoreWouldNot(int count)
    {
        // Reported on 2026-10-03: fitted at rest alone, a full dock's wave carried the end icons
        // past the display's sides, where they were cut off and awkward to click.
        var wave = StockWave();
        var fit = DockFit.Compute(count, Size, Floor, Gap, Padding, Room, wave);

        Assert.False(fit.Overflows);
        Assert.True(DockFit.WidestWidth(count, fit.IconSize, Gap, Padding, wave) <= Room);
        if (fit.Shrunk)
        {
            Assert.True(DockFit.WidestWidth(count, fit.IconSize + 1, Gap, Padding, wave) > Room);
        }
    }

    [Theory]
    [InlineData(30)]
    [InlineData(50)]
    [InlineData(80)]
    public void TheFittedSize_LeavesTheLayoutsWholeReachAtEachEnd(int count)
    {
        // The fit and the layout agree: at the size fitted, the row the layout places keeps all
        // of its reach for the wave (DockLayout.ReachWithin), so the widest wave stays inside.
        var settings = new DockSettings { BaseSize = Size, GapRatio = Gap, MinIconSize = Floor };
        var fit = DockFit.Compute(count, Size, Floor, Gap, Padding, Room, StockWave());
        var layout = new DockLayout(settings.MetricsAt(fit.IconSize));
        var slots = fit.Overflows ? fit.Visible + 1 : fit.Visible;
        var resting = layout.RestingWidth(slots);

        Assert.Equal(layout.WaveReach, layout.ReachWithin(resting, Room), 6);
        Assert.True(layout.SteadyBarWidth(slots, 1) <= Room + 1e-6);
    }

    [Fact]
    public void TheWavesRoom_ShrinksSoonerAndHoldsFewer()
    {
        var wave = StockWave();

        // 43 fit at rest at the dock's size; with the wave beside them, they are made smaller.
        Assert.False(Fit(43).Shrunk);
        Assert.True(DockFit.Compute(43, Size, Floor, Gap, Padding, Room, wave).Shrunk);
        Assert.True(
            DockFit.Capacity(Floor, Gap, Padding, Room, wave) < DockFit.Capacity(Floor, Gap, Padding, Room));
    }

    // ---- the room the dock keeps: what the end icon needs, and the display -------------

    /// <summary>The fraction DockWindow passes: the room <see cref="DockFit.WaveRoom"/> keeps, over the size.</summary>
    private static double KeptWave(DockSettings settings) =>
        DockFit.WaveRoom(new DockLayout(settings.MetricsAt(settings.IconSize)), settings.BottomMargin)
        / settings.IconSize;

    [Fact]
    public void TheEndIconsLift_IsLessThanTheWholeWave()
    {
        // Why the whole wave's room was more than needed: the end icon has neighbours on one side.
        var layout = new DockLayout(new DockSettings { BaseSize = Size }.MetricsAt(Size));

        Assert.InRange(layout.EndLift, 1, (2 * layout.WaveReach) - 1);
        Assert.True(DockFit.WaveRoom(layout, 12) < 2 * layout.WaveReach);
    }

    [Theory]
    [InlineData(1.4, 3, 50)]
    [InlineData(1.4, 3, 80)]
    [InlineData(2.0, 4, 60)]
    [InlineData(2.5, 6, 70)]
    [InlineData(2.5, 6, 40)]
    public void AtTheRoomKept_TheEndIconIsWholeAndTheBarStaysOnTheDisplay(double scale, int neighbours, int count)
    {
        // Reported the same evening: the whole wave's room shrank the dock more than it had to.
        // What it must still do: with the pointer on an end icon the bar keeps its margin, and
        // with the widest wave anywhere it stays on the display — at every alignment.
        const double margin = 12;
        var settings = new DockSettings
        {
            BaseSize = Size, GapRatio = Gap, MinIconSize = Floor, MaxScale = scale,
            InfluenceIcons = neighbours, BottomMargin = margin
        };
        var fit = DockFit.Compute(count, Size, Floor, Gap, Padding, Room, KeptWave(settings));
        var layout = new DockLayout(settings.MetricsAt(fit.IconSize));
        var slots = fit.Overflows ? fit.Visible + 1 : fit.Visible;
        var resting = layout.RestingWidth(slots);
        var widest = layout.SteadyBarWidth(slots, 1) - resting;

        foreach (var alignment in new[] { 0.0, 0.5, 1.0 })
        {
            // The display runs 0 to Room + 2·margin; the row's room is inside the margin.
            var left = layout.RestingLeft(resting, margin, Room, alignment, Room);
            var right = left + resting;
            var label = $"{count} at {scale}x/{neighbours}, aligned {alignment} [size {fit.IconSize} slots {slots} resting {resting:0.0} reach {layout.WaveReach:0.0} within {layout.ReachWithin(resting, Room):0.0} widest {widest:0.0} endlift {layout.EndLift:0.0} left {left:0.0} kept {KeptWave(settings) * fit.IconSize:0.0}]";

            Assert.True(left - (layout.EndLift / 2) >= margin - 1e-6, label + ": left end icon pointed at");
            Assert.True(right + (layout.EndLift / 2) <= margin + Room + 1e-6, label + ": right end icon pointed at");
            Assert.True(left - (widest / 2) >= -1e-6, label + ": widest wave, left");
            Assert.True(right + (widest / 2) <= Room + (2 * margin) + 1e-6, label + ": widest wave, right");
        }
    }

    [Fact]
    public void TheWaveSpansTheNeighboursSet_AtEverySize()
    {
        // 6 × (45 + 7.2) ÷ 52.2 came back as 6.000000000000001 and rounded up to 7: a wave an
        // icon wider at some sizes than at others, which the room kept for it did not allow for.
        for (var size = DockSettings.IconSizeMin; size <= DockSettings.IconSizeMax; size++)
        {
            for (var neighbours = 1; neighbours <= 6; neighbours++)
            {
                var metrics = new DockSettings { InfluenceIcons = neighbours }.MetricsAt(size);
                Assert.Equal(neighbours, metrics.EffectiveInfluenceRange / metrics.Pitch, 9);
            }
        }
    }

    [Fact]
    public void TheRoomKept_IsLessThanTheWholeWaves()
    {
        // So the dock is let a little bigger than with the whole wave's room: more on the bar.
        var settings = new DockSettings { BaseSize = Size, GapRatio = Gap, MinIconSize = Floor };

        Assert.True(KeptWave(settings) < StockWave());
        Assert.True(
            DockFit.Capacity(Floor, Gap, Padding, Room, KeptWave(settings))
                >= DockFit.Capacity(Floor, Gap, Padding, Room, StockWave()));
    }

    [Fact]
    public void NoWave_IsTheRestingFit()
    {
        foreach (var count in new[] { 10, 50, 90 })
        {
            Assert.Equal(Fit(count), DockFit.Compute(count, Size, Floor, Gap, Padding, Room, 0));
        }
    }

    [Fact]
    public void ASmallerDisplay_TakesFewer()
    {
        // The 4K display's work area is 3840 wide at 150%: 2560 DIPs, as the main one — so a
        // display of its own size in DIPs fits the same. A narrower one fits fewer.
        Assert.True(
            DockFit.Capacity(Floor, Gap, Padding, 1366 - 24) < DockFit.Capacity(Floor, Gap, Padding, Room));
    }
}
