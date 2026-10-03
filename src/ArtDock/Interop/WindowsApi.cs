using System.Runtime.InteropServices;
using System.Text;

namespace ArtDock.Interop;

/// <summary>
/// Win32 surface for finding, identifying and activating other applications' windows.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="NativeMethods"/>, which is only about the dock's own window.
/// These use classic <c>DllImport</c> rather than <c>LibraryImport</c> because several take
/// callback delegates, which the source generator does not marshal.
/// </remarks>
internal static class WindowsApi
{
    internal delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    internal delegate void WinEventProc(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    // ---- enumeration ---------------------------------------------------------

    internal const int GWL_EXSTYLE = -20;
    internal const uint WS_EX_TOOLWINDOW = 0x0000_0080;
    internal const uint GW_OWNER = 4;

    /// <summary>
    /// DWMWA_CLOAKED. Essential on Windows 10+: suspended Store apps keep a visible top-level
    /// window that is cloaked by the shell, and counting those would light up the running
    /// indicator for apps the user cannot see.
    /// </summary>
    internal const uint DWMWA_CLOAKED = 14;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint hWnd, uint uCmd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(
        nint hwnd, uint dwAttribute, out int pvAttribute, int cbAttribute);

    // ---- process identity ----------------------------------------------------

    /// <summary>PROCESS_QUERY_LIMITED_INFORMATION — enough to read the image path, and it
    /// succeeds against elevated processes where PROCESS_QUERY_INFORMATION would not.</summary>
    internal const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(
        uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(
        nint hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint hObject);

    /// <summary>
    /// A handle to watch a process exit by, or 0 when it cannot be opened — gone already.
    /// Limited information, which an elevated process allows too, and is all
    /// <see cref="HasExited"/> needs. Held, it keeps the process id from being reused.
    /// </summary>
    internal static nint OpenToWatch(uint processId) =>
        OpenProcess(ProcessQueryLimitedInformation, false, processId);

    /// <summary>Whether a process watched by <see cref="OpenToWatch"/> has exited; true when that cannot be read.</summary>
    internal static bool HasExited(nint process) =>
        !GetExitCodeProcess(process, out var code) || code != StillActive;

    private const uint StillActive = 259;

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(nint process, out uint exitCode);

    // ---- activation ----------------------------------------------------------

    internal const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint hWnd);

    /// <summary>The window's own popup that was active last — a dialog open over it — or the window itself.</summary>
    [DllImport("user32.dll")]
    internal static extern nint GetLastActivePopup(nint hWnd);

    /// <summary>Whether a key is down this moment, in the high bit, whichever window has the keyboard.</summary>
    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    /// <summary>True when the window is maximized.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsZoomed(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachThreadInput(
        uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    // ---- the desktop ---------------------------------------------------------

    /// <summary>The window that draws the desktop, which covers every display.</summary>
    [DllImport("user32.dll")]
    internal static extern nint GetShellWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    internal static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    internal static string GetWindowClass(nint hwnd)
    {
        // 256 is the longest a window class name can be.
        var buffer = new StringBuilder(256);
        return GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    // ---- window lifetime events ---------------------------------------------

    internal const uint EVENT_OBJECT_CREATE = 0x8000;
    internal const uint EVENT_OBJECT_DESTROY = 0x8001;
    internal const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;

    /// <summary>CHILDID_SELF: an event about the window itself, not one of its parts.</summary>
    internal const int CHILDID_SELF = 0;

    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    internal const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    /// <summary>OBJID_WINDOW — filters out the flood of control-level accessibility events.</summary>
    internal const int OBJID_WINDOW = 0;

    [DllImport("user32.dll")]
    internal static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        WinEventProc lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hWinEventHook);

    // ---- launching -----------------------------------------------------------

    internal static string? TryGetProcessPath(uint processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            var capacity = 1024;
            var buffer = new StringBuilder(capacity);
            return QueryFullProcessImageName(handle, 0, buffer, ref capacity)
                ? buffer.ToString()
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    // ---- closing another program's window ------------------------------------------

    private const uint WM_CLOSE = 0x0010;
    private const int TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    private const int ErrorAccessDenied = 5;

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, int access, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint token, int infoClass, nint info, int length, out int returned);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthorityCount(nint sid);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthority(nint sid, int index);

    /// <summary>
    /// Asks a window to close, as its title bar's close button does: a <c>WM_CLOSE</c> posted to
    /// it, which leaves any question about unsaved work to the program.
    /// </summary>
    /// <returns>False when it could not be posted — a window of a program above the dock's
    /// integrity level, which Windows does not let an unelevated program send to.</returns>
    internal static bool RequestClose(nint hwnd) => PostMessage(hwnd, WM_CLOSE, 0, 0);

    /// <summary>
    /// Whether the dock can close a window: true unless its program runs at a higher integrity
    /// level than the dock — elevated — or that cannot be read.
    /// </summary>
    /// <remarks>
    /// Measured against Task Manager (TODO.md, *Resolved*): the level of an elevated program
    /// reads from the unelevated dock, and a <c>WM_CLOSE</c> posted to its window fails with
    /// error 5. Asked rather than tried, so a close button that would do nothing is not drawn.
    /// </remarks>
    internal static bool CanClose(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return false;
        }

        var ours = IntegrityLevel(0);
        var theirs = IntegrityLevel(processId);
        return ours is { } own && theirs is { } other && other <= own;
    }

    /// <summary>
    /// Whether a window's program runs at a higher integrity level than the dock — started as
    /// administrator, for the unelevated dock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from the program's token where Windows lets the dock read it, as
    /// <see cref="CanClose"/> does; Task Manager's can be. An ordinary elevated program's cannot:
    /// measured 2026-10-03 on Rider started as administrator, the process opens for limited
    /// information — its path reads — but its token is refused (error 5), and so is the label on
    /// the process itself. The refusal is the answer, then. A program of the dock's own user at
    /// the dock's level always lets its token be read, so a token refused by a process that
    /// opened, in the dock's own session, is one above the dock. Other sessions are left out:
    /// a service refuses as well, and is not elevated by anyone at the desktop.
    /// </para>
    /// <para>
    /// Anything else unreadable is not elevated, so a doubt is not shown as one. A program
    /// started as another user would be, refusing its token the same way; it is not this user's
    /// either.
    /// </para>
    /// </remarks>
    internal static bool IsElevated(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0 || IntegrityLevel(0, out _) is not { } ours)
        {
            return false;
        }

        return IntegrityLevel(processId, out var refused) is { } theirs
            ? theirs > ours
            : refused && InOurSession(processId);
    }

    /// <summary>Whether a process runs in the dock's own session — at this desktop.</summary>
    private static bool InOurSession(uint processId) =>
        ProcessIdToSessionId(processId, out var theirs)
        && ProcessIdToSessionId((uint)Environment.ProcessId, out var ours)
        && theirs == ours;

    /// <summary>A process's integrity level (0x2000 medium, 0x3000 high); 0 for this one; null when unreadable.</summary>
    private static int? IntegrityLevel(uint processId) => IntegrityLevel(processId, out _);

    /// <summary>
    /// A process's integrity level, as <see cref="IntegrityLevel(uint)"/>; and, when it is
    /// null, whether that was because the process opened and its token was refused.
    /// </summary>
    private static int? IntegrityLevel(uint processId, out bool tokenRefused)
    {
        tokenRefused = false;
        var process = processId == 0 ? GetCurrentProcess() : OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return null;
        }

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
            {
                tokenRefused = Marshal.GetLastWin32Error() == ErrorAccessDenied;
                return null;
            }

            try
            {
                GetTokenInformation(token, TokenIntegrityLevel, 0, 0, out var length);
                if (length <= 0)
                {
                    return null;
                }

                var buffer = Marshal.AllocHGlobal(length);
                try
                {
                    if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out _))
                    {
                        return null;
                    }

                    // TOKEN_MANDATORY_LABEL starts with the label's SID; its last sub-authority is the level.
                    var sid = Marshal.ReadIntPtr(buffer);
                    var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                    return count == 0 ? null : Marshal.ReadInt32(GetSidSubAuthority(sid, count - 1));
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            if (processId != 0)
            {
                CloseHandle(process);
            }
        }
    }

    internal static string GetWindowTitle(nint hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 1);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    /// <summary>True for windows a user would consider "an open window" — the Alt-Tab rules.</summary>
    internal static bool IsAltTabWindow(nint hwnd)
    {
        if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != 0)
        {
            return false;
        }

        var exStyle = (uint)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        if ((exStyle & WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) != 0
            || cloaked == 0;
    }

    /// <summary>
    /// True for a window the shell has hidden without hiding it — a suspended Store app, or
    /// one on another virtual desktop. Visible as far as <c>IsWindowVisible</c> is concerned,
    /// and nowhere on screen.
    /// </summary>
    internal static bool IsCloaked(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0
        && cloaked != 0;
}
