using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers the wave's entry and exit (<see cref="WaveRamp"/>): it starts gently, turns round
/// without a jump, comes out the same however its time is cut into frames, and settles.
/// </summary>
public class WaveRampTests
{
    private const double Frame = 1000.0 / 120;

    /// <summary>Runs frames of <paramref name="frameMs"/> until settled; the values each frame drew.</summary>
    private static List<double> Run(ref double value, ref double velocity, double target, double rate, double frameMs, int maxFrames = 1000)
    {
        var drawn = new List<double>();
        for (var i = 0; i < maxFrames; i++)
        {
            var settled = WaveRamp.Step(ref value, ref velocity, target, rate, frameMs);
            drawn.Add(value);
            if (settled)
            {
                break;
            }
        }

        return drawn;
    }

    [Fact]
    public void EntryStartsGently()
    {
        double value = 0, velocity = 0;
        var drawn = Run(ref value, ref velocity, 1, WaveRamp.EnterRate, Frame);

        // The tween this replaced drew 17% on its first frame at 120 Hz.
        Assert.True(drawn[0] < 0.05, $"first frame {drawn[0]:F3}");

        // And no frame's step is much more than half the tween's first, which was 0.17.
        var steps = drawn.Zip(drawn.Skip(1), (a, b) => b - a).Prepend(drawn[0]);
        Assert.All(steps, step => Assert.True(step is >= 0 and < 0.1, $"step {step:F3}"));
    }

    [Fact]
    public void EntryIsMostlyThereInAboutAFifthOfASecond()
    {
        double value = 0, velocity = 0;
        var drawn = Run(ref value, ref velocity, 1, WaveRamp.EnterRate, Frame);

        var at95 = drawn.FindIndex(v => v >= 0.95) * Frame;
        Assert.InRange(at95, 130, 200);
        Assert.Equal(1, value);
        Assert.Equal(0, velocity);
    }

    [Fact]
    public void TurningRoundKeepsTheSpeed()
    {
        // In for 60 ms, then out: the first frame of the way out still moves outward a little,
        // and nothing jumps.
        double value = 0, velocity = 0;
        for (var i = 0; i < 7; i++)
        {
            WaveRamp.Step(ref value, ref velocity, 1, WaveRamp.EnterRate, Frame);
        }

        var before = value;
        var drawn = Run(ref value, ref velocity, 0, WaveRamp.LeaveRate, Frame);

        Assert.True(drawn[0] >= before, "the turn is a slowing first");
        var steps = drawn.Zip(drawn.Skip(1), (a, b) => Math.Abs(b - a)).Prepend(Math.Abs(drawn[0] - before));
        Assert.All(steps, step => Assert.True(step < 0.1, $"step {step:F3}"));
        Assert.Equal(0, value);
    }

    [Fact]
    public void FramesDoNotChangeTheMotion()
    {
        double a = 0, va = 0, b = 0, vb = 0;
        for (var i = 0; i < 6; i++)
        {
            WaveRamp.Step(ref a, ref va, 1, WaveRamp.EnterRate, 2 * Frame);
            WaveRamp.Step(ref b, ref vb, 1, WaveRamp.EnterRate, Frame);
            WaveRamp.Step(ref b, ref vb, 1, WaveRamp.EnterRate, Frame);
        }

        Assert.Equal(a, b, 9);
        Assert.Equal(va, vb, 6);
    }

    [Fact]
    public void StaysWithinZeroAndOne()
    {
        // Whatever speed it is handed toward the end it is going to.
        double value = 0.9, velocity = 40;
        foreach (var v in Run(ref value, ref velocity, 1, WaveRamp.EnterRate, Frame))
        {
            Assert.InRange(v, 0, 1);
        }

        value = 0.1;
        velocity = -40;
        foreach (var v in Run(ref value, ref velocity, 0, WaveRamp.LeaveRate, Frame))
        {
            Assert.InRange(v, 0, 1);
        }
    }

    [Fact]
    public void ExitSettlesSoonEnoughToLetTheLoopGo()
    {
        double value = 1, velocity = 0;
        var drawn = Run(ref value, ref velocity, 0, WaveRamp.LeaveRate, Frame);

        Assert.InRange(drawn.Count * Frame, 200, 450);
        Assert.Equal(0, value);
    }
}
