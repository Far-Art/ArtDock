using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers the curve auto-hide's slide follows: from where it starts to where it ends, always
/// onward, and without the cubic's lurch at the start that it was asked to lose (2026-11-20).
/// </summary>
public class DockSlideTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void It_starts_where_it_is_and_ends_where_it_is_going(bool reveal)
    {
        Assert.Equal(0, DockSlide.Progress(reveal, 0), precision: 9);
        Assert.Equal(1, DockSlide.Progress(reveal, 1), precision: 9);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void It_never_turns_back_or_overshoots(bool reveal)
    {
        var steps = Enumerable.Range(0, 1001).Select(i => DockSlide.Progress(reveal, i / 1000.0)).ToList();

        Assert.All(steps.Zip(steps.Skip(1), (a, b) => b - a), step => Assert.True(step >= 0));
        Assert.All(steps, p => Assert.InRange(p, 0, 1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Time_outside_the_slide_is_held_to_its_ends(bool reveal)
    {
        Assert.Equal(0, DockSlide.Progress(reveal, -0.5), precision: 9);
        Assert.Equal(1, DockSlide.Progress(reveal, 1.5), precision: 9);
    }

    /// <summary>
    /// The first frame's step, at 120 Hz, against the slide's average step: the cubic it
    /// replaced took three times the average, which read as a jump.
    /// </summary>
    [Theory]
    [InlineData(true, 1.6)]
    [InlineData(false, 0.1)]
    public void The_first_frame_moves_gently(bool reveal, double mostTimesTheAverage)
    {
        var frame = (1000.0 / 120) / DockSlide.Duration(reveal).TotalMilliseconds;

        Assert.True(DockSlide.Progress(reveal, frame) / frame <= mostTimesTheAverage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_stock_speed_is_the_stock_pace_and_a_higher_one_is_quicker(bool reveal)
    {
        var stock = reveal ? DockSlide.RevealDuration : DockSlide.HideDuration;

        Assert.Equal(stock, DockSlide.Duration(reveal, 100));
        Assert.Equal(stock / 2, DockSlide.Duration(reveal, 200));
        Assert.Equal(stock * 2, DockSlide.Duration(reveal, 50));
    }

    /// <summary>The settings file can be edited by hand; a speed of 0 would never arrive.</summary>
    [Theory]
    [InlineData(0, DockSlide.SlowestSpeed)]
    [InlineData(-40, DockSlide.SlowestSpeed)]
    [InlineData(10_000, DockSlide.FastestSpeed)]
    public void A_speed_outside_the_slider_is_held_to_it(int speed, int heldTo)
    {
        Assert.Equal(DockSlide.Duration(reveal: true, heldTo), DockSlide.Duration(reveal: true, speed));
    }

    [Fact]
    public void Coming_up_is_the_quicker_way_and_both_are_slower_than_they_were()
    {
        Assert.True(DockSlide.RevealDuration < DockSlide.HideDuration);
        Assert.True(DockSlide.RevealDuration > TimeSpan.FromMilliseconds(220));
    }
}
