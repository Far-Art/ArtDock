using System.Diagnostics;
using System.Windows;
using ArtDock.Dock;
using ArtDock.Services;

namespace ArtDock.Interop;

/// <summary>
/// Which program has the window in front: when that window fills a display, for the edge, and
/// whatever it fills, for the hotkeys.
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

        return ProgramOf(window);
    }

    /// <summary>
    /// The executable of the program in front, whatever the size of its window and whichever
    /// display it is on; null when nothing is, or the desktop is.
    /// </summary>
    /// <remarks>
    /// What the hotkeys stand down for: the keys go to the window in front, wherever it is, so a
    /// program on the Exclusions page has them whether or not it fills a display — asked on
    /// 2026-10-02. The desktop is ruled out as <see cref="FillingDisplay"/> rules it out. Asked as
    /// often as that, and as cheap: the program behind a window is looked up once.
    /// </remarks>
    public string? InFront()
    {
        var window = WindowsApi.GetForegroundWindow();
        return window == 0 || IsDesktop(window) ? null : ProgramOf(window);
    }

    /// <summary>The executable of the program a window belongs to, kept until another window asks.</summary>
    private string? ProgramOf(nint window)
    {
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
    /// How much of the dock's display the window in front has taken — all of the work area,
    /// as a maximized window does, or the whole display for itself, as a fullscreen one does —
    /// whichever program it belongs to.
    /// </summary>
    /// <param name="display">The dock's display, in physical pixels.</param>
    /// <param name="workArea">Its work area, likewise.</param>
    /// <remarks>
    /// <para>
    /// What the dock hides for, whatever <em>Always on top</em> says, until the pointer brings
    /// it back. Any program, not only the ones on the Exclusions page — those it hides for as
    /// well, by the list's own test (<c>DockWindow.IsFullscreenAppInFront</c>), and stays away.
    /// Asked on 2026-09-30 for fullscreen windows, and the same day for maximized ones: the
    /// dock floats above the taskbar, inside the work area, so a window maximized with the
    /// taskbar showing lies right over it, and <em>Always on top</em> had the dock drawn over
    /// the bottom of that window.
    /// </para>
    /// <para>
    /// The work area rather than the display: a maximized window stops at the taskbar, and a
    /// fullscreen one covers more than the work area, so one question answers for both. That
    /// retired the one this used to ask — whether a window filling the display was fullscreen
    /// or only maximized, which took a window's style and state to tell, and was easy to get
    /// wrong: a game's borderless fullscreen is a maximized popup with no title bar, so "not
    /// maximized" never meant fullscreen — since both now get the same answer. Geometry first,
    /// and only then the desktop ruled out, which settles it for nearly every window without
    /// reading its class: it is asked four times a second for as long as the dock runs.
    /// </para>
    /// <para>
    /// The two were told apart again on 2026-10-01, for the handle alone, which is not drawn over
    /// a fullscreen window — a video, mostly — and still marks a dock hidden for a maximized one
    /// (<see cref="FullscreenApps.IsFullscreen"/>). The window's state and style are read only
    /// for a window that covers the whole display, which is rare enough not to count.
    /// </para>
    /// </remarks>
    public static FrontFill Filling(Rect display, Rect workArea)
    {
        var window = WindowsApi.GetForegroundWindow();
        if (window == 0 || !NativeMethods.GetWindowRect(window, out var bounds))
        {
            return FrontFill.None;
        }

        var rect = new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        if (!FullscreenApps.Fills(rect, workArea) || IsDesktop(window))
        {
            return FrontFill.None;
        }

        if (!FullscreenApps.Fills(rect, display))
        {
            return FrontFill.Maximized;
        }

        var style = (uint)NativeMethods.GetWindowLongPtr(window, NativeMethods.GWL_STYLE);

        return FullscreenApps.IsFullscreen(
            rect,
            display,
            workArea,
            maximized: WindowsApi.IsZoomed(window),
            captioned: (style & WS_CAPTION) == WS_CAPTION)
            ? FrontFill.Fullscreen
            : FrontFill.Maximized;
    }

    /// <summary>
    /// True while the window in front is one of the shell's passing surfaces — the taskbar,
    /// the task switcher, Task View, Start, search, the notification centre, the lock screen —
    /// which come to the front over whatever the user is working in, and go.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What the dock holds still for: while one is in front, the dock does neither of the things
    /// the window in front would otherwise have it do. Since the dock began to hide for a window
    /// filling its display, on 2026-09-30, each change of what is in front can slide it away or
    /// back, and these are not the user leaving the window they were in. Read off this machine
    /// the same day: Alt+Tab's switcher is a window of Explorer's, class
    /// <c>XamlExplorerHostIslandWindow</c>, titled <i>Task Switching</i>, and exactly the main
    /// display's work area — taken for a maximized window it would have slid the dock away for
    /// every Alt+Tab, and taken for nothing it would have slid a dock hidden for a maximized
    /// window up for every Alt+Tab over one. A click on the taskbar is the same question.
    /// </para>
    /// <para>
    /// By class, which is all these have in common. Start, search, the notification centre and
    /// the lock screen are top-level <c>Windows.UI.Core.CoreWindow</c>s of their hosts; Windows
    /// 10's switcher and Task View are <c>MultitaskingViewFrame</c>; <c>ForegroundStaging</c> is
    /// Explorer's own, passed through while the foreground moves. A Store app's window is not a
    /// <c>CoreWindow</c> at the top level — its frame, <c>ApplicationFrameWindow</c>, is — and a
    /// File Explorer window is <c>CabinetWClass</c>, so no program's window is caught. The
    /// desktop is not one of these: clicking the wallpaper is leaving the window in front, and
    /// brings the dock back (<see cref="IsDesktop"/>).
    /// </para>
    /// </remarks>
    public static bool IsPassingShellInFront()
    {
        var window = WindowsApi.GetForegroundWindow();
        return window != 0
            && WindowsApi.GetWindowClass(window) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
                or "XamlExplorerHostIslandWindow" or "MultitaskingViewFrame" or "ForegroundStaging"
                or "Windows.UI.Core.CoreWindow" or "TopLevelWindowForOverflowXamlIsland"
                or "NotifyIconOverflowWindow";
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
