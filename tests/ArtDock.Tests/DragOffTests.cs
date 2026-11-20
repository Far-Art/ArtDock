using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers dragging an icon off the dock to remove it, as macOS does: pulled far enough from the
/// dock that <i>Remove</i> shows, then let go; nearer, it drops back.
/// </summary>
/// <remarks>
/// The gesture was taken away once because it fired on a drag that wandered. These hold what
/// makes it deliberate now: the distance, and coming back.
/// </remarks>
public class DragOffTests
{
    private static DragOff Make() => new() { LiftDistance = 18, RemoveDistance = 54, Slack = 9 };

    [Fact]
    public void ADragOnTheDockStaysOnTheBar()
    {
        var off = Make();

        Assert.Equal(DragOffState.OnBar, off.Look(-30));
        Assert.Equal(DragOffState.OnBar, off.Look(-5));
    }

    [Fact]
    public void JustPastTheDocksSpaceIsNotFarEnoughToLift()
    {
        var off = Make();

        Assert.Equal(DragOffState.OnBar, off.Look(10));
        Assert.Equal(DragOffState.OnBar, off.Look(17.9));
    }

    [Fact]
    public void PulledClearTheIconLiftsButWouldDropBack()
    {
        var off = Make();

        Assert.Equal(DragOffState.Lifted, off.Look(18));
        Assert.Equal(DragOffState.Lifted, off.Look(53.9));
        Assert.True(off.IsLifted);
    }

    [Fact]
    public void FarEnoughItIsRemovable()
    {
        var off = Make();

        off.Look(30);

        Assert.Equal(DragOffState.Armed, off.Look(54));
    }

    [Fact]
    public void AFastPullGoesStraightToRemovable()
    {
        // A frame can find the pointer already well clear: it does not have to be seen lifted first.
        Assert.Equal(DragOffState.Armed, Make().Look(200));
    }

    [Fact]
    public void ComingBackShortOfTheDistanceTakesRemoveAway()
    {
        var off = Make();

        off.Look(80);

        // Within the slack it stays removable, so a hand resting at the line does not flicker.
        Assert.Equal(DragOffState.Armed, off.Look(46));
        Assert.Equal(DragOffState.Lifted, off.Look(44.9));
    }

    [Fact]
    public void ComingBackIntoTheDockPutsItBackOnTheBar()
    {
        var off = Make();

        off.Look(80);

        Assert.Equal(DragOffState.OnBar, off.Look(-1));
        Assert.False(off.IsLifted);
    }

    [Fact]
    public void BetweenTheLineAndTheDockItStaysLifted()
    {
        // Lifted at the line, back only inside the dock's space: a hand resting at the line
        // does not have the icon leave and come back on every frame.
        var off = Make();

        off.Look(18);

        Assert.Equal(DragOffState.Lifted, off.Look(1));
        Assert.Equal(DragOffState.Lifted, off.Look(17));
    }

    [Fact]
    public void ResetStartsANewDragOnTheBar()
    {
        var off = Make();

        off.Look(80);
        off.Reset();

        Assert.Equal(DragOffState.OnBar, off.State);
        Assert.Equal(DragOffState.Lifted, off.Look(30));
    }

    [Fact]
    public void NotANumberChangesNothing()
    {
        var off = Make();

        off.Look(30);

        Assert.Equal(DragOffState.Lifted, off.Look(double.NaN));
    }
}
