using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using ArtDock.Controls;
using ArtDock.Dock;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers moving the dock along its edge, away from the middle.
/// </summary>
/// <remarks>
/// <para>
/// The dock was centred by construction: the window in the work area, the row in the window,
/// and every change of width shared out half to each end. Pushing it towards one end means
/// the row is anchored instead — it grows away from the end it is nearest — while the wave
/// keeps growing about the row's middle, because a wave held against one end pushes the icon
/// under the pointer out from under it. The wave is given room at both ends instead.
/// </para>
/// <para>
/// So the properties worth holding are: centred is exactly the dock as it was; nothing the
/// dock draws leaves its span at any alignment; at the ends it goes exactly as far as that
/// allows and no further; the end it was pushed to stays put as icons come and go; and the
/// window growing underneath it — which the settings dialog does on purpose — moves nothing.
/// </para>
/// </remarks>
public class DockAlongEdgeTests
{
    private static void OnStaThread(Action body)
    {
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure?.Throw();
    }

    /// <summary>The metrics a settings file with these values projects to.</summary>
    private static DockMetrics Metrics(double scale = 1.4, int influence = 3) =>
        new DockSettings { BaseSize = 50, MaxScale = scale, InfluenceIcons = influence }.Metrics;

    private static DockLayout Layout(double scale = 1.4, int influence = 3) =>
        new(Metrics(scale, influence));

    private static DockItem[] Pins(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new DockItem { Id = $"p{i}", Label = $"P{i}" })];

    /// <summary>A dock laid out in a window of the size it asks for.</summary>
    private static DockBar Arranged(DockMetrics metrics, int count, double alignment)
    {
        var bar = new DockBar(metrics) { RowAlignment = alignment };
        bar.SetItems(Pins(count));

        var size = bar.PreferredSize();
        bar.Width = size.Width;
        bar.Height = size.Height;
        bar.Measure(size);
        bar.Arrange(new Rect(size));
        bar.UpdateLayout();

        return bar;
    }

    // Where the row is asked to live: a work area, less the margin at each end.
    private const double SpanStart = 40;
    private const double SpanLength = 1600;

    // ---- the setting ------------------------------------------------------------

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(-0.5, 0.25)]
    [InlineData(0, 0.5)]
    [InlineData(1, 1)]
    [InlineData(-7, 0)]
    [InlineData(7, 1)]
    [InlineData(double.NaN, 0.5)]
    public void TheOffset_BecomesTheRowsAlignment(double offset, double alignment)
    {
        // Out of range is held at the end rather than taken past it, and a value that is not
        // a number at all is the middle, which is where every dock sat before this existed.
        Assert.Equal(alignment, new DockSettings { OffsetAlongEdge = offset }.EdgeAlignment, 6);
    }

    [Fact]
    public void AFileFromBeforeTheOffset_ReadsAsCentred()
    {
        Assert.Equal(0.5, new DockSettings().EdgeAlignment, 6);
    }

    // ---- the wave's room ----------------------------------------------------------

    [Theory]
    [InlineData(1.4, 3)]
    [InlineData(2.5, 6)]
    [InlineData(1.8, 1)]
    public void WaveReach_IsHalfOfWhatAWholeWaveAdds(double scale, int influence)
    {
        // Closed-form, against the sweep that finds it the long way. A row of 2N + 3 icons
        // holds a whole wave with room to spare.
        var layout = Layout(scale, influence);

        Assert.Equal(layout.MaxLift((2 * influence) + 3), 2 * layout.WaveReach, 3);
    }

    [Fact]
    public void WaveReach_BoundsEveryRowWhateverItHolds()
    {
        var layout = Layout();

        for (var count = 1; count <= 16; count++)
        {
            Assert.True(
                layout.MaxLift(count) <= (2 * layout.WaveReach) + 0.001,
                $"a row of {count} lifts by {layout.MaxLift(count)}, past the {2 * layout.WaveReach} it is given");
        }
    }

    [Fact]
    public void WithoutMagnification_ThereIsNoWaveToMakeRoomFor()
    {
        Assert.Equal(0, Layout(scale: 1).WaveReach, 6);
    }

    // ---- where the row sits ---------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(13)]
    public void Centred_IsWhereTheDockHasAlwaysSat(int count)
    {
        var layout = Layout();
        const double container = 900;
        const double slack = 24;
        var resting = layout.RestingWidth(count);

        Assert.Equal(
            (container - resting) / 2,
            layout.RestingLeft(resting, slack, container - (2 * slack), alignment: 0.5),
            6);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.2)]
    [InlineData(0.5)]
    [InlineData(0.8)]
    [InlineData(1.0)]
    public void NoWaveTakesThePushedDockOutOfItsSpan(double alignment)
    {
        // Every width the bar is drawn at — every point of a wave crossing it and past its
        // ends — for rows too short to hold a whole wave and rows long enough to, at an
        // ordinary magnification and at the strongest there is.
        foreach (var (scale, influence) in new[] { (1.4, 3), (2.5, 6) })
        {
            var layout = Layout(scale, influence);

            foreach (var count in new[] { 1, 2, 5, 12 })
            {
                var resting = layout.RestingWidth(count);
                var restingLeft = layout.RestingLeft(resting, SpanStart, SpanLength, alignment);

                for (var pointer = -100d; pointer <= resting + 100; pointer += 2)
                {
                    var width = layout.BarWidth(layout.Sizes(count, pointer));

                    // Grown about the resting row's middle, which is how DockBar draws it.
                    var left = restingLeft - ((width - resting) / 2);

                    Assert.True(
                        left >= SpanStart - 1e-6,
                        $"{count} icons at {scale}x: the bar starts at {left}, before the span at {SpanStart}");
                    Assert.True(
                        left + width <= SpanStart + SpanLength + 1e-6,
                        $"{count} icons at {scale}x: the bar ends at {left + width}, past the span");
                }
            }
        }
    }

    [Fact]
    public void AtEitherEnd_TheWidestWaveMeetsTheEdgeExactly()
    {
        // As far as it goes means exactly that: pushed all the way, the widest bar the dock
        // can draw touches the end of its span — not short of it, not past it.
        var layout = Layout();
        const int count = 9;
        var resting = layout.RestingWidth(count);
        var reach = (layout.SteadyBarWidth(count, 1) - resting) / 2;

        var leftmost = layout.RestingLeft(resting, SpanStart, SpanLength, alignment: 0) - reach;
        var rightmost = layout.RestingLeft(resting, SpanStart, SpanLength, alignment: 1)
            + resting + reach;

        Assert.Equal(SpanStart, leftmost, 3);
        Assert.Equal(SpanStart + SpanLength, rightmost, 3);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void ThePushedEndStaysPutAsIconsComeAndGo(double alignment)
    {
        // The point that far along the row — its left end at 0, its middle at ½, its right end
        // at 1 — is where the row is anchored, and a row that gains or loses an icon grows or
        // shrinks about it. A dock pushed to one end therefore stays at that end.
        var layout = Layout();
        double? anchor = null;

        for (var count = 1; count <= 12; count++)
        {
            var resting = layout.RestingWidth(count);
            var here = layout.RestingLeft(resting, SpanStart, SpanLength, alignment)
                + (alignment * resting);

            anchor ??= here;
            Assert.Equal(anchor.Value, here, 6);
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.35)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void TheDockLandsInTheSamePlaceWhateverSizeItsWindowIs(double alignment) => OnStaThread(() =>
    {
        // What DockWindow and DockBar do between them: the window aligned along the work area
        // between insets of the margin less the window's slack, and the row aligned inside
        // the window's slack. The settings dialog holds the window wider than the dock; if
        // the row's place depended on the window's width it would wander along the edge while
        // a slider was being dragged.
        var layout = Layout();
        const double workLeft = -2560;
        const double workWidth = 2560;
        const double margin = 12;
        var slack = DockBar.SideInset;
        var side = margin - slack;
        var resting = layout.RestingWidth(7);

        var direct = layout.RestingLeft(resting, workLeft + margin, workWidth - (2 * margin), alignment);

        foreach (var windowWidth in new[] { 700.0, 940, 1800 })
        {
            var windowLeft = DockLayout.Align(
                workLeft + side, workWidth - (2 * side), windowWidth, alignment);
            var rowLeft = windowLeft
                + layout.RestingLeft(resting, slack, windowWidth - (2 * slack), alignment);

            Assert.Equal(direct, rowLeft, 6);
        }
    });

    // ---- the dock itself ------------------------------------------------------------

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void TheBarAndItsIconsRestWhereTheLayoutPutsThem(double alignment) => OnStaThread(() =>
    {
        var metrics = Metrics();
        var layout = new DockLayout(metrics);
        var bar = Arranged(metrics, count: 6, alignment);

        var resting = layout.RestingWidth(6);
        var expected = layout.RestingLeft(
            resting, DockBar.SideInset, bar.ActualWidth - (2 * DockBar.SideInset), alignment);

        Assert.Equal(expected, bar.BarRect.X, 6);
        Assert.Equal(resting, bar.BarRect.Width, 6);

        // Laid out from the same edge the bar is drawn from. The two are computed on
        // different paths, and a row that disagreed with its own bar would sit off-centre in
        // it by exactly the distance the dock had been moved.
        Assert.Equal(expected + metrics.PaddingX, Canvas.GetLeft(bar.Children[0]), 6);
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(9)]
    public void PushedToEitherEnd_TheWindowStillHoldsTheWave(int count) => OnStaThread(() =>
    {
        // The envelope is the widest the bar can get. It has to stay inside the window's slack
        // at both ends, short row or long — a short row lifts by less than the reach it is
        // given, which is the extra width the window asks for it. And its shadow has to stay
        // inside the window: the acrylic sheet is the window's twin and draws the shadow while
        // the blur is on, so any of it past the window's edge would be cut off.
        var metrics = Metrics();

        foreach (var alignment in new[] { 0.0, 1.0 })
        {
            var bar = Arranged(metrics, count, alignment);
            var envelope = bar.MaxBarRect;
            var drawn = bar.BarRect;

            Assert.True(
                envelope.Left >= DockBar.SideInset - 1e-6,
                $"{count} at {alignment}: the envelope starts at {envelope.Left}, inside the slack");
            Assert.True(
                envelope.Right <= bar.ActualWidth - DockBar.SideInset + 1e-6,
                $"{count} at {alignment}: the envelope ends at {envelope.Right}, inside the slack");
            Assert.True(
                drawn.Left >= envelope.Left - 1e-6 && drawn.Right <= envelope.Right + 1e-6,
                $"{count} at {alignment}: the bar is not inside the envelope");

            for (var pass = 0; pass < BarShadow.Passes.Count; pass++)
            {
                var shadow = BarShadow.Outermost(envelope, metrics.BarRadius, pass).Bounds;
                Assert.True(
                    shadow.Left >= 0 && shadow.Right <= bar.ActualWidth && shadow.Bottom <= bar.ActualHeight,
                    $"{count} at {alignment}: the widest bar's shadow reaches {shadow}, outside the {bar.ActualWidth}×{bar.ActualHeight} window");
            }
        }
    });

    [Fact]
    public void ADockPushedLeftKeepsItsLeftEnd() => OnStaThread(() =>
    {
        // Each at the window it asks for, which is wider the more it holds.
        var metrics = Metrics();
        var layout = new DockLayout(metrics);
        var home = DockBar.SideInset + layout.WaveReach;

        for (var count = 1; count <= 9; count++)
        {
            Assert.Equal(home, Arranged(metrics, count, alignment: 0).BarRect.X, 6);
        }
    });

    [Fact]
    public void ADockPushedRightKeepsItsRightEnd() => OnStaThread(() =>
    {
        var metrics = Metrics();
        var layout = new DockLayout(metrics);
        var home = DockBar.SideInset + layout.WaveReach;

        for (var count = 1; count <= 9; count++)
        {
            var bar = Arranged(metrics, count, alignment: 1);
            Assert.Equal(home, bar.ActualWidth - bar.BarRect.Right, 6);
        }
    });

    [Fact]
    public void AnIconArrivingOpensTheBarAwayFromThePushedEnd() => OnStaThread(() =>
    {
        // Mid-animation rather than settled: the bar eases open over a new icon, and the
        // easing has to be anchored too. Centred, it opened half a pitch each way; pushed
        // left, the left end must not move at all while the right one opens.
        var bar = Arranged(Metrics(), count: 6, alignment: 0);
        var before = bar.BarRect;

        bar.SetItems(Pins(7));

        Assert.Equal(before.Left, bar.BarRect.Left, 6);
    });

    /// <summary>
    /// Pushed to one end, the window reaches past the side of the screen by the slack beside
    /// the bar less the margin — transparent, except that a label is clamped to the window
    /// rather than to the screen.
    /// </summary>
    [Fact]
    public void ALongNameOnTheEndIconStaysOnTheScreen() => OnStaThread(() =>
    {
        // Where the side of the screen falls inside the window of a dock pushed left: the
        // slack of 24 less a margin of 12.
        const double screenEdge = 12;

        DockItem[] pins =
        [
            new() { Id = "long", Label = "A name long enough to reach well past the end of the dock" },
            new() { Id = "b", Label = "B" },
            new() { Id = "c", Label = "C" }
        ];

        var bar = new DockBar(Metrics()) { RowAlignment = 0 };
        bar.SetItems(pins);

        var size = bar.PreferredSize();
        var w = (int)Math.Ceiling(size.Width);
        var h = (int)Math.Ceiling(size.Height);
        bar.Width = w;
        bar.Height = h;
        bar.Measure(new Size(w, h));
        bar.Arrange(new Rect(0, 0, w, h));
        bar.UpdateLayout();
        bar.FocusItem(pins[0]);

        // The bubble is the only thing drawn near-opaque: the bar is half transparent and its
        // shadow much fainter still.
        int LeftmostOpaque()
        {
            // A render target draws what the element last rendered, and holding a label or
            // moving the screen edge only invalidates it; the layout pass is what redraws it.
            bar.UpdateLayout();

            var target = new System.Windows.Media.Imaging.RenderTargetBitmap(
                w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            target.Render(bar);

            var pixels = new int[w * h];
            target.CopyPixels(pixels, w * 4, 0);

            for (var x = 0; x < w; x++)
            {
                for (var y = 0; y < h; y++)
                {
                    if ((uint)pixels[(y * w) + x] >> 24 > 200)
                    {
                        return x;
                    }
                }
            }

            return w;
        }

        // Unbounded first, to show the case is real: held only inside the window, the label
        // is drawn across the side of the screen.
        Assert.True(LeftmostOpaque() < screenEdge, "the label never reached the edge, so this proves nothing");

        bar.OnScreen = (screenEdge, double.PositiveInfinity);

        Assert.True(
            LeftmostOpaque() >= screenEdge + 1,
            "the end icon's label was drawn past the side of the screen");
    });

    [Fact]
    public void MovingTheRowAlongPutsTheBarWhereTheNewAlignmentSays() => OnStaThread(() =>
    {
        // The setting arrives on every tick of the slider, on a dock already laid out.
        var metrics = Metrics();
        var layout = new DockLayout(metrics);
        var bar = Arranged(metrics, count: 6, alignment: 0.5);

        bar.RowAlignment = 0.2;

        var resting = layout.RestingWidth(6);
        var expected = layout.RestingLeft(
            resting, DockBar.SideInset, bar.ActualWidth - (2 * DockBar.SideInset), 0.2);

        Assert.Equal(expected, bar.BarRect.X, 6);
        Assert.Equal(expected + metrics.PaddingX, Canvas.GetLeft(bar.Children[0]), 6);
    });
}
