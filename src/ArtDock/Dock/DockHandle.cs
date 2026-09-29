using System.Windows;

namespace ArtDock.Dock;

/// <summary>
/// Where the handle an auto-hidden dock leaves behind goes: a slim bar just above the taskbar,
/// centred under where the dock will come up.
/// </summary>
/// <remarks>
/// <para>
/// The phone's home indicator, on a desktop. Pure, and in physical pixels throughout, so it
/// can be tested against this machine's two displays at their real scales — the handle is a
/// window of its own, placed by <c>SetWindowPos</c>, and nothing about it passes through WPF's
/// idea of the scale.
/// </para>
/// <para>
/// Centred on the dock's bar <i>at rest</i> rather than on the bar as drawn. It marks where the
/// dock will come up, and the bar as drawn breathes with the wave and eases open when a slot
/// arrives — a mark that followed that would be following something the dock is not doing
/// while it is away. It also keeps the handle under the dock wherever the dock has been moved
/// along its edge, since the resting bar is already placed for that.
/// </para>
/// </remarks>
public static class DockHandle
{
    /// <summary>How thick the handle is, in DIPs — the phone's is five points.</summary>
    public const double Thickness = 5;

    /// <summary>How far above the work area's bottom — the top of the taskbar — it sits, in DIPs.</summary>
    /// <remarks>
    /// Clear of the taskbar, so it is not read as part of it, and below where the dock's bar
    /// rests at the default margin, so it sits under the dock rather than in its place.
    /// </remarks>
    public const double Lift = 6;

    /// <summary>
    /// How far above the handle the pointer still counts as on it, in DIPs.
    /// </summary>
    /// <remarks>
    /// Five DIPs is a thin thing to rest a pointer on. Below it the target already reaches down
    /// to the taskbar, so this is the only side that needs the help.
    /// </remarks>
    public const double Reach = 6;

    public const double MinWidth = 40;
    public const double MaxWidth = 600;

    /// <summary>
    /// A width of its own, for when it is not the dock's: about the phone's, which is a third
    /// of a screen that is not much wider than a dock of six icons.
    /// </summary>
    public const double DefaultWidth = 140;

    /// <summary>
    /// The handle's rectangle, in physical pixels.
    /// </summary>
    /// <param name="barLeft">The resting bar's left edge on the screen, in physical pixels.</param>
    /// <param name="barRight">Its right edge.</param>
    /// <param name="workAreaBottom">The bottom of the display's work area, in physical pixels.</param>
    /// <param name="scale">The display's scale factor, 1 at 96 dpi.</param>
    /// <param name="matchDock">Whether the handle is as wide as the bar.</param>
    /// <param name="width">Its own width in DIPs, used when it is not the bar's.</param>
    /// <remarks>
    /// Whole pixels, because that is all a window can be placed in. The width is rounded before
    /// the left edge is worked out from it, so an odd width is off-centre by half a pixel at most
    /// rather than a pixel wider or narrower depending on where it happens to fall.
    /// </remarks>
    public static Rect Place(
        double barLeft, double barRight, double workAreaBottom, double scale, bool matchDock, double width)
    {
        var pixels = scale > 0 && double.IsFinite(scale) ? scale : 1;
        var centre = (barLeft + barRight) / 2;

        var wide = Math.Max(1, Math.Round(matchDock
            ? barRight - barLeft
            : Math.Clamp(double.IsFinite(width) ? width : DefaultWidth, MinWidth, MaxWidth) * pixels));
        var tall = Math.Max(1, Math.Round(Thickness * pixels));
        var bottom = Math.Round(workAreaBottom - (Lift * pixels));

        return new Rect(Math.Round(centre - (wide / 2)), bottom - tall, wide, tall);
    }

    /// <summary>
    /// How far an inverted colour is pushed towards black or white, whichever the colour behind
    /// it is not, as a share of the way.
    /// </summary>
    /// <remarks>
    /// A plain inversion has a blind spot, and it is the commonest colour on a desktop: the
    /// inverse of mid-grey is mid-grey, and a handle over a grey status bar would vanish into
    /// it. Pushed, the colour behind can never come back as itself — the least it differs by is
    /// fifty levels, at the grey where the push changes direction — while a colour well away
    /// from grey still reads as its inverse.
    /// </remarks>
    public const int PushPercent = 40;

    /// <summary>
    /// What the handle shows over <paramref name="behind"/>: each pixel inverted and pushed away
    /// from the colour behind it, opaque.
    /// </summary>
    /// <param name="behind">32-bit BGRA pixels, as read off the screen. Alpha is ignored.</param>
    /// <param name="shown">Where the result goes, at least as long.</param>
    /// <remarks>
    /// Whole pixels only, in integers — this runs over every pixel under the handle each time
    /// what is behind it changes. Light and dark are told apart by the Rec. 709 luminance, the
    /// same weighting the dock's colour outline uses.
    /// </remarks>
    public static void Invert(ReadOnlySpan<byte> behind, Span<byte> shown)
    {
        var length = Math.Min(behind.Length, shown.Length) / 4 * 4;

        for (var i = 0; i < length; i += 4)
        {
            int blue = behind[i];
            int green = behind[i + 1];
            int red = behind[i + 2];

            // Out of 2 550 000, the luminance of white; light is the top half.
            var light = (2126 * red) + (7152 * green) + (722 * blue) >= 1_275_000;
            var toward = light ? 0 : 255;

            shown[i] = Push(255 - blue, toward);
            shown[i + 1] = Push(255 - green, toward);
            shown[i + 2] = Push(255 - red, toward);
            shown[i + 3] = 255;
        }
    }

    private static byte Push(int value, int toward) =>
        (byte)(value + ((toward - value) * PushPercent / 100));

    /// <summary>
    /// Where the pointer counts as on the handle: the handle, a little above it, and everything
    /// below it down to the taskbar.
    /// </summary>
    /// <remarks>
    /// Down to the work area's bottom and no further. Below that is the taskbar, which the
    /// pointer visits for reasons of its own, and below the taskbar is the reveal edge, which
    /// is a way back of its own already.
    /// </remarks>
    public static Rect Target(Rect handle, double workAreaBottom, double scale)
    {
        if (handle.IsEmpty)
        {
            return Rect.Empty;
        }

        var pixels = scale > 0 && double.IsFinite(scale) ? scale : 1;
        var top = handle.Top - (Reach * pixels);
        var bottom = Math.Max(handle.Bottom, workAreaBottom);

        return new Rect(handle.Left, top, handle.Width, bottom - top);
    }
}
