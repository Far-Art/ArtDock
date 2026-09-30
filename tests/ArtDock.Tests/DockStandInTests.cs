using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.Controls;
using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers the dock while the acrylic sheet behind it draws the bar (<see cref="DockBar.DrawsBar"/>).
/// </summary>
/// <remarks>
/// The sheet draws the bar so the bar and its blur reach the screen in the same frame, which
/// two windows drawn by two different paths could not be made to do. What the dock must still
/// do is keep the bar's area its own: its window is layered, and Windows hit-tests a layered
/// window by the alpha of what it paints — the sheet under it passes every click by.
/// </remarks>
public class DockStandInTests
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

    private static DockMetrics Tuned() => new() { BaseSize = 50, MaxSize = 70, Gap = 8 };

    private const int W = 320;
    private const int H = 220;

    private static DockBar Laid(bool drawsBar)
    {
        var bar = new DockBar(Tuned()) { Width = W, Height = H, DrawsBar = drawsBar };
        bar.SetItems([]);
        bar.SetBarAppearance("#FF0000", 0.5);
        bar.Measure(new Size(W, H));
        bar.Arrange(new Rect(0, 0, W, H));
        bar.UpdateLayout();
        return bar;
    }

    /// <summary>The alpha painted at each pixel.</summary>
    private static byte[] Alphas(DockBar bar)
    {
        var target = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        target.Render(bar);

        var pixels = new int[W * H];
        target.CopyPixels(pixels, W * 4, 0);
        return [.. pixels.Select(pixel => (byte)((uint)pixel >> 24))];
    }

    private static byte At(byte[] alphas, double x, double y) => alphas[((int)y * W) + (int)x];

    [Fact]
    public void WhileTheSheetDrawsTheBar_TheDockStillPaintsItsArea_Invisibly() => OnStaThread(() =>
    {
        var bar = Laid(drawsBar: false);
        var alphas = Alphas(bar);
        var rect = bar.BarRect;

        var middle = At(alphas, rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
        Assert.True(middle >= 1, "the bar painted nothing, so its layered window would pass every click through");
        Assert.True(middle <= 2, $"the bar painted alpha {middle} where the sheet already draws it");

        // The strip the shadow falls into took clicks when the dock drew the shadow, and still does.
        var shadow = At(alphas, rect.X + (rect.Width / 2), rect.Bottom + 12);
        Assert.True(shadow is >= 1 and <= 2, $"the shadow's strip painted alpha {shadow}");

        // Where neither was, nothing: that is where clicks go through to the window behind.
        Assert.Equal(0, At(alphas, rect.X + (rect.Width / 2), rect.Y - 30));
        Assert.Equal(0, At(alphas, 2, rect.Y + (rect.Height / 2)));
    });

    [Fact]
    public void DrawingTheBarItself_PaintsItAtItsOpacity() => OnStaThread(() =>
    {
        var bar = Laid(drawsBar: true);
        var rect = bar.BarRect;
        var middle = At(Alphas(bar), rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));

        Assert.InRange(middle, 120, 135);
    });

    [Fact]
    public void TheFillTheSheetPaints_IsTheOneTheDockWould() => OnStaThread(() =>
    {
        var bar = new DockBar(Tuned());
        bar.SetBarAppearance("#FF0000", 0.5);

        Assert.Equal(Color.FromArgb(127, 0xFF, 0x00, 0x00), bar.BarFill);
    });

    /// <summary>
    /// The sheet draws the visible bar, so it has to hear of a move smaller than a pixel: an
    /// edge that followed only whole steps would step, and would come to rest where the last
    /// small move left it rather than where the icons are. And it hears of it once the dock has
    /// drawn it, not as it is computed — the sheet's road to the screen is the shorter one, and
    /// told at once it drew the bar ahead of the icons.
    /// </summary>
    [Fact]
    public void AMoveOfAFractionOfAPixel_IsAnnounced_OnceItHasBeenDrawn() => OnStaThread(() =>
    {
        var metrics = Tuned();
        var bar = new DockBar(metrics) { Width = W, Height = H };
        var announced = new List<Rect>();
        bar.BarRectChanged += (_, rect) => announced.Add(rect);

        bar.SetItems([]);
        bar.Measure(new Size(W, H));
        bar.Arrange(new Rect(0, 0, W, H));
        bar.UpdateLayout();
        Pump(() => announced.Count > 0);
        Assert.Equal(bar.BarRect, bar.RenderedBarRect);

        announced.Clear();
        var before = bar.RenderedBarRect;
        bar.UpdateMetrics(metrics with { PaddingX = metrics.PaddingX + 0.1 });

        Assert.Empty(announced);
        Assert.Equal(before, bar.RenderedBarRect);

        Pump(() => announced.Count > 0);
        Assert.NotEmpty(announced);
        Assert.Equal(bar.BarRect, announced[^1]);
        Assert.Equal(bar.BarRect, bar.RenderedBarRect);
    });

    /// <summary>Runs the dispatcher — and with it the render loop — until something has happened, or two seconds.</summary>
    private static void Pump(Func<bool> done)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var until = DateTime.UtcNow.AddSeconds(2);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
        timer.Tick += (_, _) =>
        {
            if (done() || DateTime.UtcNow > until)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };

        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
