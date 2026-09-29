using System.Runtime.ExceptionServices;
using ArtDock.Controls;
using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers the room the dock asks its window for.
/// </summary>
/// <remarks>
/// A drop preview splices an extra icon into the row, and a window that grew to fit it had
/// to shrink again the moment the drag moved on. Each of those is a re-place of a layered
/// window — which shows its old bitmap inside its new bounds until it repaints — so waving a
/// file over the dock made it flinch by half a pitch each way, and a drop did it twice in
/// one message. The slot is reserved in advance instead.
/// </remarks>
public class DockBarWindowSizeTests
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

    /// <summary>
    /// Deliberately not the defaults: a dock is only pinched by this when a slot is wider
    /// than the slack the window already carries, which the default 36px icon is not.
    /// </summary>
    private static DockMetrics Tuned() => new() { BaseSize = 50, MaxSize = 70, Gap = 8 };

    private static DockItem[] Pins(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new DockItem { Id = $"p{i}", Label = $"P{i}" })];

    [Fact]
    public void TheWindowAlreadyHasRoomForTheSlotADropOpens() => OnStaThread(() =>
    {
        var metrics = Tuned();
        var bar = new DockBar(metrics);
        bar.SetItems(Pins(6));

        // Wide enough for a seventh icon at full magnification, which is what a preview
        // spliced into a dock of six amounts to.
        var withPreview = new DockLayout(metrics).MaxBarWidth(7);

        Assert.True(
            bar.PreferredSize().Width >= withPreview,
            $"window {bar.PreferredSize().Width} is narrower than the {withPreview} a preview needs");
    });

    [Fact]
    public void ReorderingDoesNotChangeIt() => OnStaThread(() =>
    {
        // The reserve must not have turned a resize that never happened into one that does.
        var bar = new DockBar(Tuned());
        var pins = Pins(6);
        bar.SetItems(pins);

        var before = bar.PreferredSize();
        bar.SetItems([pins[2], pins[0], pins[1], pins[3], pins[4], pins[5]]);

        Assert.Equal(before.Width, bar.PreferredSize().Width, 6);
    });
}
