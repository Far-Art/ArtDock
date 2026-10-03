using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Threading;

namespace ArtDock.Interop;

/// <summary>One tab of a File Explorer window, as <see cref="ExplorerWindows.Tabs"/> reads it.</summary>
/// <param name="Window">The Explorer window the tab is in, a <c>CabinetWClass</c>.</param>
/// <param name="Tab">The tab's own window, a <c>ShellTabWindowClass</c> inside it.</param>
/// <param name="Folder">The folder it shows, by the name <see cref="ShellNames.FolderName"/> gives a folder.</param>
/// <param name="Name">What its header says: the folder's name as Explorer shows it, or null if the shell will not say.</param>
public readonly record struct ExplorerTab(nint Window, nint Tab, string Folder, string? Name);

/// <summary>
/// Which folders File Explorer's windows show, asked of Explorer's own list of them, and the
/// way to bring one of their tabs to the front.
/// </summary>
/// <remarks>
/// <para>
/// A folder pin is lit while a window shows its folder, in any of its tabs, and by those windows
/// alone — see <c>DockItem.RunningTarget</c> — so the census asks this whenever anything changes,
/// a window's title included, since going to another folder or tab changes nothing else. Asked
/// on a thread of the census's own: every call here crosses into Explorer's process, and one of
/// Explorer's windows hanging — on a network share gone away, say — would hang the dock with it.
/// </para>
/// <para>
/// The list is <c>IShellWindows</c>, which holds an entry for every tab of every Explorer
/// window: each tab is a shell browser of its own, with a window of its own,
/// <c>ShellTabWindowClass</c>, inside the Explorer window's <c>CabinetWClass</c>. Every tab's
/// window is visible, the ones behind as much as the one in front; the one in front is the
/// first of them in the window's z-order (read on 2026-10-03, before and after switching tabs).
/// </para>
/// </remarks>
public static class ExplorerWindows
{
    /// <summary>CLSID_ShellWindows: Explorer's list of its windows.</summary>
    private static readonly Guid ShellWindowsClass = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");

    /// <summary>SID_STopLevelBrowser: what a tab answers as, asked for its shell browser.</summary>
    private static readonly Guid TopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    /// <summary>SIGDN_DESKTOPABSOLUTEPARSING, the name <see cref="ShellNames.FolderName"/> gives.</summary>
    private const uint SigdnDesktopAbsoluteParsing = 0x80028000;

    /// <summary>SIGDN_NORMALDISPLAY: the name a tab's header shows.</summary>
    private const uint SigdnNormalDisplay = 0;

    /// <summary>GA_ROOT: the top-level window a tab is inside.</summary>
    private const uint GaRoot = 2;

    /// <summary>The class of a tab's own window.</summary>
    private const string TabClass = "ShellTabWindowClass";

    /// <summary>
    /// The class of the island Explorer's tab strip is drawn in, a child of its window: searching
    /// from it rather than from the whole window found the tabs in 10 ms rather than 100.
    /// </summary>
    private const string TabStripClass = "Microsoft.UI.Content.DesktopChildSiteBridge";

    /// <summary>
    /// Every tab of every File Explorer window, and the folder each shows — none when Explorer
    /// will not say.
    /// </summary>
    /// <remarks>
    /// A window on another virtual desktop is left out, as the census leaves it out (see
    /// <see cref="WindowsApi.IsAltTabWindow"/>).
    /// </remarks>
    public static IReadOnlyList<ExplorerTab> Tabs()
    {
        var tabs = new List<ExplorerTab>();
        object? list = null;
        try
        {
            list = Type.GetTypeFromCLSID(ShellWindowsClass) is { } type ? Activator.CreateInstance(type) : null;
            if (list is not IShellWindows windows)
            {
                return tabs;
            }

            var count = windows.Count;
            for (var i = 0; i < count; i++)
            {
                if (TabAt(windows, i) is { } tab)
                {
                    tabs.Add(tab);
                }
            }
        }
        catch (Exception e) when (IsShellFailure(e))
        {
            // Explorer gone, or restarting: no windows, so no tabs.
        }
        finally
        {
            Release(list);
        }

        return tabs;
    }

    /// <summary>Whether a tab is the one its window shows.</summary>
    public static bool IsInFront(ExplorerTab tab) =>
        FindWindowEx(tab.Window, 0, TabClass, null) == tab.Tab;

    /// <summary>
    /// Brings a File Explorer window forward on one of its tabs: at once, when that tab is in
    /// front already, and otherwise once the tab has been brought to the front, which is asked
    /// of Explorer off the dock's thread. A tab that cannot be brought to the front leaves the
    /// window coming forward on the tab it had.
    /// </summary>
    /// <param name="tab">The tab to show.</param>
    /// <param name="dock">The dock's thread, which brings the window forward: it has the input.</param>
    /// <returns>Whether the window was brought forward, or is about to be.</returns>
    public static bool Activate(ExplorerTab tab, Dispatcher dock)
    {
        if (tab.Name is not { } name || IsInFront(tab))
        {
            return AppLauncher.Activate(tab.Window);
        }

        Task.Run(() =>
        {
            SelectTab(tab.Window, name);
            dock.BeginInvoke(() => AppLauncher.Activate(tab.Window));
        });

        return true;
    }

    /// <summary>
    /// Brings to the front the tab of a File Explorer window whose header says
    /// <paramref name="name"/>, by UI Automation — which is how Windows lets another program
    /// choose a tab: Explorer's tab strip is a XAML <c>TabView</c>, each tab a list item that can
    /// be selected (read on 2026-10-03).
    /// </summary>
    /// <remarks>
    /// The first such tab not selected already: the selected one is the tab in front, which was
    /// not this one, or this would not have been asked — so of two tabs named alike, one in
    /// front, it is the other.
    /// </remarks>
    /// <returns>Whether a tab was selected.</returns>
    private static bool SelectTab(nint window, string name)
    {
        try
        {
            var strip = FindWindowEx(window, 0, TabStripClass, null);
            var tabs = AutomationElement.FromHandle(strip != 0 ? strip : window).FindAll(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem),
                    new PropertyCondition(AutomationElement.NameProperty, name)));

            foreach (AutomationElement tab in tabs)
            {
                if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern)
                    && pattern is SelectionItemPattern item
                    && !item.Current.IsSelected)
                {
                    item.Select();
                    return true;
                }
            }
        }
        catch (Exception e) when (e is ElementNotAvailableException or ElementNotEnabledException
            or InvalidOperationException or ArgumentException or COMException)
        {
            // The window gone, or its tab strip not what it was read as: it comes forward as it is.
        }

        return false;
    }

    /// <summary>
    /// The tab Explorer lists <paramref name="index"/>th, and the folder it shows; null if it will
    /// not say.
    /// </summary>
    private static ExplorerTab? TabAt(IShellWindows windows, int index)
    {
        object? entry = null;
        IShellBrowser? browser = null;
        object? view = null;
        object? shown = null;
        nint folder = 0;
        try
        {
            entry = windows.Item(index);
            if (entry is not IComServiceProvider services)
            {
                return null;
            }

            var service = TopLevelBrowser;
            var asked = typeof(IShellBrowser).GUID;
            if (services.QueryService(ref service, ref asked, out var unknown) != 0 || unknown == 0)
            {
                return null;
            }

            try
            {
                browser = (IShellBrowser)Marshal.GetObjectForIUnknown(unknown);
            }
            finally
            {
                Marshal.Release(unknown);
            }

            if (browser.GetWindow(out var tab) != 0 || !WindowsApi.IsWindowVisible(tab))
            {
                return null;
            }

            var window = GetAncestor(tab, GaRoot);
            if (window == 0 || WindowsApi.IsCloaked(window))
            {
                return null;
            }

            if (browser.QueryActiveShellView(out view) != 0 || view is not IFolderView folderView)
            {
                return null;
            }

            var persist = typeof(IPersistFolder2).GUID;
            if (folderView.GetFolder(ref persist, out shown) != 0
                || shown is not IPersistFolder2 current
                || current.GetCurFolder(out folder) != 0
                || NameOf(folder, SigdnDesktopAbsoluteParsing) is not { } path)
            {
                return null;
            }

            return new ExplorerTab(window, tab, path, NameOf(folder, SigdnNormalDisplay));
        }
        catch (Exception e) when (IsShellFailure(e))
        {
            // A window closing while it is asked about.
            return null;
        }
        finally
        {
            if (folder != 0)
            {
                Marshal.FreeCoTaskMem(folder);
            }

            Release(shown);
            Release(view);
            Release(browser);
            Release(entry);
        }
    }

    /// <summary>
    /// What a call into Explorer can throw: the runtime turns its HRESULTs into whichever
    /// exception they map to, as <see cref="ShellLink"/> found.
    /// </summary>
    private static bool IsShellFailure(Exception e) =>
        e is COMException or InvalidCastException or InvalidComObjectException
            or UnauthorizedAccessException or ArgumentException;

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }

    /// <summary>One of a folder's names, or null when the shell will not say.</summary>
    private static string? NameOf(nint folder, uint sigdn)
    {
        if (SHGetNameFromIDList(folder, sigdn, out var name) != 0 || name == 0)
        {
            return null;
        }

        try
        {
            var text = Marshal.PtrToStringUni(name);
            return string.IsNullOrEmpty(text) ? null : text;
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
        }
    }

    /// <summary><c>IShellWindows</c>, as far as its count and its items.</summary>
    /// <remarks>A dual interface: these two come first after <c>IDispatch</c>'s own, in this order.</remarks>
    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        int Count { get; }

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object? Item([MarshalAs(UnmanagedType.Struct)] object index);
    }

    /// <summary>COM's <c>IServiceProvider</c>, not .NET's of the same name.</summary>
    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out nint ppvObject);
    }

    /// <summary><c>IShellBrowser</c>, as far as <c>QueryActiveShellView</c>.</summary>
    /// <remarks>
    /// Every method before it is declared, in order, because each takes a place in the vtable.
    /// Only the first and the last are called; the rest are placeholders, whose parameters do
    /// not matter since nothing calls them.
    /// </remarks>
    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        // IOleWindow, which IShellBrowser extends.
        [PreserveSig]
        int GetWindow(out nint window);

        void ContextSensitiveHelp(int enterMode);

        void InsertMenusSB();

        void SetMenuSB();

        void RemoveMenusSB();

        void SetStatusTextSB();

        void EnableModelessSB();

        void TranslateAcceleratorSB();

        void BrowseObject();

        void GetViewStateStream();

        void GetControlWindow();

        void SendControlMsg();

        [PreserveSig]
        int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object? view);
    }

    /// <summary><c>IFolderView</c>, as far as <c>GetFolder</c>.</summary>
    [ComImport]
    [Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        void GetCurrentViewMode(out uint mode);

        void SetCurrentViewMode(uint mode);

        [PreserveSig]
        int GetFolder(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? folder);
    }

    /// <summary><c>IPersistFolder2</c>, whose one method of its own gives the folder shown.</summary>
    [ComImport]
    [Guid("1AC3D9F0-175C-11D1-95BE-00609797EA4F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFolder2
    {
        // IPersist, then IPersistFolder, which it extends.
        void GetClassID(out Guid classId);

        void Initialize(nint folder);

        [PreserveSig]
        int GetCurFolder(out nint folder);
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetNameFromIDList(nint pidl, uint sigdnName, out nint ppszName);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint childAfter, string className, string? windowName);
}
