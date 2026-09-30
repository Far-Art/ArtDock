using System.Runtime.ExceptionServices;
using System.Windows;
using ArtDock.Controls;
using ArtDock.Dock;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers where the handle that marks a dock out of sight goes, and how it looks.
/// </summary>
/// <remarks>
/// <para>
/// In physical pixels, against this machine's two displays at their real sizes: the main one
/// at 100%, its work area ending at 1392, and the 4K one to its left at 150%, from (-3840,8)
/// with its work area ending at 2096. The handle is a window of its own placed by
/// <c>SetWindowPos</c>, so nothing about it passes through WPF's idea of the scale, and the
/// scale is the thing most likely to be got wrong.
/// </para>
/// <para>
/// What matters: it is centred on the dock wherever the dock sits along its edge; as wide as
/// the bar when it matches the dock, and a width in DIPs — so wider in pixels at 150% — when it
/// does not; just above the taskbar and never on it; and in whole pixels. Nothing here is about
/// the pointer: the handle is a mark only, and the edge below the taskbar is what brings the
/// dock up. Where the pointer counted as on it was tested here until 2026-09-30, when resting
/// on it stopped bringing the dock.
/// </para>
/// </remarks>
public class DockHandleTests
{
    private const double MainWorkBottom = 1392;
    private const double WideWorkBottom = 8 + 2088;

    [Fact]
    public void A_handle_as_wide_as_the_dock_is_the_bar_exactly()
    {
        var handle = DockHandle.Place(1000, 1560, MainWorkBottom, 1, matchDock: true, width: 140);

        Assert.Equal(1000, handle.Left);
        Assert.Equal(1560, handle.Right);
    }

    [Theory]
    [InlineData(1000, 1560)]
    [InlineData(12, 400)]
    [InlineData(2000, 2548)]
    public void A_width_of_its_own_is_centred_on_the_bar_wherever_the_bar_is(double left, double right)
    {
        var handle = DockHandle.Place(left, right, MainWorkBottom, 1, matchDock: false, width: 140);

        Assert.Equal(140, handle.Width);
        Assert.Equal((left + right) / 2, handle.Left + (handle.Width / 2), precision: 0);
    }

    [Fact]
    public void A_width_of_its_own_wider_than_the_dock_is_still_centred_on_it()
    {
        var handle = DockHandle.Place(1200, 1300, MainWorkBottom, 1, matchDock: false, width: 400);

        Assert.Equal(1050, handle.Left);
        Assert.Equal(1450, handle.Right);
    }

    [Fact]
    public void A_width_of_its_own_is_in_DIPs_so_it_is_wider_in_pixels_on_a_scaled_display()
    {
        var main = DockHandle.Place(1000, 1560, MainWorkBottom, 1, matchDock: false, width: 140);
        var wide = DockHandle.Place(-2400, -1560, WideWorkBottom, 1.5, matchDock: false, width: 140);

        Assert.Equal(140, main.Width);
        Assert.Equal(210, wide.Width);
    }

    [Fact]
    public void It_sits_just_above_the_taskbar_and_never_on_it()
    {
        var handle = DockHandle.Place(1000, 1560, MainWorkBottom, 1, matchDock: true, width: 140);

        Assert.Equal(MainWorkBottom - DockHandle.Lift, handle.Bottom);
        Assert.Equal(DockHandle.Thickness, handle.Height);
        Assert.True(handle.Bottom < MainWorkBottom);
    }

    [Fact]
    public void Its_thickness_and_its_lift_scale_with_the_display()
    {
        var handle = DockHandle.Place(-2400, -1560, WideWorkBottom, 1.5, matchDock: true, width: 140);

        // 5 DIPs is 7.5 pixels, which a window cannot be: it rounds, and the lift of 6 is 9.
        Assert.Equal(8, handle.Height);
        Assert.Equal(WideWorkBottom - 9, handle.Bottom);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 10_000)]
    [InlineData(1.5, 0)]
    [InlineData(1.5, 10_000)]
    public void A_width_of_its_own_is_held_to_the_slider_s_range(double scale, double width)
    {
        var handle = DockHandle.Place(1000, 1560, MainWorkBottom, scale, matchDock: false, width);

        Assert.InRange(
            handle.Width,
            Math.Round(DockHandle.MinWidth * scale),
            Math.Round(DockHandle.MaxWidth * scale));
    }

    [Theory]
    [InlineData(1000.3, 1561.1, 1)]
    [InlineData(-2400.25, -1559.5, 1.5)]
    [InlineData(-2400.25, -1559.5, 1.25)]
    public void It_is_placed_in_whole_pixels(double left, double right, double scale)
    {
        foreach (var match in new[] { true, false })
        {
            var handle = DockHandle.Place(left, right, WideWorkBottom, scale, match, 141.7);

            Assert.Equal(Math.Round(handle.X), handle.X);
            Assert.Equal(Math.Round(handle.Y), handle.Y);
            Assert.Equal(Math.Round(handle.Width), handle.Width);
            Assert.Equal(Math.Round(handle.Height), handle.Height);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_scale_that_is_no_scale_is_taken_as_one(double scale)
    {
        var handle = DockHandle.Place(1000, 1560, MainWorkBottom, scale, matchDock: false, width: 140);

        Assert.Equal(140, handle.Width);
        Assert.Equal(DockHandle.Thickness, handle.Height);
    }

    [Fact]
    public void A_width_that_is_no_width_is_the_default()
    {
        var handle = DockHandle.Place(1000, 1560, MainWorkBottom, 1, matchDock: false, double.NaN);

        Assert.Equal(DockHandle.DefaultWidth, handle.Width);
    }

    // ---- the inverted look ------------------------------------------------------

    /// <summary>One BGRA pixel through <see cref="DockHandle.Invert"/>, as (red, green, blue, alpha).</summary>
    private static (int R, int G, int B, int A) Inverted(byte red, byte green, byte blue, byte alpha = 0)
    {
        var shown = new byte[4];
        DockHandle.Invert([blue, green, red, alpha], shown);
        return (shown[2], shown[1], shown[0], shown[3]);
    }

    [Fact]
    public void White_comes_back_black_and_black_white()
    {
        Assert.Equal((0, 0, 0, 255), Inverted(255, 255, 255));
        Assert.Equal((255, 255, 255, 255), Inverted(0, 0, 0));
    }

    /// <summary>
    /// The one that matters: a plain inversion gives mid-grey back as mid-grey, and a handle
    /// over a grey status bar would vanish into it.
    /// </summary>
    [Fact]
    public void No_grey_comes_back_as_itself()
    {
        for (var level = 0; level <= 255; level++)
        {
            var grey = (byte)level;
            var (r, g, b, _) = Inverted(grey, grey, grey);

            Assert.Equal(r, g);
            Assert.Equal(g, b);
            Assert.True(Math.Abs(r - level) >= 50, $"grey {level} came back as {r}");
        }
    }

    [Fact]
    public void A_colour_comes_back_as_its_opposite()
    {
        // Red is dark by luminance, so its inverse, cyan, is pushed lighter — and stays cyan.
        var (r, g, b, _) = Inverted(255, 0, 0);
        Assert.True(r < g && r < b, $"red came back as ({r}, {g}, {b})");
        Assert.Equal(g, b);

        // Yellow is light, so its inverse, blue, is pushed darker — and stays blue.
        (r, g, b, _) = Inverted(255, 255, 0);
        Assert.True(b > r && b > g, $"yellow came back as ({r}, {g}, {b})");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    public void What_is_shown_is_opaque_whatever_the_read_said_of_alpha(byte alpha)
    {
        Assert.Equal(255, Inverted(12, 200, 90, alpha).A);
    }

    [Fact]
    public void Every_pixel_of_a_strip_is_inverted_and_nothing_past_it_is_touched()
    {
        byte[] behind = [255, 255, 255, 0, 0, 0, 0, 0, 7];
        var shown = new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 42 };

        DockHandle.Invert(behind, shown);

        Assert.Equal(new byte[] { 0, 0, 0, 255, 255, 255, 255, 255, 42 }, shown);
    }

    // ---- what it is measured from -----------------------------------------------

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

    private static DockBar Arranged(int count, double alignment)
    {
        var bar = new DockBar(new DockSettings { BaseSize = 50 }.Metrics) { RowAlignment = alignment };
        bar.SetItems([.. Enumerable.Range(0, count).Select(i => new DockItem { Id = $"p{i}", Label = $"P{i}" })]);

        var size = bar.PreferredSize();
        bar.Width = size.Width;
        bar.Height = size.Height;
        bar.Measure(size);
        bar.Arrange(new Rect(size));
        bar.UpdateLayout();

        return bar;
    }

    /// <summary>
    /// The resting bar the handle is measured from is the bar a dock at rest actually draws —
    /// wherever it has been moved along its edge, and empty as well, where the bar is one slot
    /// drawn from a different path.
    /// </summary>
    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(1, 0.5)]
    [InlineData(6, 0.5)]
    [InlineData(6, 0)]
    [InlineData(6, 1)]
    [InlineData(0, 1)]
    public void The_resting_bar_is_the_bar_a_dock_at_rest_draws(int count, double alignment) =>
        OnStaThread(() =>
        {
            var bar = Arranged(count, alignment);

            Assert.Equal(bar.BarRect.X, bar.RestingBarRect.X, precision: 6);
            Assert.Equal(bar.BarRect.Width, bar.RestingBarRect.Width, precision: 6);
            Assert.Equal(bar.BarRect.Y, bar.RestingBarRect.Y, precision: 6);
        });
}
