using System.Windows;

namespace ArtDock.Dock;

/// <summary>
/// The bar's drop shadow: rounded rectangles stacked outward from the bar, their alpha adding up
/// towards it, laid out so that together they are a real blurred shadow.
/// </summary>
/// <remarks>
/// <para>
/// Two passes, the way light actually falls on an object held a little above a surface. The
/// contact pass is the shade right where the bar meets the desktop: tight, barely displaced,
/// darkest at the edge, and what keeps the bar from looking pasted on. The key pass is the
/// shadow of the light from above: soft, pushed downward and drawn in a little, so it falls
/// mostly below the bar, less beside it, and hardly at all above it.
/// </para>
/// <para>
/// Each pass is a Gaussian blur of the bar's shape, as a shadow is. Its rings are evenly
/// spaced, each painted at whatever alpha brings the stack to that blur's falloff — so the
/// stack follows the blur's own curve rather than a ramp of its own invention, and no ring is
/// a wide band of one shade with a hard edge, which on a plain desktop read as an outline round
/// the shadow. Rings that would lie wholly under the bar, where nothing of them is seen, are
/// left out.
/// </para>
/// <para>
/// The strength asked for is an opacity over the whole of what is laid out here
/// (<see cref="Opacity"/>), so the shape never changes with it. The slider's full strength is
/// <see cref="Strongest"/> of the layout, not all of it: all of it was seen on the dock and was
/// more than anyone would want.
/// </para>
/// <para>
/// Described here rather than where it is drawn because it is drawn in two places, which must
/// agree: by the dock itself when the blur is off, and by the acrylic sheet behind the bar when
/// the blur is on — the sheet draws the whole bar then, so that nothing of its outline can
/// arrive on screen a frame apart from the blur it sits on.
/// </para>
/// </remarks>
public static class BarShadow
{
    /// <summary>One blurred shadow of the bar, built from rings.</summary>
    public sealed class Pass
    {
        /// <param name="sigma">The blur's standard deviation, in DIPs.</param>
        /// <param name="offsetY">Downward displacement, as if lit from above.</param>
        /// <param name="spread">How much larger than the bar the shape being blurred is;
        /// negative draws it in.</param>
        /// <param name="peak">The opacity the pass reaches where it is darkest, at full strength.</param>
        /// <param name="step">How far apart the rings are, in DIPs.</param>
        public Pass(double sigma, double offsetY, double spread, double peak, double step)
        {
            Sigma = sigma;
            OffsetY = offsetY;
            Spread = spread;
            Peak = peak;

            // From where the falloff fades below two steps of alpha — one at the strength a dock
            // starts with, and past it a tail no one sees and the window would need room for — in
            // to where a ring would lie wholly under the bar: drawn in by more than the pass is
            // pushed down, nothing of it reaches out from behind the bar on any side.
            var outer = -sigma * InverseNormal(2.0 / 255 / peak);
            var spreads = new List<double>();
            var alphas = new List<byte>();
            var covered = 0.0;
            for (var past = outer; spread + past + offsetY > 0; past -= step)
            {
                // Inside this ring and outside the next, the stack should be what the blur is
                // midway between them. Each ring's alpha is whatever takes the stack there from
                // what the rings outside it have made, to the nearest step of alpha it can be
                // painted in — and what one rounds off, the next makes up, so in the faint tail
                // a ring is left out wherever the blur has not yet grown by a whole step.
                var target = peak * Normal(-(past - (step / 2)) / sigma);
                var alpha = (byte)Math.Clamp(Math.Round((1 - ((1 - target) / (1 - covered))) * 255), 0, 255);
                if (alpha == 0)
                {
                    continue;
                }

                covered = 1 - ((1 - covered) * (1 - (alpha / 255.0)));
                spreads.Add(spread + past);
                alphas.Add(alpha);
            }

            Spreads = spreads;
            Alphas = alphas;
        }

        public double Sigma { get; }
        public double OffsetY { get; }
        public double Spread { get; }

        /// <summary>The opacity the pass reaches where it is darkest, at full strength.</summary>
        public double Peak { get; }

        /// <summary>How far past the bar each ring reaches, outermost first, in DIPs.</summary>
        public IReadOnlyList<double> Spreads { get; }

        /// <summary>The alpha each ring is painted at, in the order of <see cref="Spreads"/>.</summary>
        public IReadOnlyList<byte> Alphas { get; }

        /// <summary>How many rings the pass is made of.</summary>
        public int Layers => Spreads.Count;
    }

    /// <summary>The contact pass, then the key pass.</summary>
    public static IReadOnlyList<Pass> Passes { get; } =
    [
        new(sigma: 1.5, offsetY: 0.75, spread: 0, peak: 0.18, step: 0.5),
        new(sigma: 16, offsetY: 12, spread: -6, peak: 0.5, step: 1.25)
    ];

    /// <summary>
    /// The opacity the shadow laid out here is drawn at when the setting is at full strength —
    /// what was 60% when the setting reached the whole of the layout, chosen on the dock on
    /// 2026-11-28 as the strongest it should go.
    /// </summary>
    public const double Strongest = 0.6;

    /// <summary>
    /// The strength a dock starts with, 0 to 1: drawn at about half the layout's opacity, as it
    /// was before <see cref="Strongest"/> capped the setting.
    /// </summary>
    public const double DefaultStrength = 0.85;

    /// <summary>The opacity the shadow is drawn at for a strength of 0 to 1.</summary>
    public static double Opacity(double strength) => Math.Clamp(strength, 0, 1) * Strongest;

    /// <summary>
    /// The furthest any pass reaches past the bar, offset included, and a little more — the room
    /// a window has to keep below the bar for the shadow not to be sliced off in a hard line.
    /// </summary>
    public static double Reach { get; } = Passes.Max(pass => pass.Spreads[0] + pass.OffsetY) + 2;

    /// <summary>How many rings the shadow is made of.</summary>
    public static int RingCount { get; } = Passes.Sum(pass => pass.Layers);

    /// <summary>One layer of the shadow.</summary>
    /// <param name="Bounds">The rounded rectangle's bounds.</param>
    /// <param name="Radius">Its corner radius.</param>
    /// <param name="Pass">Which of <see cref="Passes"/> it belongs to.</param>
    /// <param name="Alpha">The alpha it is painted at, in black, at full strength.</param>
    public readonly record struct Ring(Rect Bounds, double Radius, int Pass, byte Alpha);

    /// <summary>
    /// Every layer of the shadow of a bar, in the order they are painted: each pass from its
    /// outermost ring inward.
    /// </summary>
    /// <param name="bar">The bar, in whatever units <paramref name="scale"/> says a DIP is.</param>
    /// <param name="radius">The bar's corner radius, in the same units.</param>
    /// <param name="scale">How many of those units make a DIP: 1 to draw in WPF, the display's
    /// scale to draw in device pixels.</param>
    /// <param name="rings">At least <see cref="RingCount"/> long.</param>
    public static void Rings(Rect bar, double radius, double scale, Span<Ring> rings)
    {
        var index = 0;
        for (var pass = 0; pass < Passes.Count; pass++)
        {
            for (var layer = 0; layer < Passes[pass].Layers; layer++)
            {
                rings[index++] = Layer(bar, radius, scale, pass, layer);
            }
        }
    }

    /// <summary>
    /// The outermost ring of one pass — the edge of what that pass covers. Together, those of
    /// every pass are the whole of the shadow's footprint.
    /// </summary>
    public static Ring Outermost(Rect bar, double radius, int pass) =>
        Layer(bar, radius, 1, pass, 0);

    private static Ring Layer(Rect bar, double radius, double scale, int pass, int layer)
    {
        var grow = Passes[pass].Spreads[layer] * scale;
        var width = Math.Max(0, bar.Width + (grow * 2));
        var height = Math.Max(0, bar.Height + (grow * 2));
        return new Ring(
            new Rect(
                bar.X + ((bar.Width - width) / 2),
                bar.Y + ((bar.Height - height) / 2) + (Passes[pass].OffsetY * scale),
                width,
                height),
            Math.Max(0, radius + grow),
            pass,
            Passes[pass].Alphas[layer]);
    }

    /// <summary>
    /// How much of a standard normal distribution lies below <paramref name="x"/> — the share
    /// of a blurred edge's shade that reaches a point <paramref name="x"/> standard deviations
    /// inside it. Through the complementary error function, by the Numerical Recipes
    /// Chebyshev fit, good to about one part in ten million.
    /// </summary>
    public static double Normal(double x)
    {
        var z = Math.Abs(x) / Math.Sqrt(2);
        var t = 1 / (1 + (z / 2));
        double[] fit = [-1.26551223, 1.00002368, 0.37409196, 0.09678418, -0.18628806, 0.27886807, -1.13520398, 1.48851587, -0.82215223, 0.17087277];
        var sum = 0.0;
        for (var i = fit.Length - 1; i >= 0; i--)
        {
            sum = (sum * t) + fit[i];
        }

        var erfc = t * Math.Exp((-z * z) + sum);
        return x >= 0 ? 1 - (erfc / 2) : erfc / 2;
    }

    /// <summary>
    /// The point below which a standard normal distribution holds <paramref name="p"/> of its
    /// weight — Acklam's rational approximation, good to about one part in a billion, which is
    /// far finer than any ring can be placed.
    /// </summary>
    public static double InverseNormal(double p)
    {
        if (p <= 0)
        {
            return double.NegativeInfinity;
        }

        if (p >= 1)
        {
            return double.PositiveInfinity;
        }

        const double low = 0.02425;
        double[] a = [-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00];
        double[] b = [-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01];
        double[] c = [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00];
        double[] d = [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00];

        if (p < low)
        {
            var q = Math.Sqrt(-2 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5])
                / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }

        if (p > 1 - low)
        {
            var q = Math.Sqrt(-2 * Math.Log(1 - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5])
                / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }

        var r = p - 0.5;
        var s = r * r;
        return (((((a[0] * s + a[1]) * s + a[2]) * s + a[3]) * s + a[4]) * s + a[5]) * r
            / (((((b[0] * s + b[1]) * s + b[2]) * s + b[3]) * s + b[4]) * s + 1);
    }
}
