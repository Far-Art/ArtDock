using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.Dock;
using Windows.Graphics.Effects;
using Windows.UI.Composition;

namespace ArtDock.Interop;

/// <summary>
/// The taskbar's acrylic, as a brush the compositor paints with: Windows' own recipe for it,
/// effect for effect, given a <see cref="TaskbarMaterial"/>.
/// </summary>
/// <remarks>
/// <para>
/// The graph is the one Windows' <c>AcrylicBrush</c> builds for acrylic drawn over what is
/// behind it in the same window, which is the kind the taskbar has: a Gaussian blur of what is
/// behind, mirrored at the visual's edges; a blend that gives it the tint's lightness; a blend
/// that gives it the tint's hue; and a tile of grain over the top. Direct2D has the two blends'
/// names the wrong way round, and Windows asks for them by the wrong names to get the right
/// ones, so the modes below are as it writes them: <i>colour</i> first, then <i>luminosity</i>.
/// </para>
/// <para>
/// Held to the taskbar by measurement, not by eye — see <see cref="TaskbarMaterial"/>. A sheet
/// drawn with this over the desktop came out within a third of a level of the recipe worked
/// out in software, and that within a level of the taskbar itself.
/// </para>
/// </remarks>
internal static class TaskbarAcrylic
{
    /// <summary>Direct2D's blend modes, by the names Direct2D gives them.</summary>
    private const uint BlendColour = 22;

    private const uint BlendLuminosity = 23;

    /// <summary>The blur's quality and speed, balanced: Windows' choice.</summary>
    private const uint BlurBalanced = 1;

    /// <summary>The blur's edges mirrored rather than faded out.</summary>
    private const uint BorderHard = 1;

    private const uint EdgeWrap = 1;
    private const uint CompositeSourceOver = 0;

    /// <summary>The grain tile's side, in pixels.</summary>
    private const int GrainSize = 256;

    /// <summary>
    /// The grain's range of greys. Their middle and their spread are the taskbar's own, measured:
    /// its pixels sit two hundredths of the way to mid grey, and stray from their neighbours by
    /// what a tile of this spread strays at that opacity.
    /// </summary>
    private const int GrainDarkest = 38;

    private const int GrainLightest = 218;

    /// <summary>
    /// The brush, or null on a machine whose compositor will not build it.
    /// </summary>
    /// <param name="scale">Device pixels to a DIP, which the blur's reach grows with as the taskbar's does.</param>
    /// <param name="behind">A brush of what is behind the visual: <see cref="Compositor.CreateBackdropBrush"/>.</param>
    /// <param name="grain">
    /// The grain tile (<see cref="Grain"/>) as a brush, or null to go without: its average is
    /// then laid on flat, which keeps the colour and gives up only the texture.
    /// </param>
    public static CompositionEffectBrush? TryCreate(
        Compositor compositor, TaskbarMaterial material, double scale, CompositionBrush behind, CompositionBrush? grain)
    {
        try
        {
            var tint = material.Tint;
            var luminosity = Windows.UI.Color.FromArgb(Alpha(material.LuminosityOpacity), tint.R, tint.G, tint.B);
            var hue = Windows.UI.Color.FromArgb(Alpha(material.TintOpacity), tint.R, tint.G, tint.B);

            var blurred = new D2DEffect(
                D2DEffect.GaussianBlur,
                [(float)(TaskbarMaterial.BlurRadius * scale), BlurBalanced, BorderHard],
                new CompositionEffectSourceParameter("Behind"));

            var lit = new D2DEffect(
                D2DEffect.Blend, [BlendColour], blurred, new D2DEffect(D2DEffect.Flood, [D2DEffect.Rgba(luminosity)]));

            var tinted = new D2DEffect(
                D2DEffect.Blend, [BlendLuminosity], lit, new D2DEffect(D2DEffect.Flood, [D2DEffect.Rgba(hue)]));

            IGraphicsEffectSource tile = grain is null
                ? new D2DEffect(D2DEffect.Flood, [new[] { GrainMiddle, GrainMiddle, GrainMiddle, 1f }])
                : new D2DEffect(D2DEffect.Border, [EdgeWrap, EdgeWrap], new CompositionEffectSourceParameter("Grain"));

            var grained = new D2DEffect(
                D2DEffect.Composite,
                [CompositeSourceOver],
                tinted,
                new D2DEffect(D2DEffect.Opacity, [(float)TaskbarMaterial.GrainOpacity], tile));

            var brush = compositor.CreateEffectFactory(grained).CreateBrush();
            brush.SetSourceParameter("Behind", behind);
            if (grain is not null)
            {
                brush.SetSourceParameter("Grain", grain);
            }

            return brush;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException
            or UnauthorizedAccessException or NotSupportedException)
        {
            // No effects on this machine's compositor. The caller paints the bar flat.
            return null;
        }
    }

    /// <summary>
    /// The grain: a square of greys with no pattern to it, the same on every run so the bar does
    /// not change its texture from one start to the next.
    /// </summary>
    public static BitmapSource Grain()
    {
        var random = new Random(0x41727444);
        var pixels = new byte[GrainSize * GrainSize * 4];

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var grey = (byte)random.Next(GrainDarkest, GrainLightest + 1);
            pixels[i] = grey;
            pixels[i + 1] = grey;
            pixels[i + 2] = grey;
            pixels[i + 3] = 0xFF;
        }

        var bitmap = BitmapSource.Create(
            GrainSize, GrainSize, 96, 96, PixelFormats.Pbgra32, null, pixels, GrainSize * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The grain's average, 0 to 1, for where the tile itself cannot be had.</summary>
    private const float GrainMiddle = (GrainDarkest + GrainLightest) / 2f / 255f;

    private static byte Alpha(double opacity) =>
        (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255, MidpointRounding.AwayFromZero);
}
