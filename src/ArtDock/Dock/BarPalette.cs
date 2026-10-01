using System.Globalization;
using System.Windows.Media;

namespace ArtDock.Dock;

/// <summary>
/// Turns the stored bar colour into something drawable, and offers a few starting points.
/// </summary>
/// <remarks>
/// Stored as a hex string rather than a serialised <see cref="Color"/> so the settings file
/// stays legible and hand-editable, which is the point of keeping it as JSON.
/// </remarks>
public static class BarPalette
{
    /// <summary>The bar's stock colour, and the fallback for anything unparseable.</summary>
    /// <remarks>
    /// With <see cref="DefaultOpacity"/>, the look every dock on the stock settings has had at
    /// every start. Until 2026-10-01 the settings said <c>#EEF1FF</c> at 0.76 and the dock
    /// painted this instead — see <c>DockSettings.Migrate</c> — so when the two were made to
    /// agree, it was the settings that moved to what had been on the screen.
    /// </remarks>
    public static readonly Color Default = Color.FromRgb(0xCC, 0xD2, 0xFF);

    /// <summary>How opaque the stock bar is, 0 to 1.</summary>
    public const double DefaultOpacity = 0.5;

    /// <summary>
    /// Ready-made colours offered in the settings dialog, so choosing one does not require
    /// knowing hex.
    /// </summary>
    public static readonly IReadOnlyList<string> Swatches =
    [
        "#1B1E2B", "#000000", "#2E3345", "#3A2B4C",
        "#1E3330", "#3A2222", "#2B2418", "#E8EAF2"
    ];

    /// <summary>
    /// Reads <c>#RRGGBB</c> (or <c>#AARRGGBB</c>, or any name WPF knows), falling back to
    /// <see cref="Default"/>. Alpha is dropped: the bar's transparency is a separate setting,
    /// and letting a colour carry its own would make the two fight.
    /// </summary>
    public static Color Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        try
        {
            if (ColorConverter.ConvertFromString(value.Trim()) is Color color)
            {
                return Color.FromRgb(color.R, color.G, color.B);
            }
        }
        catch (FormatException)
        {
            // Half-typed hex, which is normal while the user is still typing.
        }
        catch (NotSupportedException)
        {
        }

        return Default;
    }

    /// <summary>
    /// What stands in for the desktop behind the bar where the bar is painted solid, under
    /// <c>DockSettings.NoGpu</c>: the grey its colour is mixed with in place of whatever would
    /// have shown through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bar is chosen by eye at the opacity it has, over a desktop: the stock one is a pale
    /// blue at a half, and what is seen is a good deal greyer than the colour stored. Painted
    /// solid in that colour alone it came out too light — reported the first time it was
    /// looked at — and a white separator all but vanished on it.
    /// </para>
    /// <para>
    /// The grey is what the acrylic sheet would have put there over a desktop of mid grey: its
    /// wash, <c>#141620</c> at 22%, over <c>#808080</c>. A desktop is not mid grey, but it is
    /// the one guess that is wrong by the same amount in both directions, and a bar that
    /// neither darkens nor lightens much against the one it replaces is the point.
    /// </para>
    /// </remarks>
    public static readonly Color Underlay = Color.FromRgb(0x68, 0x69, 0x6B);

    /// <summary>
    /// The opaque colour that <paramref name="color"/> at <paramref name="opacity"/> makes over
    /// <paramref name="under"/>.
    /// </summary>
    public static Color Over(Color color, double opacity, Color under)
    {
        var share = double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 1;

        byte Mix(byte top, byte bottom) =>
            (byte)Math.Round((top * share) + (bottom * (1 - share)), MidpointRounding.AwayFromZero);

        return Color.FromRgb(Mix(color.R, under.R), Mix(color.G, under.G), Mix(color.B, under.B));
    }

    /// <summary>Formats a colour the way the settings file stores it.</summary>
    public static string ToHex(Color color) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"#{color.R:X2}{color.G:X2}{color.B:X2}");
}
