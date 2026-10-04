using System.Runtime.InteropServices;
using System.Text;

namespace ArtDock.Interop;

/// <summary>What the shell can do with a type of file, as its associations say.</summary>
public static class ShellVerbs
{
    private const int AssocStrCommand = 1;
    private const int AssocStrDelegateExecute = 18;

    /// <summary>
    /// True when Windows can start a type of file as administrator: when its association has
    /// the <c>runas</c> verb, which is what Explorer's <em>Run as administrator</em> is.
    /// </summary>
    /// <remarks>
    /// Asked of the system rather than of a list, as <see cref="ShellIcons.IsPictureType"/> is.
    /// Read here on 2026-10-03: <c>.exe</c>, <c>.bat</c>, <c>.cmd</c> and <c>.msc</c> have it;
    /// <c>.lnk</c>, <c>.com</c>, <c>.msi</c>, <c>.ps1</c>, <c>.vbs</c>, documents and folders do
    /// not. A shortcut is judged by what it points at. A verb is either a command or a
    /// <c>DelegateExecute</c> handler, so either counts.
    /// </remarks>
    /// <param name="extension">The extension, with its dot: <c>.exe</c>.</param>
    public static bool HasRunAs(string extension) =>
        extension is { Length: > 1 }
        && (Has(extension, AssocStrCommand) || Has(extension, AssocStrDelegateExecute));

    /// <summary>
    /// The app that opens a type of file, by Windows' name for it (<see cref="AppIds"/>), or null
    /// when it has none: what a document's window is known by, whatever program it belongs to.
    /// </summary>
    /// <remarks>
    /// Read here on 2026-10-03: <c>.docx</c> is <c>Microsoft.Office.Desktop_8wekyb3d8bbwe!Word</c>,
    /// <c>.jpg</c> and <c>.png</c> <c>Microsoft.Windows.Photos_8wekyb3d8bbwe!App</c>, <c>.pdf</c>
    /// <c>MSEdge</c>, <c>.svg</c> <c>Chrome</c>, <c>.zip</c> <c>Microsoft.Windows.Explorer</c> —
    /// each the name the app's windows carry. <c>.psd</c>, Photoshop's, has none.
    /// </remarks>
    /// <param name="extension">The extension, with its dot.</param>
    public static string? HandlerAppId(string extension) => Query(extension, AssocStrAppId);

    /// <summary>
    /// The program that opens a type of file, by its full path, or null when the shell will not
    /// say — as for every Store app's type, whose program is the package's.
    /// </summary>
    /// <param name="extension">The extension, with its dot.</param>
    public static string? HandlerProgram(string extension) => Query(extension, AssocStrExecutable);

    /// <summary>
    /// True when the shell's own menu for an item — a <c>shell:AppsFolder\&lt;AUMID&gt;</c>, say —
    /// has <em>Run as administrator</em>: what Start offers it by, for a Store app that has no
    /// association to ask.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The item's context menu is built, as Explorer builds it, and its verbs read by their
    /// language-neutral names; nothing is invoked. Read here on 2026-10-03: 27 of the 51 Store
    /// apps installed have <c>runas</c> — Terminal, Notepad, Paint, Word, Spotify — and
    /// Calculator and Settings do not; the ones that have it are the desktop programs packaged
    /// for the Store, which run with full trust.
    /// </para>
    /// <para>
    /// Not cheap: about 35 ms an item here, 200 for Terminal's, which brings a handler of its own,
    /// and more for the first in a process. Ask it off the dock's thread, on one that is STA, as
    /// the shell's menus need.
    /// </para>
    /// </remarks>
    /// <param name="parsingName">The item, as the shell parses it.</param>
    public static bool ItemHasRunAs(string parsingName)
    {
        IShellItem? item = null;
        IContextMenu? menu = null;
        var popup = (nint)0;
        try
        {
            SHCreateItemFromParsingName(parsingName, 0, typeof(IShellItem).GUID, out item);
            item.BindToHandler(0, BhidSfUiObject, typeof(IContextMenu).GUID, out var pointer);
            try
            {
                menu = (IContextMenu)Marshal.GetObjectForIUnknown(pointer);
            }
            finally
            {
                Marshal.Release(pointer);
            }

            popup = CreatePopupMenu();
            if (popup == 0 || menu.QueryContextMenu(popup, 0, FirstCommand, LastCommand, CmfExtendedVerbs) < 0)
            {
                return false;
            }

            var verb = new StringBuilder(64);
            for (var i = GetMenuItemCount(popup) - 1; i >= 0; i--)
            {
                var id = GetMenuItemID(popup, i);
                if (id < FirstCommand || id > LastCommand)
                {
                    continue;
                }

                verb.Clear();
                if (menu.GetCommandString((nuint)(id - FirstCommand), GcsVerbW, 0, verb, verb.Capacity) == 0
                    && verb.ToString().Equals("runas", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException
            or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
        finally
        {
            if (popup != 0)
            {
                DestroyMenu(popup);
            }

            if (menu is not null)
            {
                Marshal.ReleaseComObject(menu);
            }

            if (item is not null)
            {
                Marshal.ReleaseComObject(item);
            }
        }
    }

    /// <summary>BHID_SFUIObject: an item's handler for the shell's user interface, its context menu among them.</summary>
    private static readonly Guid BhidSfUiObject = new("3981e225-f559-11d3-8e3a-00c04f6837d5");

    private const uint FirstCommand = 1;
    private const uint LastCommand = 0x7FFF;

    /// <summary>CMF_EXTENDEDVERBS: the menu as Shift+right-click has it, so no verb is held back.</summary>
    private const uint CmfExtendedVerbs = 0x100;

    /// <summary>GCS_VERBW: a command's verb, by its language-neutral name.</summary>
    private const uint GcsVerbW = 4;

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint pbc, in Guid bhid, in Guid riid, out nint ppv);

        void GetParent(out IShellItem ppsi);

        void GetDisplayName(uint sigdnName, out nint ppszName);

        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [Guid("000214e4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(nint pici);

        [PreserveSig]
        int GetCommandString(
            nuint idCmd, uint uType, nint pReserved,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMax);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        nint pbc,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(nint hMenu);

    [DllImport("user32.dll")]
    private static extern uint GetMenuItemID(nint hMenu, int nPos);

    private const int AssocStrExecutable = 2;
    private const int AssocStrAppId = 21;

    private static string? Query(string extension, int what)
    {
        if (extension is not { Length: > 1 })
        {
            return null;
        }

        var length = 1024u;
        var buffer = new StringBuilder((int)length);
        return AssocQueryString(0, what, extension, null, buffer, ref length) == 0 && buffer.Length > 0
            ? buffer.ToString()
            : null;
    }

    private static bool Has(string extension, int what)
    {
        var length = 1024u;
        var buffer = new StringBuilder((int)length);
        return AssocQueryString(0, what, extension, "runas", buffer, ref length) == 0;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, EntryPoint = "AssocQueryStringW")]
    private static extern int AssocQueryString(
        int flags, int str, string assoc, string? extra, StringBuilder output, ref uint length);
}
