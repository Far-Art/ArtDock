using System.Windows;
using System.Windows.Media;

namespace ArtDock.Controls;

/// <summary>
/// The puff of cloud an icon goes up in as it is dragged off the dock: soft round puffs, shaded
/// below and lit above, bursting out from where the icon was and drifting apart as they fade.
/// </summary>
/// <remarks>
/// <para>
/// Asked for on 2026-11-20, and drawn here rather than played from a sprite, so it is ArtDock's
/// own and sharp at any scale, and nothing has to be shipped for it. One number drives it,
/// <see cref="Progress"/>, 0 to 1, animated by WPF — it moves nothing but what this element
/// draws, so WPF's clock is right for it (see CLAUDE.md on moving windows).
/// </para>
/// <para>
/// Drawn in three passes — every puff's shade, then every puff's body, then its light — so the
/// puffs read as one body; a first try gave each puff a grey rim of its own, and the rims showed
/// through one another like the petals of a flower. Chosen the same day, on the dock, over two
/// looks of fractal-noise smoke — one body, and a ring bursting out — which TODO.md item 36
/// records. The puffs are fixed, not random at play time: every removal looks the same, and a
/// test can hold what is drawn.
/// </para>
/// </remarks>
public sealed class PoofCloud : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(PoofCloud),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// The puffs, as (angle in degrees, how far out, how big, how fast it drifts, how late it
    /// starts), each a share of the reach. Uneven on purpose: a ring of equal puffs reads as a
    /// flower, not a cloud. Made from the same seed every time.
    /// </summary>
    private static readonly (double Angle, double Out, double Size, double Drift, double Delay)[] Puffs =
        MakePuffs();

    private static readonly Brush Shade = Radial(
        (0x90, 0x86, 0x8D, 0x9B, 0), (0x70, 0x8E, 0x95, 0xA2, 0.6), (0x00, 0x9A, 0xA0, 0xAC, 1));

    private static readonly Brush Body = Radial(
        (0xFF, 0xEE, 0xF0, 0xF4, 0), (0xF0, 0xE2, 0xE6, 0xEC, 0.65), (0x00, 0xD8, 0xDC, 0xE4, 1));

    private static readonly Brush Light = Radial(
        (0xE0, 0xFF, 0xFF, 0xFF, 0), (0x60, 0xFF, 0xFF, 0xFF, 0.6), (0x00, 0xFF, 0xFF, 0xFF, 1));

    public PoofCloud() => IsHitTestVisible = false;

    /// <summary>How far through the puff, 0 before it to 1 when it has gone.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var t = Math.Clamp(Progress, 0, 1);
        var reach = Math.Min(ActualWidth, ActualHeight) / 2;
        if (t <= 0 || t >= 1 || reach <= 0)
        {
            return;
        }

        var centre = new Point(ActualWidth / 2, ActualHeight / 2);

        // In over the first few hundredths, then fading away.
        var opacity = Math.Min(1, t / 0.07) * Math.Pow(1 - t, 0.9);

        var placed = new (Point Place, double Radius)[Puffs.Length];
        for (var i = 0; i < Puffs.Length; i++)
        {
            var (angle, outward, size, drift, delay) = Puffs[i];

            // Out fast and slowing, as a burst does; late on the puffs thin and drift apart, so it
            // clears as separate puffs rather than as one shrinking ball.
            var local = Math.Clamp((t - delay) / (1 - delay), 0, 1);
            var spread = 1 - Math.Pow(1 - local, 3);
            var radians = angle * Math.PI / 180;
            var distance = reach * outward * (0.25 + (0.75 * spread) + (0.4 * drift * t * t));
            var radius = reach * size * (0.45 + (0.7 * spread)) * (1 - (0.45 * t * t));
            var rise = reach * 0.15 * drift * t;
            placed[i] = (
                new Point(centre.X + (Math.Cos(radians) * distance), centre.Y + (Math.Sin(radians) * distance) - rise),
                radius);
        }

        // Top first, so the lower puffs' light lies over the upper ones' shade.
        Array.Sort(placed, (a, b) => a.Place.Y.CompareTo(b.Place.Y));

        drawingContext.PushOpacity(opacity);
        foreach (var (place, radius) in placed)
        {
            drawingContext.DrawEllipse(Shade, null, new Point(place.X, place.Y + (radius * 0.22)), radius * 1.05, radius);
        }

        foreach (var (place, radius) in placed)
        {
            drawingContext.DrawEllipse(Body, null, new Point(place.X, place.Y + (radius * 0.03)), radius * 0.95, radius * 0.92);
        }

        foreach (var (place, radius) in placed)
        {
            drawingContext.DrawEllipse(
                Light, null, new Point(place.X - (radius * 0.22), place.Y - (radius * 0.28)), radius * 0.55, radius * 0.5);
        }

        drawingContext.Pop();
    }

    private static (double, double, double, double, double)[] MakePuffs()
    {
        var random = new Random(7);
        return Enumerable.Range(0, 22).Select(_ => (
            random.NextDouble() * 360,
            Math.Sqrt(random.NextDouble()) * 0.42,
            0.15 + (random.NextDouble() * 0.18),
            0.6 + (random.NextDouble() * 0.6),
            random.NextDouble() * 0.12)).ToArray();
    }

    private static Brush Radial(params (byte A, byte R, byte G, byte B, double At)[] stops)
    {
        var brush = new RadialGradientBrush();
        foreach (var (a, r, g, b, at) in stops)
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(a, r, g, b), at));
        }

        brush.Freeze();
        return brush;
    }
}
