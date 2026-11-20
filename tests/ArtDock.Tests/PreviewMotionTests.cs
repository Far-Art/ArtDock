using System.Windows;
using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers how the window previews move (<see cref="PreviewMotion"/>): the curves measured off
/// the taskbar, the card's edges moved each on its own, a set of cards standing on the card's
/// bottom, and a window's picture cut to what can be seen.
/// </summary>
public class PreviewMotionTests
{
    [Fact]
    public void Bezier_StartsAtZero_EndsAtOne_AndClampsOutside()
    {
        var curve = PreviewMotion.Glide;
        Assert.Equal(0, curve.Ease(0));
        Assert.Equal(1, curve.Ease(1));
        Assert.Equal(0, curve.Ease(-0.5));
        Assert.Equal(1, curve.Ease(1.5));
    }

    [Fact]
    public void Bezier_Linear_IsTheIdentity()
    {
        var linear = new CubicBezier(1 / 3.0, 1 / 3.0, 2 / 3.0, 2 / 3.0);
        foreach (var t in new[] { 0.1, 0.25, 0.5, 0.8, 0.95 })
        {
            Assert.Equal(t, linear.Ease(t), 5);
        }
    }

    [Fact]
    public void Bezier_MatchesTheCssEaseCurve()
    {
        // CSS's `ease`, whose values are well known: halfway in time is about 80% of the way.
        var ease = new CubicBezier(0.25, 0.1, 0.25, 1);
        Assert.Equal(0.8024, ease.Ease(0.5), 3);
    }

    [Fact]
    public void Glide_StartsSlow_IsFastestInTheMiddle_AndEndsSlowly()
    {
        var glide = PreviewMotion.Glide;
        var first = glide.Ease(0.1);
        var middle = glide.Ease(0.55) - glide.Ease(0.45);
        var last = 1 - glide.Ease(0.9);

        Assert.True(first < 0.05, $"the first tenth went {first:P1} of the way");
        Assert.True(middle > first && middle > last, "the middle should be the fastest part");
        Assert.True(last < 0.02, $"the last tenth went {last:P1} of the way");
    }

    [Fact]
    public void Glide_FollowsTheTaskbarsRecordedLongJump()
    {
        // Read off the recording (TODO.md item 25): a 309-pixel glide had gone 7% of the way
        // 70 ms in, 29% at 104 ms, 67% at 137 ms and 96% at 253 ms.
        Assert.InRange(PreviewMotion.Glide.Ease(70 / PreviewMotion.GlideMs), 0.03, 0.11);
        Assert.InRange(PreviewMotion.Glide.Ease(104 / PreviewMotion.GlideMs), 0.24, 0.34);
        Assert.InRange(PreviewMotion.Glide.Ease(137 / PreviewMotion.GlideMs), 0.62, 0.72);
        Assert.InRange(PreviewMotion.Glide.Ease(253 / PreviewMotion.GlideMs), 0.93, 0.99);
    }

    [Fact]
    public void Rise_AndFadeOut_AreMostlyDoneEarly()
    {
        Assert.True(PreviewMotion.Rise.Ease(0.25) > 0.6);
        Assert.True(PreviewMotion.FadeOut.Ease(0.5) > 0.6);
    }

    [Fact]
    public void Rise_StaysWithinTheGapAboveTheDock()
    {
        // Rising from no lower than the gap it keeps, nothing of the card is ever below the line
        // it stands above, where it would be cut off in mid-air.
        Assert.True(PreviewMotion.RiseDips <= new PreviewMetrics().Lift);
    }

    [Fact]
    public void Progress_IsClamped_AndInstantWithoutADuration()
    {
        Assert.Equal(0, PreviewMotion.Progress(-5, 100));
        Assert.Equal(0.5, PreviewMotion.Progress(50, 100));
        Assert.Equal(1, PreviewMotion.Progress(500, 100));
        Assert.Equal(1, PreviewMotion.Progress(0, 0));
    }

    [Fact]
    public void Between_KeepsASharedBottom_WhileTheRestMoves()
    {
        var from = new Int32Rect(856, 381, 212, 159);
        var to = new Int32Rect(809, 423, 217, 117);

        for (var t = 0.0; t <= 1; t += 0.05)
        {
            var r = PreviewMotion.Between(from, to, t);
            Assert.Equal(540, r.Y + r.Height);
        }
    }

    [Fact]
    public void Between_EndsExactlyAtEachEnd()
    {
        var from = new Int32Rect(10, 20, 30, 40);
        var to = new Int32Rect(100, 5, 60, 55);
        Assert.Equal(from, PreviewMotion.Between(from, to, 0));
        Assert.Equal(to, PreviewMotion.Between(from, to, 1));
        // Edges, not sizes: top 20 → 5 is 13 halfway, the bottom stays at 60.
        Assert.Equal(new Int32Rect(55, 13, 45, 47), PreviewMotion.Between(from, to, 0.5));
    }

    [Fact]
    public void Anchor_FillsACardLaidOutForIt_AndStandsOnTheBottomOfAnother()
    {
        var card = new Int32Rect(500, 380, 220, 160);
        Assert.Equal((500, 380), PreviewMotion.Anchor(card, 220, 160));

        // A narrower, shorter set: centred across, its bottom on the card's.
        Assert.Equal((520, 420), PreviewMotion.Anchor(card, 180, 120));
    }

    [Fact]
    public void Cut_WhollyInside_ShowsTheWholeWindow()
    {
        var cut = PreviewMotion.Cut(new Int32Rect(10, 10, 160, 90), new Int32Rect(0, 0, 400, 300), new Size(1600, 900));
        Assert.Equal(new PictureCut(new Int32Rect(10, 10, 160, 90), new Int32Rect(0, 0, 1600, 900)), cut);
    }

    [Fact]
    public void Cut_WhollyOutside_ShowsNothing()
    {
        Assert.Null(PreviewMotion.Cut(new Int32Rect(10, 400, 160, 90), new Int32Rect(0, 0, 400, 300), new Size(1600, 900)));
    }

    [Fact]
    public void Cut_ByTheFloor_ShowsTheTopOfTheWindow()
    {
        // The card rising from below the line it stands above: the bottom third is below it.
        var cut = PreviewMotion.Cut(new Int32Rect(10, 240, 160, 90), new Int32Rect(0, 0, 400, 300), new Size(1600, 900));
        Assert.NotNull(cut);
        Assert.Equal(new Int32Rect(10, 240, 160, 60), cut.Value.Destination);
        Assert.Equal(new Int32Rect(0, 0, 1600, 600), cut.Value.Source);
    }

    [Fact]
    public void Cut_ByANarrowerCard_ShowsTheMiddleOfTheWindow()
    {
        var cut = PreviewMotion.Cut(new Int32Rect(0, 0, 200, 100), new Int32Rect(50, 0, 100, 300), new Size(2000, 1000));
        Assert.NotNull(cut);
        Assert.Equal(new Int32Rect(50, 0, 100, 100), cut.Value.Destination);
        Assert.Equal(new Int32Rect(500, 0, 1000, 1000), cut.Value.Source);
    }
}
