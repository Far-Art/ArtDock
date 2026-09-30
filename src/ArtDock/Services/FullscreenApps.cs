using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ArtDock.Services;

/// <summary>
/// The applications the dock stays down for while they fill its display — games, mostly,
/// which scroll when the pointer reaches the edge of the screen.
/// </summary>
/// <remarks>
/// <para>
/// Holding the pointer against the bottom edge brings a hidden dock back and lifts a covered
/// one above whatever covers it, the game in front included. In a game that scrolls its view
/// when the pointer reaches the edge, that is the dock rising over the game every time the
/// map is scrolled down. The list is the user's answer: while one of these is in front and
/// fills the dock's display, the edge does nothing.
/// </para>
/// <para>
/// Pure, so it can be tested; reading the window in front is <c>Interop.ForegroundApp</c>'s.
/// </para>
/// </remarks>
public static class FullscreenApps
{
    /// <summary>
    /// True when <paramref name="processPath"/> is one of <paramref name="apps"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By the executable's file name, not its whole path. Games move: an update unpacks into a
    /// versioned folder beside the old one, a Steam library is moved to another drive, a
    /// launcher runs the real game from somewhere of its own. A path that stopped matching
    /// would fail in the direction that matters — the dock rising over the game again, with
    /// nothing to say why. The price is that another program sharing the file name is matched
    /// as well, which fails the harmless way: the dock stays down over that one too.
    /// </para>
    /// <para>
    /// The paths are kept in the list all the same, so the settings dialog can say which
    /// program each entry is. An entry that is only a file name — written by hand — matches
    /// just as well.
    /// </para>
    /// </remarks>
    public static bool Contains(IEnumerable<string?> apps, string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }

        var name = KeyOf(processPath);
        foreach (var app in apps)
        {
            if (!string.IsNullOrWhiteSpace(app)
                && string.Equals(KeyOf(app), name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What an entry is matched on: its executable's file name.</summary>
    public static string KeyOf(string path) => Path.GetFileName(path.Trim());

    /// <summary>
    /// True when a window covers the whole of an area — a display, or a display's work area.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both in physical pixels. Covering rather than equalling: a borderless window can reach
    /// a pixel or two past the display, a maximized one hangs its invisible resize border past
    /// the work area, and one spread across several displays covers each of them.
    /// </para>
    /// <para>
    /// Asked of two areas. The Exclusions page asks it of the display's whole bounds, so a
    /// listed program stands the edge down only when it is fullscreen or borderless — a
    /// maximized window stops at the taskbar, a whole taskbar short of the bottom, and the
    /// pointer at the edge is then on the taskbar rather than on the game. Where the taskbar
    /// hides itself a maximized window does fill the display, and then it is counted, which is
    /// what it looks like. The dock's own stand-aside asks it of the work area, which a
    /// maximized window and a fullscreen one both cover (<c>ForegroundApp.FillsWorkArea</c>).
    /// </para>
    /// </remarks>
    public static bool Fills(Rect window, Rect display) =>
        !display.IsEmpty
        && display.Width > 0
        && display.Height > 0
        && window.Contains(display);

    /// <summary>
    /// What to call a program in the settings dialog: the description it carries, which is
    /// what Task Manager shows, or its file name when it has none.
    /// </summary>
    public static string DisplayName(string path) =>
        Description(path) ?? Path.GetFileNameWithoutExtension(KeyOf(path));

    /// <summary>The description a program carries, or null when it carries none.</summary>
    public static string? Description(string path)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            return string.IsNullOrEmpty(description) ? null : description;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Uninstalled since it was listed, or somewhere this account cannot read: the
            // file name will have to say what it was.
            return null;
        }
    }
}
