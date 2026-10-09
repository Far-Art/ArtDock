using System.Text.Json;

namespace ArtDock.Services;

/// <summary>
/// What the settings dialog would save, less what changes in it without the user changing
/// anything — so that two can be compared to tell whether there is anything to save.
/// </summary>
/// <remarks>
/// <para>
/// For the update, which closes the dialog and so would take its unsaved changes with it: it
/// asks whether to save them first, and only when there are some. The dialog takes one of these
/// as it opens and another when asked, and compares the two — itself against itself, so a
/// value it reads one way and writes another (a display stored by name alone and written with
/// its path) is the same both times rather than a change nobody made.
/// </para>
/// <para>
/// Left out: which page is open, which is where the user is rather than something they
/// changed; <em>Run at login</em>, which the dialog follows from Windows while it is open and
/// is compared with Windows instead; and the pinned items unless the list has been edited
/// here, since until then the dialog follows the dock's own changes to them.
/// </para>
/// </remarks>
public static class SettingsFingerprint
{
    /// <param name="settings">What the dialog would save.</param>
    /// <param name="withPins">Whether the pinned items count: true once the list has been edited in the dialog.</param>
    public static string Of(DockSettings settings, bool withPins)
    {
        var copy = settings.Clone();
        copy.SettingsPage = 0;
        copy.RunAtLogin = false;
        if (!withPins)
        {
            copy.PinnedApps = [];
        }

        return JsonSerializer.Serialize(copy, DockSettingsContext.Default.DockSettings);
    }
}
