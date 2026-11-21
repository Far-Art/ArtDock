using System.Windows.Media;

namespace ArtDock.Dock;

/// <summary>
/// What Windows paints the taskbar with: the recipe of its acrylic, and the plain colour it
/// falls back to where there is no transparency.
/// </summary>
/// <remarks>
/// <para>
/// The taskbar's background is not a colour. It is Windows' acrylic — the control's own name
/// for it is <c>AcrylicInAppFillColorBaseBrush</c>, read out of the taskbar's compiled markup —
/// and acrylic is a recipe run on whatever is behind it: that is blurred, its lightness is
/// swapped for the tint's by <see cref="LuminosityOpacity"/> with its own hue kept, the result is
/// pulled towards the tint's hue by <see cref="TintOpacity"/>, and a fine grain goes over the
/// top at <see cref="GrainOpacity"/>. So a wallpaper's colours show through a light taskbar as
/// a pale wash of themselves, which no colour at an opacity can do: that greys them instead.
/// </para>
/// <para>
/// Measured against the taskbar's own pixels on 2026-11-21, in the light theme on a display at
/// 100% and one at 150%: this recipe, run on the wallpaper behind the taskbar, came out within a
/// level of what Windows had drawn, the grain being all of the difference. The blur was the one
/// thing to settle by measuring — it covers only what is behind the bar itself, mirrored at its
/// edges, and it grows with the display's scale. The dark theme's values and the accent's are
/// Windows' published ones for the same brush, and were not on a screen to be measured.
/// </para>
/// </remarks>
/// <param name="Tint">The material's colour.</param>
/// <param name="TintOpacity">How far what is behind is pulled to the tint's hue, 0 to 1.</param>
/// <param name="LuminosityOpacity">How far its lightness is replaced by the tint's, 0 to 1.</param>
/// <param name="Solid">
/// What the taskbar is with Windows' transparency effects off, and so what a bar that cannot
/// have the material is painted.
/// </param>
/// <param name="Opaque">
/// True when the taskbar is a plain colour whatever is behind it: under a contrast theme.
/// </param>
public readonly record struct TaskbarMaterial(
    Color Tint, double TintOpacity, double LuminosityOpacity, Color Solid, bool Opaque = false)
{
    /// <summary>How far the material blurs what is behind it, in DIPs, as a standard deviation.</summary>
    public const double BlurRadius = 30;

    /// <summary>How much of the grain shows over the material, 0 to 1.</summary>
    public const double GrainOpacity = 0.02;

    /// <summary>
    /// The line along the taskbar's edge, <c>SurfaceStrokeColorDefault</c>: the same in the light
    /// theme and the dark. The dock's bar takes it as its rim.
    /// </summary>
    public static readonly Color Stroke = Color.FromArgb(0x66, 0x75, 0x75, 0x75);

    /// <summary>The taskbar with no accent on it, in the light theme or the dark.</summary>
    public static TaskbarMaterial Neutral(bool light) => light
        ? new(Color.FromRgb(0xF3, 0xF3, 0xF3), 0, 0.9, Color.FromRgb(0xEE, 0xEE, 0xEE))
        : new(Color.FromRgb(0x20, 0x20, 0x20), 0.5, 0.96, Color.FromRgb(0x1C, 0x1C, 0x1C));

    /// <summary>
    /// The taskbar with <i>Show accent color on Start and taskbar</i> on, given the shade of the
    /// accent Windows uses for it: its third darkest in the dark theme.
    /// </summary>
    public static TaskbarMaterial Accent(Color shade) => new(shade, 0.8, 0.8, shade);

    /// <summary>A taskbar that is one colour and nothing else.</summary>
    public static TaskbarMaterial Flat(Color color) => new(color, 1, 1, color, Opaque: true);
}
