using System.Runtime.InteropServices;
using System.Text;

namespace ArtDock.Interop;

/// <summary>
/// Windows' own name for an app — its AppUserModelID — as a window carries it, and as a
/// shortcut does.
/// </summary>
/// <remarks>
/// <para>
/// The taskbar groups windows by this, not by the program that owns them, and so must the dock:
/// an app's program is often not the one its pin names. Measured 2026-10-03: Word, installed from
/// the Store, runs as <c>...\WindowsApps\Microsoft.Office.Desktop.Word_...\WINWORD.EXE</c>, and its
/// window's process says <c>Microsoft.Office.Desktop_8wekyb3d8bbwe!Word</c>; Photos is
/// <c>Photos.exe</c> in its package and says <c>Microsoft.Windows.Photos_8wekyb3d8bbwe!App</c>; the
/// Settings app's window belongs to <c>ApplicationFrameHost.exe</c>, a host every such app shares,
/// and carries <c>windows.immersivecontrolpanel_...</c> on the window itself. Those are the names
/// a Store pin is made of, and the names the shell gives a type of file's handler
/// (<see cref="ShellVerbs.HandlerAppId"/>).
/// </para>
/// <para>
/// A window's own name comes first, as it does for the taskbar: Chrome's windows carry
/// <c>Chrome</c>, the frame of a Store app its app's. Then its process's, which only a packaged
/// app has. A desktop program that sets neither has none, and is known by its path alone.
/// </para>
/// </remarks>
public static class AppIds
{
    /// <summary><c>PKEY_AppUserModel_ID</c>.</summary>
    private static readonly PropertyKey AppUserModelId = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    private static readonly Guid PropertyStoreId = typeof(IPropertyStore).GUID;

    /// <summary>The app a window belongs to, by Windows' name for it, or null for one with none.</summary>
    /// <param name="window">A top-level window.</param>
    /// <param name="processId">The process that owns it.</param>
    public static string? OfWindow(nint window, uint processId) =>
        OfWindowItself(window) ?? OfProcess(processId);

    /// <summary>The app ID a shortcut gives what it starts, or null for one that gives none.</summary>
    /// <param name="link">The shortcut's property store, as <see cref="ShellLink"/> opens it.</param>
    internal static string? OfShortcut(IPropertyStore link) => Read(link, AppUserModelId);

    private static string? OfWindowItself(nint window)
    {
        var id = PropertyStoreId;
        if (SHGetPropertyStoreForWindow(window, ref id, out var store) != 0 || store is null)
        {
            return null;
        }

        try
        {
            return Read(store, AppUserModelId);
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static string? OfProcess(uint processId)
    {
        var process = WindowsApi.OpenProcess(WindowsApi.ProcessQueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return null;
        }

        try
        {
            // Answered for a packaged process only; any other is told APPMODEL_ERROR_NO_APPLICATION.
            var length = 256;
            var buffer = new StringBuilder(length);
            return GetApplicationUserModelId(process, ref length, buffer) == 0 && buffer.Length > 0
                ? buffer.ToString()
                : null;
        }
        finally
        {
            WindowsApi.CloseHandle(process);
        }
    }

    /// <summary>A string property, or null when it is not there or not a string.</summary>
    private static string? Read(IPropertyStore store, PropertyKey key)
    {
        var name = key;
        if (store.GetValue(ref name, out var value) != 0)
        {
            return null;
        }

        try
        {
            return value.Type == VtLpwstr && value.Pointer != 0
                && Marshal.PtrToStringUni(value.Pointer) is { Length: > 0 } text
                    ? text
                    : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    /// <summary><c>VT_LPWSTR</c>.</summary>
    private const ushort VtLpwstr = 31;

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(
        nint hwnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(nint process, ref int length, StringBuilder id);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct PropertyKey(Guid Format, uint Id);

    /// <summary>
    /// <c>PROPVARIANT</c>, as far as a string needs it: the type, three reserved words, and the
    /// union, which is sixteen bytes wide on x64.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant
    {
        [FieldOffset(0)]
        public ushort Type;

        [FieldOffset(8)]
        public nint Pointer;
    }

    /// <summary><c>IPropertyStore</c>, every method declared, in vtable order.</summary>
    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int GetAt(uint index, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);

        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant value);

        [PreserveSig]
        int Commit();
    }
}
