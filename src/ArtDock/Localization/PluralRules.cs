using System.Globalization;

namespace ArtDock.Localization;

/// <summary>
/// Which plural form a number takes, in the languages a translation is likeliest to come in.
/// </summary>
/// <remarks>
/// <para>
/// English has two forms and it is easy to write code that assumes every language does.
/// Russian has three for whole numbers, Arabic six, Japanese one. The categories are CLDR's —
/// <c>zero</c>, <c>one</c>, <c>two</c>, <c>few</c>, <c>many</c>, <c>other</c> — so a
/// translator who has seen them anywhere else recognises them here.
/// </para>
/// <para>
/// A subset of CLDR's rules rather than all of them: the families below cover most of the
/// languages anyone is likely to translate a Windows utility into, and anything not listed
/// takes the English rule. A language that needs a form not listed can still give every
/// number the same wording under <c>.other</c>, which is always consulted before English.
/// </para>
/// </remarks>
public static class PluralRules
{
    /// <summary>The category a number falls in, in a culture's language.</summary>
    public static string Select(CultureInfo culture, double number)
    {
        var n = Math.Abs(number);
        var whole = Math.Abs(n - Math.Round(n)) < 1e-9;
        var i = (long)Math.Floor(n);

        switch (culture.TwoLetterISOLanguageName)
        {
            // No plural forms at all.
            case "ja" or "zh" or "ko" or "vi" or "th" or "id" or "ms" or "lo" or "my" or "km":
                return "other";

            // One for nought and one alike.
            case "fr" or "hy" or "kab":
                return i is 0 or 1 ? "one" : "other";

            case "ru" or "uk" or "be":
                if (!whole)
                {
                    return "other";
                }

                return (i % 10, i % 100) switch
                {
                    (1, not 11) => "one",
                    ( >= 2 and <= 4, < 12 or > 14) => "few",
                    _ => "many"
                };

            case "pl":
                if (!whole)
                {
                    return "other";
                }

                if (i == 1)
                {
                    return "one";
                }

                return (i % 10, i % 100) switch
                {
                    ( >= 2 and <= 4, < 12 or > 14) => "few",
                    _ => "many"
                };

            case "cs" or "sk":
                if (!whole)
                {
                    return "many";
                }

                return i switch
                {
                    1 => "one",
                    >= 2 and <= 4 => "few",
                    _ => "other"
                };

            case "he":
                if (!whole)
                {
                    return "other";
                }

                return i switch
                {
                    1 => "one",
                    2 => "two",
                    _ => "other"
                };

            case "ar":
                if (!whole)
                {
                    return "other";
                }

                return i switch
                {
                    0 => "zero",
                    1 => "one",
                    2 => "two",
                    _ when i % 100 is >= 3 and <= 10 => "few",
                    _ when i % 100 is >= 11 and <= 99 => "many",
                    _ => "other"
                };

            // English, and the Germanic and most Romance languages: one for exactly one.
            default:
                return whole && i == 1 ? "one" : "other";
        }
    }

    /// <summary>Every category a key can end in.</summary>
    public static readonly string[] Categories = ["zero", "one", "two", "few", "many", "other"];

    /// <summary>
    /// The key a plural variant belongs to — <c>X</c> for <c>X.few</c> — or null for a key
    /// that is not a plural variant.
    /// </summary>
    public static string? BaseKey(string key)
    {
        var dot = key.LastIndexOf('.');
        return dot > 0 && Array.IndexOf(Categories, key[(dot + 1)..]) >= 0 ? key[..dot] : null;
    }
}
