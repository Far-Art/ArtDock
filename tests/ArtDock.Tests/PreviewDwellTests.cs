using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers when the previews of an app's windows open, move and close (<see cref="PreviewDwell"/>),
/// with a clock that is only a number.
/// </summary>
public class PreviewDwellTests
{
    private static PreviewDwell Dwell() => new() { HoverTime = 400, SwitchTime = 150, LeaveTime = 700 };

    /// <summary>Opens the previews of <paramref name="item"/>, as resting on it does, and returns the time.</summary>
    private static long Opened(PreviewDwell dwell, string item = "a")
    {
        dwell.Look(0, item, onPanel: false, pressed: false);
        Assert.Equal(PreviewMove.Open, dwell.Look(400, item, onPanel: false, pressed: false));
        return 400;
    }

    [Fact]
    public void RestingOnARunningItem_OpensItsPreviews_AfterTheHoverTime()
    {
        var dwell = Dwell();
        Assert.Equal(PreviewMove.None, dwell.Look(0, "a", false, false));
        Assert.Equal(PreviewMove.None, dwell.Look(399, "a", false, false));
        Assert.Equal(PreviewMove.Open, dwell.Look(400, "a", false, false));
        Assert.Equal("a", dwell.Showing);
    }

    [Fact]
    public void MovingOnBeforeTheHoverTime_StartsTheWaitAgain()
    {
        var dwell = Dwell();
        dwell.Look(0, "a", false, false);
        dwell.Look(300, "b", false, false);
        Assert.Equal(PreviewMove.None, dwell.Look(600, "b", false, false));
        Assert.Equal(PreviewMove.Open, dwell.Look(700, "b", false, false));
        Assert.Equal("b", dwell.Showing);
    }

    [Fact]
    public void NothingUnderThePointer_NeverOpens()
    {
        var dwell = Dwell();
        for (var t = 0; t < 2000; t += 32)
        {
            Assert.Equal(PreviewMove.None, dwell.Look(t, null, false, false));
        }
    }

    [Fact]
    public void AButtonHeldDown_KeepsThemShut()
    {
        var dwell = Dwell();
        dwell.Look(0, "a", false, pressed: true);
        Assert.Equal(PreviewMove.None, dwell.Look(1000, "a", false, pressed: true));

        // Let go, and the wait starts from there.
        Assert.Equal(PreviewMove.None, dwell.Look(1100, "a", false, false));
        Assert.Equal(PreviewMove.Open, dwell.Look(1500, "a", false, false));
    }

    [Fact]
    public void OnThePanel_TheyStayOpen_HoweverLong()
    {
        var dwell = Dwell();
        var t = Opened(dwell);
        for (; t < 5000; t += 32)
        {
            Assert.Equal(PreviewMove.None, dwell.Look(t, null, onPanel: true, pressed: false));
        }

        Assert.Equal("a", dwell.Showing);
    }

    [Fact]
    public void Away_TheyCloseAfterTheLeaveTime()
    {
        var dwell = Dwell();
        var t = Opened(dwell);
        Assert.Equal(PreviewMove.None, dwell.Look(t + 1, null, false, false));
        Assert.Equal(PreviewMove.None, dwell.Look(t + 700, null, false, false));
        Assert.Equal(PreviewMove.Close, dwell.Look(t + 701, null, false, false));
        Assert.Null(dwell.Showing);
    }

    [Fact]
    public void ComingBackBeforeTheLeaveTime_KeepsThemOpen_AndStartsTheCountAgain()
    {
        var dwell = Dwell();
        var t = Opened(dwell);
        dwell.Look(t + 1, null, false, false);
        dwell.Look(t + 600, null, onPanel: true, pressed: false);
        dwell.Look(t + 601, null, false, false);
        Assert.Equal(PreviewMove.None, dwell.Look(t + 1300, null, false, false));
        Assert.Equal(PreviewMove.Close, dwell.Look(t + 1301, null, false, false));
    }

    [Fact]
    public void RestingOnAnotherRunningItem_MovesThemThere_AfterTheSwitchTime()
    {
        var dwell = Dwell();
        var t = Opened(dwell);
        Assert.Equal(PreviewMove.None, dwell.Look(t + 1, "b", false, false));
        Assert.Equal(PreviewMove.None, dwell.Look(t + 150, "b", false, false));
        Assert.Equal(PreviewMove.Switch, dwell.Look(t + 151, "b", false, false));
        Assert.Equal("b", dwell.Showing);
    }

    [Fact]
    public void CuttingAcrossANeighbour_OnTheWayToThePanel_DoesNotMoveThem()
    {
        var dwell = Dwell();
        var t = Opened(dwell);
        dwell.Look(t + 32, "b", false, false);
        dwell.Look(t + 64, "b", false, false);
        Assert.Equal(PreviewMove.None, dwell.Look(t + 96, null, onPanel: true, pressed: false));
        Assert.Equal("a", dwell.Showing);

        // And the neighbour's count does not carry over to the next time across it.
        dwell.Look(t + 500, "b", false, false);
        Assert.Equal(PreviewMove.None, dwell.Look(t + 600, "b", false, false));
    }

    [Fact]
    public void OnAnotherItem_TheyDoNotCloseForBeingAway()
    {
        var dwell = Dwell();
        var t = Opened(dwell);
        dwell.Look(t + 1, null, false, false);
        Assert.Equal(PreviewMove.None, dwell.Look(t + 690, "b", false, false));
        Assert.Equal(PreviewMove.Switch, dwell.Look(t + 840, "b", false, false));
    }

    [Fact]
    public void Dismissed_TheyStayShut_UntilThePointerLeavesTheItem()
    {
        var dwell = Dwell();
        var t = Opened(dwell);
        Assert.True(dwell.Dismiss("a"));
        Assert.Null(dwell.Showing);

        for (var at = t; at < t + 3000; at += 32)
        {
            Assert.Equal(PreviewMove.None, dwell.Look(at, "a", false, false));
        }

        dwell.Look(t + 3000, null, false, false);
        dwell.Look(t + 3100, "a", false, false);
        Assert.Equal(PreviewMove.Open, dwell.Look(t + 3500, "a", false, false));
    }

    [Fact]
    public void Dismissed_AnotherItemStillOpens()
    {
        var dwell = Dwell();
        Opened(dwell);
        dwell.Dismiss("a");
        dwell.Look(1000, "b", false, false);
        Assert.Equal(PreviewMove.Open, dwell.Look(1400, "b", false, false));
    }

    [Fact]
    public void DismissingWhatIsNotOpen_SaysSo()
    {
        var dwell = Dwell();
        Assert.False(dwell.Dismiss(null));
    }
}
