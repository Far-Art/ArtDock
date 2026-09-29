using System.Runtime.ExceptionServices;
using System.Windows.Media;
using ArtDock.Controls;
using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers the render state one frame of the wave produces.
/// </summary>
/// <remarks>
/// A <see cref="DockItemVisual"/> is a bare <c>FrameworkElement</c> and never needs a window,
/// so its transforms can be asserted directly — which is the whole of what magnification does
/// to an icon.
/// </remarks>
public class DockItemVisualTests
{
    private const double Resting = 40;

    /// <summary>
    /// Runs a test body on an STA thread.
    /// </summary>
    /// <remarks>
    /// Constructing any <c>FrameworkElement</c> builds WPF's input manager, which refuses to
    /// exist on an MTA thread — and xunit runs tests on MTA threads. Borrowing one thread per
    /// test keeps that requirement here rather than in a runner package.
    /// </remarks>
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

        // Rethrown with its original stack, so a failed assertion still reads as one.
        failure?.Throw();
    }

    private static DockItemVisual Icon() =>
        new(new DockItem { Id = "app", Label = "App" }, Resting);

    private static DockItemVisual Separator() =>
        new(new DockItem { Id = "sep", Label = "Separator", IsSeparator = true }, Resting);

    private static ScaleTransform ScaleOf(DockItemVisual visual) =>
        (ScaleTransform)((TransformGroup)visual.RenderTransform).Children[0];

    private static TranslateTransform TranslateOf(DockItemVisual visual) =>
        (TranslateTransform)((TransformGroup)visual.RenderTransform).Children[1];

    [Fact]
    public void Icon_ScalesToTheMagnifiedSize() => OnStaThread(() =>
    {
        var visual = Icon();
        visual.ApplyWave(60, 0);

        Assert.Equal(1.5, ScaleOf(visual).ScaleX, 6);
        Assert.Equal(1.5, ScaleOf(visual).ScaleY, 6);
    });

    [Fact]
    public void Separator_StaysAtItsRestingSize() => OnStaThread(() =>
    {
        var visual = Separator();
        visual.ApplyWave(60, 0);

        Assert.Equal(1, ScaleOf(visual).ScaleX, 6);
        Assert.Equal(1, ScaleOf(visual).ScaleY, 6);
    });

    [Fact]
    public void Separator_StillTravelsWithTheRow() => OnStaThread(() =>
    {
        // Its slot widens with the wave even though the rule drawn in it does not, so the
        // divider has to take the same displacement as everything else or it would be left
        // behind by the spreading.
        var visual = Separator();
        visual.ApplyWave(60, 12);

        Assert.Equal(12, TranslateOf(visual).X, 6);
    });

    [Fact]
    public void Separator_ResizesWithTheDocksOwnIconSize() => OnStaThread(() =>
    {
        // The wave is what a separator ignores; the icon-size setting is not.
        var visual = Separator();
        visual.SetRestingSize(64);

        Assert.Equal(64, visual.RestingSize, 6);
        Assert.Equal(64, visual.Width, 6);
    });

    [Fact]
    public void ArrivingIcon_StartsFromNothing() => OnStaThread(() =>
    {
        var visual = Icon();
        visual.BeginEntry();
        visual.ApplyWave(Resting, 0);

        Assert.True(visual.IsEntering);
        Assert.Equal(0, ScaleOf(visual).ScaleX, 6);
    });

    [Fact]
    public void ArrivingIcon_GrowsUnderTheWaveRatherThanInsteadOfIt() => OnStaThread(() =>
    {
        // Half grown and fully magnified is half of the magnified size, not half of the
        // resting one: a preview that lands under a magnified pointer has to arrive at the
        // size the wave is asking for.
        var visual = Icon();
        visual.BeginEntry();
        visual.AdvanceEntry(35);
        visual.ApplyWave(60, 0);

        Assert.Equal(1.5 * visual.EntryScale, ScaleOf(visual).ScaleX, 6);
        Assert.InRange(visual.EntryScale, 0.4, 0.6);
    });

    [Fact]
    public void ArrivingIcon_SettlesAtItsFullSize() => OnStaThread(() =>
    {
        var visual = Icon();
        visual.BeginEntry();

        // Long enough that the exponential has landed inside its epsilon.
        for (var frame = 0; frame < 30; frame++)
        {
            visual.AdvanceEntry(16);
        }

        visual.ApplyWave(60, 0);

        Assert.False(visual.IsEntering);
        Assert.Equal(1, visual.EntryScale, 6);
        Assert.Equal(1.5, ScaleOf(visual).ScaleX, 6);
    });

    [Fact]
    public void IconAlreadyInTheDock_HasNothingToGrowInto() => OnStaThread(() =>
    {
        // Only a drop preview is created mid-life; everything else is either already there
        // or replacing something that was, and must be drawn at full size on its first frame.
        var visual = Icon();

        Assert.False(visual.IsEntering);
        Assert.False(visual.AdvanceEntry(16));

        visual.ApplyWave(60, 0);
        Assert.Equal(1.5, ScaleOf(visual).ScaleX, 6);
    });
}
