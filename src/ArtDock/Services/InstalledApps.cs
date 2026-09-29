using System.IO;
using ArtDock.Interop;

namespace ArtDock.Services;

/// <summary>An app installed on this machine: the name it has in the Start menu, and its program.</summary>
public sealed record InstalledApp(string Name, string Path);

/// <summary>
/// Finds the apps installed on this machine, for the Exclusions page's <em>Scan for apps…</em>.
/// </summary>
/// <remarks>
/// <para>
/// From the shortcuts in the Start menu — everyone's and the user's own — and on the desktop,
/// not from walking Program Files. The shortcuts are the apps as the user knows them, by the
/// names they know them by, pointing at the one program that is the app; a walk of the
/// folders they are installed in turns up every helper, updater and plug-in host beside it.
/// </para>
/// <para>
/// Games are left to <em>Scan for games…</em>: a shortcut into any folder
/// <see cref="GameLibraries"/> knows as a game's is passed over, so the two lists do not
/// repeat each other. So are shortcuts that are not to a program — a help file, a web
/// address — and uninstallers and helpers, which nobody watches fullscreen.
/// </para>
/// <para>
/// Store apps are not here: their Start menu entries are not shortcuts, and what they run is
/// addressed by an id rather than a path. One that is open is on the <em>Add</em> menu.
/// </para>
/// </remarks>
public static class InstalledApps
{
    /// <summary>
    /// Every installed app, once each, Start menu before desktop.
    /// </summary>
    /// <param name="gameFolders">Folders whose programs belong to <em>Scan for games…</em> instead.</param>
    /// <remarks>
    /// Reads shortcuts through the shell, so call it on an STA thread.
    /// </remarks>
    public static IReadOnlyList<InstalledApp> Find(IEnumerable<string> gameFolders)
    {
        var games = gameFolders.ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<InstalledApp>();

        foreach (var (folder, deep) in Places())
        {
            foreach (var link in Shortcuts(folder, deep))
            {
                var name = Path.GetFileNameWithoutExtension(link);
                var target = ShellLink.ResolveTarget(link);
                if (Keep(name, target, games, File.Exists) && seen.Add(target!))
                {
                    apps.Add(new InstalledApp(name, target!));
                }
            }
        }

        return apps;
    }

    /// <summary>
    /// Whether a shortcut is to an app worth offering.
    /// </summary>
    /// <remarks>
    /// Helpers are judged by the program's file name, as the scan for games judges them. The
    /// shortcut's own name is checked only for being an uninstaller, since some uninstall
    /// with the app's own program and an argument — and the app's shortcut must not lose its
    /// program to that one.
    /// </remarks>
    public static bool Keep(string name, string? target, IReadOnlyCollection<string> gameFolders, Func<string, bool> exists) =>
        target is not null
        && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && !IsUninstaller(name)
        && !Hosts.Contains(Path.GetFileName(target))
        && !ProgramScan.LooksLikeHelper(Path.GetFileName(target))
        && !gameFolders.Any(folder => IsInside(target, folder))
        && exists(target);

    /// <summary>True when <paramref name="path"/> is in <paramref name="folder"/> or anywhere under it.</summary>
    public static bool IsInside(string path, string folder)
    {
        var root = Path.TrimEndingDirectorySeparator(folder);
        return root.Length > 0
            && path.Length > root.Length
            && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && (path[root.Length] == Path.DirectorySeparatorChar || path[root.Length] == Path.AltDirectorySeparatorChar);
    }

    private static readonly string[] UninstallWords = ["uninstall", "uninstaller", "uninst", "unins"];

    /// <summary>
    /// Programs that run something else: a shortcut to one of these is a shortcut to a
    /// script, a control panel or a console, named for what it runs and not for them.
    /// </summary>
    /// <remarks>
    /// Found in the Start menu here as "Administrative Tools", which is <c>control.exe</c>,
    /// and "Install Additional Tools for Node.js", which is <c>cmd.exe</c> — and listing either
    /// would stand the dock down over every console or every control panel.
    /// </remarks>
    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "conhost.exe", "control.exe", "mmc.exe", "rundll32.exe", "explorer.exe",
        "wscript.exe", "cscript.exe"
    };

    private static bool IsUninstaller(string name) =>
        name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Any(word => UninstallWords.Contains(word, StringComparer.OrdinalIgnoreCase));

    private static IEnumerable<(string Folder, bool Deep)> Places() =>
    [
        (Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), true),
        (Environment.GetFolderPath(Environment.SpecialFolder.Programs), true),
        (Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), false),
        (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), false)
    ];

    private static IEnumerable<string> Shortcuts(string folder, bool deep)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        try
        {
            return
            [
                .. Directory.EnumerateFiles(folder, "*.lnk", new EnumerationOptions
                {
                    RecurseSubdirectories = deep,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                })
            ];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
