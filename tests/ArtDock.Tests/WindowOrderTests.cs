using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers the order the window previews show an app's windows in (<see cref="WindowOrder"/>):
/// first seen first, whatever the user does among them.
/// </summary>
public sealed class WindowOrderTests
{
    [Fact]
    public void WindowsOpenAtTheFirstLook_ComeBackToFront()
    {
        var seen = new Dictionary<nint, long>();
        WindowOrder.Note(seen, [3, 2, 1], 0);
        Assert.Equal([1, 2, 3], WindowOrder.Arrange([3, 2, 1], seen));
    }

    [Fact]
    public void SwitchingBetweenWindows_DoesNotReorderThem()
    {
        var seen = new Dictionary<nint, long>();
        var next = WindowOrder.Note(seen, [2, 1], 0);
        WindowOrder.Note(seen, [1, 2], next);
        Assert.Equal([1, 2], WindowOrder.Arrange([1, 2], seen));
        Assert.Equal([1, 2], WindowOrder.Arrange([2, 1], seen));
    }

    [Fact]
    public void ANewWindow_GoesLast_EvenInFront()
    {
        var seen = new Dictionary<nint, long>();
        var next = WindowOrder.Note(seen, [1, 2], 0);
        WindowOrder.Note(seen, [9, 1, 2], next);
        Assert.Equal([2, 1, 9], WindowOrder.Arrange([9, 1, 2], seen));
    }

    [Fact]
    public void AClosedWindow_IsForgotten_AndItsHandleComingBackIsNew()
    {
        var seen = new Dictionary<nint, long>();
        var next = WindowOrder.Note(seen, [1, 2], 0);
        next = WindowOrder.Note(seen, [2], next);
        Assert.False(seen.ContainsKey(1));

        WindowOrder.Note(seen, [1, 2], next);
        Assert.Equal([2, 1], WindowOrder.Arrange([1, 2], seen));
    }

    [Fact]
    public void AWindowNeverNumbered_GoesLast_InTheOrderGiven()
    {
        var seen = new Dictionary<nint, long> { [5] = 0 };
        Assert.Equal([5, 7, 6], WindowOrder.Arrange([7, 5, 6], seen));
    }
}
