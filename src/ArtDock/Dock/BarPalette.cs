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
    public static readonly Color Default = Color.FromRgb(0xCC, 0xD2, 0xFF);

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

    /// <summary>Formats a colour the way the settings file stores it.</summary>
    public static string ToHex(Color color) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"#{color.R:X2}{color.G:X2}{color.B:X2}");
}
