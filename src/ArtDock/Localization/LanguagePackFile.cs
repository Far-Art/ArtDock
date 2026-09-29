using ArtDock.Packs;

namespace ArtDock.Localization;

/// <summary>
/// A language pack's <c>pack.json</c>: the common header, and the strings.
/// </summary>
/// <remarks>
/// The same file shape for the English the dock is built with (<c>Localization/en.json</c>,
/// embedded in the executable) and for every translation, so a translator's starting point is
/// a copy of that file with the values changed.
/// </remarks>
public sealed class LanguagePackFile : PackManifest
{
    /// <summary>
    /// The language's name in English, shown beside its own name in the picker so that
    /// someone who has landed in a language they cannot read can still find their way back.
    /// </summary>
    public string? EnglishName { get; set; }

    /// <summary><c>ltr</c>, the default, or <c>rtl</c> for a right-to-left language.</summary>
    public string? Direction { get; set; }

    /// <summary>
    /// Every string, by key. A key missing here is taken from English, so a partial
    /// translation is a usable one.
    /// </summary>
    /// <remarks>
    /// A key that varies with a number is written as several keys with a plural category on
    /// the end — <c>.one</c>, <c>.few</c>, <c>.many</c>, <c>.other</c> and so on, as the
    /// language needs — and <c>.other</c> is the one every language should have. See
    /// <see cref="PluralRules"/>.
    /// </remarks>
    public Dictionary<string, string>? Strings { get; set; }

    /// <summary>Whether <see cref="Direction"/> says right to left.</summary>
    public bool IsRightToLeft => string.Equals(Direction, "rtl", StringComparison.OrdinalIgnoreCase);
}
