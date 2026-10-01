using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ArtDock.IconSets;
using ArtDock.Interop;
using ArtDock.Services;

namespace ArtDock.Views;

/// <summary>
/// The icon a row of the settings dialog's item list shows: for a folder, the one the dock draws
/// it with — its own colour and symbol if it has been customized, else the icon set's or the
/// shell's — and nothing for anything else.
/// </summary>
/// <remarks>
/// <para>
/// Bound to the row's pin and to the icon set chosen on the Appearance page, so that choosing
/// another set redraws the rows as it redraws the dock, without the list being rebuilt.
/// </para>
/// <para>
/// Cheap: the dock has already asked for every pin's icon in this process, and
/// <see cref="PinnedAppsService.LoadIcon"/> keeps what it was asked. What is not kept there —
/// whether a target is a folder at all, which is a trip to the shell — is kept here, once per
/// target, so a list redrawn on every change of the dialog does not ask again.
/// </para>
/// </remarks>
public sealed class FolderRowIconConverter : IMultiValueConverter
{
    private static readonly Dictionary<string, bool> Folders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The icon set a list of pins draws its folders from: set on the list, and read by each row
    /// through its ancestor list — as the rows already read the order lock through the list's
    /// <c>Tag</c>, a data template's namescope keeping it from naming anything outside.
    /// </summary>
    public static readonly DependencyProperty IconSetProperty = DependencyProperty.RegisterAttached(
        "IconSet", typeof(IconSet), typeof(FolderRowIconConverter));

    public static IconSet? GetIconSet(DependencyObject list) => (IconSet?)list.GetValue(IconSetProperty);

    public static void SetIconSet(DependencyObject list, IconSet? set) => list.SetValue(IconSetProperty, set);

    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length > 0 && values[0] is PinnedAppSetting { IsSeparator: false } pin && IsFolder(pin)
            ? PinnedAppsService.LoadIcon(PinnedAppsService.ToDockItem(pin), values.Length > 1 ? values[1] as IconSet : null)
            : null;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
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
