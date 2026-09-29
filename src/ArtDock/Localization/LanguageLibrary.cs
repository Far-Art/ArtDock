using System.Globalization;
using System.IO;
using System.Text.Json;
using ArtDock.Packs;

namespace ArtDock.Localization;

/// <summary>One language the dock can be shown in.</summary>
/// <param name="Id">Its tag, as the settings file stores it: <c>en</c>, <c>ru</c>, <c>pt-BR</c>.</param>
/// <param name="Name">Its name in itself — Русский, Deutsch — which is how anyone looks for their own.</param>
/// <param name="EnglishName">Its name in English, when the pack gives one.</param>
/// <param name="IsBuiltIn">Shipped inside the executable rather than installed as a pack.</param>
/// <param name="Folder">The pack's folder, for an installed one.</param>
public sealed record LanguageInfo(
    string Id,
    string Name,
    string? EnglishName,
    bool IsBuiltIn,
    string? Folder)
{
    /// <summary>The file as read, so choosing the language does not read it twice.</summary>
    internal LanguagePackFile? File { get; init; }

    /// <summary>
    /// How the picker names it: in itself, and in English beside that when the two differ.
    /// </summary>
    public string DisplayName =>
        EnglishName is { Length: > 0 } english && !string.Equals(english, Name, StringComparison.CurrentCultureIgnoreCase)
            ? $"{Name} ({english})"
            : Name;
}

/// <summary>
/// The languages there are: English and any others built in, and whatever language packs
/// are installed.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot, taken when it is made. The settings dialog makes a new one when its picker is
/// opened, so a pack copied into place while the dialog is up appears without a restart.
/// </para>
/// <para>
/// Built-in languages are the <c>Localization/*.json</c> files embedded in the executable.
/// An installed pack with the same tag as a built-in one is preferred to it, so a
/// correction to a shipped translation can be tried by dropping a pack in, without a build.
/// </para>
/// </remarks>
public sealed class LanguageLibrary
{
    /// <summary>The tag of the language the dock is written in, and which every other falls back to.</summary>
    public const string EnglishId = "en";

    /// <summary>The newest language-pack format this build reads.</summary>
    public const int HighestFormat = 1;

    /// <summary>Where built-in languages live among the executable's manifest resources.</summary>
    private const string ResourcePrefix = "ArtDock.Localization.";

    private readonly List<LanguageInfo> _languages = [];
    private readonly List<PackProblem> _problems = [];

    /// <summary>Reads what is built in, and what is installed in a folder.</summary>
    /// <param name="installed">
    /// The folder language packs are installed in; usually
    /// <see cref="PackLocations.FolderFor(PackKind)"/>. A folder that does not exist is simply
    /// no packs.
    /// </param>
    public LanguageLibrary(string installed)
    {
        foreach (var file in BuiltInFiles())
        {
            Add(file, isBuiltIn: true, folder: null);
        }

        ReadInstalled(installed);

        // English first, as the language the dock is written in and the way back from any
        // other; then the rest by name, in the order a reader of the current language expects.
        _languages.Sort((a, b) =>
            a.Id == EnglishId ? -1
            : b.Id == EnglishId ? 1
            : string.Compare(a.Name, b.Name, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase));
    }

    /// <summary>English, built from the copy embedded in the executable.</summary>
    /// <remarks>
    /// Read once for the life of the process. Everything falls back to it, so it cannot be
    /// allowed to fail: the file is part of the build, and a test reads every key in it.
    /// </remarks>
    public static StringTable English => EnglishTable.Value;

    private static readonly Lazy<StringTable> EnglishTable = new(() =>
        StringTable.CreateEnglish(
            ReadBuiltIn(EnglishId)
            ?? throw new InvalidOperationException("The built-in English strings are missing from the build.")));

    /// <summary>Every language there is, English first.</summary>
    public IReadOnlyList<LanguageInfo> Languages => _languages;

    /// <summary>Packs that were found and could not be used.</summary>
    public IReadOnlyList<PackProblem> Problems => _problems;

    /// <summary>A language by its tag, or null when there is none.</summary>
    public LanguageInfo? Find(string? id) =>
        id is { Length: > 0 }
            ? _languages.FirstOrDefault(language => string.Equals(language.Id, id, StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>
    /// The language to show, from what was chosen and what Windows is set to.
    /// </summary>
    /// <param name="chosen">The stored choice, or null to follow Windows.</param>
    /// <param name="windows">Windows' display language.</param>
    /// <remarks>
    /// A chosen language that is no longer there — its pack deleted, or a settings file from
    /// another machine — follows Windows rather than dropping straight to English, because
    /// following Windows is what the dock does when it has not been told otherwise. Windows'
    /// language is matched as closely as there is a pack for: <c>pt-BR</c>, then <c>pt</c>,
    /// then any Portuguese at all, then English.
    /// </remarks>
    public string Resolve(string? chosen, CultureInfo windows)
    {
        if (Find(chosen) is { } picked)
        {
            return picked.Id;
        }

        for (var culture = windows; !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
        {
            if (Find(culture.Name) is { } match)
            {
                return match.Id;
            }
        }

        var language = windows.TwoLetterISOLanguageName;
        return _languages.FirstOrDefault(candidate =>
                   CultureInfo.GetCultureInfo(candidate.Id).TwoLetterISOLanguageName == language)?.Id
               ?? EnglishId;
    }

    /// <summary>
    /// A language's strings, ready to show — or English, for a tag there is no language for.
    /// </summary>
    public StringTable Load(string id)
    {
        if (Find(id) is not { File: { } file } language || language.Id == EnglishId && language.IsBuiltIn)
        {
            return English;
        }

        return StringTable.Create(language.Id, file, English);
    }

    private void ReadInstalled(string folder)
    {
        string[] packs;
        try
        {
            packs = Directory.Exists(folder) ? Directory.GetDirectories(folder) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var pack in packs.Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(pack);
            var (file, problem) = PackManifest.Read(pack, PackJsonContext.Default.LanguagePackFile);

            problem ??= file!.Check(PackKind.Language, HighestFormat, PackManifest.AppVersion)
                ?? CheckLanguage(file);

            if (problem is not null)
            {
                _problems.Add(new PackProblem(name, problem));
                continue;
            }

            // Installed over built-in: see the class remarks. Two installed packs with one tag
            // is a mistake, and the first one found is kept rather than either being guessed at.
            var id = CultureInfo.GetCultureInfo(file!.Id!).Name;
            if (Find(id) is { IsBuiltIn: false })
            {
                _problems.Add(new PackProblem(name, Localizer.Format("Packs.Problem.Duplicate", id)));
                continue;
            }

            _languages.RemoveAll(language => language.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            Add(file, isBuiltIn: false, pack);
        }
    }

    /// <summary>What a language pack needs beyond what every pack needs.</summary>
    private static string? CheckLanguage(LanguagePackFile file)
    {
        try
        {
            // Predefined only: a tag Windows has never heard of has no number formats or
            // plural rules to borrow, and is far likelier to be a typo than a language.
            _ = CultureInfo.GetCultureInfo(file.Id!, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            return Localizer.Format("Packs.Problem.NotALanguage", file.Id);
        }

        return file.Strings is { Count: > 0 } ? null : Localizer.Get("Packs.Problem.NoStrings");
    }

    private void Add(LanguagePackFile file, bool isBuiltIn, string? folder)
    {
        var id = CultureInfo.GetCultureInfo(file.Id!).Name;
        _languages.Add(new LanguageInfo(id, file.Name!, file.EnglishName, isBuiltIn, folder) { File = file });
    }

    /// <summary>Every language embedded in the executable.</summary>
    private static IEnumerable<LanguagePackFile> BuiltInFiles()
    {
        var names = typeof(LanguageLibrary).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

        foreach (var name in names)
        {
            if (ReadResource(name) is { } file)
            {
                yield return file;
            }
        }
    }

    private static LanguagePackFile? ReadBuiltIn(string id) => ReadResource($"{ResourcePrefix}{id}.json");

    private static LanguagePackFile? ReadResource(string name)
    {
        using var stream = typeof(LanguageLibrary).Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        try
        {
            // Not through PackManifest.Check, whose complaints are worded in the current
            // language — and English is what the current language is built on, so reading it
            // cannot depend on it. A built-in file is part of the build and a test reads it;
            // this only has to keep a broken one from being used.
            var file = PackManifest.Parse(stream, PackJsonContext.Default.LanguagePackFile);
            return file is { Format: <= HighestFormat, Id.Length: > 0, Name.Length: > 0 }
                   && string.Equals(file.Kind, PackManifest.LanguageKind, StringComparison.OrdinalIgnoreCase)
                ? file
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
