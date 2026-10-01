using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers how often the handle reads what is behind it: at the quiet pace while that keeps
/// still, every frame while it moves, and not every frame for something that only blinks.
/// </summary>
/// <remarks>
/// Against a simulated display at 120 Hz, the main one's rate here, where a read finishes at
/// the next frame after it starts and sees what that frame shows — which is how a read of the
/// screen behaves, measured at 8.3 ms back to back.
/// </remarks>
public class HandlePaceTests
{
    private const double Frame = 1000.0 / 120;

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    /// <summary>
    /// Runs the reading loop against <paramref name="behind"/>, which says what is behind the
    /// handle at a time in milliseconds, and returns when each read finished.
    /// </summary>
    private static List<double> Reads(Func<double, int> behind, double until)
    {
        var pace = new HandlePace();
        var reads = new List<double>();
        int? last = null;
        var start = 0.0;

        while (start < until)
        {
            // The next frame strictly after the read starts; the nudge keeps a start exactly on a
            // frame, which rounding can put a hair before it, from finishing on that same frame.
            var finish = (Math.Floor((start / Frame) + 1e-9) + 1) * Frame;
            var seen = behind(finish);
            var changed = seen != last;
            last = seen;
            reads.Add(finish);

            start = finish + pace.Next(Ms(finish), changed, Ms(finish - start)).TotalMilliseconds;
        }

        return reads;
    }

    private static double PerSecond(List<double> reads, double from, double to) =>
        reads.Count(r => r >= from && r < to) * 1000 / (to - from);

    /// <summary>Holds that every read from <paramref name="from"/> to <paramref name="to"/> came a frame after the one before.</summary>
    private static void EveryFrame(List<double> reads, double from, double to)
    {
        var within = reads.Where(r => r >= from && r < to).ToList();

        Assert.NotEmpty(within);
        Assert.All(within.Zip(within.Skip(1), (a, b) => b - a), gap => Assert.Equal(Frame, gap, precision: 6));
    }

    /// <summary>How late the first read to see each change in <paramref name="changes"/> was.</summary>
    private static IEnumerable<double> Lags(List<double> reads, IEnumerable<double> changes) =>
        changes.Select(change => reads.First(r => r >= change) - change);

    [Fact]
    public void What_keeps_still_is_read_at_the_quiet_pace()
    {
        var reads = Reads(_ => 0, 5000);

        Assert.InRange(PerSecond(reads, 500, 5000), 12, 16);
    }

    [Fact]
    public void What_moves_every_frame_is_read_every_frame()
    {
        var reads = Reads(t => (int)Math.Round(t / Frame), 2000);

        EveryFrame(reads, 200, 2000);
    }

    [Fact]
    public void A_video_is_followed_frame_for_frame_once_it_has_been_seen_to_move()
    {
        const double videoFrame = 1000.0 / 24;
        var reads = Reads(t => (int)(t / videoFrame), 3000);
        var changes = Enumerable.Range(1, 71).Select(k => k * videoFrame).Where(c => c >= 300);

        Assert.All(Lags(reads, changes), lag => Assert.True(lag <= Frame + 0.001, $"{lag:F1} ms late"));
    }

    [Fact]
    public void A_blinking_caret_does_not_keep_it_reading_every_frame()
    {
        var reads = Reads(t => (int)(t / 530) % 2, 10_000);

        Assert.InRange(PerSecond(reads, 1000, 10_000), 12, 18);
    }

    [Fact]
    public void The_first_change_of_something_moving_is_seen_at_the_quiet_pace_at_worst()
    {
        // Still for a second, then moving every frame.
        var reads = Reads(t => t < 1000 ? 0 : (int)Math.Round(t / Frame), 2000);

        var lag = Lags(reads, [1000]).Single();
        Assert.True(lag <= HandlePace.StillInterval.TotalMilliseconds + (2 * Frame), $"{lag:F1} ms late");
    }

    [Fact]
    public void Once_still_again_it_goes_back_to_the_quiet_pace()
    {
        // Moving every frame for a second, then still.
        var reads = Reads(t => t < 1000 ? (int)Math.Round(t / Frame) : -1, 4000);

        EveryFrame(reads, 500, 1000);
        Assert.InRange(PerSecond(reads, 1000 + HandlePace.Settle.TotalMilliseconds + 100, 4000), 12, 16);
    }

    [Fact]
    public void A_change_is_read_again_at_once()
    {
        var pace = new HandlePace();

        Assert.Equal(TimeSpan.Zero, pace.Next(Ms(0), changed: true, took: Ms(Frame)));
    }

    [Fact]
    public void A_single_change_gets_one_read_after_it_and_no_more()
    {
        var pace = new HandlePace();

        pace.Next(Ms(0), changed: true, took: Ms(Frame));

        Assert.Equal(HandlePace.StillInterval, pace.Next(Ms(Frame), changed: false, took: Ms(Frame)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(3)]
    public void A_read_that_did_not_wait_for_a_frame_is_held_to_one_a_frame(double took)
    {
        var pace = new HandlePace();

        Assert.Equal(HandlePace.FrameInterval - Ms(took), pace.Next(Ms(100), changed: true, took: Ms(took)));
    }

    [Fact]
    public void A_slower_display_is_read_every_frame_of_its_own_without_waiting()
    {
        var pace = new HandlePace();

        Assert.Equal(TimeSpan.Zero, pace.Next(Ms(0), changed: true, took: Ms(1000.0 / 60)));
    }
}
