namespace ArtDock.Dock;

/// <summary>
/// Tuning values for the dock's geometry, in device-independent pixels.
///
/// Defaults mirror the web component this dock is modelled on so the two can be compared
/// side by side while tuning.
/// </summary>
public sealed record DockMetrics
{
    /// <summary>Resting icon size.</summary>
    public double BaseSize { get; init; } = 36;

    /// <summary>Fully magnified icon size.</summary>
    public double MaxSize { get; init; } = 46.8;

    /// <summary>Radius around the pointer over which icons are magnified.</summary>
    public double InfluenceRange { get; init; } = 80;

    /// <summary>Gap between icons.</summary>
    public double Gap { get; init; } = 8;

    /// <summary>Horizontal padding inside the bar.</summary>
    public double PaddingX { get; init; } = 14;

    /// <summary>Vertical padding inside the bar.</summary>
    public double PaddingY { get; init; } = 10;

    /// <summary>
    /// How rounded the bar's ends are, 0 to 1. 1 is a full stadium — the bar's own height
    /// as its radius — and 0 is <see cref="MinBarRadius"/>.
    /// </summary>
    public double Roundness { get; init; } = 1;

    /// <summary>
    /// The radius at <see cref="Roundness"/> 0.
    /// </summary>
    /// <remarks>
    /// Not zero on purpose: a hard-cornered rectangle reads as a panel that failed to
    /// render rather than as a deliberate shape, and the drawn shadow — which follows this
    /// radius — turns into a visible box around it. A few pixels is enough to keep the bar
    /// looking like an object.
    /// </remarks>
    public const double MinBarRadius = 6;

    /// <summary>
    /// The bar's height, which stays constant while icons magnify — magnified icons
    /// overflow upward past the bar's top edge rather than making the bar grow taller.
    /// </summary>
    public double BarHeight => BaseSize + (PaddingY * 2);

    /// <summary>
    /// The bar's corner radius, interpolated between a subtle rounding and a full stadium.
    /// </summary>
    public double BarRadius
    {
        get
        {
            var full = BarHeight / 2;
            var min = Math.Min(MinBarRadius, full);
            return min + ((full - min) * Math.Clamp(Roundness, 0, 1));
        }
    }

    /// <summary>Distance between adjacent icon centres at rest.</summary>
    public double Pitch => BaseSize + Gap;

    /// <summary>
    /// <see cref="InfluenceRange"/> snapped to a whole number of icon pitches.
    /// </summary>
    /// <remarks>
    /// The raised-cosine falloff only sums to a constant across its neighbours when its
    /// range spans whole pitches — that is the condition that stops the dock's total width
    /// changing as the pointer moves between icons. An arbitrary range leaves a residue
    /// that shows up as the dock breathing, so the setting is treated as "about this far"
    /// rather than exactly.
    /// </remarks>
    /// <remarks>
    /// Rounded <b>up</b>, never down. Rounding to nearest can shorten the reach the user
    /// asked for — 80 against a 55px pitch would collapse to one pitch, leaving only the
    /// immediate neighbours moving and a wave far tighter than a dock should have. Rounding
    /// up keeps at least the requested reach, and a wider wave is the one that reads as
    /// natural.
    /// </remarks>
    public double EffectiveInfluenceRange =>
        Pitch <= 0 ? 0 : Math.Max(1, Math.Ceiling(InfluenceRange / Pitch)) * Pitch;
}
