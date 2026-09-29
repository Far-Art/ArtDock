using System.Globalization;
using System.Text.RegularExpressions;

namespace ArtDock.Localization;

/// <summary>
/// One language's strings, with English behind them.
/// </summary>
/// <remarks>
/// <para>
/// Immutable once built, and built once per change of language, so a lookup is a dictionary
/// read. Every lookup that misses falls through to English, and English to the key itself —
/// a missing string shows up as its key on screen, which is ugly and findable, rather than as
/// an empty control, which is neither.
/// </para>
/// <para>
/// A translation is checked against English as it is loaded, and any string that could break
/// the dock is dropped in favour of the English one: an empty value, a key English does not
/// have, and above all a placeholder English does not supply. <c>string.Format</c> throws on
/// <c>{2}</c> when it was given two arguments, and a throw inside a <c>DragOver</c> handler
/// makes the OLE drag loop refuse the drop, silently.
/// </para>
/// </remarks>
public sealed partial class StringTable
{
    private readonly IReadOnlyDictionary<string, string> _strings;
    private readonly StringTable? _fallback;

    private StringTable(
        string id,
        CultureInfo culture,
        bool rightToLeft,
        IReadOnlyDictionary<string, string> strings,
        StringTable? fallback,
        IReadOnlyList<string> rejected)
    {
        Id = id;
        Culture = culture;
        IsRightToLeft = rightToLeft;
        _strings = strings;
        _fallback = fallback;
        Rejected = rejected;
    }

    /// <summary>The language's tag, such as <c>en</c> or <c>pt-BR</c>.</summary>
    public string Id { get; }

    /// <summary>The culture numbers are formatted in, and plural forms chosen by.</summary>
    public CultureInfo Culture { get; }

    /// <summary>Whether the language reads right to left.</summary>
    public bool IsRightToLeft { get; }

    /// <summary>
    /// Keys whose translation was refused, and why — for whoever is checking a translation.
    /// English has shown through wherever these are.
    /// </summary>
    public IReadOnlyList<string> Rejected { get; }

    /// <summary>Every key that resolves to something, in this language or in English.</summary>
    public IEnumerable<string> Keys =>
        _fallback is null ? _strings.Keys : _strings.Keys.Union(_fallback.Keys, StringComparer.Ordinal);

    /// <summary>A string as it stands, with no arguments put into it.</summary>
    public string Get(string key) => Lookup(key, []) ?? key;

    /// <summary>
    /// A string with arguments put into it, in this language's number formats.
    /// </summary>
    /// <remarks>
    /// A key that has no plain entry but has plural variants is chosen among them by the
    /// first argument, so the same call says "1 icon" and "3 icons" — and whatever the
    /// translation's language says instead.
    /// </remarks>
    public string Format(string key, params object?[] args)
    {
        var pattern = Lookup(key, args) ?? key;
        try
        {
            return string.Format(Culture, pattern, args);
        }
        catch (FormatException)
        {
            // English is checked by the tests and a translation is checked on load, so this is
            // a call site passing fewer arguments than its own string wants. The pattern with
            // its holes showing is a better failure than an exception on the UI thread.
            return pattern;
        }
    }

    /// <summary>Whether this language, or English behind it, has anything for a key.</summary>
    public bool Contains(string key) => Lookup(key, [1]) is not null;

    private string? Lookup(string key, object?[] args)
    {
        if (_strings.TryGetValue(key, out var value))
        {
            return value;
        }

        if (args is [var first, ..] && ToNumber(first) is { } count)
        {
            var category = PluralRules.Select(Culture, count);
            if (_strings.TryGetValue($"{key}.{category}", out value)
                || _strings.TryGetValue($"{key}.other", out value))
            {
                return value;
            }
        }

        return _fallback?.Lookup(key, args);
    }

    private static double? ToNumber(object? value) => value switch
    {
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
            => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        _ => null
    };

    /// <summary>
    /// Builds English: the table every other one falls back to, and which has nothing behind
    /// it.
    /// </summary>
    public static StringTable CreateEnglish(LanguagePackFile file) =>
        new(
            LanguageLibrary.EnglishId,
            CultureInfo.GetCultureInfo(LanguageLibrary.EnglishId),
            rightToLeft: false,
            new Dictionary<string, string>(file.Strings ?? [], StringComparer.Ordinal),
            fallback: null,
            rejected: []);

    /// <summary>
    /// Builds a translation, keeping only the strings that are safe to show.
    /// </summary>
    /// <param name="id">The language's tag, already checked to be one Windows knows.</param>
    /// <param name="file">The pack as read.</param>
    /// <param name="english">What the translation is checked against, and falls back to.</param>
    public static StringTable Create(string id, LanguagePackFile file, StringTable english)
    {
        var culture = CultureInfo.GetCultureInfo(id);
        var kept = new Dictionary<string, string>(StringComparer.Ordinal);
        var rejected = new List<string>();

        foreach (var (key, value) in file.Strings ?? [])
        {
            if (Refuse(key, value, english, culture) is { } reason)
            {
                rejected.Add($"{key}: {reason}");
                continue;
            }

            kept[key] = value;
        }

        return new StringTable(id, culture, file.IsRightToLeft, kept, english, rejected);
    }

    /// <summary>Why a translated string cannot be used, or null when it can.</summary>
    private static string? Refuse(string key, string? value, StringTable english, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "empty";
        }

        // A plural variant is held against English's own wording for many, since not every
        // language has the variant English has — Russian's .few has no English counterpart.
        var reference = english._strings.GetValueOrDefault(key)
            ?? (PluralRules.BaseKey(key) is { } baseKey
                ? english._strings.GetValueOrDefault($"{baseKey}.other")
                : null);

        if (reference is null)
        {
            return "not a key English has";
        }

        // Leaving one out is allowed — "one icon" need not say 1 — but one English does not
        // supply would throw every time the string was shown.
        var allowed = Placeholders(reference);
        if (Placeholders(value).Where(index => !allowed.Contains(index)).Order().FirstOrDefault(-1)
            is var extra and >= 0)
        {
            return $"uses {{{extra}}}, which English does not supply";
        }

        // Formats as it will be formatted, with as many arguments as English is given. That
        // catches the malformed — an unclosed brace, a bad format specifier — before a user
        // does, rather than the first time the string is shown.
        if (allowed.Count > 0)
        {
            try
            {
                _ = string.Format(culture, value, Enumerable.Repeat<object?>(1.0, allowed.Max() + 1).ToArray());
            }
            catch (FormatException)
            {
                return "not a valid format string";
            }
        }

        return null;
    }

    /// <summary>The argument indices a string refers to, skipping escaped braces.</summary>
    public static HashSet<int> Placeholders(string pattern) =>
        [.. PlaceholderPattern().Matches(pattern.Replace("{{", string.Empty).Replace("}}", string.Empty))
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))];

    [GeneratedRegex(@"\{(\d+)[^{}]*\}")]
    private static partial Regex PlaceholderPattern();
}
