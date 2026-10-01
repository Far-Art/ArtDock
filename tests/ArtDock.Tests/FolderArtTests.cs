using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.Dock;
using ArtDock.IconSets;
using ArtDock.Interop;
using ArtDock.Packs;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers the folders the dock draws: the design it reads them from, what it draws for a colour,
/// a symbol or text and the symbol's tone, where they stand against the other icons, and which
/// pins are offered one.
/// </summary>
/// <remarks>
/// <para>
/// The design is a file built into the program, read the first time a folder is drawn, and it
/// refuses anything it would not draw as a browser does. So the first test is the one that
/// meets a refusal — here, rather than on somebody's dock.
/// </para>
/// <para>
/// Everything written to disk goes in a temporary folder; the user's own settings and sets are
/// never read.
/// </para>
/// </remarks>
public class FolderArtTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ArtDockTests", Guid.NewGuid().ToString("n"));

    public FolderArtTests() => Directory.CreateDirectory(_dir);

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

    /// <summary>Windows' own folder yellow, which a drawn folder starts in.</summary>
    private static readonly Color WindowsYellow = Color.FromRgb(0xFF, 0xBE, 0x16);

    private static readonly Color Blue = Color.FromRgb(0x3A, 0x8D, 0xF0);

    private static BitmapSource Drawn(Color color, string? glyph = null, string? text = null, FolderSymbolTone tone = FolderSymbolTone.Toned) =>
        FolderArt.Draw(FolderArt.Look(color, glyph, text, tone));

    // ---- the design -----------------------------------------------------------------

    [Fact]
    public void TheDesign_IsReadWhole_AndStartsFromWindowsOwnYellow() => OnStaThread(() =>
    {
        Assert.Equal(WindowsYellow, FolderArt.DefaultColor);

        // The editor's swatches lead with it, so a folder's first colour is one it can pick again.
        Assert.Equal(FolderArt.FormatColor(FolderArt.DefaultColor), FolderArt.Swatches[0]);

        var drawn = Drawn(FolderArt.DefaultColor);
        Assert.Equal(FolderArt.PixelSize, drawn.PixelWidth);
        Assert.Equal(FolderArt.PixelSize, drawn.PixelHeight);
    });

    /// <summary>
    /// Beside Windows' own folders on the dock, ours has to sit at their height. Drawn first a
    /// little low and a little small, it was seen to sit under the Downloads folder next to it —
    /// so it is held to the box the shell's own folder icon fills, asked of the shell at the size
    /// the dock asks for. Every folder icon of Windows' measured fills the same one.
    /// </summary>
    [Fact]
    public void TheFolder_FillsTheBoxWindowsOwnFoldersFill() => OnStaThread(() =>
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "plain")).FullName;
        var windows = ShellIcons.Load(folder, FolderArt.PixelSize) as BitmapSource;
        Assert.NotNull(windows);

        var theirs = InkBox(windows!);
        var ours = InkBox(Drawn(Blue));

        // To the pixel, give or take the edge's antialiasing.
        Assert.InRange(ours.Left, theirs.Left - 1, theirs.Left + 1);
        Assert.InRange(ours.Right, theirs.Right - 1, theirs.Right + 1);
        Assert.InRange(ours.Top, theirs.Top - 1, theirs.Top + 1);
        Assert.InRange(ours.Bottom, theirs.Bottom - 1, theirs.Bottom + 1);
    });

    [Fact]
    public void AFolder_HasItsColourAtTheBack_AndTheSameLightenedInFront() => OnStaThread(() =>
    {
        var red = Color.FromRgb(0xE5, 0x48, 0x4D);
        var drawn = Drawn(red);

        // A corner is outside the folder altogether.
        Assert.Equal(0, Pixel(drawn, 1, 1).A);

        // The tab, at the top left, where the shade laid over the back has barely begun.
        var back = Pixel(drawn, 20, 20);
        Assert.Equal(255, back.A);
        Assert.InRange((int)back.R, red.R - 12, red.R + 12);
        Assert.InRange((int)back.G, red.G - 12, red.G + 12);
        Assert.InRange((int)back.B, red.B - 12, red.B + 12);

        // The front: the same red, lighter, as Windows' folders are lit.
        var front = Pixel(drawn, 64, 90);
        Assert.Equal(255, front.A);
        Assert.True(front.R > front.G && front.R > front.B, $"the front is not red: {front}");
        Assert.True(Luminance(front) > Luminance(back), "the front is not the lighter");
    });

    /// <summary>
    /// The shrink from the drawing at four times the size is an average, not a sample. Shrunk
    /// by drawing again smaller, RenderTargetBitmap read 4 of every 16 pixels whatever scaling
    /// mode it was given, and edges came out in steps: a slanted edge went from opaque straight
    /// to clear in row after row. Averaged, the pixel a slanted edge crosses is part covered in
    /// every row it crosses — here the tab's slope.
    /// </summary>
    [Fact]
    public void ASlantedEdge_IsSmoothedInEveryRow() => OnStaThread(() =>
    {
        var pixels = Pixels(Drawn(Blue));

        for (var y = 18; y <= 28; y++)
        {
            var row = Enumerable.Range(40, 31).Select(x => pixels[x, y].A).ToList();
            Assert.True(
                row.Any(alpha => alpha is >= 24 and <= 231),
                $"row {y} of the tab's slope steps from opaque to clear: {string.Join(" ", row)}");
        }
    });

    // ---- the symbol, and text in its place ------------------------------------------------

    [Fact]
    public void ASymbol_IsOnTheFront_InADeeperToneOfTheFolder() => OnStaThread(() =>
    {
        var plain = Pixels(Drawn(Blue));
        var marked = Pixels(Drawn(Blue, "\uE896"));
        var changed = Changed(plain, marked);

        Assert.NotEmpty(changed);

        // Within the place the design gives it (70-186 by 78-194 of 256, a little more for the
        // outline), and nowhere else.
        Assert.All(changed, point =>
        {
            Assert.InRange(point.X, 33, 95);
            Assert.InRange(point.Y, 37, 99);
        });

        // Tone-on-tone: the deepest of it is the folder's own blue, darker than the front.
        var deepest = changed.Select(point => marked[point.X, point.Y]).MinBy(Luminance);
        Assert.True(deepest.B > deepest.R && deepest.B > deepest.G, $"the symbol is not blue: {deepest}");
        Assert.True(Luminance(deepest) < Luminance(plain[64, 68]) * 0.8, "the symbol is not darker than the front");
    });

    [Fact]
    public void AWhiteOrBlackSymbol_IsWhiteOrBlack() => OnStaThread(() =>
    {
        var plain = Pixels(Drawn(Blue));

        var white = Pixels(Drawn(Blue, "\uE735", tone: FolderSymbolTone.White));
        var lightest = Changed(plain, white).Select(point => white[point.X, point.Y]).MaxBy(Luminance);
        Assert.True(lightest.R > 240 && lightest.G > 240 && lightest.B > 240, $"the white symbol is not white: {lightest}");

        var black = Pixels(Drawn(Blue, "\uE735", tone: FolderSymbolTone.Black));
        var darkest = Changed(plain, black).Select(point => black[point.X, point.Y]).MinBy(Luminance);
        Assert.True(Luminance(darkest) < 60, $"the black symbol is not black: {darkest}");
    });

    /// <summary>
    /// A symbol's lines are as heavy as the lines on Windows' own folders: 10 of the 256 units,
    /// where Windows' Downloads arrow measured 9.9 and the Documents folder's lines 10. The font
    /// draws them 7.25 at the symbol's size and the design's outline makes up the rest \u2014 5 at
    /// first, which beside Windows' Downloads read as the heavier.
    /// </summary>
    [Fact]
    public void ASymbolsLines_AreAsHeavyAsTheLinesOnWindowsOwnFolders() => OnStaThread(() =>
    {
        var plain = Pixels(Drawn(Blue));
        var marked = Pixels(Drawn(Blue, "\uE896", tone: FolderSymbolTone.Black));

        // Across the arrow's stem, 100 units down: how many pixels' worth of the symbol, each
        // pixel counted by how far it has gone from the front towards the stem's middle.
        const int y = 50;
        var middle = Enumerable.Range(54, 21).MinBy(x => Luminance(marked[x, y]));
        var full = plain[middle, y].G - marked[middle, y].G;
        var width = Enumerable.Range(middle - 10, 21).Sum(x => (plain[x, y].G - marked[x, y].G) / (double)full);

        Assert.InRange(width * 256 / FolderArt.PixelSize, 9.4, 10.6);
    });

    /// <summary>
    /// A white symbol casts the shadow the white symbols on Windows' own folders cast: a soft dark
    /// ring round it, deepest at its edge and gone a few pixels out. A toned or black one casts
    /// none, as the toned person on Windows' User folder casts none \u2014 it changes only what it
    /// covers.
    /// </summary>
    [Fact]
    public void AWhiteSymbol_CastsAShadow_AndATonedOrBlackOneNone() => OnStaThread(() =>
    {
        var plain = Pixels(Drawn(Blue));
        var covered = Changed(plain, Pixels(Drawn(Blue, "\uE896"))).ToHashSet();
        var edge = Grow(covered);

        var black = Pixels(Drawn(Blue, "\uE896", tone: FolderSymbolTone.Black));
        Assert.All(Changed(plain, black), point => Assert.Contains(point, edge));

        var white = Pixels(Drawn(Blue, "\uE896", tone: FolderSymbolTone.White));
        var ring = Changed(plain, white).Where(point => !edge.Contains(point)).ToList();
        Assert.NotEmpty(ring);
        Assert.All(ring, point => Assert.True(
            Luminance(white[point.X, point.Y]) < Luminance(plain[point.X, point.Y]),
            $"the shadow lightens the front at {point}"));

        // Leftwards from the arrow's stem, 100 units down: deepest beside it, fading, and gone.
        const int y = 50;
        var stem = covered.Where(point => point.Y == y).Min(point => point.X);
        double Darkening(int x) => 1 - (Luminance(white[x, y]) / Luminance(plain[x, y]));

        Assert.InRange(Darkening(stem - 1), 0.04, 0.15);
        Assert.True(Darkening(stem - 1) > Darkening(stem - 3), "the shadow does not fade");
        Assert.True(Darkening(stem - 3) > Darkening(stem - 5), "the shadow does not fade");
        Assert.True(Distance(plain[stem - 10, y], white[stem - 10, y]) <= 2, "the shadow reaches 20 units out");
    });

    [Fact]
    public void Text_TakesTheSymbolsPlace_InsideItsBox() => OnStaThread(() =>
    {
        // The same picture whether a symbol was stored alongside or not: text wins.
        Assert.Same(FolderArt.For("#3A8DF0", null, "2024"), FolderArt.For("#3A8DF0", "E896", "2024"));

        var plain = Pixels(Drawn(Blue));
        var lettered = Pixels(Drawn(Blue, text: "PHOTOS"));
        var changed = Changed(plain, lettered);

        Assert.NotEmpty(changed);

        // Within its box (36-220 by 100-172 of 256), a little more for the outline.
        Assert.All(changed, point =>
        {
            Assert.InRange(point.X, 16, 112);
            Assert.InRange(point.Y, 48, 88);
        });

        // Six letters run the width of the box; one letter is held to its height instead.
        var wide = changed.Max(point => point.X) - changed.Min(point => point.X);
        Assert.True(wide > 80, $"six letters span only {wide} px");

        var single = Changed(plain, Pixels(Drawn(Blue, text: "A")));
        var tall = single.Max(point => point.Y) - single.Min(point => point.Y);
        Assert.InRange(tall, 20, 40);
    });

    [Fact]
    public void AStoredText_IsTrimmed_AndHeldToSixCharacters()
    {
        Assert.Null(FolderArt.ParseText(null));
        Assert.Null(FolderArt.ParseText("   "));
        Assert.Equal("2024", FolderArt.ParseText(" 2024 "));
        Assert.Equal("ABCDEF", FolderArt.ParseText("ABCDEFGH"));

        // Never cut through a character that takes two: a face as the sixth would be half of one.
        Assert.Equal("ABCDE", FolderArt.ParseText("ABCDE\U0001F600"));
    }

    [Fact]
    public void AStoredTone_RoundTrips_AndAnythingElseIsToned()
    {
        foreach (var tone in Enum.GetValues<FolderSymbolTone>())
        {
            Assert.Equal(tone, FolderArt.ParseTone(FolderArt.FormatTone(tone)));
        }

        Assert.Null(FolderArt.FormatTone(FolderSymbolTone.Toned));
        Assert.Equal(FolderSymbolTone.White, FolderArt.ParseTone("white"));
        Assert.Equal(FolderSymbolTone.Toned, FolderArt.ParseTone("purple"));
        Assert.Equal(FolderSymbolTone.Toned, FolderArt.ParseTone("7"));
    }

    // ---- what a pin stores ------------------------------------------------------------

    [Fact]
    public void TheSameLook_IsDrawnOnce_AndShared() => OnStaThread(() =>
    {
        var first = FolderArt.For("#3A8DF0", "E896");

        Assert.NotNull(first);
        Assert.Same(first, FolderArt.For("#3a8df0", "e896"));
        Assert.NotSame(first, FolderArt.For("#3A8DF0", null));
        Assert.NotSame(first, FolderArt.For("#3A8DF1", "E896"));
        Assert.NotSame(first, FolderArt.For("#3A8DF0", "E896", tone: "White"));

        // A tone with nothing to paint is no different a look.
        Assert.Same(FolderArt.For("#3A8DF0", null), FolderArt.For("#3A8DF0", null, tone: "Black"));
    });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void APinWithNoColour_KeepsTheShellsIcon(string? color) => OnStaThread(() =>
    {
        Assert.Null(FolderArt.For(color, "E896"));
    });

    [Fact]
    public void AStoredColour_IsReadAsTheBarsIs_AndOneThatCannotBeIsStillDrawn() => OnStaThread(() =>
    {
        Assert.Null(FolderArt.ParseColor(null));
        Assert.Equal(Color.FromRgb(0x10, 0x20, 0x30), FolderArt.ParseColor("#102030"));

        // Alpha is dropped, as the bar's is: a folder is drawn opaque.
        Assert.Equal(Color.FromRgb(0x10, 0x20, 0x30), FolderArt.ParseColor("#80102030"));

        // Somebody chose to draw this folder; a mangled colour is not a reason to stop.
        Assert.Equal(FolderArt.DefaultColor, FolderArt.ParseColor("not a colour"));
        Assert.Equal("#102030", FolderArt.FormatColor(Color.FromRgb(0x10, 0x20, 0x30)));
    });

    [Fact]
    public void TheHexBox_ReadsAColourAsItIsTyped_AndNothingBefore() => OnStaThread(() =>
    {
        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x00), FolderArt.TryParseColor("#FF0000"));

        // Without its hash, as hex is often copied.
        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x00), FolderArt.TryParseColor("ff0000"));
        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x00), FolderArt.TryParseColor("#F00"));

        // Half-typed: nothing yet, rather than the default.
        Assert.Null(FolderArt.TryParseColor("#FF00F"));
        Assert.Null(FolderArt.TryParseColor("#"));
        Assert.Null(FolderArt.TryParseColor(""));
    });

    [Fact]
    public void AStoredSymbol_RoundTrips_AndNothingButASymbolIsRead()
    {
        foreach (var symbol in FolderArt.Symbols)
        {
            Assert.Equal(symbol.Glyph, FolderArt.ParseSymbol(symbol.Code));
        }

        Assert.Equal("E896", FolderArt.FormatSymbol("\uE896"));
        Assert.Equal("\uE896", FolderArt.ParseSymbol(" e896 "));

        // Only the private-use area the symbol fonts draw in: a letter is not a symbol.
        Assert.Null(FolderArt.ParseSymbol("0041"));
        Assert.Null(FolderArt.ParseSymbol("not hex"));
        Assert.Null(FolderArt.ParseSymbol(null));
    }

    // ---- the choices offered ------------------------------------------------------------

    public static TheoryData<string> SymbolFonts => ["Segoe Fluent Icons", "Segoe MDL2 Assets"];

    /// <summary>
    /// As for the menus' glyphs: a code point a font does not have draws as nothing, or as a box,
    /// and nothing fails. Both fonts the theme's symbol font names are held to it.
    /// </summary>
    [Theory]
    [MemberData(nameof(SymbolFonts))]
    public void EverySymbol_IsInTheFont(string font)
    {
        var typeface = new Typeface(font);
        Assert.True(typeface.TryGetGlyphTypeface(out var glyphs), $"{font} is not installed");

        // With the editor's own tile for no symbol, which is drawn in the same font.
        var missing = FolderArt.Symbols
            .Select(symbol => symbol.Glyph)
            .Append(FolderArt.NoSymbolGlyph)
            .Where(glyph => !glyphs.CharacterToGlyphMap.ContainsKey(glyph[0]))
            .Select(FolderArt.FormatSymbol)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EverySymbol_IsOfferedOnce()
    {
        Assert.Equal(FolderArt.Symbols.Count, FolderArt.Symbols.Select(symbol => symbol.Glyph).Distinct().Count());
        Assert.Equal(FolderArt.Symbols.Count, FolderArt.Symbols.Select(symbol => symbol.NameKey).Distinct().Count());
    }

    /// <summary>
    /// A swatch that reads as another one is a swatch wasted: a second blue barely apart from the
    /// first was asked to be taken out. None is within 55 of another, measured straight across RGB.
    /// </summary>
    [Fact]
    public void NoTwoSwatches_ReadAsOne() => OnStaThread(() =>
    {
        var colours = FolderArt.Swatches.Select(hex => FolderArt.ParseColor(hex)!.Value).ToList();

        for (var i = 0; i < colours.Count; i++)
        {
            for (var j = i + 1; j < colours.Count; j++)
            {
                var (a, b) = (colours[i], colours[j]);
                var distance = Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));
                Assert.True(distance >= 55, $"{FolderArt.Swatches[i]} and {FolderArt.Swatches[j]} are {distance:F0} apart");
            }
        }

        // And one of them is all but white, and one all but black.
        Assert.Contains(colours, colour => colour.R > 220 && colour.G > 220 && colour.B > 220);
        Assert.Contains(colours, colour => colour.R < 40 && colour.G < 40 && colour.B < 40);
    });

    // ---- against the other icons ----------------------------------------------------------

    [Fact]
    public void ADrawnFolder_WinsOverTheIconSet_AndAChosenImageOverIt() => OnStaThread(() =>
    {
        var target = Directory.CreateDirectory(Path.Combine(_dir, "Projects")).FullName;
        var set = MakeSet();
        var drawn = FolderArt.For("#E5484D", null);

        var coloured = new DockItem { Id = "a", Label = "Projects", TargetPath = target, FolderColor = "#E5484D" };
        Assert.Same(drawn, PinnedAppsService.LoadIcon(coloured, set));

        // Without a colour the set has it, as it has every folder.
        var plain = new DockItem { Id = "b", Label = "Projects", TargetPath = target };
        var fromSet = PinnedAppsService.LoadIcon(plain, set);
        Assert.NotNull(fromSet);
        Assert.NotSame(drawn, fromSet);

        // And an image chosen for the one item beats the folder chosen for it.
        var image = Path.Combine(_dir, "chosen.png");
        WritePng(image, 64);
        var chosen = new DockItem { Id = "c", Label = "Projects", TargetPath = target, FolderColor = "#E5484D", IconPath = image };
        var shown = PinnedAppsService.LoadIcon(chosen, set);
        Assert.NotNull(shown);
        Assert.NotSame(drawn, shown);
    });

    /// <summary>
    /// The reason nothing is written to disk: a folder carried in by an import — or left in the
    /// settings by an earlier install — is drawn from the file alone, and there is no picture
    /// beside it that could have gone missing. Here the folder does not even exist.
    /// </summary>
    [Fact]
    public void AnImportedFolder_IsDrawnFromItsSettingsAlone() => OnStaThread(() =>
    {
        var path = Path.Combine(_dir, "imported.json");
        File.WriteAllText(path, """
            {
              "Version": 1,
              "PinnedApps": [
                { "Id": "a", "Label": "Projects", "TargetPath": "Z:\\Nowhere\\Projects", "FolderColor": "#3DAF4E", "FolderSymbol": "E943", "FolderSymbolTone": "White" },
                { "Id": "b", "Label": "Year", "TargetPath": "Z:\\Nowhere\\2024", "FolderColor": "#EC5FA6", "FolderText": "2024" }
              ]
            }
            """);

        var pins = SettingsStore.Import(path).PinnedApps;
        Assert.Equal(2, pins.Count);
        Assert.Equal("White", pins[0].FolderSymbolTone);
        Assert.Equal("2024", pins[1].FolderText);

        Assert.Same(FolderArt.For("#3DAF4E", "E943", null, "White"), PinnedAppsService.LoadIcon(PinnedAppsService.ToDockItem(pins[0])));
        Assert.Same(FolderArt.For("#EC5FA6", null, "2024"), PinnedAppsService.LoadIcon(PinnedAppsService.ToDockItem(pins[1])));
    });

    [Fact]
    public void ExportThenImport_KeepsAFoldersLook()
    {
        var path = Path.Combine(_dir, "export.json");
        SettingsStore.Export(new DockSettings
        {
            PinnedApps =
            [
                new PinnedAppSetting { Id = "a", Label = "Music", TargetPath = @"C:\Music", FolderColor = "#EC5FA6", FolderSymbol = "E8D6", FolderSymbolTone = "Black" },
                new PinnedAppSetting { Id = "b", Label = "Tax", TargetPath = @"C:\Tax", FolderColor = "#7D8796", FolderText = "TAX" }
            ]
        }, path);

        var pins = SettingsStore.Import(path).PinnedApps;
        Assert.Equal("#EC5FA6", pins[0].FolderColor);
        Assert.Equal("E8D6", pins[0].FolderSymbol);
        Assert.Equal("Black", pins[0].FolderSymbolTone);
        Assert.Equal("TAX", pins[1].FolderText);
    }

    /// <summary>
    /// The settings dialog's item list shows every item's icon beside its name — the one the
    /// dock draws, with the set chosen in the dialog — and nothing on a separator's row.
    /// </summary>
    [Fact]
    public void TheItemList_ShowsEveryItemsIcon() => OnStaThread(() =>
    {
        var converter = new ArtDock.Views.ItemRowIconConverter();
        object? Row(PinnedAppSetting pin, IconSet? set = null) =>
            converter.Convert([pin, set], typeof(ImageSource), null, System.Globalization.CultureInfo.InvariantCulture);

        var folder = Directory.CreateDirectory(Path.Combine(_dir, "Work")).FullName;
        var file = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(file, "x");

        var notes = new PinnedAppSetting { Id = "b", Label = "Notes", TargetPath = file };
        Assert.NotNull(Row(new PinnedAppSetting { Id = "a", Label = "Work", TargetPath = folder }));
        Assert.NotNull(Row(notes));
        Assert.Same(PinnedAppsService.LoadIcon(PinnedAppsService.ToDockItem(notes)), Row(notes));
        Assert.Null(Row(new PinnedAppSetting { Id = "d", Label = "Separator", IsSeparator = true }));

        // A customized folder is its drawn self, wherever it points — even where nothing is.
        var drawn = new PinnedAppSetting { Id = "e", Label = "Old", TargetPath = @"Z:\Nowhere", FolderColor = "#9B6BF2", FolderSymbol = "E735" };
        Assert.Same(FolderArt.For("#9B6BF2", "E735"), Row(drawn));

        // And with a set chosen, a folder that is not customized shows the set's folder.
        var set = MakeSet();
        var plain = new PinnedAppSetting { Id = "f", Label = "Work", TargetPath = folder };
        Assert.Same(
            PinnedAppsService.LoadIcon(PinnedAppsService.ToDockItem(plain), set),
            Row(plain, set));
        Assert.NotSame(Row(plain), Row(plain, set));
    });

    /// <summary>
    /// The mark at the far end of a row is on exactly the items the editor offers to draw as a
    /// folder: a folder on disk, by path or <c>shell:</c> name, and one already drawn wherever it
    /// points — and on nothing else, whatever its icon looks like.
    /// </summary>
    [Fact]
    public void TheItemList_MarksTheFolders_AndNothingElse() => OnStaThread(() =>
    {
        var converter = new ArtDock.Views.FolderRowMarkConverter();
        Visibility Mark(PinnedAppSetting pin) =>
            (Visibility)converter.Convert(pin, typeof(Visibility), null, System.Globalization.CultureInfo.InvariantCulture);

        var folder = Directory.CreateDirectory(Path.Combine(_dir, "Marked")).FullName;
        var file = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(file, "x");

        Assert.Equal(Visibility.Visible, Mark(new PinnedAppSetting { Id = "a", Label = "Marked", TargetPath = folder }));
        Assert.Equal(Visibility.Visible, Mark(new PinnedAppSetting { Id = "b", Label = "Downloads", TargetPath = "shell:Downloads" }));
        Assert.Equal(Visibility.Visible, Mark(new PinnedAppSetting { Id = "c", Label = "Old", TargetPath = @"Z:\Nowhere", FolderColor = "#9B6BF2" }));

        Assert.Equal(Visibility.Collapsed, Mark(new PinnedAppSetting { Id = "d", Label = "Notes", TargetPath = file }));
        Assert.Equal(Visibility.Collapsed, Mark(new PinnedAppSetting { Id = "e", Label = "This PC", TargetPath = "shell:MyComputerFolder" }));
        Assert.Equal(Visibility.Collapsed, Mark(new PinnedAppSetting { Id = "f", Label = "Calculator", Aumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" }));
        Assert.Equal(Visibility.Collapsed, Mark(new PinnedAppSetting { Id = "g", Label = "Separator", IsSeparator = true }));
    });

    /// <summary>The mark is drawn in the theme's symbol font, so it is held to both, as the symbols are.</summary>
    [Theory]
    [MemberData(nameof(SymbolFonts))]
    public void TheFolderMark_IsInTheFont(string font)
    {
        var typeface = new Typeface(font);
        Assert.True(typeface.TryGetGlyphTypeface(out var glyphs), $"{font} is not installed");
        Assert.True(glyphs.CharacterToGlyphMap.ContainsKey(ArtDock.Views.FolderRowMarkConverter.Glyph[0]));
    }

    // ---- which pins are offered one -----------------------------------------------------------

    [Fact]
    public void AFolder_IsOneOnDisk_ByPathOrByShellName() => OnStaThread(() =>
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "plain")).FullName;
        var file = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(file, "x");
        var zip = Path.Combine(_dir, "archive.zip");
        ZipFile.CreateFromDirectory(folder, zip);

        Assert.True(ShellNames.IsFileSystemFolder(folder));

        // How the Add menu pins Downloads, which no Directory.Exists would recognise.
        Assert.True(ShellNames.IsFileSystemFolder("shell:Downloads"));

        Assert.False(ShellNames.IsFileSystemFolder(file));

        // Folders to the shell, but nothing on disk behind them — and a file it lets you browse.
        Assert.False(ShellNames.IsFileSystemFolder("shell:MyComputerFolder"));
        Assert.False(ShellNames.IsFileSystemFolder("shell:RecycleBinFolder"));
        Assert.False(ShellNames.IsFileSystemFolder(zip));

        Assert.False(ShellNames.IsFileSystemFolder(null));
        Assert.False(ShellNames.IsFileSystemFolder(Path.Combine(_dir, "not there")));
    });

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>A set with an image for every folder, as a set's author would write one.</summary>
    private IconSet MakeSet()
    {
        var folder = Path.Combine(_dir, "set");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, PackLocations.ManifestFileName), """
            { "format": 1, "kind": "iconSet", "id": "folders", "name": "Folders", "icons": [ { "match": { "folder": true }, "file": "f.png" } ] }
            """);
        WritePng(Path.Combine(folder, "f.png"), 256);

        var (set, problem, _) = IconSet.Load(folder);
        Assert.True(set is not null, problem);
        return set!;
    }

    /// <summary>Every pixel that differs between two drawings by more than a shade.</summary>
    private static List<(int X, int Y)> Changed(Color[,] before, Color[,] after)
    {
        var changed = new List<(int X, int Y)>();
        for (var y = 0; y < before.GetLength(1); y++)
        {
            for (var x = 0; x < before.GetLength(0); x++)
            {
                if (Distance(before[x, y], after[x, y]) > 8)
                {
                    changed.Add((x, y));
                }
            }
        }

        return changed;
    }

    /// <summary>Points and every point beside one, corners too: a shape with its edge's antialiasing.</summary>
    private static HashSet<(int X, int Y)> Grow(HashSet<(int X, int Y)> points)
    {
        var grown = new HashSet<(int X, int Y)>(points);
        foreach (var (x, y) in points)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    grown.Add((x + dx, y + dy));
                }
            }
        }

        return grown;
    }

    private static Color Pixel(BitmapSource image, int x, int y)
    {
        var bgra = new byte[4];
        image.CopyPixels(new Int32Rect(x, y, 1, 1), bgra, 4, 0);
        return Color.FromArgb(bgra[3], bgra[2], bgra[1], bgra[0]);
    }

    private static Color[,] Pixels(BitmapSource image)
    {
        var size = image.PixelWidth;
        var bgra = new byte[size * size * 4];
        image.CopyPixels(bgra, size * 4, 0);

        var pixels = new Color[size, size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var i = ((y * size) + x) * 4;
                pixels[x, y] = Color.FromArgb(bgra[i + 3], bgra[i + 2], bgra[i + 1], bgra[i]);
            }
        }

        return pixels;
    }

    /// <summary>The smallest rectangle holding every pixel more than a little opaque.</summary>
    private static (int Left, int Top, int Right, int Bottom) InkBox(BitmapSource source)
    {
        var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = image.PixelWidth, height = image.PixelHeight;
        var bgra = new byte[width * height * 4];
        image.CopyPixels(bgra, width * 4, 0);

        int left = width, top = height, right = -1, bottom = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (bgra[(((y * width) + x) * 4) + 3] > 24)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return (left, top, right, bottom);
    }

    private static int Distance(Color a, Color b) =>
        Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A)));

    private static double Luminance(Color color) => (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);

    private static void WritePng(string path, int size)
    {
        var pixels = new byte[size * size * 4];
        Array.Fill<byte>(pixels, 0xFF);
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
