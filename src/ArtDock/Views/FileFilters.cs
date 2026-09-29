using ArtDock.Localization;

namespace ArtDock.Views;

/// <summary>
/// The filters the file dialogs offer, with their descriptions in the dock's language.
/// </summary>
/// <remarks>
/// The patterns are the same in every language and stay in code; only the words in front of
/// them are translated, so a translation cannot break a filter by mistyping a pattern.
/// </remarks>
internal static class FileFilters
{
    /// <summary>Anything at all, with applications and shortcuts as the second choice.</summary>
    /// <remarks>
    /// Everything first: anything the machine can open can be pinned, so a filter that showed
    /// only <c>.exe</c> and <c>.lnk</c> would be describing a rule the dock no longer has.
    /// </remarks>
    public static string Pinnable => $"{AllFiles}|{Applications}";

    /// <summary>Pictures an item's icon can be taken from, then anything.</summary>
    public static string Images =>
        $"{Localizer.Get("FileDialog.ImagesAndIcons")} (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.exe;*.dll)"
        + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.exe;*.dll"
        + $"|{AllFiles}";

    /// <summary>Programs, then anything — for the programs the dock stays down for.</summary>
    public static string Programs =>
        $"{Localizer.Get("FileDialog.Programs")} (*.exe)|*.exe|{AllFiles}";

    /// <summary>Settings files, then anything.</summary>
    public static string Settings =>
        $"{Localizer.Get("FileDialog.SettingsFiles")} (*.json)|*.json|{AllFiles}";

    private static string AllFiles => $"{Localizer.Get("FileDialog.AllFiles")} (*.*)|*.*";

    private static string Applications =>
        $"{Localizer.Get("FileDialog.AppsAndShortcuts")} (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url";
}
