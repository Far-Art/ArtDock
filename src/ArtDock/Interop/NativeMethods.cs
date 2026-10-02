using System.Runtime.InteropServices;

namespace ArtDock.Interop;

/// <summary>
/// Hand-written P/Invoke surface for window chrome.
///
/// Kept hand-written rather than CsWin32-generated: this is a handful of flat calls where
/// a source generator buys nothing. CsWin32 is introduced later for the shell COM work
/// (IShellItemImageFactory, IShellLink), where the marshalling actually is hard.
/// </summary>
internal static partial class NativeMethods
{
    // ---- window styles -------------------------------------------------------

    internal const int GWL_EXSTYLE = -20;
    internal const int GWL_STYLE = -16;

    /// <summary>
    /// A sizing frame. Carried by the backdrop window purely because DWM draws a shadow
    /// around windows that have one and none around windows that do not.
    /// </summary>
    internal const uint WS_THICKFRAME = 0x0004_0000;

    internal const uint WS_POPUP = 0x8000_0000;

    /// <summary>In the band above every ordinary window.</summary>
    internal const uint WS_EX_TOPMOST = 0x0000_0008;

    /// <summary>
    /// No redirection surface. Without it a window keeps one, and DWM composes it over any
    /// composition visual attached to the window — which renders the visual invisible and
    /// the window black.
    /// </summary>
    internal const uint WS_EX_NOREDIRECTIONBITMAP = 0x0020_0000;

    internal const int SW_HIDE = 0;

    /// <summary>Show without activating, which is the only way this dock ever shows anything.</summary>
    internal const int SW_SHOWNA = 8;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

    internal const uint WS_EX_TOOLWINDOW = 0x0000_0080;
    internal const uint WS_EX_TRANSPARENT = 0x0000_0020;
    internal const uint WS_EX_NOACTIVATE = 0x0800_0000;

    /// <summary>
    /// Layered. With <see cref="WS_EX_TRANSPARENT"/> it makes a window click-through: hit-testing
    /// passes it by entirely. <see cref="WS_EX_TRANSPARENT"/> alone does not, reliably — a window
    /// with only that can still be what <c>WindowFromPoint</c>, and so a click, finds.
    /// </summary>
    internal const uint WS_EX_LAYERED = 0x0008_0000;

    /// <summary>For <see cref="SetLayeredWindowAttributes"/>: the alpha is the window's opacity.</summary>
    internal const uint LWA_ALPHA = 0x0000_0002;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(nint hWnd, uint crKey, byte bAlpha, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    // ---- windows of our own ----------------------------------------------------

    internal delegate nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowEx(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint hWnd);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? moduleName);

    // ---- z-order -------------------------------------------------------------

    internal static readonly nint HWND_TOPMOST = -1;
    internal static readonly nint HWND_NOTOPMOST = -2;

    /// <summary>Top of the ordinary band, for a dock that is not floating above everything.</summary>
    internal static readonly nint HWND_TOP = 0;

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_FRAMECHANGED = 0x0020;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(
        nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    // ---- mouse messages ------------------------------------------------------
    //
    // Read straight from the window procedure. WS_EX_NOACTIVATE stops WPF delivering mouse
    // input to this window, but the messages themselves still arrive at the HWND.

    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_LBUTTONUP = 0x0202;
    internal const int WM_RBUTTONUP = 0x0205;

    // ---- system colour changes -----------------------------------------------
    //
    // Both are needed: DWM announces a new accent, and the light/dark switch arrives as an
    // ordinary settings change. A dock matching the taskbar answers to either.

    internal const int WM_SETTINGCHANGE = 0x001A;
    internal const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
    internal const int WM_NCCALCSIZE = 0x0083;

    /// <summary>
    /// The first message number an application may give a meaning of its own, for messages
    /// it asks to be posted to its own windows.
    /// </summary>
    internal const int WM_APP = 0x8000;

    /// <summary>
    /// Sent when a window moves to a monitor with a different scale.
    /// </summary>
    /// <remarks>
    /// WPF's default handling rescales the window to suit, which is right for a window with
    /// content of its own and wrong for the backdrop, whose size is dictated by the dock in
    /// device pixels. Left alone it silently shrank the sheet by the ratio between the two
    /// monitors.
    /// </remarks>
    internal const int WM_DPICHANGED = 0x02E0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    /// <summary>The window directly above this one in z-order.</summary>
    internal const uint GW_HWNDPREV = 3;

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint hWnd, uint uCmd);
    internal const int WM_CAPTURECHANGED = 0x0215;

    // ---- mouse capture -------------------------------------------------------
    //
    // Held for the length of a drag. Without it the button-up lands on whatever window is
    // under the cursor, and a reorder that wandered off the bar — which is easily done,
    // since the pointer leaves it the moment the drag goes high or past either end — would
    // never be seen to finish at all.

    [DllImport("user32.dll")]
    internal static extern nint SetCapture(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    // ---- foreground ----------------------------------------------------------
    //
    // Pointed at MenuHost's window before a context menu opens. Without it a click outside
    // the menu goes to whatever window is under it and the menu is left hanging open.

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hWnd);

    /// <summary>
    /// Hands this process's right to come forward to whoever is about to be asked to.
    /// </summary>
    /// <remarks>
    /// Called by a second copy of ArtDock on its way out, so that the copy already running
    /// can raise its settings window. Without it the running instance is a background
    /// process asking to be foregrounded, which Windows answers by flashing its taskbar
    /// button instead — and a tray app has no taskbar button to flash.
    /// </remarks>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AllowSetForegroundWindow(int dwProcessId);

    /// <summary>Lets any process take the foreground, which is the only sense in which this is used.</summary>
    internal const int ASFW_ANY = -1;

    // ---- hotkeys -------------------------------------------------------------
    //
    // Registered against the dock's own window, and heard in its window procedure. No keyboard
    // hook, for the reason there is no mouse hook: a low-level hook sits in the input path of
    // every program on the desktop, and a slow one slows them all.

    /// <summary>Posted when a registered hotkey is pressed; <c>wParam</c> is its id.</summary>
    internal const int WM_HOTKEY = 0x0312;

    /// <summary>One message for a key held down, not one for every repeat of it.</summary>
    internal const uint MOD_NOREPEAT = 0x4000;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint hWnd, int id);

    /// <summary>For <see cref="MapVirtualKey"/>: the character a key types with nothing held, on the calling thread's layout.</summary>
    internal const uint MAPVK_VK_TO_CHAR = 2;

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    internal static partial uint MapVirtualKey(uint uCode, uint uMapType);

    // ---- the shell's own windows ---------------------------------------------

    /// <summary>
    /// Finds a top-level window by class, which for our purposes means the taskbar.
    /// </summary>
    /// <remarks>
    /// The name is passed as null: <c>Shell_TrayWnd</c> identifies the taskbar on its own,
    /// and its title is neither stable nor localised the same way twice.
    /// </remarks>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
    internal static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    /// <summary>
    /// Posts a message and returns without waiting for it to be handled.
    /// </summary>
    /// <remarks>
    /// Posted rather than sent. <c>SendMessage</c> blocks until the receiving window has
    /// finished, and what this one asks for is the Start menu opening — an animation in
    /// another process, on a UI thread that is not ours. Blocking the dock's thread on it
    /// stops the wave dead for the duration.
    /// </remarks>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    internal const uint WM_SYSCOMMAND = 0x0112;

    /// <summary>
    /// Asks the taskbar to show the Start menu — the same command its own button sends.
    /// </summary>
    /// <remarks>
    /// The only route there. The Start menu is not a thing that can be launched: it has no
    /// path, no AUMID, and no entry in the shell namespace, so <c>ShellExecute</c> has
    /// nothing to open. Activating <c>StartMenuExperienceHost</c> by AUMID starts the
    /// process that hosts it, which is not the same as opening the menu. Verified on this
    /// build of Windows 11: after posting it, the foreground window's class is
    /// <c>Windows.UI.Core.CoreWindow</c>, which is the menu.
    /// </remarks>
    internal const nint SC_TASKLIST = 0xF130;

    // ---- cursor --------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// Polled once per rendered frame while the wave is live. WPF's MouseMove stops firing
    /// where the window has no drawn content, and the dock deliberately has gaps between
    /// magnified icons; reading the cursor directly is what keeps the wave from collapsing
    /// as the pointer crosses one.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out NativePoint lpPoint);

    // ---- displays ------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    /// <summary>
    /// Asks for a monitor's device interface path in <see cref="DisplayDevice.DeviceID"/>
    /// rather than its hardware ID — the path is what tells two identical monitors apart.
    /// </summary>
    internal const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x1;

    /// <summary>On a monitor rather than an adapter: it is attached and in use.</summary>
    internal const uint DISPLAY_DEVICE_ACTIVE = 0x1;

    /// <summary>
    /// With a display's device name, such as <c>\\.\DISPLAY1</c>, enumerates the monitors
    /// on that output; with null, the outputs themselves.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevices(
        string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

    /// <summary>No monitor at all when the rectangle is on none, rather than the nearest.</summary>
    internal const uint MONITOR_DEFAULTTONULL = 0;

    /// <summary>The display nearest the rectangle when it is on none.</summary>
    internal const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromRect(ref NativeRect lprc, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);
}
