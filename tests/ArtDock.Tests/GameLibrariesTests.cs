using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers finding the games the launchers on a machine have installed: reading each
/// launcher's own record, telling a game publisher's program from anyone else's, and keeping
/// one entry per folder.
/// </summary>
/// <remarks>
/// <para>
/// The records are the formats as they were read on this machine — Steam's
/// <c>libraryfolders.vdf</c> with a second library on another drive, and its app manifests,
/// one of them for Steam's own redistributables. Only the parsing is tested here; the reading
/// of the registry and the disk around it was checked against this machine by hand.
/// </para>
/// <para>
/// The publisher test that matters is the one that is not a game: "Patriot Memory", whose RGB
/// software a match on part of a name took for Riot Games'.
/// </para>
/// </remarks>
public class GameLibrariesTests
{
    // ---- Steam -----------------------------------------------------------------------

    /// <summary>The head of this machine's <c>libraryfolders.vdf</c>, with a library on E: as well.</summary>
    private const string LibraryFolders = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"label"		""
        		"contentid"		"4149446629321113508"
        		"totalsize"		"0"
        		"apps"
        		{
        			"228980"		"0"
        		}
        	}
        	"1"
        	{
        		"path"		"E:\\Programms\\Steam"
        		"label"		""
        		"apps"
        		{
        			"1091500"		"70893456120"
        		}
        	}
        }
        """;

    [Fact]
    public void EverySteamLibrary_IsRead_WithItsBackslashesUnescaped()
    {
        Assert.Equal(
            [@"C:\Program Files (x86)\Steam", @"E:\Programms\Steam"],
            GameLibraries.SteamLibraryPaths(LibraryFolders));
    }

    [Fact]
    public void AFileWithNoLibraries_HasNoLibraries()
    {
        Assert.Empty(GameLibraries.SteamLibraryPaths("\"libraryfolders\"\n{\n}\n"));
        Assert.Empty(GameLibraries.SteamLibraryPaths(string.Empty));
    }

    [Fact]
    public void ASteamManifest_GivesTheGamesNameAndItsFolder()
    {
        // The name can differ from the folder — a colon cannot be in a folder name.
        const string acf = """
            "AppState"
            {
            	"appid"		"1222140"
            	"name"		"Detroit: Become Human"
            	"installdir"		"Detroit Become Human"
            }
            """;

        Assert.Equal(("Detroit: Become Human", "Detroit Become Human"), GameLibraries.SteamManifest(acf));
    }

    [Fact]
    public void ASteamManifestWithNoName_IsNamedForItsFolder()
    {
        Assert.Equal(
            ("Half-Life 2", "Half-Life 2"),
            GameLibraries.SteamManifest("\"AppState\" { \"installdir\" \"Half-Life 2\" }"));
    }

    [Fact]
    public void ASteamManifestWithNoFolder_IsNothing()
    {
        Assert.Null(GameLibraries.SteamManifest("\"AppState\" { \"name\" \"Something\" }"));
    }

    // ---- Epic ------------------------------------------------------------------------

    [Fact]
    public void AnEpicManifest_GivesTheGamesNameAndItsFolder()
    {
        const string item = """
            { "DisplayName": "Fortnite", "InstallLocation": "D:\\Epic Games\\Fortnite", "AppName": "Fortnite" }
            """;

        Assert.Equal(("Fortnite", @"D:\Epic Games\Fortnite"), GameLibraries.EpicManifest(item));
    }

    [Theory]
    [InlineData("""{ "DisplayName": "Uninstalled" }""")]
    [InlineData("""{ "DisplayName": "Blank", "InstallLocation": "" }""")]
    [InlineData("""[ "not", "a", "manifest" ]""")]
    [InlineData("this is not JSON")]
    public void AnEpicManifestWithNoFolder_OrNoManifestAtAll_IsNothing(string item)
    {
        // Nothing, rather than a throw: one broken file costs its own game, not the others'.
        Assert.Null(GameLibraries.EpicManifest(item));
    }

    // ---- publishers ------------------------------------------------------------------

    [Theory]
    [InlineData("Blizzard Entertainment", "StarCraft II", GameLauncher.BattleNet)]
    [InlineData("Electronic Arts", "Battlefield 2042", GameLauncher.EaApp)]
    [InlineData("Riot Games, Inc", "VALORANT", GameLauncher.RiotGames)]
    [InlineData("GOG.com", "The Witcher 3: Wild Hunt", GameLauncher.Gog)]
    [InlineData("Ubisoft", "Assassin's Creed Valhalla", GameLauncher.UbisoftConnect)]
    [InlineData("Rockstar Games", "Grand Theft Auto V", GameLauncher.RockstarGames)]
    [InlineData("  blizzard entertainment  ", "Diablo IV", GameLauncher.BattleNet)]
    public void AGamePublishersGame_IsItsLaunchers(string publisher, string game, GameLauncher launcher)
    {
        Assert.Equal(launcher, GameLibraries.LauncherOf(publisher, game));
    }

    [Theory]
    [InlineData("Blizzard Entertainment", "Battle.net")]
    [InlineData("Electronic Arts", "EA app")]
    [InlineData("Rockstar Games", "Rockstar Games Launcher")]
    [InlineData("Rockstar Games", "Rockstar Games Social Club")]
    [InlineData("GOG.com", "GOG GALAXY")]
    public void ALauncherItself_IsNotOneOfItsGames(string publisher, string launcher)
    {
        Assert.Null(GameLibraries.LauncherOf(publisher, launcher));
    }

    [Theory]
    [InlineData("Patriot Memory")]
    [InlineData("Valve Corporation")]
    [InlineData("Amazon Web Services")]
    [InlineData("Microsoft Corporation")]
    [InlineData("")]
    [InlineData(null)]
    public void AnyoneElsesProgram_IsNoLaunchersGame(string? publisher)
    {
        // "Patriot" has "riot" in it. Matching part of a name made its RGB software a game.
        Assert.Null(GameLibraries.LauncherOf(publisher, "Anything"));
    }

    // ---- keeping one of each -----------------------------------------------------------

    [Fact]
    public void AFolderFoundTwice_IsKeptOnce_UnderTheNameFirstGiven()
    {
        // A Battle.net game has an uninstall entry and sits in a default folder: the entry's
        // name is the better one, and it is asked first.
        var games = GameLibraries.Merge(
            [
                new GameFolder("StarCraft II", @"E:\Games\StarCraft II", GameLauncher.BattleNet),
                new GameFolder("StarCraft II folder", @"e:\games\starcraft ii\", GameLauncher.BattleNet)
            ],
            _ => true);

        var game = Assert.Single(games);
        Assert.Equal("StarCraft II", game.Name);
        Assert.Equal(@"E:\Games\StarCraft II", game.Path);
    }

    [Fact]
    public void AFolderThatIsNotThere_IsDropped()
    {
        // An uninstall entry outlives a game deleted by hand.
        var games = GameLibraries.Merge(
            [
                new GameFolder("Here", @"C:\Games\Here", GameLauncher.Steam),
                new GameFolder("Gone", @"C:\Games\Gone", GameLauncher.Steam)
            ],
            path => !path.EndsWith("Gone", StringComparison.Ordinal));

        Assert.Equal("Here", Assert.Single(games).Name);
    }

    [Fact]
    public void AQuotedPath_IsUnquoted()
    {
        // Uninstall entries are written by each installer as it pleases, quotes and all.
        var game = Assert.Single(GameLibraries.Merge(
            [new GameFolder("Quoted", "\"D:\\Games\\Quoted\\\"", GameLauncher.EaApp)],
            _ => true));

        Assert.Equal(@"D:\Games\Quoted", game.Path);
    }

    [Fact]
    public void AGameWithNoName_IsNamedForItsFolder()
    {
        var game = Assert.Single(GameLibraries.Merge(
            [new GameFolder("  ", @"D:\XboxGames\Halo Infinite", GameLauncher.Xbox)],
            _ => true));

        Assert.Equal("Halo Infinite", game.Name);
    }

    [Fact]
    public void GamesAreInOrderOfLauncher_ThenName()
    {
        var games = GameLibraries.Merge(
            [
                new GameFolder("StarCraft II", @"E:\Games\StarCraft II", GameLauncher.BattleNet),
                new GameFolder("The Witcher 3", @"E:\Steam\common\The Witcher 3", GameLauncher.Steam),
                new GameFolder("Cyberpunk 2077", @"E:\Steam\common\Cyberpunk 2077", GameLauncher.Steam)
            ],
            _ => true);

        Assert.Equal(["Cyberpunk 2077", "The Witcher 3", "StarCraft II"], games.Select(game => game.Name));
    }
}
