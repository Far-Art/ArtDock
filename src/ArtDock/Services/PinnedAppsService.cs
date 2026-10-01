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

    /// <summary>Projects a stored pin onto the model the dock draws from.</summary>
    public static DockItem ToDockItem(PinnedAppSetting app) => new()
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
        LinkTarget = ResolveLinkTarget(app.TargetPath),
        FontFamily = app.FontFamily,
        FontSize = app.FontSize,
        FontStyle = app.FontStyle,
        IsSeparator = app.IsSeparator
    };

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
    /// Forgets what has been read from the shell — icons, thumbnails, and where shortcuts
    /// point — so a changed file on disk is picked up.
    /// </summary>
    public static void ClearIconCache()
    {
        IconCache.Clear();
        ThumbnailCache.Clear();
        LinkTargetCache.Clear();
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
