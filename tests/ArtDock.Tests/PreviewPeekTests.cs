using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers when resting on a preview's card peeks at its window (<see cref="PreviewPeek"/>): after a
/// rest, from card to card at once, through the gap between cards, and over as the pointer leaves.
/// </summary>
public class PreviewPeekTests
{
    private const long A = 0x1000;
    private const long B = 0x2000;

    private static PreviewPeek Peek() => new() { Delay = 500, Grace = 150 };

    [Fact]
    public void RestingOnACard_PeeksAfterTheDelay_AndNotBefore()
    {
        var peek = Peek();
        Assert.Equal(PeekMove.None, peek.Look(0, A));
        Assert.Equal(PeekMove.None, peek.Look(499, A));
        Assert.Equal(PeekMove.Show, peek.Look(500, A));
        Assert.Equal(A, peek.Showing);
        Assert.Equal(PeekMove.None, peek.Look(900, A));
    }

    [Fact]
    public void PassingOverCards_PeeksAtNone()
    {
        var peek = Peek();
        peek.Look(0, A);
        peek.Look(300, B);
        Assert.Equal(PeekMove.None, peek.Look(700, B));
        Assert.Equal(PeekMove.Show, peek.Look(800, B));
        Assert.Equal(B, peek.Showing);
    }

    [Fact]
    public void FromCardToCard_ThePeekMovesAtOnce()
    {
        var peek = Peek();
        peek.Look(0, A);
        peek.Look(500, A);
        Assert.Equal(PeekMove.Show, peek.Look(520, B));
        Assert.Equal(B, peek.Showing);
    }

    [Fact]
    public void CrossingTheGapBetweenCards_KeepsThePeek()
    {
        var peek = Peek();
        peek.Look(0, A);
        peek.Look(500, A);
        Assert.Equal(PeekMove.None, peek.Look(600, 0));
        Assert.Equal(PeekMove.Show, peek.Look(700, B));
        Assert.Equal(B, peek.Showing);
    }

    [Fact]
    public void LeavingTheCards_EndsThePeek_AfterTheGrace()
    {
        var peek = Peek();
        peek.Look(0, A);
        peek.Look(500, A);
        Assert.Equal(PeekMove.None, peek.Look(600, 0));
        Assert.Equal(PeekMove.None, peek.Look(749, 0));
        Assert.Equal(PeekMove.End, peek.Look(750, 0));
        Assert.Equal(0, peek.Showing);

        // And the next one waits again.
        Assert.Equal(PeekMove.None, peek.Look(800, A));
        Assert.Equal(PeekMove.None, peek.Look(1200, A));
        Assert.Equal(PeekMove.Show, peek.Look(1300, A));
    }

    [Fact]
    public void Stop_SaysWhetherAPeekWasShowing_AndForgetsTheWait()
    {
        var peek = Peek();
        peek.Look(0, A);
        Assert.False(peek.Stop());
        Assert.Equal(PeekMove.None, peek.Look(600, A));

        peek.Look(1100, A);
        Assert.True(peek.Stop());
        Assert.Equal(0, peek.Showing);
    }
}
