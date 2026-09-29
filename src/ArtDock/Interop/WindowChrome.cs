using System.Windows;
using System.Windows.Interop;

namespace ArtDock.Interop;

/// <summary>
/// Owns every Win32-level property of the dock window: always on top, absent from the
/// taskbar and Alt-Tab, and never stealing focus.
/// </summary>
/// <remarks>
/// Per-pixel transparency itself is WPF's job (<c>AllowsTransparency</c>), which is why
/// this type is far smaller than its WinUI 3 predecessor — see
/// <c>docs/step-2-transparency.md</c> for what that cost.
/// </remarks>
public sealed class WindowChrome
{
    private readonly nint _hwnd;

    public WindowChrome(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        if (_hwnd == 0)
        {
            throw new InvalidOperationException(
                "Window handle not created yet — construct WindowChrome from SourceInitialized or later.");
        }
    }

    public nint Hwnd => _hwnd;

    /// <summary>
    /// Strips the window down to a bare floating surface: no taskbar button, no Alt-Tab
    /// entry, and no focus theft on click.
    /// </summary>
    /// <remarks>
    /// <c>WS_EX_NOACTIVATE</c> is what makes "click a dock icon to switch to a running app"
    /// work cleanly — without it the dock grabs the foreground first and the handoff to the
    /// target window flickers. It has one significant consequence: WPF stops routing mouse
    /// input to this window entirely, so the dock reads clicks from the raw window messages
    /// instead (see <c>DockWindow.OnWindowMessage</c>).
    /// </remarks>
    public void ApplyDockStyles()
    {
        var exStyle = (uint)NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, (nint)exStyle);

        BringToTop();
    }

    /// <summary>
    /// Whether the dock floats above other windows.
    /// </summary>
    /// <remarks>
    /// Remembered rather than applied here: WPF's own <c>Window.Topmost</c> does the work,
    /// and this is what <see cref="BringToTop"/> reads so it lifts the dock to the top of
    /// the right band rather than always to the top of everything.
    /// </remarks>
    public bool Topmost { get; set; } = true;

    /// <summary>Re-asserts the dock's place in the z-order. Cheap enough to call whenever
    /// the foreground changes.</summary>
    public void BringToTop() =>
        NativeMethods.SetWindowPos(
            _hwnd,
            Topmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_TOP,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

    /// <summary>
    /// The nearest window above the dock that could be covering it, or 0 when nothing is —
    /// which is when the dock is already at the top of its band.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What <see cref="BringToTop"/> lifts the dock over, taken just before, so it can be put
    /// back under the same window with <see cref="GoUnder"/>. Ours are skipped — the sheet,
    /// the menu host and the dialogs are not what anyone means by the window the dock was
    /// under — and so are windows that are nowhere on screen: hidden ones, and cloaked ones,
    /// which is what a window on another virtual desktop is.
    /// </para>
    /// <para>
    /// Windows of the other band are passed over — none is a window the dock could be put
    /// back under without changing band itself — but passed over, not stopped at. Stopping
    /// at the first topmost window assumes the z-order is two clean bands, and nothing here
    /// needs to assume that: an owned window is kept above its owner whatever its band.
    /// </para>
    /// </remarks>
    public nint WindowAbove()
    {
        var band = IsTopmostWindow(_hwnd);
        var ownProcess = (uint)Environment.ProcessId;

        for (var window = Above(_hwnd); window != 0; window = Above(window))
        {
            if (IsTopmostWindow(window) != band
                || !WindowsApi.IsWindowVisible(window)
                || WindowsApi.IsCloaked(window))
            {
                continue;
            }

            WindowsApi.GetWindowThreadProcessId(window, out var process);
            if (process != ownProcess)
            {
                return window;
            }
        }

        return 0;
    }

    /// <summary>
    /// True when putting the dock under <paramref name="window"/> would move it down: the
    /// window still exists, is in the dock's band, and is below it now.
    /// </summary>
    /// <remarks>
    /// Only ever down. If the window is above the dock already — it was clicked, and came
    /// forward over it — the dock is covered by it and there is nothing to undo. Putting the
    /// dock back directly under it regardless would lift it over anything brought forward
    /// since, which is the dock claiming a place it was never given.
    /// </remarks>
    public bool CanGoUnder(nint window)
    {
        if (window == 0
            || !WindowsApi.IsWindow(window)
            || IsTopmostWindow(window) != IsTopmostWindow(_hwnd))
        {
            return false;
        }

        for (var above = Above(_hwnd); above != 0; above = Above(above))
        {
            if (above == window)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Puts the dock directly under <paramref name="window"/>; see <see cref="CanGoUnder"/>.</summary>
    public void GoUnder(nint window) =>
        NativeMethods.SetWindowPos(
            _hwnd, window, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

    /// <summary>
    /// True when a window of another program lies over the dock somewhere, in the dock's own
    /// band — one that <see cref="BringToTop"/> would lift it above.
    /// </summary>
    /// <remarks>
    /// Asked before lifting, so a dock with nothing over it is left where it is. Re-asserting
    /// a z-order that is already right still re-composes the window, and the pointer resting
    /// on an uncovered dock would otherwise do that once per visit for nothing.
    /// </remarks>
    public bool IsCovered()
    {
        if (!NativeMethods.GetWindowRect(_hwnd, out var dock))
        {
            return false;
        }

        var band = IsTopmostWindow(_hwnd);
        var ownProcess = (uint)Environment.ProcessId;

        // Other-band windows passed over, not stopped at — see WindowAbove for why.
        for (var window = Above(_hwnd); window != 0; window = Above(window))
        {
            if (IsTopmostWindow(window) != band
                || !NativeMethods.GetWindowRect(window, out var rect)
                || rect.Left >= dock.Right || rect.Right <= dock.Left
                || rect.Top >= dock.Bottom || rect.Bottom <= dock.Top
                || !TakesThePointer(window))
            {
                continue;
            }

            WindowsApi.GetWindowThreadProcessId(window, out var process);
            if (process != ownProcess)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the point, in physical screen pixels, is on a window lying over the dock —
    /// so the pointer there belongs to that window, not to the dock under it.
    /// </summary>
    /// <remarks>
    /// Any window above, in any band and of any process, ours included: a menu the dock
    /// opened is as much on top of it as anything else. Asked by the dock whenever the
    /// pointer is inside its hover zone, which is only ever a short walk — nothing above a
    /// dock that is on top, a few dozen windows above one that is buried.
    /// </remarks>
    public bool IsCoveredAt(int x, int y)
    {
        for (var window = Above(_hwnd); window != 0; window = Above(window))
        {
            if (NativeMethods.GetWindowRect(window, out var rect)
                && x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom
                && TakesThePointer(window))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Shown, on this desktop, and not click-through — a window the pointer would actually be
    /// on, rather than one that is merely above the dock in the z-order.
    /// </summary>
    /// <remarks>
    /// Click-through matters more than it looks. Screen recorders and the GPU drivers' overlays
    /// keep whole-screen transparent windows at the top of the z-order, and counting one of
    /// those as over the dock would leave the dock never answering the pointer at all. The
    /// dock's own sheet and menu host are click-through too, and pass by the same rule.
    /// </remarks>
    private static bool TakesThePointer(nint window) =>
        WindowsApi.IsWindowVisible(window)
        && ((uint)NativeMethods.GetWindowLongPtr(window, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TRANSPARENT) == 0
        && !WindowsApi.IsCloaked(window);

    private static nint Above(nint window) => NativeMethods.GetWindow(window, NativeMethods.GW_HWNDPREV);

    private static bool IsTopmostWindow(nint window) =>
        ((uint)NativeMethods.GetWindowLongPtr(window, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOPMOST) != 0;

    /// <summary>
    /// Toggles click-through. Used while the dock is auto-hidden so the sliver still on
    /// screen does not swallow clicks meant for the window underneath.
    /// </summary>
    public void SetClickThrough(bool enabled)
    {
        var exStyle = (uint)NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle = enabled
            ? exStyle | NativeMethods.WS_EX_TRANSPARENT
            : exStyle & ~NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, (nint)exStyle);
    }
}
