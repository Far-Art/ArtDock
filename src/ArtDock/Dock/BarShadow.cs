using System.Windows;

namespace ArtDock.Dock;

/// <summary>
/// The bar's drop shadow: rounded rectangles stacked outward from the bar, their alpha adding up
/// towards it.
/// </summary>
/// <remarks>
/// <para>
/// Two passes, the way daylight actually falls on a small object. A single pass cannot be both
/// subtle and convincing: widen it and it becomes a uniform halo, tighten it and the bar looks
/// pasted on. The contact pass is tight and barely displaced, so the bar reads as resting just
/// above the desktop; the key pass is wide, faint and clearly pushed downward, which is what
/// gives the shadow a direction. Peak opacity is around 17% where they overlap, immediately
/// under the bar.
/// </para>
/// <para>
/// The layers are spaced on a power curve, so they crowd together near the bar and thin out
/// towards the edge — the dense-then-long-tail falloff a real penumbra has, instead of a linear
/// ramp with visible banding.
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
    /// <summary>One stacked-ring pass making up the shadow.</summary>
    /// <param name="Blur">How far past the bar this pass reaches.</param>
    /// <param name="OffsetY">Downward displacement, as if lit from above.</param>
    /// <param name="Layers">How many rings the pass's falloff is built from.</param>
    /// <param name="Alpha">Per-ring alpha; the pass peaks near <c>1-(1-alpha)^layers</c>.</param>
    public readonly record struct Pass(double Blur, double OffsetY, int Layers, byte Alpha);

    /// <summary>The contact pass, then the key pass.</summary>
    public static IReadOnlyList<Pass> Passes { get; } =
    [
        new(Blur: 6, OffsetY: 2, Layers: 8, Alpha: 0x02),
        new(Blur: 18, OffsetY: 8, Layers: 16, Alpha: 0x02)
    ];

    /// <summary>
    /// Exponent spacing the rings. Above 1 they crowd near the bar, which is what gives each pass
    /// a dense core and a long faint tail rather than a linear, banded ramp.
    /// </summary>
    private const double Falloff = 1.6;

    /// <summary>
    /// The furthest any pass reaches past the bar, offset included, and a little more — the room
    /// a window has to keep below the bar for the shadow not to be sliced off in a hard line.
    /// </summary>
    public static double Reach { get; } = Passes.Max(pass => pass.Blur + pass.OffsetY) + 2;

    /// <summary>How many rings the shadow is made of.</summary>
    public static int RingCount { get; } = Passes.Sum(pass => pass.Layers);

    /// <summary>One layer of the shadow.</summary>
    /// <param name="Bounds">The rounded rectangle's bounds.</param>
    /// <param name="Radius">Its corner radius.</param>
    /// <param name="Pass">Which of <see cref="Passes"/> it belongs to, and so what alpha it is painted at.</param>
    public readonly record struct Ring(Rect Bounds, double Radius, int Pass);

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
            var (blur, offsetY, layers, _) = Passes[pass];
            for (var layer = layers; layer >= 1; layer--)
            {
                var spread = blur * Math.Pow((double)layer / layers, Falloff) * scale;
                rings[index++] = new Ring(
                    new Rect(
                        bar.X - spread,
                        bar.Y - spread + (offsetY * scale),
                        bar.Width + (spread * 2),
                        bar.Height + (spread * 2)),
                    radius + spread,
                    pass);
            }
        }
    }

    /// <summary>
    /// The outermost ring of one pass — the edge of what that pass covers. Together, those of
    /// every pass are the whole of the shadow's footprint.
    /// </summary>
    public static Ring Outermost(Rect bar, double radius, int pass)
    {
        var (blur, offsetY, _, _) = Passes[pass];
        return new Ring(
            new Rect(bar.X - blur, bar.Y - blur + offsetY, bar.Width + (blur * 2), bar.Height + (blur * 2)),
            radius + blur,
            pass);
    }
}
