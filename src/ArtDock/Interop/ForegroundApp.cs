using System.Diagnostics;
using System.Windows;
using ArtDock.Services;

namespace ArtDock.Interop;

/// <summary>
/// Which program has the window in front, when that window fills a display.
/// </summary>
/// <remarks>
/// <para>
/// Asked by auto-hide on each tick the pointer spends at the edge, so it is kept cheap: the
/// geometry is checked before anything else, which settles it for every window that is not
/// fullscreen, and the program behind a window is looked up once and kept until a different
/// window comes to the front.
/// </para>
/// <para>
/// The foreground window, rather than whichever window is topmost on the display: overlays
/// — a game's frame counter, a voice chat's — are windows of their own laid over the game,
/// and they are never the foreground. The cost is that a fullscreen game left behind while
/// another display's window has focus no longer counts, and holding the edge over it lifts
/// the dock. Whoever is working on the other display has stepped away from the game.
/// </para>
/// </remarks>
internal sealed class ForegroundApp
{
    private nint _window;
    private uint _processId;
    private string? _path;

    /// <summary>
    /// The executable of the program in front, if its window covers the whole of
    /// <paramref name="display"/>; otherwise null.
    /// </summary>
    /// <param name="display">The display's bounds in physical pixels.</param>
    /// <remarks>
    /// Physical pixels on both sides. The dock declares <c>PerMonitorV2</c>, so
    /// <c>GetWindowRect</c> reports every window unscaled, whatever the window's own program
    /// declares — a game that is not DPI aware included.
    /// </remarks>
    public string? FillingDisplay(Rect display)
    {
        var window = WindowsApi.GetForegroundWindow();
        if (window == 0
            || !NativeMethods.GetWindowRect(window, out var bounds)
            || !FullscreenApps.Fills(
                new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top),
                display)
            || IsDesktop(window))
        {
            return null;
        }

        WindowsApi.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return null;
        }

        if (window != _window || processId != _processId)
        {
            _window = window;
            _processId = processId;
            _path = ExecutableOf(processId);
        }

        return _path;
    }

    /// <summary>A title bar: <c>WS_BORDER | WS_DLGFRAME</c>.</summary>
    private const uint WS_CAPTION = 0x00C0_0000;

    /// <summary>
    /// True when the window in front has taken the whole of <paramref name="display"/> for
    /// itself — a game, a video, a presentation — whichever program it belongs to.
    /// </summary>
    /// <param name="display">The display's bounds in physical pixels.</param>
    /// <remarks>
    /// <para>
    /// What the handle an auto-hidden dock leaves behind steps aside for. Any program, not only
    /// the ones on the Exclusions page — those it steps aside for as well, by the list's own
    /// test (<c>DockWindow.IsFullscreenAppInFront</c>). A topmost window over a fullscreen game
    /// is also what stops the game's frames going straight to the display.
    /// </para>
    /// <para>
    /// Not a maximized window with a title bar, which fills the display where the taskbar hides
    /// itself and is an ordinary window there — see <see cref="FullscreenApps.IsFullscreen"/>,
    /// and for why "maximized" alone is the wrong question. Geometry and window state only, with
    /// no program looked up, so it is cheap enough to ask a few times a second.
    /// </para>
    /// </remarks>
    public static bool IsFullscreen(Rect display)
    {
        var window = WindowsApi.GetForegroundWindow();
        if (window == 0 || !NativeMethods.GetWindowRect(window, out var bounds) || IsDesktop(window))
        {
            return false;
        }

        var style = (uint)NativeMethods.GetWindowLongPtr(window, NativeMethods.GWL_STYLE);

        return FullscreenApps.IsFullscreen(
            new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top),
            display,
            maximized: WindowsApi.IsZoomed(window),
            captioned: (style & WS_CAPTION) == WS_CAPTION);
    }

    /// <summary>
    /// The desktop covers every display, and it is the foreground whenever it was the last
    /// thing clicked. Only a list naming Explorer would notice — but that is a list somebody
    /// made to keep the dock down over a fullscreen Explorer window, not over the wallpaper.
    /// </summary>
    private static bool IsDesktop(nint window) =>
        window == WindowsApi.GetShellWindow()
        || WindowsApi.GetWindowClass(window) is "WorkerW" or "Progman";

    /// <summary>
    /// The program's full path, or failing that its file name.
    /// </summary>
    /// <remarks>
    /// The path needs a handle to the process, and a game's anti-cheat may refuse one — the
    /// games this list exists for are exactly the programs likeliest to. The name comes from
    /// the system's list of processes instead, which needs no handle, and it is all the match
    /// uses (<see cref="FullscreenApps.Contains"/>). It costs a walk of every process, which
    /// is why it is the fallback, and why the result is kept.
    /// </remarks>
    private static string? ExecutableOf(uint processId)
    {
        if (WindowsApi.TryGetProcessPath(processId) is { } path)
        {
            return path;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName + ".exe";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            // Gone between the window being read and the process being asked about.
            return null;
        }
    }
}
