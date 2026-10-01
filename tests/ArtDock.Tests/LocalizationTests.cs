using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using ArtDock.Localization;
using ArtDock.Packs;

namespace ArtDock.Tests;

/// <summary>
/// Covers the string table: that English is complete and well-formed, that a translation is
/// held to it, and that plurals and fallbacks land where they should.
/// </summary>
/// <remarks>
/// <para>
/// The first group reads the source. A key that is used and not defined shows its own name on
/// screen, and a <c>DynamicResource</c> that names nothing shows nothing at all — neither is
/// caught by the compiler, and the second is silent. Scanning the XAML and the code for every
/// key they name, and holding the list against <c>en.json</c> both ways, is what stands in for
/// the type checking that a string key gives up.
/// </para>
/// <para>
/// Everything that reads packs is pointed at a temporary folder. None of it may read or write
/// the user's own packs.
/// </para>
/// </remarks>
public partial class LocalizationTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ArtDockTests", Guid.NewGuid().ToString("n"));

    public LocalizationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }

        GC.SuppressFinalize(this);
    }

    // ---- English against the source ---------------------------------------------

    private static readonly string SourceRoot = FindSourceRoot();

    private static string FindSourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ArtDock.sln")))
            {
                return Path.Combine(dir.FullName, "src", "ArtDock");
            }
        }

        throw new InvalidOperationException("Could not find the repository from the test's output folder.");
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(SourceRoot, pattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>The first segment of every English key: <c>Settings</c>, <c>Menu</c> and so on.</summary>
    private static HashSet<string> KeyPrefixes() =>
        [.. LanguageLibrary.English.Keys.Select(key => key[..key.IndexOf('.')])];

    /// <summary>
    /// Every key the source names: string literals in code shaped like a key under a known
    /// prefix, and the keys the XAML asks for.
    /// </summary>
    private static HashSet<string> KeysUsedInSource()
    {
        var prefixes = KeyPrefixes();
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in SourceFiles("*.cs"))
        {
            foreach (Match match in CodeKey().Matches(File.ReadAllText(file)))
            {
                var key = match.Groups[1].Value;
                if (prefixes.Contains(key[..key.IndexOf('.')]))
                {
                    used.Add(key);
                }
            }
        }

        foreach (var file in SourceFiles("*.xaml"))
        {
            var xaml = File.ReadAllText(file);
            foreach (Match match in XamlResourceKey().Matches(xaml))
            {
                var key = match.Groups[1].Value;
                if (prefixes.Contains(key[..key.IndexOf('.')]))
                {
                    used.Add(key);
                }
            }

            foreach (Match match in XamlLocalizedKey().Matches(xaml))
            {
                used.Add(match.Groups[1].Value);
            }
        }

        return used;
    }

    [GeneratedRegex(@"""([A-Z][A-Za-z]*(?:\.[A-Za-z0-9]+)+)""")]
    private static partial Regex CodeKey();

    [GeneratedRegex(@"\{DynamicResource ([A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)\}")]
    private static partial Regex XamlResourceKey();

    [GeneratedRegex(@"Localized\.Key=""([^""]+)""")]
    private static partial Regex XamlLocalizedKey();

    [Fact]
    public void EveryKeyTheSourceUses_IsInEnglish()
    {
        var english = LanguageLibrary.English;
        var missing = KeysUsedInSource()
            .Where(key => !english.Contains(key) && !key.StartsWith("Language.", StringComparison.Ordinal))
            .Order()
            .ToList();

        Assert.True(missing.Count == 0, "Used but not in en.json: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryKeyInEnglish_IsUsedSomewhere()
    {
        var used = KeysUsedInSource();
        var unused = LanguageLibrary.English.Keys
            .Where(key => !used.Contains(PluralRules.BaseKey(key) ?? key) && !used.Contains(key))
            .Order()
            .ToList();

        Assert.True(unused.Count == 0, "In en.json but never used: " + string.Join(", ", unused));
    }

    [Fact]
    public void EveryEnglishPattern_Formats()
    {
        foreach (var key in LanguageLibrary.English.Keys)
        {
            var pattern = LanguageLibrary.English.Get(key);
            var count = StringTable.Placeholders(pattern).DefaultIfEmpty(-1).Max() + 1;

            var formatted = string.Format(
                CultureInfo.InvariantCulture, pattern, Enumerable.Repeat<object?>(1.0, count).ToArray());

            Assert.False(string.IsNullOrWhiteSpace(formatted), key);
        }
    }

    // ---- lookups ---------------------------------------------------------------

    [Fact]
    public void AMissingKey_ShowsItself()
    {
        Assert.Equal("No.Such.Key", LanguageLibrary.English.Get("No.Such.Key"));
    }

    [Theory]
    [InlineData(1, "1 icon")]
    [InlineData(3, "3 icons")]
    public void English_ChoosesItsPluralByCount(double count, string expected)
    {
        Assert.Equal(expected, LanguageLibrary.English.Format("Settings.Size.Influence.Value", count));
    }

    [Theory]
    [InlineData(1, "one")]
    [InlineData(2, "few")]
    [InlineData(5, "many")]
    [InlineData(11, "many")]
    [InlineData(21, "one")]
    [InlineData(22, "few")]
    [InlineData(112, "many")]
    [InlineData(1.5, "other")]
    public void Russian_HasThreeFormsForWholeNumbers(double count, string expected)
    {
        Assert.Equal(expected, PluralRules.Select(CultureInfo.GetCultureInfo("ru"), count));
    }

    [Theory]
    [InlineData("en", 1, "one")]
    [InlineData("en", 0, "other")]
    [InlineData("fr", 0, "one")]
    [InlineData("ja", 1, "other")]
    [InlineData("pl", 22, "few")]
    [InlineData("pl", 25, "many")]
    public void OtherLanguages_FollowTheirOwnRules(string language, double count, string expected)
    {
        Assert.Equal(expected, PluralRules.Select(CultureInfo.GetCultureInfo(language), count));
    }

    // ---- translations -----------------------------------------------------------

    private static LanguagePackFile Translation(Dictionary<string, string> strings) => new()
    {
        Kind = PackManifest.LanguageKind,
        Id = "ru",
        Name = "Русский",
        Strings = strings
    };

    [Fact]
    public void ATranslation_FallsBackToEnglish_KeyByKey()
    {
        var table = StringTable.Create(
            "ru", Translation(new() { ["Common.Save"] = "Сохранить" }), LanguageLibrary.English);

        Assert.Equal("Сохранить", table.Get("Common.Save"));
        Assert.Equal("Cancel", table.Get("Common.Cancel"));
    }

    [Fact]
    public void ATranslation_WithAPlaceholderEnglishDoesNotSupply_IsRefused()
    {
        // {1} would throw every time the string was shown, since only {0} is passed.
        var table = StringTable.Create(
            "ru",
            Translation(new() { ["Settings.Size.IconSize.Value"] = "{0:0} {1} пикс." }),
            LanguageLibrary.English);

        Assert.Equal("48 px", table.Format("Settings.Size.IconSize.Value", 48.0));
        Assert.Contains(table.Rejected, reason => reason.StartsWith("Settings.Size.IconSize.Value", StringComparison.Ordinal));
    }

    [Fact]
    public void ATranslation_ThatIsNotAFormatString_IsRefused()
    {
        var table = StringTable.Create(
            "ru",
            Translation(new() { ["Settings.Size.IconSize.Value"] = "{0:0 пикс." }),
            LanguageLibrary.English);

        Assert.Equal("48 px", table.Format("Settings.Size.IconSize.Value", 48.0));
    }

    [Fact]
    public void ATranslation_MayLeaveThePlaceholderOut()
    {
        var table = StringTable.Create(
            "ru",
            Translation(new() { ["Settings.Size.Influence.Value.one"] = "одна иконка" }),
            LanguageLibrary.English);

        Assert.Equal("одна иконка", table.Format("Settings.Size.Influence.Value", 1.0));
        Assert.Empty(table.Rejected);
    }

    [Fact]
    public void ATranslation_ChoosesAmongItsOwnPluralForms()
    {
        var table = StringTable.Create(
            "ru",
            Translation(new()
            {
                ["Settings.Size.Influence.Value.one"] = "{0:0} иконка",
                ["Settings.Size.Influence.Value.few"] = "{0:0} иконки",
                ["Settings.Size.Influence.Value.many"] = "{0:0} иконок",
                ["Settings.Size.Influence.Value.other"] = "{0} иконки"
            }),
            LanguageLibrary.English);

        Assert.Equal("1 иконка", table.Format("Settings.Size.Influence.Value", 1.0));
        Assert.Equal("3 иконки", table.Format("Settings.Size.Influence.Value", 3.0));
        Assert.Equal("5 иконок", table.Format("Settings.Size.Influence.Value", 5.0));
    }

    [Fact]
    public void ATranslation_FormatsNumbersInItsOwnCulture()
    {
        var table = StringTable.Create("de", Translation(new()), LanguageLibrary.English);

        // German puts a space before the per cent sign; English does not.
        Assert.NotEqual(
            LanguageLibrary.English.Format("Settings.Size.Gap.Value", 0.16),
            table.Format("Settings.Size.Gap.Value", 0.16));
    }

    [Fact]
    public void AKeyEnglishDoesNotHave_IsRefused()
    {
        var table = StringTable.Create(
            "ru", Translation(new() { ["Nothing.Like.This"] = "x" }), LanguageLibrary.English);

        Assert.Single(table.Rejected);
    }

    // ---- the library ------------------------------------------------------------

    private string InstallPack(string folder, string json)
    {
        var path = Path.Combine(_dir, folder);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, PackLocations.ManifestFileName), json);
        return path;
    }

    private const string GermanPack = """
        {
          // Comments and trailing commas, as a person writing this by hand would.
          "format": 1,
          "kind": "language",
          "id": "de",
          "name": "Deutsch",
          "englishName": "German",
          "strings": { "Common.Save": "Speichern", },
        }
        """;

    [Fact]
    public void English_IsAlwaysThere_AndFirst()
    {
        var library = new LanguageLibrary(_dir);

        Assert.Equal(LanguageLibrary.EnglishId, library.Languages[0].Id);
        Assert.True(library.Languages[0].IsBuiltIn);
    }

    [Fact]
    public void AnInstalledPack_IsListed_AndLoads()
    {
        InstallPack("de", GermanPack);
        var library = new LanguageLibrary(_dir);

        var german = Assert.Single(library.Languages, language => language.Id == "de");
        Assert.Equal("Deutsch (German)", german.DisplayName);
        Assert.Equal("Speichern", library.Load("de").Get("Common.Save"));
        Assert.Empty(library.Problems);
    }

    [Theory]
    [InlineData("""{ "format": 1, "kind": "iconSet", "id": "de", "name": "x", "strings": { "Common.Save": "x" } }""")]
    [InlineData("""{ "format": 99, "kind": "language", "id": "de", "name": "x", "strings": { "Common.Save": "x" } }""")]
    [InlineData("""{ "format": 1, "kind": "language", "id": "not-a-language-at-all", "name": "x", "strings": { "Common.Save": "x" } }""")]
    [InlineData("""{ "format": 1, "kind": "language", "id": "de", "name": "x" }""")]
    [InlineData("""{ "format": 1, "kind": "language", "id": "de", "name": "x", "minAppVersion": "999.0", "strings": { "Common.Save": "x" } }""")]
    [InlineData("""this is not json""")]
    public void AnUnusablePack_IsReported_NotListed(string json)
    {
        InstallPack("broken", json);
        var library = new LanguageLibrary(_dir);

        Assert.DoesNotContain(library.Languages, language => language.Id == "de");
        var problem = Assert.Single(library.Problems);
        Assert.Equal("broken", problem.Folder);
    }

    [Fact]
    public void AFolderWithNoManifest_IsReported()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "empty"));

        Assert.Single(new LanguageLibrary(_dir).Problems);
    }

    [Fact]
    public void FollowingWindows_TakesTheClosestLanguageThereIs()
    {
        InstallPack("de", GermanPack);
        var library = new LanguageLibrary(_dir);

        Assert.Equal("de", library.Resolve(null, CultureInfo.GetCultureInfo("de-AT")));
        Assert.Equal(LanguageLibrary.EnglishId, library.Resolve(null, CultureInfo.GetCultureInfo("ja-JP")));
    }

    [Fact]
    public void AChosenLanguageThatIsGone_FollowsWindows()
    {
        InstallPack("de", GermanPack);
        var library = new LanguageLibrary(_dir);

        Assert.Equal("de", library.Resolve("ru", CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal(LanguageLibrary.EnglishId, library.Resolve("en", CultureInfo.GetCultureInfo("de-DE")));
    }

    // ---- US and British English -------------------------------------------------

    private const string BritishId = "en-GB";

    /// <summary>
    /// Words spelled one way in US English and another in British: the US stem, and the
    /// British one. Only words whose spelling settles which English a string is in — "tick"
    /// and "check" are both English, and say nothing on their own.
    /// </summary>
    private static readonly (string Us, string British)[] Spellings =
    [
        ("color", "colour"),
        ("behavior", "behaviour"),
        ("center", "centre"),
        ("favorite", "favourite"),
        ("honor", "honour"),
        ("gray", "grey")
    ];

    private static bool Spells(string text, Func<(string Us, string British), string> which) =>
        Spellings.Any(pair => Regex.IsMatch(text, $@"\b{which(pair)}", RegexOptions.IgnoreCase));

    [Fact]
    public void English_IsUSEnglish_AndBritishFollowsIt()
    {
        var library = new LanguageLibrary(_dir);

        Assert.Equal(LanguageLibrary.EnglishId, library.Languages[0].Id);
        Assert.Equal("English (United States)", library.Languages[0].DisplayName);

        var british = library.Languages[1];
        Assert.Equal(BritishId, british.Id);
        Assert.True(british.IsBuiltIn);
        Assert.Equal("English (United Kingdom)", british.DisplayName);
    }

    [Fact]
    public void FollowingWindows_IsBritishOnlyForBritishEnglish()
    {
        var library = new LanguageLibrary(_dir);

        Assert.Equal(BritishId, library.Resolve(null, CultureInfo.GetCultureInfo("en-GB")));
        Assert.Equal(LanguageLibrary.EnglishId, library.Resolve(null, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(LanguageLibrary.EnglishId, library.Resolve(null, CultureInfo.GetCultureInfo("en-AU")));
        Assert.Equal(LanguageLibrary.EnglishId, library.Resolve(null, CultureInfo.GetCultureInfo("ja-JP")));
    }

    [Fact]
    public void USEnglish_HasNoBritishSpellings()
    {
        var british = LanguageLibrary.English.Keys
            .Where(key => Spells(LanguageLibrary.English.Get(key), pair => pair.British))
            .Order()
            .ToList();

        Assert.True(british.Count == 0, "Spelled the British way in en.json: " + string.Join(", ", british));
    }

    [Fact]
    public void BritishEnglish_RespellsEveryUSSpelling()
    {
        var english = LanguageLibrary.English;
        var british = new LanguageLibrary(_dir).Load(BritishId);

        Assert.Empty(british.Rejected);

        var missed = english.Keys
            .Where(key => Spells(english.Get(key), pair => pair.Us) && Spells(british.Get(key), pair => pair.Us))
            .Order()
            .ToList();

        Assert.True(missed.Count == 0, "Spelled the US way in en-GB.json, or missing from it: " + string.Join(", ", missed));
    }

    [Fact]
    public void BritishEnglish_GivesOnlyWhatItChanges()
    {
        var path = Path.Combine(SourceRoot, "Localization", BritishId + ".json");
        using var document = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

        var same = document.RootElement.GetProperty("strings").EnumerateObject()
            .Where(entry => entry.Value.GetString() == LanguageLibrary.English.Get(entry.Name))
            .Select(entry => entry.Name)
            .ToList();

        Assert.True(same.Count == 0, "The same in en-GB.json as in en.json: " + string.Join(", ", same));
    }

    // ---- XAML ----------------------------------------------------------------------

    [Fact]
    public void ALocalizedReadout_FormatsItsValue_AndItsPlural()
    {
        OnStaThread(() =>
        {
            var text = new TextBlock();
            Localized.SetKey(text, "Settings.Size.Influence.Value");

            Localized.SetValue(text, 1.0);
            Assert.Equal("1 icon", text.Text);

            Localized.SetValue(text, 4.0);
            Assert.Equal("4 icons", text.Text);
        });
    }

    private static void OnStaThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                failure = e;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }
}
