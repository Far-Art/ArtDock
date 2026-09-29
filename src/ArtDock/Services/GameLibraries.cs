using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ArtDock.Services;

/// <summary>The launchers whose games the scan finds by itself.</summary>
public enum GameLauncher
{
    Steam,
    BattleNet,
    EaApp,
    EpicGames,
    Gog,
    UbisoftConnect,
    Xbox,
    RiotGames,
    RockstarGames
}

/// <summary>One installed game's folder, and where it was learned of.</summary>
/// <param name="Name">The game's name, as its launcher gives it, or its folder's.</param>
/// <param name="Path">The folder the game is installed in.</param>
/// <param name="Launcher">The launcher that installed it, or null for a folder the user chose.</param>
public sealed record GameFolder(string Name, string Path, GameLauncher? Launcher);

/// <summary>
/// Finds the games the launchers on this machine have installed, so the Exclusions page's
/// scan can look through them without being told where they are.
/// </summary>
/// <remarks>
/// <para>
/// Each launcher is asked in its own way, because each keeps its own record: Steam in the
/// <c>libraryfolders.vdf</c> of its install, with a folder per game under each library's
/// <c>steamapps\common</c>; Epic in a manifest per game under ProgramData; GOG, Ubisoft and
/// Rockstar under keys of their own in the registry; Battle.net, EA, Riot and the rest in the
/// uninstall entries every installer writes; and Xbox in an <c>XboxGames</c> folder at the root
/// of whichever drive games were put on. The default folders the launchers install to are
/// looked in as well, for a game whose record has gone.
/// </para>
/// <para>
/// Steam's library is listed by folder rather than by its manifests. On the machine this was
/// written on six of the thirteen games in a library had no manifest — installed before a
/// reinstall of Steam, or copied over — and they are still games that go fullscreen.
/// </para>
/// <para>
/// Publishers are matched by their exact names. Matching on a part of one found "Riot" in
/// "Patriot Memory", whose RGB software would then have been offered as a game.
/// </para>
/// <para>
/// Nothing here throws: a launcher whose record cannot be read is a launcher with no games,
/// and the rest are still found. Only folders that exist are returned, each once.
/// </para>
/// </remarks>
public static partial class GameLibraries
{
    /// <summary>Every game folder found on this machine, in order of launcher and then name.</summary>
    public static IReadOnlyList<GameFolder> Find()
    {
        IEnumerable<GameFolder>[] sources =
        [
            Guard(SteamGames),
            Guard(EpicGames),
            Guard(GogGames),
            Guard(UbisoftGames),
            Guard(RockstarGames),
            Guard(UninstallEntries),
            Guard(XboxGames),
            Guard(DefaultFolders)
        ];

        return Merge(sources.SelectMany(source => source), Directory.Exists);
    }

    /// <summary>
    /// Keeps one entry per folder — the first, since the sources are asked in order of how
    /// much they know — and only folders that exist.
    /// </summary>
    public static IReadOnlyList<GameFolder> Merge(IEnumerable<GameFolder> found, Func<string, bool> exists)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<GameFolder>();

        foreach (var game in found)
        {
            var path = Normalise(game.Path);
            if (path is null || !seen.Add(path) || !exists(path))
            {
                continue;
            }

            kept.Add(game with { Path = path, Name = string.IsNullOrWhiteSpace(game.Name) ? Path.GetFileName(path) : game.Name.Trim() });
        }

        return
        [
            .. kept
                .OrderBy(game => game.Launcher)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
        ];
    }

    /// <summary>A folder as the file system would name it, or null for one that cannot be.</summary>
    private static string? Normalise(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            // Uninstall entries are hand-written by installers, and some quote the path.
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim().Trim('"')));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static IEnumerable<GameFolder> Guard(Func<IEnumerable<GameFolder>> source)
    {
        try
        {
            // Materialised here, so a failure halfway through is caught here too.
            return [.. source()];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException
            or JsonException or ArgumentException or InvalidOperationException)
        {
            return [];
        }
    }

    // ---- Steam ---------------------------------------------------------------------

    private static IEnumerable<GameFolder> SteamGames()
    {
        var steam = ReadString(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath")
            ?? ReadString(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
            ?? ReadString(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");

        if (Normalise(steam) is not { } root)
        {
            yield break;
        }

        var libraries = new List<string> { root };
        foreach (var file in new[] { @"steamapps\libraryfolders.vdf", @"config\libraryfolders.vdf" })
        {
            var vdf = Path.Combine(root, file);
            if (File.Exists(vdf))
            {
                libraries.AddRange(SteamLibraryPaths(File.ReadAllText(vdf)));
            }
        }

        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(library, "steamapps");
            var common = Path.Combine(apps, "common");
            if (!Directory.Exists(common))
            {
                continue;
            }

            // Names from the manifests where there are any; the folder's own where not.
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                if (SteamManifest(File.ReadAllText(manifest)) is { } app)
                {
                    names[app.InstallDir] = app.Name;
                }
            }

            foreach (var folder in Directory.EnumerateDirectories(common))
            {
                var name = Path.GetFileName(folder);

                // Steam's own redistributables — DirectX, the Visual C++ runtimes — which it
                // keeps in a folder of the library as though they were a game.
                if (string.Equals(name, "Steamworks Shared", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return new GameFolder(names.GetValueOrDefault(name, name), folder, GameLauncher.Steam);
            }
        }
    }

    /// <summary>The library folders a Steam <c>libraryfolders.vdf</c> lists.</summary>
    /// <remarks>
    /// Valve's KeyValues text: quoted keys and values, backslashes escaped. Only the
    /// <c>"path"</c> values are wanted, which a pattern finds without parsing the nesting.
    /// </remarks>
    public static IReadOnlyList<string> SteamLibraryPaths(string vdf) =>
    [
        .. SteamPath().Matches(vdf).Select(match => Unescape(match.Groups[1].Value))
    ];

    /// <summary>A Steam app manifest's name and install folder, or null when it has neither.</summary>
    public static (string Name, string InstallDir)? SteamManifest(string acf)
    {
        var name = SteamName().Match(acf);
        var folder = SteamInstallDir().Match(acf);
        if (!folder.Success)
        {
            return null;
        }

        var installDir = Unescape(folder.Groups[1].Value);
        return (name.Success ? Unescape(name.Groups[1].Value) : installDir, installDir);
    }

    private static string Unescape(string value) =>
        value.Replace(@"\\", @"\", StringComparison.Ordinal).Replace("\\\"", "\"", StringComparison.Ordinal);

    [GeneratedRegex("\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamPath();

    [GeneratedRegex("\"name\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamName();

    [GeneratedRegex("\"installdir\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamInstallDir();

    // ---- Epic ----------------------------------------------------------------------

    private static IEnumerable<GameFolder> EpicGames()
    {
        var manifests = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"Epic\EpicGamesLauncher\Data\Manifests");

        if (!Directory.Exists(manifests))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
        {
            if (EpicManifest(File.ReadAllText(file)) is { } game)
            {
                yield return new GameFolder(game.Name, game.InstallLocation, GameLauncher.EpicGames);
            }
        }
    }

    /// <summary>
    /// An Epic manifest's game name and install folder, or null when it has no folder or is
    /// not a manifest at all — one broken file costs its own game, not the others'.
    /// </summary>
    public static (string Name, string InstallLocation)? EpicManifest(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using var owned = document;
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("InstallLocation", out var location)
            || location.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(location.GetString()))
        {
            return null;
        }

        var name = root.TryGetProperty("DisplayName", out var display) && display.ValueKind == JsonValueKind.String
            ? display.GetString()
            : null;

        return (name ?? string.Empty, location.GetString()!);
    }

    // ---- registry keys of the launchers' own ---------------------------------------

    private static IEnumerable<GameFolder> GogGames() =>
        SubkeyValues(@"SOFTWARE\WOW6432Node\GOG.com\Games", "gameName", "path", GameLauncher.Gog);

    private static IEnumerable<GameFolder> UbisoftGames() =>
        SubkeyValues(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs", nameValue: null, "InstallDir", GameLauncher.UbisoftConnect);

    /// <summary>Rockstar's key holds its launcher and Social Club beside the games.</summary>
    private static IEnumerable<GameFolder> RockstarGames() =>
        SubkeyValues(@"SOFTWARE\WOW6432Node\Rockstar Games", nameValue: null, "InstallFolder", GameLauncher.RockstarGames)
            .Where(game => !RockstarOwn.Contains(game.Name));

    private static readonly HashSet<string> RockstarOwn = new(StringComparer.OrdinalIgnoreCase)
    {
        "Launcher", "Rockstar Games Launcher", "Rockstar Games Social Club", "Social Club", "Steam"
    };

    /// <summary>A game per subkey: its name from one value (or the key's own name), its folder from another.</summary>
    private static IEnumerable<GameFolder> SubkeyValues(string key, string? nameValue, string pathValue, GameLauncher launcher)
    {
        using var parent = Registry.LocalMachine.OpenSubKey(key);
        if (parent is null)
        {
            yield break;
        }

        foreach (var subkeyName in parent.GetSubKeyNames())
        {
            using var subkey = parent.OpenSubKey(subkeyName);
            if (subkey?.GetValue(pathValue) is not string path || string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var name = nameValue is not null && subkey.GetValue(nameValue) is string given ? given : subkeyName;

            // Ubisoft names its keys by number; the folder says more.
            if (name.All(char.IsDigit))
            {
                name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path.Trim()));
            }

            yield return new GameFolder(name, path, launcher);
        }
    }

    // ---- uninstall entries ---------------------------------------------------------

    private static IEnumerable<GameFolder> UninstallEntries()
    {
        const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        // Both views of the machine's entries, since a 32-bit installer writes to the other.
        (RegistryHive Hive, RegistryView View)[] places =
        [
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default)
        ];

        foreach (var (hive, view) in places)
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var entries = root.OpenSubKey(Uninstall);
            if (entries is null)
            {
                continue;
            }

            foreach (var name in entries.GetSubKeyNames())
            {
                using var entry = entries.OpenSubKey(name);
                if (entry is null
                    || entry.GetValue("InstallLocation") is not string location
                    || string.IsNullOrWhiteSpace(location)
                    || LauncherOf(entry.GetValue("Publisher") as string, entry.GetValue("DisplayName") as string)
                        is not { } launcher)
                {
                    continue;
                }

                yield return new GameFolder(entry.GetValue("DisplayName") as string ?? name, location, launcher);
            }
        }
    }

    /// <summary>
    /// The launcher an installed program's publisher stands for, or null when it is not a
    /// game publisher's, or is the launcher itself rather than one of its games.
    /// </summary>
    public static GameLauncher? LauncherOf(string? publisher, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(publisher)
            || !Publishers.TryGetValue(publisher.Trim(), out var launcher)
            || (displayName is not null && LaunchersOwn.Contains(displayName.Trim())))
        {
            return null;
        }

        return launcher;
    }

    /// <summary>Publishers as their installers write them. Whole names only — see the class remarks.</summary>
    private static readonly Dictionary<string, GameLauncher> Publishers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Blizzard Entertainment"] = GameLauncher.BattleNet,
        ["Electronic Arts"] = GameLauncher.EaApp,
        ["Electronic Arts, Inc."] = GameLauncher.EaApp,
        ["Electronic Arts Inc."] = GameLauncher.EaApp,
        ["Epic Games, Inc."] = GameLauncher.EpicGames,
        ["Epic Games Inc."] = GameLauncher.EpicGames,
        ["GOG.com"] = GameLauncher.Gog,
        ["GOG Ltd."] = GameLauncher.Gog,
        ["Ubisoft"] = GameLauncher.UbisoftConnect,
        ["Ubisoft Entertainment"] = GameLauncher.UbisoftConnect,
        ["Riot Games, Inc"] = GameLauncher.RiotGames,
        ["Riot Games, Inc."] = GameLauncher.RiotGames,
        ["Riot Games"] = GameLauncher.RiotGames,
        ["Rockstar Games"] = GameLauncher.RockstarGames
    };

    /// <summary>The launchers' own entries, which share their publisher with the games.</summary>
    private static readonly HashSet<string> LaunchersOwn = new(StringComparer.OrdinalIgnoreCase)
    {
        "Battle.net", "EA app", "Origin", "Epic Games Launcher", "GOG GALAXY", "GOG Galaxy",
        "Ubisoft Connect", "Uplay", "Riot Client", "Riot Vanguard", "Rockstar Games Launcher",
        "Rockstar Games Social Club"
    };

    // ---- folders ------------------------------------------------------------------

    /// <summary>Games the Xbox app installed, in <c>XboxGames</c> at the root of any drive.</summary>
    private static IEnumerable<GameFolder> XboxGames() =>
        DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
            .SelectMany(drive => GamesIn(Path.Combine(drive.RootDirectory.FullName, "XboxGames"), GameLauncher.Xbox));

    /// <summary>
    /// Where the launchers put games unless told otherwise, for a game whose record is gone.
    /// </summary>
    private static IEnumerable<GameFolder> DefaultFolders()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

        return
        [
            .. GamesIn(Path.Combine(programFiles, "EA Games"), GameLauncher.EaApp),
            .. GamesIn(Path.Combine(programFilesX86, "Origin Games"), GameLauncher.EaApp),
            .. GamesIn(Path.Combine(programFiles, "Epic Games"), GameLauncher.EpicGames),
            .. GamesIn(Path.Combine(programFilesX86, @"GOG Galaxy\Games"), GameLauncher.Gog),
            .. GamesIn(Path.Combine(programFilesX86, @"Ubisoft\Ubisoft Game Launcher\games"), GameLauncher.UbisoftConnect),
            .. GamesIn(Path.Combine(systemDrive, "Riot Games"), GameLauncher.RiotGames)
                .Where(game => !LaunchersOwn.Contains(game.Name))
        ];
    }

    /// <summary>A folder whose every subfolder is a game.</summary>
    private static IEnumerable<GameFolder> GamesIn(string library, GameLauncher launcher) =>
        Directory.Exists(library)
            ? Directory.EnumerateDirectories(library).Select(folder => new GameFolder(Path.GetFileName(folder), folder, launcher))
            : [];

    private static string? ReadString(RegistryKey hive, string key, string value)
    {
        using var opened = hive.OpenSubKey(key);
        return opened?.GetValue(value) as string is { Length: > 0 } text ? text : null;
    }
}
