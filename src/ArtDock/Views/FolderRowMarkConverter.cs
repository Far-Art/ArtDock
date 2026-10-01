using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ArtDock.Interop;
using ArtDock.Services;

namespace ArtDock.Views;

/// <summary>
/// Whether a row of the settings dialog's item list carries the folder mark at its far end:
/// shown for a pin that opens a folder, and collapsed for anything else.
/// </summary>
/// <remarks>
/// <para>
/// The row's icon does not always say it. A folder drawn in a colour of its own with a symbol on
/// its front, or one an icon set draws however it likes, reads first as that colour or that
/// picture; the mark says what it opens whatever it looks like.
/// </para>
/// <para>
/// A folder is what the item editor counts as one, so the mark is on exactly the rows whose items
/// it offers to draw as a folder. Whether a target is one is a trip to the shell, so it is kept
/// here, once per target, and a list redrawn on every change of the dialog does not ask again.
/// </para>
/// </remarks>
public sealed class FolderRowMarkConverter : IValueConverter
{
    /// <summary>
    /// The mark: <em>Folder</em>, the outline Windows draws for one, in the theme's symbol font.
    /// </summary>
    /// <remarks>
    /// In Segoe Fluent Icons and in Segoe MDL2 Assets, which the theme's font falls back to, as
    /// <c>FolderArtTests</c> holds it.
    /// </remarks>
    public const string Glyph = "";

    private static readonly Dictionary<string, bool> Folders = new(StringComparer.OrdinalIgnoreCase);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is PinnedAppSetting { IsSeparator: false } pin && IsFolder(pin)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>
    /// Whether a pin opens a folder: one the dock draws certainly does, and otherwise the shell is
    /// asked, as the item editor asks it — so <c>shell:Downloads</c> counts, and This PC does not.
    /// </summary>
    public static bool IsFolder(PinnedAppSetting pin)
    {
        if (pin.FolderColor is { Length: > 0 })
        {
            return true;
        }

        if (pin.Aumid is { Length: > 0 } || pin.TargetPath is not { Length: > 0 } target)
        {
            return false;
        }

        lock (Folders)
        {
            if (!Folders.TryGetValue(target, out var folder))
            {
                folder = ShellNames.IsFileSystemFolder(target);
                Folders[target] = folder;
            }

            return folder;
        }
    }
}
