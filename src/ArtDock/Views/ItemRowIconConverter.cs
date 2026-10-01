using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ArtDock.IconSets;
using ArtDock.Services;

namespace ArtDock.Views;

/// <summary>
/// The icon a row of the settings dialog's item list shows: the one the dock draws the item
/// with — the user's chosen image, a picture's thumbnail, a folder in its own colour and symbol,
/// the icon set's or the shell's, in the dock's order — and nothing for a separator, whose row
/// draws its rule instead.
/// </summary>
/// <remarks>
/// <para>
/// Bound to the row's pin and to the icon set chosen on the Appearance page, so that choosing
/// another set redraws the rows as it redraws the dock, without the list being rebuilt.
/// </para>
/// <para>
/// Cheap: the dock has already asked for every pin's icon in this process, and
/// <see cref="PinnedAppsService.LoadIcon"/> keeps what it was asked — all but the Recycle Bin's,
/// which it reads afresh because the bin changes by itself. A row added in the dialog asks
/// first, and the dock then finds the answer kept.
/// </para>
/// </remarks>
public sealed class ItemRowIconConverter : IMultiValueConverter
{
    /// <summary>
    /// The icon set a list of pins draws its icons from: set on the list, and read by each row
    /// through its ancestor list — as the rows already read the order lock through the list's
    /// <c>Tag</c>, a data template's namescope keeping it from naming anything outside.
    /// </summary>
    public static readonly DependencyProperty IconSetProperty = DependencyProperty.RegisterAttached(
        "IconSet", typeof(IconSet), typeof(ItemRowIconConverter));

    public static IconSet? GetIconSet(DependencyObject list) => (IconSet?)list.GetValue(IconSetProperty);

    public static void SetIconSet(DependencyObject list, IconSet? set) => list.SetValue(IconSetProperty, set);

    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length > 0 && values[0] is PinnedAppSetting { IsSeparator: false } pin
            ? PinnedAppsService.LoadIcon(PinnedAppsService.ToDockItem(pin), values.Length > 1 ? values[1] as IconSet : null)
            : null;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
