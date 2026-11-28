namespace ArtDock.Dock;

/// <summary>
/// The wave's amplitude coming up as the pointer arrives and going down as it leaves: a
/// critically damped spring toward 0 or 1, stepped exactly for whatever time a frame is given.
/// </summary>
/// <remarks>
/// <para>
/// It was a tween, 140 ms of cubic ease-out in and 200 ms out, restarted from wherever the
/// amplitude stood whenever the pointer crossed the dock's edge. Recorded frame by frame
/// (2026-11-28, 120 Hz, pointer in and out quickly), that drew the bar's edges in jumps: an
/// ease-out is at three times its average speed on its first frame, so each entry put a fifth
/// of its growth into the first frame drawn — or half, when that frame came late — and a leave
/// set off at full speed inward from edges that had been moving outward a frame before.
/// </para>
/// <para>
/// A spring starts from rest, so the first frames move little, and it keeps its speed across
/// a change of target: the pointer leaving mid-entry slows the growth and turns it round,
/// rather than reversing it between one frame and the next. Critically damped, it does not
/// overshoot on its own, and the clamp to [0, 1] holds it there whatever it is handed.
/// </para>
/// </remarks>
public static class WaveRamp
{
    /// <summary>
    /// The spring's stiffness coming up, per second: 95% of the way in about 160 ms, at about a
    /// half of the old tween's top speed.
    /// </summary>
    public const double EnterRate = 30;

    /// <summary>Going down, per second: 95% of the way in about 200 ms.</summary>
    public const double LeaveRate = 24;

    /// <summary>Close enough to the target, and slow enough, to be put on it and stopped.</summary>
    private const double SettleDistance = 0.002;

    private const double SettleSpeed = 0.1;

    /// <summary>
    /// Moves <paramref name="value"/> and its <paramref name="velocity"/> (per second)
    /// <paramref name="elapsedMs"/> toward <paramref name="target"/>; true once it is there,
    /// with the value on the target and the velocity zero.
    /// </summary>
    public static bool Step(ref double value, ref double velocity, double target, double rate, double elapsedMs)
    {
        if (!double.IsFinite(value) || !double.IsFinite(velocity))
        {
            value = target;
            velocity = 0;
            return true;
        }

        if (elapsedMs > 0 && rate > 0)
        {
            // The exact solution over the step, so the motion is the same however the time is
            // cut into frames: x(t) = target + (e + (v + ωe)t)·e^(−ωt).
            var t = elapsedMs / 1000;
            var offset = value - target;
            var carry = velocity + (rate * offset);
            var decay = Math.Exp(-rate * t);

            value = target + ((offset + (carry * t)) * decay);
            velocity = (velocity - (rate * carry * t)) * decay;
        }

        if (value <= 0 || value >= 1)
        {
            value = Math.Clamp(value, 0, 1);
            velocity = 0;
        }

        if (Math.Abs(value - target) < SettleDistance && Math.Abs(velocity) < SettleSpeed)
        {
            value = target;
            velocity = 0;
            return true;
        }

        return false;
    }
}
