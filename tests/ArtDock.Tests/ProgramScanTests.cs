using System.IO;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers the Exclusions page's Scan: walking a folder for programs, and arranging what it
/// finds into rows a person can choose from without knowing which program is the game.
/// </summary>
/// <remarks>
/// <para>
/// The case it was written for is StarCraft II, started from Battle.net. Its folder, as read
/// on this machine, holds a <c>StarCraft II.exe</c> stub at the top, the game itself as
/// <c>Versions\Base97563\SC2_x64.exe</c> and a 32-bit <c>SC2.exe</c> beside it — all three
/// describing themselves as "StarCraft II" — among editors, a switcher, a browser and an
/// error reporter. The row that matters is the one that gathers those three, whatever the
/// person ticking it thinks the game is called.
/// </para>
/// <para>
/// The walk is tested against a real folder under the temporary directory, which is made and
/// removed by each test. Nothing here reads anything of the user's.
/// </para>
/// </remarks>
public class ProgramScanTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ArtDockTests", Guid.NewGuid().ToString("n"));

    public ProgramScanTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>StarCraft II's programs and what each calls itself, as read from its folder.</summary>
    private static readonly Dictionary<string, string> StarCraft = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"E:\Games\StarCraft II\StarCraft II Editor.exe"] = "StarCraft II Editor",
        [@"E:\Games\StarCraft II\StarCraft II Editor_x64.exe"] = "StarCraft II Editor",
        [@"E:\Games\StarCraft II\StarCraft II.exe"] = "StarCraft II",
        [@"E:\Games\StarCraft II\Support\BlizzardError.exe"] = "BlizzardError (x64)",
        [@"E:\Games\StarCraft II\Support\SC2Editor.exe"] = "SC2Edit",
        [@"E:\Games\StarCraft II\Support\SC2Switcher.exe"] = "SC2Switcher",
        [@"E:\Games\StarCraft II\Support\StarWeb.exe"] = "StarWeb",
        [@"E:\Games\StarCraft II\Support\BlizzardBrowser\BlizzardBrowser.exe"] = "Blizzard Browser",
        [@"E:\Games\StarCraft II\Support64\SC2Editor_x64.exe"] = "SC2Edit",
        [@"E:\Games\StarCraft II\Support64\SC2Switcher_x64.exe"] = "SC2Switcher",
        [@"E:\Games\StarCraft II\Versions\Base97563\SC2.exe"] = "StarCraft II",
        [@"E:\Games\StarCraft II\Versions\Base97563\SC2_x64.exe"] = "StarCraft II"
    };

    private static IReadOnlyList<ScannedApp> ArrangeStarCraft() =>
        ProgramScan.Arrange(
            StarCraft.Keys.Select(path => new FoundFile(path, "StarCraft II")),
            _ => DateTime.UnixEpoch,
            path => Facts(StarCraft[path]));

    /// <summary>A program that describes itself by <paramref name="name"/> and has an icon.</summary>
    private static ProgramFacts Facts(string name) => new(name, Described: true, HasIcon: true, Size: 0);

    /// <summary>Files all found in one game's folder.</summary>
    private static IEnumerable<FoundFile> InOneGame(params string[] paths) =>
        paths.Select(path => new FoundFile(path, "The Game"));

    // ---- arranging -----------------------------------------------------------------

    [Fact]
    public void TheGameAndItsLauncher_AreOneRow()
    {
        var row = Assert.Single(ArrangeStarCraft(), app => app.Name == "StarCraft II");

        Assert.Equal(
            ["SC2.exe", "SC2_x64.exe", "StarCraft II.exe"],
            row.Programs.Select(program => program.FileName));
        Assert.False(row.IsHelper);
    }

    [Fact]
    public void PinningTheGamesRow_PinsTheStubAtTheTop_NotTheGameBelowIt()
    {
        // SC2_x64.exe is what runs, but started by hand it does not: the stub goes through
        // Battle.net, which is what starting the game means.
        var row = Assert.Single(ArrangeStarCraft(), app => app.Name == "StarCraft II");

        Assert.Equal(@"E:\Games\StarCraft II\StarCraft II.exe", ProgramScan.ToPin(row).Path);
    }

    [Fact]
    public void PinningARow_AtOneDepth_TakesTheProgramWithAnIcon_ThenTheLarger()
    {
        var facts = new Dictionary<string, ProgramFacts>
        {
            [@"C:\Game\a.exe"] = new("Game", Described: true, HasIcon: false, Size: 900),
            [@"C:\Game\b.exe"] = new("Game", Described: true, HasIcon: true, Size: 100),
            [@"C:\Game\c.exe"] = new("Game", Described: true, HasIcon: true, Size: 500)
        };

        var app = Assert.Single(ProgramScan.Arrange(InOneGame([.. facts.Keys]), _ => DateTime.UnixEpoch, path => facts[path]));

        Assert.Equal(@"C:\Game\c.exe", ProgramScan.ToPin(app).Path);
    }

    [Fact]
    public void TheErrorReporter_IsAHelper_AndComesLast()
    {
        var apps = ArrangeStarCraft();

        Assert.Equal("BlizzardError (x64)", apps[^1].Name);
        Assert.True(apps[^1].IsHelper);
        Assert.All(apps.SkipLast(1), app => Assert.False(app.IsHelper));
    }

    [Fact]
    public void RowsAreInOrderOfName()
    {
        var names = ArrangeStarCraft().Where(app => !app.IsHelper).Select(app => app.Name).ToList();

        Assert.Equal(names.Order(StringComparer.CurrentCultureIgnoreCase), names);
    }

    [Fact]
    public void CopiesOfOneProgram_AreOneProgram_TheNewestKept()
    {
        // A game that keeps a folder per version: the list matches by file name, so it could
        // not tell the copies apart even if it offered them separately.
        var old = @"C:\Game\Versions\Base1\game.exe";
        var current = @"C:\Game\Versions\Base2\game.exe";
        var modified = new Dictionary<string, DateTime>
        {
            [old] = new(2024, 1, 1),
            [current] = new(2026, 9, 1)
        };

        var app = Assert.Single(ProgramScan.Arrange(InOneGame(old, current), path => modified[path], _ => Facts("Game")));
        var program = Assert.Single(app.Programs);

        Assert.Equal(current, program.Path);
        Assert.Equal(2, program.Copies);
    }

    [Fact]
    public void CopiesAreRecognised_WhateverTheCaseOfTheirNames()
    {
        var app = Assert.Single(ProgramScan.Arrange(
            InOneGame(@"C:\A\Game.exe", @"C:\B\GAME.EXE"), _ => DateTime.UnixEpoch, _ => Facts("Game")));

        Assert.Equal(2, Assert.Single(app.Programs).Copies);
    }

    [Fact]
    public void NamesThatDifferOnlyInCase_AreOneRow()
    {
        var apps = ProgramScan.Arrange(
            InOneGame(@"C:\Game\a.exe", @"C:\Game\b.exe"),
            _ => DateTime.UnixEpoch,
            path => Facts(path.EndsWith("a.exe") ? "Some Game" : "SOME GAME"));

        Assert.Equal(2, Assert.Single(apps).Programs.Count);
    }

    [Fact]
    public void ProgramsThatSayNothing_AreNotLumpedTogether()
    {
        // With no description, a program goes by its file name — two such programs are two
        // rows, not one row of everything nameless.
        var apps = ProgramScan.Arrange(
            InOneGame(@"C:\Tools\first.exe", @"C:\Tools\second.exe"),
            _ => DateTime.UnixEpoch,
            path => Facts(Path.GetFileNameWithoutExtension(path)));

        Assert.Equal(2, apps.Count);
    }

    [Fact]
    public void AHelperSharingTheGamesName_IsKeptApartFromIt()
    {
        // So hiding the helpers never hides the game along with them.
        var apps = ProgramScan.Arrange(
            InOneGame(@"C:\Game\game.exe", @"C:\Game\GameSetup.exe"),
            _ => DateTime.UnixEpoch,
            _ => Facts("The Game"));

        Assert.Equal(2, apps.Count);
        Assert.False(apps[0].IsHelper);
        Assert.Equal("game.exe", Assert.Single(apps[0].Programs).FileName);
        Assert.True(apps[1].IsHelper);
    }

    [Fact]
    public void EachProgramsNameIsRead_OnceForAllItsCopies()
    {
        var asked = new List<string>();

        ProgramScan.Arrange(
            InOneGame(@"C:\A\game.exe", @"C:\B\game.exe", @"C:\C\game.exe"),
            _ => DateTime.UnixEpoch,
            path =>
            {
                asked.Add(path);
                return Facts("Game");
            });

        Assert.Single(asked);
    }

    [Fact]
    public void TwoGamesShippingTheSameProgram_AreTwoRows()
    {
        // A launcher.exe in each of two games is two games' launchers, not one row saying one.
        var apps = ProgramScan.Arrange(
            [
                new FoundFile(@"E:\Steam\common\Alpha\Launcher.exe", "Alpha"),
                new FoundFile(@"E:\Steam\common\Beta\Launcher.exe", "Beta")
            ],
            _ => DateTime.UnixEpoch,
            _ => Facts("Launcher"));

        Assert.Equal(["Alpha", "Beta"], apps.Select(app => app.Game));
        Assert.All(apps, app => Assert.Single(app.Programs));
    }

    [Fact]
    public void GamesAreInOrderOfName_WithEachGamesHelpersLast()
    {
        var apps = ProgramScan.Arrange(
            [
                new FoundFile(@"E:\Games\Zeta\unins000.exe", "Zeta"),
                new FoundFile(@"E:\Games\Zeta\zeta.exe", "Zeta"),
                new FoundFile(@"E:\Games\Alpha\alpha.exe", "Alpha")
            ],
            _ => DateTime.UnixEpoch,
            path => Facts(Path.GetFileNameWithoutExtension(path)));

        Assert.Equal(
            [("Alpha", false), ("Zeta", false), ("Zeta", true)],
            apps.Select(app => (app.Game, app.IsHelper)));
    }

    // ---- most likely first ----------------------------------------------------------

    /// <summary>
    /// StarCraft II's programs as they really are: names, whether they carry an icon, and
    /// their sizes in megabytes. The editor is larger than the game.
    /// </summary>
    private static readonly (string Path, string? Description, bool Icon, double Megabytes)[] StarCraftFacts =
    [
        (@"E:\Games\StarCraft II\StarCraft II.exe", "StarCraft II", true, 5.0),
        (@"E:\Games\StarCraft II\StarCraft II Editor.exe", "StarCraft II Editor", true, 0.4),
        (@"E:\Games\StarCraft II\Support\SC2Switcher.exe", "SC2Switcher", true, 1.0),
        (@"E:\Games\StarCraft II\Support\StarWeb.exe", null, false, 1.3),
        (@"E:\Games\StarCraft II\Support\BlizzardBrowser\BlizzardBrowser.exe", "Blizzard Browser", true, 1.6),
        (@"E:\Games\StarCraft II\Support64\SC2Editor_x64.exe", "SC2Edit", true, 72.9),
        (@"E:\Games\StarCraft II\Versions\Base97563\SC2_x64.exe", "StarCraft II", true, 62.0)
    ];

    private static IReadOnlyList<ScannedApp> ArrangeStarCraftFacts() =>
        ProgramScan.Arrange(
            StarCraftFacts.Select(program => new FoundFile(program.Path, "StarCraft II")),
            _ => DateTime.UnixEpoch,
            path =>
            {
                var program = StarCraftFacts.Single(candidate => candidate.Path == path);
                return new ProgramFacts(
                    program.Description ?? Path.GetFileNameWithoutExtension(path),
                    program.Description is not null,
                    program.Icon,
                    (long)(program.Megabytes * 1024 * 1024));
            });

    [Fact]
    public void TheGame_ComesFirst_AndTheProgramWithNoIconOrName_Last()
    {
        var ordered = ProgramScan.ByRelevance(ArrangeStarCraftFacts(), _ => false);

        Assert.Equal("StarCraft II", ordered[0].Name);
        Assert.Equal("StarWeb", ordered[^1].Name);
    }

    [Fact]
    public void ANameThatIsTheGames_OutweighsTheLargestProgram()
    {
        // The editor is the largest thing in the folder, and still not the game.
        var ordered = ProgramScan.ByRelevance(ArrangeStarCraftFacts(), _ => false).Select(app => app.Name).ToList();

        Assert.True(ordered.IndexOf("StarCraft II") < ordered.IndexOf("SC2Edit"));
    }

    [Fact]
    public void AProgramThatIsOpen_ComesFirstOfAll()
    {
        var ordered = ProgramScan.ByRelevance(ArrangeStarCraftFacts(), app => app.Name == "SC2Switcher");

        Assert.Equal("SC2Switcher", ordered[0].Name);
    }

    [Fact]
    public void AGameWithAProgramOpen_ComesBeforeTheOtherGames()
    {
        var apps = ProgramScan.Arrange(
            [new FoundFile(@"E:\A\alpha.exe", "Alpha"), new FoundFile(@"E:\Z\zeta.exe", "Zeta")],
            _ => DateTime.UnixEpoch,
            path => Facts(Path.GetFileNameWithoutExtension(path)));

        var ordered = ProgramScan.ByRelevance(apps, app => app.Game == "Zeta");

        Assert.Equal(["Zeta", "Alpha"], ordered.Select(app => app.Game));
    }

    [Fact]
    public void AnIconOfItsOwn_RanksAProgramAboveOneWithout()
    {
        var withIcon = App("Player", icon: true);
        var without = App("Agent", icon: false);

        Assert.True(ProgramScan.Relevance(withIcon, isOpen: false, largest: 0)
            > ProgramScan.Relevance(without, isOpen: false, largest: 0));
    }

    [Fact]
    public void ANameIsTheGames_WhateverItsSpacingAndSigns()
    {
        // "DetroitBecomeHuman" is Detroit: Become Human, and "The Last of Us™ Part I" is its
        // game's name with the trademark sign.
        Assert.True(ProgramScan.Relevance(App("DetroitBecomeHuman", game: "Detroit: Become Human"), false, 0)
            > ProgramScan.Relevance(App("breakpad_server", game: "Detroit: Become Human"), false, 0));
        Assert.True(ProgramScan.Relevance(App("The Last of Us™ Part I", game: "The Last of Us Part I"), false, 0)
            >= ProgramScan.Relevance(App("x", game: "The Last of Us Part I"), false, 0) + 100);
    }

    [Fact]
    public void AppsWithNoGames_AreRankedAsOneList()
    {
        // Installed apps: one ranking across all of them, open first, then an icon.
        var apps = new[] { App("Zed", icon: true, game: ""), App("Agent", icon: false, game: ""), App("Media", icon: true, game: "") };

        var ordered = ProgramScan.ByRelevance(apps, app => app.Name == "Media", perGame: false);

        Assert.Equal(["Media", "Zed", "Agent"], ordered.Select(app => app.Name));
    }

    [Fact]
    public void ByName_IsByGameThenName_OrByNameAloneForApps()
    {
        var apps = new[] { App("b", game: "Two"), App("a", game: "Two"), App("c", game: "One") };

        Assert.Equal(["c", "a", "b"], ProgramScan.ByName(apps).Select(app => app.Name));
        Assert.Equal(["a", "b", "c"], ProgramScan.ByName(apps, perGame: false).Select(app => app.Name));
    }

    [Theory]
    [InlineData("star", true)]
    [InlineData("SC2_X64", true)]
    [InlineData("craft", true)]
    [InlineData("  starcraft  ", true)]
    [InlineData("", true)]
    [InlineData("cyberpunk", false)]
    public void TheFilter_MatchesTheName_AFileName_OrTheGame(string filter, bool matches)
    {
        var app = new ScannedApp(
            "StarCraft II",
            "StarCraft II",
            [new ScannedProgram(@"E:\Games\StarCraft II\Versions\Base97563\SC2_x64.exe", "SC2_x64.exe", 1, Facts("StarCraft II"))],
            IsHelper: false);

        Assert.Equal(matches, ProgramScan.Matches(app, filter));
    }

    [Fact]
    public void TheFilter_MatchesEveryRowOfAGameByTheGamesName()
    {
        var editor = new ScannedApp(
            "StarCraft II",
            "SC2Edit",
            [new ScannedProgram(@"E:\Games\StarCraft II\Support64\SC2Editor_x64.exe", "SC2Editor_x64.exe", 1, Facts("SC2Edit"))],
            IsHelper: false);

        Assert.True(ProgramScan.Matches(editor, "starcraft"));
    }

    [Fact]
    public void WindowsOwnTools_RankBelowInstalledApps_ButAboveAProgramWithNoIcon()
    {
        var installed = App("Media Player", game: "");
        var windows = new ScannedApp(
            "",
            "Remote Desktop Connection",
            [new ScannedProgram(@"C:\Windows\System32\mstsc.exe", "mstsc.exe", 1, new ProgramFacts("Remote Desktop Connection", true, true, 0, IsSystem: true))],
            IsHelper: false);
        var bare = App("Ollama", icon: false, game: "");

        var ordered = ProgramScan.ByRelevance([bare, windows, installed], _ => false, perGame: false);

        Assert.Equal(["Media Player", "Remote Desktop Connection", "Ollama"], ordered.Select(app => app.Name));
    }

    private static ScannedApp App(string name, bool icon = true, string game = "Game") =>
        new(game, name, [new ScannedProgram($@"C:\{name}.exe", name + ".exe", 1, new ProgramFacts(name, true, icon, 0))], IsHelper: false);

    // ---- telling helpers from apps -------------------------------------------------

    [Theory]
    [InlineData("unins000.exe")]
    [InlineData("Uninstall.exe")]
    [InlineData("UnityCrashHandler64.exe")]
    [InlineData("crashpad_handler.exe")]
    [InlineData("CrashReportClient.exe")]
    [InlineData("BlizzardError.exe")]
    [InlineData("vcredist_x64.exe")]
    [InlineData("DXSETUP.exe")]
    [InlineData("setup.exe")]
    [InlineData("dotNetFx40_Full_setup.exe")]
    [InlineData("UE4PrereqSetup_x64.exe")]
    [InlineData("Updater.exe")]
    [InlineData("EpicWebHelper.exe")]
    [InlineData("CefSharp.BrowserSubprocess.exe")]
    [InlineData("crs-handler.exe")]
    [InlineData("crs-uploader.exe")]
    [InlineData("crs-video.exe")]
    [InlineData("breakpad_server.exe")]
    [InlineData("7za.exe")]
    [InlineData("EAUpdater.exe")]
    [InlineData("MySQLInstaller.exe")]
    [InlineData("REDEngineErrorReporter.exe")]
    public void AnInstallerUpdaterOrCrashReporter_IsAHelper(string fileName)
    {
        Assert.True(ProgramScan.LooksLikeHelper(fileName));
    }

    [Theory]
    [InlineData("SC2_x64.exe")]
    [InlineData("StarCraft II.exe")]
    [InlineData("StarCraft II Editor_x64.exe")]
    [InlineData("SC2Switcher_x64.exe")]
    [InlineData("BlizzardBrowser.exe")]
    [InlineData("Launcher.exe")]
    [InlineData("cs2.exe")]
    [InlineData("Terror.exe")]
    [InlineData("Uprising.exe")]
    [InlineData("Setupper.exe")]
    [InlineData("REDprelauncher.exe")]
    [InlineData("Cyberpunk2077.exe")]
    [InlineData("GoWR.exe")]
    [InlineData("tlou-i.exe")]
    [InlineData("RiftApart.exe")]
    [InlineData("Crsytal.exe")]
    [InlineData("SC2Editor_x64.exe")]
    [InlineData("MySQLWorkbench.exe")]
    [InlineData("HTMLViewer.exe")]
    public void AGameOrAnApp_IsNotAHelper(string fileName)
    {
        // Whole words, not substrings: "Terror" contains "error", "Setupper" begins "Setup".
        Assert.False(ProgramScan.LooksLikeHelper(fileName));
    }

    // ---- walking a folder ----------------------------------------------------------

    private string Make(string relative)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }

    [Fact]
    public void TheWalk_FindsProgramsAtEveryDepth_AndNothingElse()
    {
        var top = Make("Game.exe");
        var deep = Make(@"Versions\Base1\bin\game_x64.EXE");
        Make(@"Versions\Base1\readme.txt");
        Make(@"Support\game.exe.bak");
        Directory.CreateDirectory(Path.Combine(_dir, "Folder.exe"));

        var found = ProgramScan.Executables(_dir).Order(StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal([top, deep], found);
    }

    [Fact]
    public void TheWalk_CountsTheFoldersItEnters()
    {
        Make(@"a\b\c\game.exe");
        Make(@"d\tool.exe");
        var entered = 0;

        _ = ProgramScan.Executables(_dir, folderEntered: count => entered = count).ToList();

        // a, a\b, a\b\c and d.
        Assert.Equal(4, entered);
    }

    [Fact]
    public void TheWalk_EntersNoFolderOnceStopped()
    {
        Make(@"a\b\game.exe");
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        Assert.Empty(ProgramScan.Executables(_dir, stop.Token));
    }

    [Fact]
    public void TheWalk_DoesNotFollowAJunctionBackIntoItself()
    {
        // A junction to an ancestor is a loop; followed, the walk would never end.
        Make(@"inner\game.exe");
        var loop = Path.Combine(_dir, "inner", "loop");
        if (!TryMakeJunction(loop, _dir))
        {
            return;
        }

        try
        {
            var found = ProgramScan.Executables(_dir).ToList();

            Assert.Single(found);
        }
        finally
        {
            // On its own and first: a recursive delete refuses a folder holding a junction,
            // where removing the junction by itself removes only the link.
            Directory.Delete(loop);
        }
    }

    /// <summary>
    /// A directory junction, which needs no elevation, unlike a symbolic link. False when the
    /// file system will not have one, in which case there is nothing to test.
    /// </summary>
    private static bool TryMakeJunction(string link, string target)
    {
        using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            ArgumentList = { "/c", "mklink", "/J", link, target },
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;

        mklink.WaitForExit();
        return mklink.ExitCode == 0 && Directory.Exists(link);
    }
}
