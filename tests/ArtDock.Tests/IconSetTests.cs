using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.IconSets;
using ArtDock.Packs;

namespace ArtDock.Tests;

/// <summary>
/// Covers icon sets: which image a pin gets, and what a set is not allowed to do.
/// </summary>
/// <remarks>
/// <para>
/// The matching is the part a set's author depends on — a set that says "every folder looks
/// like this, and this one folder looks like that" has to get both right — and the refusals
/// are the part the user depends on. A set is a folder anyone can write a file into, so its
/// image paths are held to that folder, and to PNG, before anything is read.
/// </para>
/// <para>Every set is built in a temporary folder; the user's own sets are never read.</para>
/// </remarks>
public class IconSetTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ArtDockTests", Guid.NewGuid().ToString("n"));

    public IconSetTests() => Directory.CreateDirectory(_dir);

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

    /// <summary>Writes a set to disk: its manifest's icons, and an empty file for each image named.</summary>
    private string MakeSet(string icons, string id = "test", params string[] images)
    {
        var folder = Path.Combine(_dir, id);
        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, PackLocations.ManifestFileName), $$"""
            { "format": 1, "kind": "iconSet", "id": "{{id}}", "name": "Test set", "icons": [ {{icons}} ] }
            """);

        foreach (var image in images)
        {
            File.WriteAllBytes(Path.Combine(folder, image), []);
        }

        return folder;
    }

    private static IconSet Load(string folder)
    {
        var (set, problem, _) = IconSet.Load(folder);
        Assert.True(set is not null, problem);
        return set!;
    }

    private static string? Picked(IconSet set, IconSubject subject) =>
        set.FileFor(subject) is { } file ? Path.GetFileName(file) : null;

    // ---- matching -------------------------------------------------------------------

    [Fact]
    public void AnExecutable_IsMatchedByName_WhereverItIs()
    {
        var set = Load(MakeSet("""{ "match": { "exe": "notepad.exe" }, "file": "n.png" }""", images: "n.png"));

        Assert.Equal("n.png", Picked(set, new IconSubject(@"C:\Windows\System32\NOTEPAD.EXE", null, null)));
        Assert.Null(Picked(set, new IconSubject(@"C:\Windows\System32\calc.exe", null, null)));
    }

    [Fact]
    public void AShortcut_IsMatchedByWhatItRuns()
    {
        var set = Load(MakeSet("""{ "match": { "exe": "chrome.exe" }, "file": "c.png" }""", images: "c.png"));

        Assert.Equal("c.png", Picked(set, new IconSubject(@"C:\Links\Chrome.lnk", @"C:\Apps\chrome.exe", null)));
    }

    [Fact]
    public void AStoreApp_IsMatchedByItsId()
    {
        var set = Load(MakeSet("""{ "match": { "aumid": "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" }, "file": "c.png" }""", images: "c.png"));

        Assert.Equal("c.png", Picked(set, new IconSubject(null, null, "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")));
    }

    [Fact]
    public void APlace_IsMatchedByItsTarget()
    {
        var set = Load(MakeSet("""{ "match": { "target": "shell:MyComputerFolder" }, "file": "pc.png" }""", images: "pc.png"));

        Assert.Equal("pc.png", Picked(set, new IconSubject("shell:MyComputerFolder", null, null)));
    }

    [Fact]
    public void TheMostSpecificMatch_Wins_WhateverTheOrder()
    {
        var set = Load(MakeSet(
            """
            { "match": { "folder": true }, "file": "any.png" },
            { "match": { "path": "C:\\Work" }, "file": "work.png" }
            """,
            images: ["any.png", "work.png"]));

        Assert.Equal("work.png", Picked(set, new IconSubject(@"C:\Work\", null, null, IsFolder: true)));
        Assert.Equal("any.png", Picked(set, new IconSubject(@"C:\Other", null, null, IsFolder: true)));
    }

    [Fact]
    public void AmongEqualMatches_TheFirstListedWins()
    {
        var set = Load(MakeSet(
            """
            { "match": { "exe": "a.exe" }, "file": "first.png" },
            { "match": { "exe": "a.exe" }, "file": "second.png" }
            """,
            images: ["first.png", "second.png"]));

        Assert.Equal("first.png", Picked(set, new IconSubject(@"C:\a.exe", null, null)));
    }

    [Fact]
    public void TheRecycleBin_IsDrawnByState_WhenTheSetSaysSo()
    {
        var set = Load(MakeSet(
            """
            { "match": { "target": "shell:RecycleBinFolder" }, "file": "bin.png" },
            { "match": { "target": "shell:RecycleBinFolder", "state": "full" }, "file": "full.png" }
            """,
            images: ["bin.png", "full.png"]));

        Assert.True(set.MatchesRecycleBinState);
        Assert.Equal("full.png", Picked(set, new IconSubject("shell:RecycleBinFolder", null, null, RecycleBinEmpty: false)));
        Assert.Equal("bin.png", Picked(set, new IconSubject("shell:RecycleBinFolder", null, null, RecycleBinEmpty: true)));
    }

    [Fact]
    public void ADocument_IsMatchedByType()
    {
        var set = Load(MakeSet("""{ "match": { "extension": "pdf" }, "file": "pdf.png" }""", images: "pdf.png"));

        Assert.Equal("pdf.png", Picked(set, new IconSubject(@"C:\Docs\report.PDF", null, null)));
    }

    // ---- what a set may not do ------------------------------------------------------

    [Theory]
    [InlineData("""{ "match": { "exe": "a.exe" }, "file": "..\\outside.png" }""")]
    [InlineData("""{ "match": { "exe": "a.exe" }, "file": "C:\\Windows\\outside.png" }""")]
    [InlineData("""{ "match": { "exe": "a.exe" }, "file": "\\\\server\\share\\outside.png" }""")]
    [InlineData("""{ "match": { "exe": "a.exe" }, "file": "script.ps1" }""")]
    [InlineData("""{ "match": { "exe": "a.exe" }, "file": "missing.png" }""")]
    [InlineData("""{ "match": { }, "file": "ok.png" }""")]
    [InlineData("""{ "match": { "state": "full" }, "file": "ok.png" }""")]
    public void AnImageItCannotUse_IsDropped(string rule)
    {
        // Something outside the set's folder that a careless check would accept.
        File.WriteAllBytes(Path.Combine(_dir, "outside.png"), []);

        var folder = MakeSet(
            rule + """, { "match": { "exe": "b.exe" }, "file": "ok.png" }""",
            images: ["ok.png", "script.ps1"]);

        var (set, problem, skipped) = IconSet.Load(folder);

        Assert.Null(problem);
        Assert.Equal(1, set!.Count);
        Assert.Single(skipped);
        Assert.Null(set.FileFor(new IconSubject(@"C:\a.exe", null, null)));
    }

    [Fact]
    public void ASetWithNothingUsable_IsRefused()
    {
        var (set, problem, _) = IconSet.Load(MakeSet("""{ "match": { "exe": "a.exe" }, "file": "missing.png" }"""));

        Assert.Null(set);
        Assert.NotNull(problem);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("has space")]
    [InlineData("")]
    public void AnIdThatIsNotSafe_IsRefused(string id)
    {
        var folder = Path.Combine(_dir, "set");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "a.png"), []);
        File.WriteAllText(Path.Combine(folder, PackLocations.ManifestFileName), $$"""
            { "format": 1, "kind": "iconSet", "id": "{{id}}", "name": "x",
              "icons": [ { "match": { "exe": "a.exe" }, "file": "a.png" } ] }
            """);

        var (set, problem, _) = IconSet.Load(folder);

        Assert.Null(set);
        Assert.NotNull(problem);
    }

    [Fact]
    public void TheLibrary_ListsGoodSets_AndReportsBadOnes()
    {
        MakeSet("""{ "match": { "exe": "a.exe" }, "file": "a.png" }""", id: "good", images: "a.png");
        MakeSet("""{ "match": { "exe": "a.exe" }, "file": "gone.png" }""", id: "bad");

        var library = new IconSetLibrary(_dir);

        Assert.Equal("good", Assert.Single(library.Sets).Id);
        Assert.NotNull(library.Find("GOOD"));
        Assert.Equal("bad", Assert.Single(library.Problems).Folder);
    }

    [Fact]
    public void TheKey_ChangesWhenAnImageIsEdited()
    {
        var folder = MakeSet("""{ "match": { "exe": "a.exe" }, "file": "a.png" }""", images: "a.png");
        var before = Load(folder).Key;

        File.SetLastWriteTimeUtc(Path.Combine(folder, "a.png"), DateTime.UtcNow.AddMinutes(5));

        Assert.NotEqual(before, Load(folder).Key);
    }

    // ---- images ---------------------------------------------------------------------

    [Fact]
    public void AnImage_IsDecoded_AndTheFileLetGo()
    {
        var folder = MakeSet("""{ "match": { "exe": "a.exe" }, "file": "a.png" }""");
        WritePng(Path.Combine(folder, "a.png"), 512);

        OnStaThread(() =>
        {
            var set = Load(folder);
            var image = set.IconFor(new IconSubject(@"C:\a.exe", null, null)) as BitmapSource;

            Assert.NotNull(image);

            // Scaled down to what the dock can use, never up.
            Assert.True(image!.PixelWidth <= 384);
        });

        // Not held open: a set can be edited or removed while it is in use.
        File.Delete(Path.Combine(folder, "a.png"));
    }

    private static void WritePng(string path, int size)
    {
        var pixels = new byte[size * size * 4];
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
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
