using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.Controls;
using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers the bar a dock draws when nothing is pinned.
/// </summary>
/// <remarks>
/// An empty dock used to draw nothing at all. That was invisible for as long as the list
/// could not be emptied on purpose, and became reachable the moment the Items page grew a
/// Clear all. It is worse than it looks: the dock's window is <c>AllowsTransparency</c>, so
/// Windows hit-tests it by the alpha of what has been painted. No bar meant no opaque pixels,
/// every click passing through to whatever was behind, and no way to reach the right-click
/// menu that exists precisely to refill an emptied dock — the tray icon was the only route
/// back. The bar is therefore drawn at one slot wide, which is the room the window already
/// reserves for a drop preview.
/// </remarks>
public class DockEmptyBarTests
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

    private static DockItem[] Pins(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new DockItem { Id = $"p{i}", Label = $"P{i}" })];

    [Fact]
    public void AnEmptyDockStillDrawsABar() => OnStaThread(() =>
    {
        var metrics = Tuned();
        var bar = new DockBar(metrics);
        bar.SetItems([]);

        Assert.True(
            bar.BarRect.Width > 0,
            "an empty dock drew no bar, so there was nothing to click, drop onto or even see");

        // One resting slot: the same room the window already holds for a drop preview.
        Assert.Equal(new DockLayout(metrics).RestingWidth(1), bar.BarRect.Width, 6);
    });

    [Fact]
    public void ItIsTallEnoughToBeAimedAt() => OnStaThread(() =>
    {
        var metrics = Tuned();
        var bar = new DockBar(metrics);
        bar.SetItems([]);

        // Full bar height, not a sliver: it has to be a target a pointer can find.
        Assert.Equal(metrics.BarHeight, bar.BarRect.Height, 6);
    });

    /// <summary>
    /// The one that matters: pixels, not geometry.
    /// </summary>
    /// <remarks>
    /// A correct <see cref="DockBar.BarRect"/> is not enough. The dock's window is
    /// <c>WS_EX_LAYERED</c> and Windows hit-tests it by the alpha of what was actually
    /// painted, so an empty dock that computes a bar but does not draw one is still a window
    /// nothing can land on.
    /// </remarks>
    [Fact]
    public void ItPaintsSomething() => OnStaThread(() =>
    {
        const int w = 240;
        const int h = 160;

        var bar = new DockBar(Tuned()) { Width = w, Height = h };
        bar.SetItems([]);
        bar.Measure(new Size(w, h));
        bar.Arrange(new Rect(0, 0, w, h));
        bar.UpdateLayout();

        var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        target.Render(bar);

        var pixels = new int[w * h];
        target.CopyPixels(pixels, w * 4, 0);
        var painted = pixels.Count(p => (uint)p >> 24 != 0);

        Assert.True(
            painted > 0,
            "an empty dock painted nothing, so its layered window has no alpha and no click can land on it");
    });

    /// <summary>
    /// The first icon arrives into a slot the bar is already drawn as having.
    /// </summary>
    /// <remarks>
    /// The bar eases open by a pitch per slot a new icon adds, and that was counted from the
    /// item count — which is zero for an empty dock even though the bar is drawn at one slot.
    /// Adding the first icon therefore took a whole pitch off a one-slot bar, collapsing it to
    /// a stub before it swelled back out.
    /// </remarks>
    [Fact]
    public void AddingTheFirstIconDoesNotCollapseTheBar() => OnStaThread(() =>
    {
        var metrics = Tuned();
        var oneSlot = new DockLayout(metrics).RestingWidth(1);

        var bar = new DockBar(metrics);
        bar.SetItems([]);           // settles the bar at one slot
        bar.SetItems(Pins(1));      // the first icon arrives into it

        Assert.True(
            bar.BarRect.Width >= oneSlot - 0.5,
            $"the bar fell to {bar.BarRect.Width}, narrower than the {oneSlot} it was already drawn at");
    });

    [Fact]
    public void ASecondIconStillOpensASlot() => OnStaThread(() =>
    {
        // The floor must not have turned the growth off: going from one icon to two still has
        // a slot to open, and the bar still starts a pitch short of where it is heading.
        var metrics = Tuned();
        var layout = new DockLayout(metrics);

        var bar = new DockBar(metrics);
        bar.SetItems([]);
        bar.SetItems(Pins(1));
        bar.SetItems(Pins(2));

        Assert.True(
            bar.BarRect.Width < layout.RestingWidth(2),
            "the bar arrived at its two-icon width outright instead of opening into it");
    });

    [Fact]
    public void EmptyingAFullDockLeavesOneSlot() => OnStaThread(() =>
    {
        // The width is eased while icons come and go, and the geometry that eases it does not
        // run without icons — so the last populated width would otherwise be left behind.
        var metrics = Tuned();
        var bar = new DockBar(metrics);
        bar.SetItems(Pins(6));
        bar.SetItems([]);

        Assert.Equal(new DockLayout(metrics).RestingWidth(1), bar.BarRect.Width, 6);
    });
}
