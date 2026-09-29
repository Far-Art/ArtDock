using System.IO;
using System.IO.Enumeration;

namespace ArtDock.Services;

/// <summary>An executable the walk found, and the game folder it was found under.</summary>
/// <param name="Path">Where it is.</param>
/// <param name="Game">The game it belongs to — the name of the folder the walk started from.</param>
public readonly record struct FoundFile(string Path, string Game);

/// <summary>What reading a program tells about it.</summary>
/// <param name="Name">What it calls itself, or its file name when it says nothing.</param>
/// <param name="Described">Whether it said anything — whether <paramref name="Name"/> is its own.</param>
/// <param name="HasIcon">Whether it carries an icon of its own.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="IsSystem">Whether it is Windows' own, installed in the Windows folder.</param>
public readonly record struct ProgramFacts(string Name, bool Described, bool HasIcon, long Size, bool IsSystem = false);

/// <summary>One program a scan found: the newest copy of an executable, by file name.</summary>
/// <param name="Path">The newest copy found, which is the one added to the list.</param>
/// <param name="FileName">The executable's file name — what the list matches on.</param>
/// <param name="Copies">How many copies of it the scan came across.</param>
/// <param name="Facts">What reading it told.</param>
public sealed record ScannedProgram(string Path, string FileName, int Copies, ProgramFacts Facts)
{
    /// <summary>What the program calls itself, or its file name when it says nothing.</summary>
    public string Name => Facts.Name;
}

/// <summary>
/// The programs a scan found that call themselves by the same name, which are one app to
/// anyone choosing from the list.
/// </summary>
/// <param name="Game">The game they were found in.</param>
/// <param name="Name">The name they share.</param>
/// <param name="Programs">The programs, by file name.</param>
/// <param name="IsHelper">
/// Whether they look like an installer, an updater or a crash reporter — shown only when
/// asked for, since none of them is ever the window a game plays in.
/// </param>
public sealed record ScannedApp(string Game, string Name, IReadOnlyList<ScannedProgram> Programs, bool IsHelper);

/// <summary>
/// Finds the programs in a folder, for the Exclusions page's Scan.
/// </summary>
/// <remarks>
/// <para>
/// For a game whose program nobody knows the name of, because nobody starts it by hand: a
/// launcher starts it, from wherever the launcher put it. StarCraft II is the case this was
/// written for — started from Battle.net, and on disk as a <c>StarCraft II.exe</c> at the top
/// of its folder that is only a stub, with the game itself two folders down, as
/// <c>SC2_x64.exe</c>. All three of its programs describe themselves as "StarCraft II".
/// </para>
/// <para>
/// Which is why programs are gathered by the name they give, and ticked together: the right
/// one is in the row whatever it is called, and the ones that are not the game cost nothing
/// on the list — a program that never fills the screen never stands the edge down.
/// </para>
/// <para>
/// The walk and the arranging are separate so the arranging can be tested without a disk,
/// and so it can be handed only the copies worth reading the names of.
/// </para>
/// </remarks>
public static class ProgramScan
{
    /// <summary>
    /// Every executable under <paramref name="root"/>, however deep.
    /// </summary>
    /// <param name="root">The folder to look in.</param>
    /// <param name="stop">Stops the walk: no folder is entered once it is set.</param>
    /// <param name="folderEntered">Told the running count of folders entered, for a progress line.</param>
    /// <remarks>
    /// <para>
    /// Folders that cannot be read are passed over rather than ending the walk — a game folder
    /// can hold one the account may not open. Links and junctions are not followed, which is
    /// what keeps a walk from going round in a loop, and hidden system folders such as
    /// <c>System Volume Information</c> are left out along with them.
    /// </para>
    /// <para>
    /// Lazy: the walk happens as the result is read, on whatever thread reads it.
    /// </para>
    /// </remarks>
    public static IEnumerable<string> Executables(
        string root, CancellationToken stop = default, Action<int>? folderEntered = null)
    {
        var folders = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            MatchType = MatchType.Simple
        };

        return new FileSystemEnumerable<string>(
            root,
            (ref FileSystemEntry entry) => entry.ToFullPath(),
            options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                !entry.IsDirectory && entry.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),

            ShouldRecursePredicate = (ref FileSystemEntry entry) =>
            {
                if (stop.IsCancellationRequested)
                {
                    return false;
                }

                folderEntered?.Invoke(++folders);
                return true;
            }
        };
    }

    /// <summary>
    /// Turns what a walk found into the rows to choose from.
    /// </summary>
    /// <param name="files">Every executable found, copies included, with the game it is in.</param>
    /// <param name="modified">When a file was last written, to pick the newest of its copies.</param>
    /// <param name="describe">What reading a program tells about it; asked once per program.</param>
    /// <remarks>
    /// <para>
    /// Everything is within its game. Two games can each ship a <c>Launcher.exe</c>, and a row
    /// that merged them would say one game where there are two.
    /// </para>
    /// <para>
    /// Within a game, copies of one file name are one program, because the list matches by
    /// file name and so cannot tell them apart anyway: a game keeping one folder per version
    /// (StarCraft II's <c>Versions\Base…</c>) would otherwise offer the same program once for
    /// each. The newest copy is the one kept, as the likeliest to be what runs.
    /// </para>
    /// <para>
    /// Programs giving the same name are then one row, compared without regard to case. A
    /// helper is kept apart even from a program of the same name, so that hiding the helpers
    /// never hides the game along with them. Games are in order of name; within one, the rows
    /// that are not helpers come first, then both are in order of name.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ScannedApp> Arrange(
        IEnumerable<FoundFile> files, Func<string, DateTime> modified, Func<string, ProgramFacts> describe)
    {
        var programs = files
            .GroupBy(
                file => (Game: file.Game.ToUpperInvariant(), File: FullscreenApps.KeyOf(file.Path).ToUpperInvariant()))
            .Select(copies =>
            {
                var newest = copies
                    .Select(file => file.Path)
                    .OrderByDescending(modified)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .First();

                return (Game: copies.First().Game,
                    Program: new ScannedProgram(newest, FullscreenApps.KeyOf(newest), copies.Count(), describe(newest)));
            })
            .ToList();

        return
        [
            .. programs
                .GroupBy(found => (
                    Game: found.Game.ToUpperInvariant(),
                    Name: found.Program.Name.ToUpperInvariant(),
                    Helper: LooksLikeHelper(found.Program.FileName)))
                .Select(same => new ScannedApp(
                    same.First().Game,
                    same.First().Program.Name,
                    [.. same.Select(found => found.Program).OrderBy(program => program.FileName, StringComparer.OrdinalIgnoreCase)],
                    same.Key.Helper))
                .OrderBy(app => app.Game, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(app => app.IsHelper)
                .ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
        ];
    }

    // ---- order and filter ------------------------------------------------------------

    /// <summary>
    /// How likely a row is to be the program the game plays in — higher is likelier.
    /// </summary>
    /// <param name="app">The row.</param>
    /// <param name="isOpen">Whether one of its programs has a window open now.</param>
    /// <param name="largest">The size of the largest program in the row's game.</param>
    /// <remarks>
    /// <para>
    /// Weighed from what was measured on the games of the machine this was written on, in
    /// order of how much each says. Running now says the most: the user has the game open,
    /// and it is the one being looked for. Then a name that is the game's — "StarCraft II" in
    /// StarCraft II, "DetroitBecomeHuman" in Detroit: Become Human, once both are reduced to
    /// their letters and digits — and less for one that only contains it, which is the game's
    /// editor or its crash reporter as often as the game.
    /// </para>
    /// <para>
    /// Then an icon of its own. Every game there had one, and not one of the crash handlers,
    /// uploaders, web helpers and unpackers beside them did. A description counts for less,
    /// since a good many games ship without one. The largest program in the game counts too —
    /// the game is the largest thing in its folder almost always, though StarCraft II's editor
    /// is larger than StarCraft II, which is why a name outweighs it.
    /// </para>
    /// <para>
    /// Windows' own programs sit below the ones that were installed, by less than an icon is
    /// worth, so a tool of Windows' still ranks above a program with no icon at all.
    /// </para>
    /// <para>
    /// A helper sinks below everything, whatever else it has: a crash reporter named for its
    /// game ("Cyberpunk 2077 Crash Reporter") would otherwise float up on the name.
    /// </para>
    /// </remarks>
    public static int Relevance(ScannedApp app, bool isOpen, long largest)
    {
        var score = 0;

        if (isOpen)
        {
            score += 1000;
        }

        score += NameMatch(app.Name, app.Game) switch
        {
            Match.Same => 100,
            Match.Contains => 50,
            _ => 0
        };

        if (app.Programs.Any(program => program.Facts.HasIcon))
        {
            score += 40;
        }

        if (largest > 0 && app.Programs.Any(program => program.Facts.Size == largest))
        {
            score += 20;
        }

        if (app.Programs.Any(program => program.Facts.Described))
        {
            score += 10;
        }

        // Windows' own tools below the apps that were installed: Character Map and Disk
        // Cleanup are in everyone's Start menu and nobody's way. Below, not out — Remote
        // Desktop Connection is one of them, and it goes fullscreen.
        if (app.Programs.All(program => program.Facts.IsSystem))
        {
            score -= 30;
        }

        if (app.IsHelper)
        {
            score -= 2000;
        }

        return score;
    }

    private enum Match { None, Contains, Same }

    /// <summary>
    /// Whether a program's name is its game's, comparing only letters and digits, so that
    /// punctuation, spacing and trademark signs do not stand in the way.
    /// </summary>
    /// <remarks>
    /// Containment only for names long enough to mean something: a three-letter name such as
    /// "Run" is inside a great many games' names without being any of them.
    /// </remarks>
    private static Match NameMatch(string name, string game)
    {
        var a = Letters(name);
        var b = Letters(game);
        if (a.Length == 0 || b.Length == 0)
        {
            return Match.None;
        }

        if (a == b)
        {
            return Match.Same;
        }

        return Math.Min(a.Length, b.Length) >= 4 && (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
            ? Match.Contains
            : Match.None;
    }

    private static string Letters(string text) =>
        string.Concat(text.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    /// <summary>
    /// The rows with the likeliest first: games with a program open ahead of the rest and
    /// otherwise by name, and within each game by <see cref="Relevance"/>, then by name.
    /// </summary>
    /// <param name="apps">The rows.</param>
    /// <param name="isOpen">Whether a row has a program open now.</param>
    /// <param name="perGame">
    /// False for a list that is not arranged in games — installed apps — which is one
    /// ranking across every row: open first, then an icon of its own, then a description.
    /// </param>
    public static IReadOnlyList<ScannedApp> ByRelevance(
        IEnumerable<ScannedApp> apps, Func<ScannedApp, bool> isOpen, bool perGame = true) =>
        perGame ? ByRelevanceInGames(apps, isOpen) :
        [
            .. apps
                .OrderByDescending(app => Relevance(app, isOpen(app), largest: 0))
                .ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
        ];

    private static IReadOnlyList<ScannedApp> ByRelevanceInGames(IEnumerable<ScannedApp> apps, Func<ScannedApp, bool> isOpen) =>
    [
        .. apps
            .GroupBy(app => app.Game, StringComparer.OrdinalIgnoreCase)
            .Select(game =>
            {
                var largest = game.SelectMany(app => app.Programs).Select(program => program.Facts.Size).DefaultIfEmpty().Max();
                return game
                    .Select(app => (App: app, Score: Relevance(app, isOpen(app), largest)))
                    .OrderByDescending(scored => scored.Score)
                    .ThenBy(scored => scored.App.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(scored => scored.App)
                    .ToList();
            })
            .OrderByDescending(game => game.Any(isOpen))
            .ThenBy(game => game[0].Game, StringComparer.CurrentCultureIgnoreCase)
            .SelectMany(game => game)
    ];

    /// <summary>
    /// The rows in order of name: games by name, and within each, programs by name — or,
    /// for a list not arranged in games, every row by name.
    /// </summary>
    public static IReadOnlyList<ScannedApp> ByName(IEnumerable<ScannedApp> apps, bool perGame = true) =>
    [
        .. apps
            .OrderBy(app => perGame ? app.Game : string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(app => app.IsHelper)
    ];

    /// <summary>
    /// True when a row answers to what was typed into the filter: its name, one of its
    /// programs' file names, or its game's name contains it, whatever the case. A blank filter
    /// is answered by everything.
    /// </summary>
    public static bool Matches(ScannedApp app, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var wanted = filter.Trim();
        return app.Name.Contains(wanted, StringComparison.CurrentCultureIgnoreCase)
            || app.Game.Contains(wanted, StringComparison.CurrentCultureIgnoreCase)
            || app.Programs.Any(program => program.FileName.Contains(wanted, StringComparison.CurrentCultureIgnoreCase));
    }

    // ---- helpers --------------------------------------------------------------------

    /// <summary>
    /// The words that mark a program as something other than an app: installers,
    /// uninstallers, updaters, crash and error reporters, redistributables and the helper
    /// processes that apps start behind themselves.
    /// </summary>
    private static readonly HashSet<string> HelperWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "unins", "uninst", "uninstall", "uninstaller",
        "setup", "install", "installer", "prereq", "prerequisites", "redist", "vcredist",
        "dxsetup", "dotnetfx", "repair",
        "update", "updater", "patcher",
        "crash", "crashpad", "crashreporter", "error", "errorreporter", "reporter", "bugreport",
        "breakpad", "crs", "uploader", "handler",
        "helper", "subprocess", "service", "cleanup",

        // Whole names that do not split into words: the 7-Zip console several games ship
        // to unpack their own updates.
        "7z", "7za"
    };

    /// <summary>
    /// True when a program's file name says it is a helper rather than an app.
    /// </summary>
    /// <remarks>
    /// By whole words of the name, not by what it contains: <c>Terror.exe</c> is a game, and
    /// <c>BlizzardError.exe</c> is not. A name is split where punctuation, a change from
    /// lower case to upper, or a change between letters and digits falls, so
    /// <c>UnityCrashHandler64</c> is <c>Unity Crash Handler 64</c> and <c>unins000</c> is
    /// <c>unins 000</c> — and where an acronym runs into a word, so <c>EAUpdater</c> is
    /// <c>EA Updater</c> and <c>MySQLInstaller</c> is <c>My SQL Installer</c>, both of which
    /// were offered as apps until the acronym was split off. What this gets wrong costs a tick of the box that shows them.
    /// The words came from real folders: <c>crs-handler</c>, <c>crs-uploader</c> and
    /// <c>crs-video</c> are the crash reporter Nixxes ships with its PC ports,
    /// <c>breakpad_server</c> Detroit's, and they were all offered as games until added.
    /// </remarks>
    public static bool LooksLikeHelper(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        return HelperWords.Contains(name) || Words(name).Any(HelperWords.Contains);
    }

    private static IEnumerable<string> Words(string name)
    {
        var start = -1;
        for (var i = 0; i <= name.Length; i++)
        {
            var c = i < name.Length ? name[i] : ' ';
            if (!char.IsLetterOrDigit(c))
            {
                if (start >= 0)
                {
                    yield return name[start..i];
                    start = -1;
                }

                continue;
            }

            if (start >= 0)
            {
                var previous = name[i - 1];
                var boundary = (char.IsLower(previous) && char.IsUpper(c))
                    || char.IsDigit(previous) != char.IsDigit(c);

                if (boundary)
                {
                    yield return name[start..i];
                    start = i;
                }
                else if (char.IsLower(c) && i - start >= 2 && char.IsUpper(previous) && char.IsUpper(name[i - 2]))
                {
                    // An acronym running into a word: the last capital starts the word.
                    yield return name[start..(i - 1)];
                    start = i - 1;
                }
            }
            else
            {
                start = i;
            }
        }
    }
}
