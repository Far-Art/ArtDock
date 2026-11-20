using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArtDock.Interop;
using ArtDock.Localization;

namespace ArtDock.Views;

/// <summary>
/// An app's jump list in the dock's menu, as the taskbar shows it above its own entries: each
/// group under a greyed heading, each entry with its icon and in the app's own words.
/// </summary>
public static class JumpListMenu
{
    /// <summary>The widest an entry's words are drawn before they are cut short, in DIPs.</summary>
    /// <remarks>
    /// The taskbar cuts a long entry with an ellipsis rather than widening the menu for it; a
    /// tab's title in a browser's <em>Recently closed</em> can be a sentence.
    /// </remarks>
    private const double MostWidth = 320;

    /// <summary>The size an entry's icon is read at: twice the menu's 16, for a 200% display.</summary>
    private const int IconPixels = 32;

    /// <summary>The most icons kept between menus before they are all let go.</summary>
    private const int MostIcons = 400;

    /// <summary>
    /// Icons already read, by where they came from. A recent file's icon is its type's, and the
    /// menu is built on every right-click.
    /// </summary>
    private static readonly Dictionary<string, ImageSource> Icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Puts a jump list's groups into a menu.
    /// </summary>
    /// <param name="menu">The menu, which they are added to the end of.</param>
    /// <param name="categories">The groups, as <see cref="JumpLists.Read"/> gives them.</param>
    /// <param name="appIcon">The app's own icon, for an entry that names none — as the taskbar does.</param>
    /// <param name="launch">Starts an entry once it is chosen.</param>
    /// <returns>True when anything was added.</returns>
    public static bool AddTo(
        ContextMenu menu, IReadOnlyList<JumpListCategory> categories, ImageSource? appIcon, Action<JumpListEntry> launch)
    {
        var added = false;
        foreach (var category in categories)
        {
            menu.Items.Add(MenuHost.Heading(Heading(category)));
            foreach (var entry in category.Entries)
            {
                if (entry.IsSeparator)
                {
                    menu.Items.Add(new Separator());
                    continue;
                }

                var words = new TextBlock
                {
                    Text = entry.Title,
                    MaxWidth = MostWidth,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                var chosen = entry;
                var item = MenuHost.Item(words, MenuIcons.Picture(IconOf(entry) ?? appIcon), () => launch(chosen));

                // Cut short, the whole of it is a pause away.
                item.ToolTip = entry.Title.Length > 40 ? entry.Title : null;
                menu.Items.Add(item);
            }

            added = true;
        }

        return added;
    }

    /// <summary>A group's heading: the app's own name for one of its own, Windows' for the rest.</summary>
    private static string Heading(JumpListCategory category) => category.Group switch
    {
        JumpListGroup.Pinned => Localizer.Get("Menu.JumpList.Pinned"),
        JumpListGroup.Recent => Localizer.Get("Menu.JumpList.Recent"),
        JumpListGroup.Frequent => Localizer.Get("Menu.JumpList.Frequent"),
        JumpListGroup.Tasks => Localizer.Get("Menu.JumpList.Tasks"),
        _ => category.Name ?? ""
    };

    /// <summary>
    /// An entry's icon: the one it names, or else its target's — a file's type, a folder, a
    /// program's own — or null for none, which the caller fills with the app's.
    /// </summary>
    /// <remarks>
    /// A target is asked only when it is a file or folder that is there: a shell item's name
    /// that is not a path, or a file since deleted, can cost the shell a search or a network
    /// wait, on the click that opens the menu.
    /// </remarks>
    private static ImageSource? IconOf(JumpListEntry entry)
    {
        if (entry.IconLocation is { Length: > 0 } location)
        {
            return Cached(location, () => ShellIcons.LoadFromLocation(location, IconPixels));
        }

        if (entry.Path is { Length: > 0 } path && Path.IsPathFullyQualified(path)
            && !path.StartsWith(@"\\", StringComparison.Ordinal)
            && (File.Exists(path) || Directory.Exists(path)))
        {
            // A program's own icon, a folder's own; a document's, its type's, kept once per type.
            var extension = Path.GetExtension(path);
            var key = File.Exists(path) && extension.Length > 1 && !OwnIconTypes.Contains(extension)
                ? "type:" + extension
                : path;
            return Cached(key, () => ShellIcons.Load(path, IconPixels));
        }

        return null;
    }

    /// <summary>The types of file whose icon is the file's own rather than its type's.</summary>
    private static readonly HashSet<string> OwnIconTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".url", ".ico", ".appref-ms", ".msc", ".cpl"
    };

    private static ImageSource? Cached(string key, Func<ImageSource?> load)
    {
        if (Icons.TryGetValue(key, out var icon))
        {
            return icon;
        }

        // Not remembered when there is none, so one bad answer does not stay.
        icon = load();
        if (icon is not null)
        {
            if (Icons.Count >= MostIcons)
            {
                Icons.Clear();
            }

            Icons[key] = icon;
        }

        return icon;
    }
}
