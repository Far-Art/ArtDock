using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.Dock;
using ArtDock.IconSets;
using ArtDock.Interop;

namespace ArtDock.Services;

/// <summary>
/// Supplies the dock's pinned applications and resolves their icons.
/// </summary>
public sealed class PinnedAppsService
{
    /// <summary>Pixel size icons are extracted at; large enough to stay crisp when magnified.</summary>
    internal const int IconPixelSize = 128;

    /// <summary>
    /// Icons already pulled from the shell, keyed by what was asked for.
    /// </summary>
    /// <remarks>
    /// Extraction is COM work against the shell and easily the most expensive thing that
    /// happens when settings change. Nothing about an icon depends on the dock's geometry,
    /// so re-reading them because a slider moved is pure waste — and it was enough of it to
    /// make the appearance settings feel laggy. Entries are frozen and immutable, so
    /// sharing one across items is safe. Everything is cached except the Recycle Bin, whose
    /// icon changes while it is pinned — see <see cref="LoadIcon"/>.
    /// </remarks>
    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What each pinned shortcut points at, keyed by the shortcut's own path.
    /// </summary>
    /// <remarks>
    /// Cached for the same reason the icons are: resolving a shortcut is COM work against
    /// the shell, and every settings preview rebuilds every item. Misses are cached too — a
    /// shortcut the shell cannot resolve must not be re-asked on every tick of a slider.
    /// </remarks>
    private static readonly Dictionary<string, string?> LinkTargetCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The folder each target opens in File Explorer, if any, keyed by the target.
    /// </summary>
    /// <remarks>
    /// Cached as <see cref="LinkTargetCache"/> is, since the answer is the shell's — and
    /// written for nearly every pin, where that one is written only for shortcuts, so it is
    /// made safe for the tests, which make items on several threads at once.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, string?> ExplorerFolderCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The app ID each pinned shortcut gives, keyed by the shortcut: see <see cref="ShortcutAppId"/>.</summary>
    private static readonly ConcurrentDictionary<string, string?> ShortcutAppIdCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The app and the program that open each type of file, keyed by its extension: see
    /// <see cref="DocumentTarget"/>. A registry read, so asked once, as <see cref="PictureTypes"/> is.
    /// </summary>
    private static readonly ConcurrentDictionary<string, (string? AppId, string? Program)> HandlerCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Pictures' thumbnails, keyed by the file's path, with when the file was written.
    /// </summary>
    /// <remarks>
    /// Apart from <see cref="IconCache"/> because a thumbnail is of the file's contents, which
    /// change under a pin in a way an application's icon does not: a picture being worked on
    /// is re-read the next time the dock rebuilds its items, rather than never.
    /// </remarks>
    private static readonly Dictionary<string, (DateTime Written, ImageSource? Image)> ThumbnailCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether Windows counts an extension as a picture — a registry read, so asked once.</summary>
    private static readonly Dictionary<string, bool> PictureTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether Windows can run an extension as administrator — a registry read, so asked once.</summary>
    private static readonly Dictionary<string, bool> RunAsTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether Windows can run a Store app as administrator, by its AUMID — asked of the shell's
    /// menu for it (<see cref="ShellVerbs.ItemHasRunAs"/>), which is slow, so asked once, and off
    /// the dock's thread (<see cref="LearnRunAsAdministrator"/>).
    /// </summary>
    private static readonly ConcurrentDictionary<string, bool> RunAsApps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fills an empty pin list with what a new dock starts with — see
    /// <see cref="DockPresets.CreateDefaults"/> — so a fresh install is never an empty bar.
    /// Returns true when it changed anything, which is the caller's cue to persist.
    /// </summary>
    public bool SeedIfEmpty(DockSettings settings)
    {
        if (settings.PinnedApps.Count > 0)
        {
            return false;
        }

        settings.PinnedApps = DockPresets.CreateDefaults();
        return settings.PinnedApps.Count > 0;
    }

    /// <summary>
    /// Builds a pin for a dropped or picked path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Anything that exists can be pinned: an application, a shortcut, a folder, or a
    /// document, which the shell opens in whatever owns its type — exactly as
    /// double-clicking it would.
    /// </para>
    /// <para>
    /// Documents were deliberately turned away once, the reasoning being that a dock of
    /// documents is not what anyone means by pinning. That was a judgement about what the
    /// user wanted, made on their behalf, and enforced by doing nothing at all when they
    /// dropped one — no icon, no explanation. Nothing else in the dock treats a document
    /// pin differently, either: it launches through the same <c>ShellExecute</c>, it takes
    /// its icon from the same shell call, and the one thing that would be wrong for it — a
    /// running indicator — is ruled out by <see cref="DockItem.RunningTarget"/> rather than
    /// by refusing the pin.
    /// </para>
    /// </remarks>
    /// <returns>
    /// A pin, or <see langword="null"/> when there is nothing at <paramref name="path"/>.
    /// </returns>
    public static PinnedAppSetting? CreatePin(string path, string? fallbackLabel = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path))
        {
            return null;
        }

        return new PinnedAppSetting
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = LabelFor(path, isDirectory, fallbackLabel),
            TargetPath = path
        };
    }

    /// <summary>
    /// What to call a pin: a folder's own name, an application's description, and for
    /// anything else the file's name.
    /// </summary>
    /// <remarks>
    /// The version resource is asked of executables only. A document has none, so reading it
    /// would be a file opened per pin for an answer that is always absent — and the name
    /// wanted for a document is its own, which is what its window will be titled anyway.
    /// </remarks>
    private static string LabelFor(string path, bool isDirectory, string? fallbackLabel)
    {
        if (isDirectory)
        {
            return new DirectoryInfo(path).Name;
        }

        var name = fallbackLabel ?? Path.GetFileNameWithoutExtension(path);
        return Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
            ? DescribeApp(path, name)
            : name;
    }

    /// <summary>
    /// A pin for a path the user picked through a file dialog, whether or not it is there.
    /// </summary>
    /// <remarks>
    /// <see cref="CreatePin"/> pins anything that exists, so the two now differ only for a
    /// target the dock cannot see — a disconnected network share, or a file removed between
    /// the picking and the pinning. A deliberate pick answered by nothing at all looks like
    /// a broken button, so this one always produces a pin; a target that is missing shows
    /// the shell's generic icon and fails to launch, which is at least visible.
    /// </remarks>
    public static PinnedAppSetting CreateChosenPin(string path) =>
        CreatePin(path) ?? new PinnedAppSetting
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = Path.GetFileNameWithoutExtension(path),
            TargetPath = path
        };

    /// <summary>
    /// The pins for what the Add menu's <em>Search apps…</em> or <em>Search games…</em> found,
    /// each under the name the search showed it by.
    /// </summary>
    /// <remarks>
    /// That name rather than the one <see cref="CreatePin"/> reads off the program: for an app
    /// it is the Start menu's, which is the name the user knows it by — "Word", not "Microsoft
    /// Word" — and the one they just ticked. A program gone since the search found it is left
    /// out, as a drop of one would be.
    /// </remarks>
    public static List<PinnedAppSetting> CreateFoundPins(IEnumerable<(string Path, string Name)> found)
    {
        var pins = new List<PinnedAppSetting>();
        foreach (var (path, name) in found)
        {
            if (CreatePin(path) is { } pin)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    pin.Label = name;
                }

                pins.Add(pin);
            }
        }

        return pins;
    }

    /// <summary>
    /// Where the pins open, as paths, for a search to show what is on the dock already.
    /// </summary>
    public static IEnumerable<string> TargetPaths(IEnumerable<PinnedAppSetting> pins) =>
        pins
            .Where(pin => !pin.IsSeparator && !string.IsNullOrWhiteSpace(pin.TargetPath))
            .Select(pin => Environment.ExpandEnvironmentVariables(pin.TargetPath!.Trim()));

    /// <summary>
    /// What a pin opening <paramref name="target"/>, or the Store app
    /// <paramref name="aumid"/>, would be called if it were pinned now — which is what the item
    /// editor's <em>Reset</em> puts back. Null when there is nothing to name it from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked of what the pin opens rather than kept from when it was made, so it follows a
    /// target that has since been changed, and is in the dock's language as it is now. Each
    /// kind of pin is named as the way it is made names it: a preset as the Add menu does, a
    /// place or a Store app as Explorer shows it, and a file or folder as
    /// <see cref="CreatePin"/> does — or, when it is not there, as
    /// <see cref="CreateChosenPin"/> does.
    /// </para>
    /// <para>
    /// A web address, which only the editor makes, is named for its site. Any other address —
    /// <c>ms-settings:</c>, a command this build does not know — has no name of its own to go
    /// back to, and turning it into one would be a guess.
    /// </para>
    /// </remarks>
    public static string? DefaultLabel(string? target, string? aumid)
    {
        if (DockPresets.PresetLabel(target, aumid) is { } preset)
        {
            return preset;
        }

        if (aumid is { Length: > 0 })
        {
            return ShellNames.DisplayName($@"shell:AppsFolder\{aumid.Trim()}");
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var path = target.Trim();
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return ShellNames.DisplayName(path);
        }

        var isDirectory = Directory.Exists(path);
        if (isDirectory || File.Exists(path))
        {
            return LabelFor(path, isDirectory, fallbackLabel: null);
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out var address) && !address.IsFile)
        {
            if (address.Scheme is not ("http" or "https") || address.Host.Length == 0)
            {
                return null;
            }

            var site = address.Host;
            return site.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? site[4..] : site;
        }

        return Path.GetFileNameWithoutExtension(path) is { Length: > 0 } name ? name : null;
    }

    /// <summary>Projects a stored pin onto the model the dock draws from.</summary>
    public static DockItem ToDockItem(PinnedAppSetting app)
    {
        var linkTarget = ResolveLinkTarget(app.TargetPath);
        var explorerFolder = ExplorerFolder(app.TargetPath, linkTarget);
        return new DockItem
        {
            Id = app.Id,
            Label = app.Label,
            TargetPath = app.TargetPath,
            Aumid = app.Aumid,
            IconPath = app.IconPath,
            UseIconNotThumbnail = app.UseIconNotThumbnail,
            FolderColor = app.FolderColor,
            FolderSymbol = app.FolderSymbol,
            FolderText = app.FolderText,
            FolderSymbolTone = app.FolderSymbolTone,
            RunAsAdministrator = app.RunAsAdministrator,
            CanRunAsAdministrator = !app.IsSeparator
                && (app.Aumid is { Length: > 0 } aumid
                    ? RunAsApps.TryGetValue(aumid, out var elevates) && elevates
                    : CanRunAsAdministrator(app.TargetPath, linkTarget)),
            LinkTarget = linkTarget,
            ExplorerFolder = explorerFolder,
            LaunchedProgram = LaunchedProgram(app.TargetPath, linkTarget),
            AppId = ShortcutAppId(app.TargetPath),
            Document = explorerFolder is null && !app.IsSeparator ? DocumentTarget(app.TargetPath, linkTarget) : null,
            IsSeparator = app.IsSeparator
        };
    }

    /// <summary>
    /// The program a launcher starts, when the pin's program is a launcher named after it:
    /// <c>Battle.net Launcher.exe</c> beside <c>Battle.net.exe</c>. Null for anything else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Such a launcher opens no window of its own — it starts the program and exits — so the
    /// census, which matches by path and then by file name, never found the program under the
    /// pin, and the dot stayed dark however long it ran. Found 2026-10-03 on the Battle.net pin
    /// the games scan had made from Blizzard's own Start menu shortcut, which points at the
    /// launcher and carries no app ID to tie the two together.
    /// </para>
    /// <para>
    /// Narrow on purpose: the name without <c>Launcher</c>, in the same folder, and only if that
    /// file is there. Lighting a pin by any program beside it would light the wrong pin wherever
    /// several share a folder. Read as the pin is read, so the file is looked for once.
    /// </para>
    /// </remarks>
    /// <param name="target">The pin's target.</param>
    /// <param name="linkTarget">Where it points, when it is a shortcut: see <see cref="ResolveLinkTarget"/>.</param>
    public static string? LaunchedProgram(string? target, string? linkTarget)
    {
        if ((linkTarget ?? target) is not { Length: > 0 } program
            || !Path.GetExtension(program).Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || !Path.IsPathFullyQualified(program))
        {
            return null;
        }

        const string suffix = "Launcher";
        var name = Path.GetFileNameWithoutExtension(program);
        if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var stem = name[..^suffix.Length].TrimEnd(' ', '-', '_', '.');
        if (stem.Length == 0 || Path.GetDirectoryName(program) is not { Length: > 0 } folder)
        {
            return null;
        }

        var launched = Path.Combine(folder, stem + ".exe");
        return File.Exists(launched) ? launched : null;
    }

    /// <summary>
    /// The app ID a pinned shortcut gives what it starts (<see cref="ShellLink.AppId"/>), or null
    /// for a pin that is not a shortcut, or a shortcut that gives none.
    /// </summary>
    public static string? ShortcutAppId(string? target) =>
        target is { Length: > 0 } path && Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            ? ShortcutAppIdCache.GetOrAdd(path, ShellLink.AppId)
            : null;

    /// <summary>
    /// Which windows are a document pin's: those whose title names the document, among the windows
    /// of the app that opens it — see <see cref="DocumentWindows"/>. Null for a pin that opens no
    /// document: a program, a folder, a command, a web address, or a file that is not there.
    /// </summary>
    /// <remarks>
    /// The app is the shell's for the file's type, by its app ID where it has one and by its
    /// program where the shell will say — Photoshop's <c>.psd</c> has neither, and is known by
    /// its title alone. A shortcut opens what it points at, so it is asked about that.
    /// </remarks>
    /// <param name="target">The pin's target.</param>
    /// <param name="linkTarget">Where it points, when it is a shortcut: see <see cref="ResolveLinkTarget"/>.</param>
    public static RunningTarget? DocumentTarget(string? target, string? linkTarget)
    {
        var opens = string.Equals(Path.GetExtension(target), ".lnk", StringComparison.OrdinalIgnoreCase)
            ? linkTarget
            : target;

        if (opens is not { Length: > 0 } path
            || DockCommands.IsCommand(path)
            || !Path.IsPathFullyQualified(path)
            || Path.GetExtension(path) is not { Length: > 1 } extension
            || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path))
        {
            return null;
        }

        var (appId, program) = HandlerCache.GetOrAdd(
            extension, type => (ShellVerbs.HandlerAppId(type), ShellVerbs.HandlerProgram(type)));

        return new RunningTarget { Document = Path.GetFileName(path), AppId = appId, Program = program };
    }

    /// <summary>
    /// The folder a pin opens in File Explorer — a folder or a drive, This PC, the Recycle Bin,
    /// a shortcut to a folder — by the shell's own name for it, which is what File Explorer's
    /// windows showing it are found by: see <see cref="DockItem.RunningTarget"/>. Null for a pin
    /// that opens no folder.
    /// </summary>
    /// <param name="target">The pin's target.</param>
    /// <param name="linkTarget">Where it points, when it is a shortcut: see <see cref="ResolveLinkTarget"/>.</param>
    /// <remarks>
    /// A shortcut opens what it points at, so it is asked about that, and one the shell cannot
    /// resolve opens nothing that can be named. An executable and a command are answered
    /// without asking, being the commonest pins and never folders.
    /// </remarks>
    public static string? ExplorerFolder(string? target, string? linkTarget)
    {
        var opens = string.Equals(Path.GetExtension(target), ".lnk", StringComparison.OrdinalIgnoreCase)
            ? linkTarget
            : target;

        return opens is { Length: > 0 } path
            && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && !DockCommands.IsCommand(path)
                ? ExplorerFolderCache.GetOrAdd(path, ShellNames.FolderName)
                : null;
    }

    /// <summary>
    /// The executable behind a pinned shortcut, or <see langword="null"/> for anything that
    /// is not one.
    /// </summary>
    /// <remarks>
    /// A <c>.lnk</c> is stored as the shortcut, because that is what carries the arguments,
    /// the working directory and the icon its author chose — but nothing on the desktop runs
    /// under the shortcut's own path, so <see cref="RunningAppsService"/> needs the target to
    /// match against. See <see cref="ShellLink"/>.
    /// </remarks>
    public static string? ResolveLinkTarget(string? target)
    {
        if (target is not { Length: > 0 } path
            || !Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (LinkTargetCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var resolved = ShellLink.ResolveTarget(path);
        LinkTargetCache[path] = resolved;
        return resolved;
    }

    /// <summary>
    /// What makes two pins the same, for turning away a drop of something already on the
    /// dock. Null for a pin with nothing to compare — a Store app, or a separator.
    /// </summary>
    /// <remarks>
    /// Deliberately the stored target rather than the resolved one: a shortcut and the
    /// executable behind it are different pins on purpose, since the shortcut can carry
    /// arguments, a working directory and an icon the bare executable does not. Anything
    /// that is not a path — the edit dialog takes free text, and a web address is a
    /// legitimate thing to pin — is compared as written.
    /// </remarks>
    public static string? TargetKey(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var trimmed = target.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
        {
            return trimmed;
        }

        try
        {
            // So that a folder dropped as C:\Work\ matches one pinned as C:\Work.
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
        }
        catch (Exception e) when (e is ArgumentException or PathTooLongException or IOException)
        {
            return trimmed;
        }
    }

    /// <summary>Resolves icons for items that do not have one yet.</summary>
    /// <param name="items">The items.</param>
    /// <param name="iconSet">The set the dock draws from, or null for the apps' own icons.</param>
    public void ResolveIcons(IEnumerable<DockItem> items, IconSet? iconSet = null)
    {
        foreach (var item in items)
        {
            item.Icon ??= LoadIcon(item, iconSet);
        }
    }

    /// <summary>
    /// The icon for an item: the user's chosen image if there is one and it still exists;
    /// otherwise, for a picture, its thumbnail; otherwise, for a folder given a colour, the
    /// folder the dock draws; otherwise the icon set's, if a set is in use and has one for it;
    /// otherwise whatever the shell has for the target.
    /// </summary>
    /// <remarks>
    /// The user's own choice beats the set because it is the more particular of the two: it
    /// was made for this one item, and a set is made for everybody's. A picture's thumbnail
    /// sits between them for the same reason — it is this one file, where the most a set can
    /// match a picture by is its type, and would draw every photo on the dock the same. So does
    /// a drawn folder: its colour and symbol were chosen for this one folder. A set with
    /// nothing for an item leaves it its own icon rather than a blank, so a small set is a
    /// usable one.
    /// </remarks>
    public static ImageSource? LoadIcon(DockItem item, IconSet? iconSet = null)
    {
        if (item.IconPath is not { Length: > 0 } && ThumbnailFor(item) is { } thumbnail)
        {
            return thumbnail;
        }

        // The set's key carries when its files last changed, so an edited set is not served
        // from what was cached for the old one.
        var key = $"{iconSet?.Key}{item.IconPath}{item.ShellTarget}{item.FolderColor}{item.FolderSymbol}{item.FolderText}{item.FolderSymbolTone}";
        if (IconCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var icon = item.IconPath is { Length: > 0 } custom ? LoadImageFile(custom) : null;

        // Drawn rather than read, from nothing but the pin's own two settings — so an image
        // chosen for it that has since gone missing falls back to the folder chosen with it.
        icon ??= FolderArt.For(item.FolderColor, item.FolderSymbol, item.FolderText, item.FolderSymbolTone);

        if (icon is null && iconSet is not null)
        {
            icon = iconSet.IconFor(SubjectOf(item, iconSet));
        }

        // A command carries no target the shell could be asked about, so its image ships
        // with the application. Still behind the user's own choice, like every other icon.
        icon ??= DockCommands.IsCommand(item.ShellTarget)
            ? DockCommands.IconFor(item.ShellTarget)
            : DockPresets.IsRecycleBin(item.ShellTarget)
                ? RecycleBinIcon()
                : ShellIcons.Load(item.ShellTarget, IconPixelSize);

        // The Recycle Bin is drawn full or empty and changes between the two by itself, so it
        // is the one icon read afresh every time. A cached copy is wrong from the moment the
        // bin changes — and a bin unpinned, emptied and pinned again would come back wrong,
        // because nothing tells a dock about a pin it does not have. Forgetting the entry on
        // each change was the first answer, and that was the hole in it.
        if (!DockPresets.IsRecycleBin(item.ShellTarget))
        {
            IconCache[key] = icon;
        }

        return icon;
    }

    /// <summary>
    /// The Recycle Bin, full or empty as it is now: Windows' icon for what the bin holds,
    /// rather than the shell's icon for the bin, which no longer reliably follows it — see
    /// <see cref="RecycleBin.IconLocation"/>. The shell's is the fallback, for a location that
    /// names no icon.
    /// </summary>
    private static ImageSource? RecycleBinIcon() =>
        ShellIcons.LoadFromLocation(RecycleBin.IconLocation(RecycleBin.IsEmpty()), IconPixelSize)
        ?? ShellIcons.Load(DockPresets.RecycleBinTarget, IconPixelSize);

    /// <summary>
    /// Whether a pin's target is a picture, and so has a thumbnail it could be drawn with.
    /// </summary>
    /// <remarks>
    /// A path on this machine or a share, and nothing else: a web address can end in
    /// <c>.png</c> too, and asking the shell for its thumbnail would be a download on the
    /// thread that draws the dock.
    /// </remarks>
    public static bool OpensPicture(string? target)
    {
        if (target is not { Length: > 0 } path || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        if (!PictureTypes.TryGetValue(extension, out var picture))
        {
            picture = ShellIcons.IsPictureType(extension);
            PictureTypes[extension] = picture;
        }

        return picture;
    }

    /// <summary>
    /// Whether a target is a program Windows can start as administrator — one whose type has
    /// the shell's <c>runas</c> verb (<see cref="ShellVerbs.HasRunAs"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A shortcut counts by what it points at, and is still what is started, so it keeps its
    /// arguments; one the shell cannot resolve has nothing to say it is a program. A bare name
    /// the shell finds on the <c>PATH</c> — <c>notepad.exe</c> — counts; a web address does
    /// not, whatever it ends in.
    /// </para>
    /// <para>
    /// Not a Store app, which has no path and no type to ask: that is
    /// <see cref="CanRunAppAsAdministrator"/>.
    /// </para>
    /// </remarks>
    /// <param name="target">The pin's target.</param>
    /// <param name="linkTarget">What it points at, for a shortcut; see <see cref="ResolveLinkTarget"/>.</param>
    public static bool CanRunAsAdministrator(string? target, string? linkTarget)
    {
        var program = Path.GetExtension(target ?? string.Empty).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            ? linkTarget
            : target;

        if (program is not { Length: > 0 } path
            || (Uri.TryCreate(path, UriKind.Absolute, out var address) && !address.IsFile))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        if (!RunAsTypes.TryGetValue(extension, out var elevates))
        {
            elevates = ShellVerbs.HasRunAs(extension);
            RunAsTypes[extension] = elevates;
        }

        return elevates;
    }

    /// <summary>
    /// Whether Windows can start a Store app as administrator: whether Start's menu for it has
    /// <em>Run as administrator</em> (<see cref="ShellVerbs.ItemHasRunAs"/>). Asked once per app,
    /// and then known.
    /// </summary>
    /// <remarks>
    /// Asked on the calling thread, which must be STA, at up to a fifth of a second; the item
    /// editor asks it so, for the one app it shows. The dock learns its pins' answers off its
    /// thread instead (<see cref="LearnRunAsAdministrator"/>), and an item made before the answer
    /// is known says no until it is.
    /// </remarks>
    public static bool CanRunAppAsAdministrator(string aumid) =>
        RunAsApps.GetOrAdd(aumid, id => ShellVerbs.ItemHasRunAs($@"shell:AppsFolder\{id}"));

    /// <summary>
    /// Asks, on a thread of its own, whether each of the Store apps among some items can be
    /// started as administrator, for those not yet known, and calls back on that thread with
    /// each app's answer as it comes.
    /// </summary>
    /// <returns>False when there was nothing to ask, and nothing will be called back.</returns>
    public static bool LearnRunAsAdministrator(IEnumerable<DockItem> items, Action<string, bool> learned)
    {
        var asked = items
            .Select(item => item.Aumid)
            .OfType<string>()
            .Where(aumid => aumid.Length > 0 && !RunAsApps.ContainsKey(aumid))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (asked.Count == 0)
        {
            return false;
        }

        var thread = new Thread(() =>
        {
            foreach (var aumid in asked)
            {
                learned(aumid, CanRunAppAsAdministrator(aumid));
            }
        })
        {
            IsBackground = true,
            Name = "Store apps' Run as administrator"
        };

        // The shell's menus are apartment-threaded.
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return true;
    }

    /// <summary>
    /// A picture's thumbnail, or null for a pin that is not a picture, one whose user asked for
    /// the icon instead, or a picture the shell has no thumbnail for.
    /// </summary>
    public static ImageSource? ThumbnailFor(DockItem item)
    {
        if (item.UseIconNotThumbnail
            || item.Aumid is { Length: > 0 }
            || !OpensPicture(item.TargetPath))
        {
            return null;
        }

        var path = item.TargetPath!;
        DateTime written;
        try
        {
            // A file that is not there reads as written in 1601, which is as good a stamp as
            // any: the miss is kept until the file appears.
            written = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        if (ThumbnailCache.TryGetValue(path, out var cached) && cached.Written == written)
        {
            return cached.Image;
        }

        var image = ShellIcons.LoadThumbnail(path, IconPixelSize);
        ThumbnailCache[path] = (written, image);
        return image;
    }

    /// <summary>
    /// What an icon set is told about an item.
    /// </summary>
    /// <remarks>
    /// The two questions that cost something are asked only of a set that would use the
    /// answer: whether the target is a folder is a trip to the filesystem, and whether the
    /// Recycle Bin is empty a shell query of tens of milliseconds — see
    /// <see cref="RecycleBin.IsEmpty"/>. Neither is on the wave's path; both are on the path
    /// of every rebuild of the items, and of every change the shell announces to the bin.
    /// </remarks>
    internal static IconSubject SubjectOf(DockItem item, IconSet iconSet)
    {
        var isFolder = false;
        if (iconSet.MatchesFolders && item.TargetPath is { Length: > 0 } target)
        {
            isFolder = Directory.Exists(target);
        }

        bool? binEmpty = iconSet.MatchesRecycleBinState && DockPresets.IsRecycleBin(item.TargetPath)
            ? RecycleBin.IsEmpty()
            : null;

        return new IconSubject(item.TargetPath, item.LinkTarget, item.Aumid, isFolder, binEmpty);
    }

    /// <summary>
    /// Forgets what has been read from the shell — icons, thumbnails, where shortcuts point and
    /// which targets are folders, and what opens a document — so a changed file on disk is picked up.
    /// </summary>
    public static void ClearIconCache()
    {
        IconCache.Clear();
        ThumbnailCache.Clear();
        LinkTargetCache.Clear();
        ExplorerFolderCache.Clear();
        ShortcutAppIdCache.Clear();
        HandlerCache.Clear();
    }

    /// <summary>
    /// Loads an icon from a file the user picked.
    /// </summary>
    /// <remarks>
    /// Executables, shortcuts and .ico files go through the shell, which knows how to pick
    /// a frame out of them; ordinary images are decoded directly. Decoding is done on load
    /// so the file is not left locked — a dock holding a handle on someone's PNG would stop
    /// them moving or deleting it.
    /// </remarks>
    public static ImageSource? LoadImageFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".exe" or ".dll" or ".lnk" or ".ico")
        {
            return ShellIcons.Load(path, IconPixelSize);
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = IconPixelSize;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception e) when (e is NotSupportedException or IOException or UriFormatException or ArgumentException)
        {
            // Not an image this machine can decode; fall back to the target's own icon.
            return null;
        }
    }

    /// <summary>
    /// Prefers the executable's own description ("Windows Command Processor") over a
    /// hard-coded name, so the dock is labelled in the user's display language.
    /// </summary>
    private static string DescribeApp(string path, string fallback)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var description = info.FileDescription?.Trim();
            return string.IsNullOrEmpty(description) ? fallback : description;
        }
        catch (FileNotFoundException)
        {
            return fallback;
        }
    }
}
