namespace ArtDock.Dock;

/// <summary>What a click on an item asks for, by the button and the keys held with it.</summary>
public enum DockClick
{
    /// <summary>A window of it brought forward, or it started when it has none.</summary>
    Open,

    /// <summary>It started again, beside the windows it has: Shift, or the middle button.</summary>
    NewWindow,

    /// <summary>
    /// It started as administrator when it has no window, and brought forward as for
    /// <see cref="Open"/> when it has: Ctrl+Shift.
    /// </summary>
    AsAdministrator
}

/// <summary>Reads a click as the taskbar reads one.</summary>
/// <remarks>
/// <para>
/// Ctrl+Shift+click is how the taskbar and Start start a program as administrator, and Shift+click
/// or the middle button how the taskbar opens another window of an app. Ctrl alone is a plain
/// click: the taskbar's Ctrl+click steps through a group's windows, which the dock's click already
/// does.
/// </para>
/// <para>
/// One difference, asked for on 2026-10-03 of the menu's <em>Run as administrator</em> and kept
/// here: as administrator never starts a second copy. The taskbar's Ctrl+Shift+click starts one
/// beside the window that is open; the dock's brings that window forward, as a click on a pin
/// ticked to run as administrator does.
/// </para>
/// </remarks>
public static class DockClicks
{
    /// <summary>What a click means, with the keys held as the button came up.</summary>
    /// <param name="middle">Whether it was the middle button rather than the left.</param>
    public static DockClick Of(bool middle, bool control, bool shift) =>
        control && shift ? DockClick.AsAdministrator
        : middle || shift ? DockClick.NewWindow
        : DockClick.Open;
}
