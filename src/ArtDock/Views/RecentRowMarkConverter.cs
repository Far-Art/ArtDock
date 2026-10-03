using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ArtDock.Localization;
using ArtDock.Services;

namespace ArtDock.Views;

/// <summary>
/// Whether a row of the settings dialog's item list carries the <em>New</em> mark: shown for a
/// pin added within the last day (<see cref="PinnedAppSetting.IsRecent"/>), collapsed for
/// anything else. Asked for on 2026-10-03, so what was just added is found at a glance.
/// </summary>
/// <remarks>
/// Asked as a row is drawn, against the clock then: a mark that runs out while the dialog stays
/// open goes when the list next draws that row, which a day's mark can afford.
/// </remarks>
public sealed class RecentRowMarkConverter : IValueConverter
{
    /// <summary>
    /// What to give back instead of a visibility: the row's status for a screen reader —
    /// <em>Recently added</em>, or nothing.
    /// </summary>
    public bool AsStatus { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var recent = value is PinnedAppSetting pin && pin.IsRecent(DateTimeOffset.UtcNow);
        if (AsStatus)
        {
            return recent ? Localizer.Get("Settings.Items.New.Status") : string.Empty;
        }

        return recent ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
