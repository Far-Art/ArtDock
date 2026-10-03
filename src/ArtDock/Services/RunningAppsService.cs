using System.Windows.Threading;
using ArtDock.Interop;

namespace ArtDock.Services;

/// <summary>
/// Tracks which applications currently have windows open, and which window to raise when one
/// of them is clicked in the dock.
/// </summary>
/// <remarks>
/// <para>
/// Driven by <c>SetWinEventHook</c> rather than a polling timer: window churn is bursty and
/// mostly absent, so a timer would either lag behind the user or burn CPU all day for
/// nothing. The hook fires on the dock's UI thread, whose message loop pumps it.
/// </para>
/// <para>
/// Events are debounced before a rescan. Opening a single app can raise dozens of
/// <c>EVENT_OBJECT_CREATE</c>s in a few milliseconds, and each rescan enumerates every
/// top-level window on the desktop.
/// </para>
/// <para>
/// A folder is lit by the File Explorer windows showing it, in any of their tabs, not by every
/// window of <c>explorer.exe</c>, which every folder window is — see <c>DockItem.RunningTarget</c>.
/// Which folders each window's tabs show is Explorer's to say (<see cref="ExplorerWindows.Tabs"/>),
/// and is asked after every rescan that finds a window of Explorer's, on a thread of its own:
/// the question crosses into Explorer's process, and a window of Explorer's that hangs must not
/// hang the dock with it. Going to another folder or tab makes and destroys no window, but it
/// does change the window's title, so an Explorer window's change of name is heard as well.
/// </para>
/// </remarks>
public sealed class RunningAppsService : IDisposable
{
    /// <summary>The class of a File Explorer window, whose change of title may be a change of folder.</summary>
    private const string ExplorerWindowClass = "CabinetWClass";

    /// <summary>The program every File Explorer window belongs to, by file name.</summary>
    private const string ExplorerFileName = "explorer.exe";

    /// <summary>Open windows keyed by their process's full image path.</summary>
    private readonly Dictionary<string, List<nint>> _windowsByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The same windows keyed by executable file name alone.
    /// </summary>
    /// <remarks>
    /// Several apps that ship with Windows 11 are launcher stubs: pinning
    /// <c>C:\Windows\System32\notepad.exe</c> starts a process whose image path is
    /// <c>...\WindowsApps\Microsoft.WindowsNotepad_...\Notepad\Notepad.exe</c>, so matching on
    /// the full path alone never lights up the indicator. File names still line up across that
    /// redirection, which is what makes this a useful second key.
    /// </remarks>
    private readonly Dictionary<string, List<nint>> _windowsByFileName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// File Explorer's windows by the folders their tabs show, keyed by the shell's name for the
    /// folder (<see cref="ShellNames.FolderName"/>), in the census's order.
    /// </summary>
    private Dictionary<string, List<nint>> _windowsByFolder = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What Explorer last said its windows' tabs show.</summary>
    private IReadOnlyList<ExplorerTab> _tabs = [];

    /// <summary>
    /// Set to have Explorer's folders read again. Set while a reading is under way, it comes to
    /// one more reading, however many times it was set.
    /// </summary>
    private readonly AutoResetEvent _foldersWanted = new(false);

    private readonly DispatcherTimer _debounce = new()
    {
        Interval = TimeSpan.FromMilliseconds(150)
    };

    /// <summary>
    /// The processes that own each program's windows, by image path, as of the last census —
    /// what is still running under a program when its windows have all gone.
    /// </summary>
    private Dictionary<string, HashSet<uint>> _processesByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The programs whose last window has gone and whose process has not: see <see cref="ClosingPrograms{T}"/>.</summary>
    private readonly ClosingPrograms<nint> _closing = new(WindowsApi.HasExited, process => WindowsApi.CloseHandle(process));

    /// <summary>
    /// Looks at the closing programs ten times a second, while there are any. Not at Background
    /// priority, which the frame loop starves while the pointer is on the dock — and the pointer
    /// is on the dock, having just clicked.
    /// </summary>
    private readonly DispatcherTimer _closingWatch = new(DispatcherPriority.Normal)
    {
        Interval = TimeSpan.FromMilliseconds(100)
    };

    /// <summary>Held as a field so the GC cannot collect the delegate the hook still calls.</summary>
    private readonly WindowsApi.WinEventProc _hookCallback;

    private readonly List<nint> _hooks = [];
    private readonly Dictionary<string, int> _cycleIndex = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When each open window was first seen, for <see cref="WindowsOf"/>.</summary>
    private readonly Dictionary<nint, long> _firstSeen = [];

    private long _nextSeen;

    /// <summary>Volatile, being read on the thread that reads Explorer's folders as well.</summary>
    private volatile bool _disposed;

    public RunningAppsService()
    {
        _hookCallback = OnWinEvent;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Rescan();
        };

        _closingWatch.Tick += (_, _) =>
        {
            var ended = _closing.Check(DateTime.UtcNow);
            if (!_closing.Any)
            {
                _closingWatch.Stop();
            }

            if (ended)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>Raised after the set of running applications changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Begins watching, and takes a first census of what is already open.</summary>
    public void Start()
    {
        var dock = Dispatcher.CurrentDispatcher;
        var reader = new Thread(() => ReadFolders(dock))
        {
            IsBackground = true,
            Name = "ArtDock folders"
        };

        // The shell's own apartment, as the dock's thread is.
        reader.SetApartmentState(ApartmentState.STA);
        reader.Start();

        Hook(WindowsApi.EVENT_SYSTEM_FOREGROUND, WindowsApi.EVENT_SYSTEM_FOREGROUND);
        Hook(WindowsApi.EVENT_OBJECT_CREATE, WindowsApi.EVENT_OBJECT_DESTROY);
        Hook(WindowsApi.EVENT_OBJECT_NAMECHANGE, WindowsApi.EVENT_OBJECT_NAMECHANGE);
        Rescan();
    }

    /// <summary>
    /// True when <paramref name="target"/> — an executable's path, or a folder's name, as
    /// <c>DockItem.RunningTarget</c> gives them — has at least one open window.
    /// </summary>
    public bool IsRunning(string? target) => FindWindows(target).Count > 0;

    /// <summary>
    /// True when the program <paramref name="target"/> goes by is closing: its windows have gone
    /// and its process has not, yet. See <see cref="ClosingPrograms{T}"/>.
    /// </summary>
    public bool IsClosing(string? target) => _closing.Find(target) is not null;

    /// <summary>
    /// Runs <paramref name="then"/> once the program <paramref name="target"/> goes by has
    /// finished closing, if it is closing.
    /// </summary>
    /// <returns>True when it was held to wait; false, and nothing run, when it is not closing.</returns>
    public bool WhenClosed(string? target, Action then) => _closing.WhenClosed(target, then);

    /// <summary>
    /// True when any open window of <paramref name="target"/> belongs to a program running as
    /// administrator — above the dock's integrity level (<see cref="WindowsApi.IsElevated"/>).
    /// </summary>
    /// <remarks>
    /// Asked of the windows as they stand rather than kept with the census: a process's level is
    /// read from its token, a few calls per window, and only for the pins that are running.
    /// </remarks>
    public bool IsElevated(string? target) => FindWindows(target).Any(WindowsApi.IsElevated);

    /// <summary>
    /// The open windows of a pinned target, in the order they were first seen — the order the
    /// window previews show them in, which does not change as the user moves between them.
    /// </summary>
    /// <remarks>A copy: the census replaces its lists, but a caller may hold this one.</remarks>
    public IReadOnlyList<nint> WindowsOf(string? target) =>
        WindowOrder.Arrange(FindWindows(target), _firstSeen);

    /// <summary>
    /// Open windows for a pinned target: an exact image-path match if there is one, otherwise
    /// a match on executable file name to cover Windows' launcher stubs — or, for a folder,
    /// the File Explorer windows showing it.
    /// </summary>
    private IReadOnlyList<nint> FindWindows(string? target)
    {
        if (target is not { Length: > 0 } path)
        {
            return [];
        }

        // Anything but an executable is a folder, by the shell's name for it: nothing else is
        // given a running target.
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return _windowsByFolder.TryGetValue(path, out var showing) ? showing : [];
        }

        if (_windowsByPath.TryGetValue(path, out var exact))
        {
            return exact;
        }

        var fileName = System.IO.Path.GetFileName(path);
        return _windowsByFileName.TryGetValue(fileName, out var byName) ? byName : [];
    }

    /// <summary>
    /// The window to raise for this app. Repeat calls cycle through its windows, so clicking
    /// a multi-window app in the dock walks them rather than re-raising the same one.
    /// </summary>
    public nint NextWindow(string? target)
    {
        var windows = FindWindows(target);
        if (windows.Count == 0)
        {
            return 0;
        }

        var path = target!;
        _cycleIndex.TryGetValue(path, out var index);

        // A second click on an app that is already in front moves to its next window.
        var foreground = WindowsApi.GetForegroundWindow();
        if (IndexOf(windows, foreground) >= 0)
        {
            index = (IndexOf(windows, foreground) + 1) % windows.Count;
        }
        else if (index >= windows.Count)
        {
            index = 0;
        }

        _cycleIndex[path] = index;
        return windows[index];
    }

    private static int IndexOf(IReadOnlyList<nint> windows, nint target)
    {
        for (var i = 0; i < windows.Count; i++)
        {
            if (windows[i] == target)
            {
                return i;
            }
        }

        return -1;
    }

    private void Hook(uint min, uint max)
    {
        var handle = WindowsApi.SetWinEventHook(
            min,
            max,
            0,
            _hookCallback,
            idProcess: 0,
            idThread: 0,
            WindowsApi.WINEVENT_OUTOFCONTEXT | WindowsApi.WINEVENT_SKIPOWNPROCESS);

        if (handle != 0)
        {
            _hooks.Add(handle);
        }
    }

    private void OnWinEvent(
        nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Only top-level window events matter; the rest is control-level accessibility noise.
        if (idObject != WindowsApi.OBJID_WINDOW || hwnd == 0)
        {
            return;
        }

        // Titles change all the time, everywhere. Only a File Explorer window's says that it
        // may have gone to another folder, or another tab.
        if (eventType == WindowsApi.EVENT_OBJECT_NAMECHANGE
            && (idChild != WindowsApi.CHILDID_SELF || WindowsApi.GetWindowClass(hwnd) != ExplorerWindowClass))
        {
            return;
        }

        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>
    /// The programs that have a window open right now, by their full path.
    /// </summary>
    /// <remarks>
    /// Taken afresh rather than from a running watch: the settings dialog asks once, when it
    /// offers the programs that are open, and it has no dock of its own to ask.
    /// </remarks>
    public static IReadOnlyList<string> WithWindows() => [.. Census().Keys];

    /// <summary>Every open window a user would call one, grouped by the program it belongs to.</summary>
    private static Dictionary<string, List<nint>> Census()
    {
        var census = new Dictionary<string, List<nint>>(StringComparer.OrdinalIgnoreCase);

        WindowsApi.EnumWindows(
            (hwnd, _) =>
            {
                if (!WindowsApi.IsAltTabWindow(hwnd))
                {
                    return true;
                }

                WindowsApi.GetWindowThreadProcessId(hwnd, out var processId);
                if (processId == 0 || WindowsApi.TryGetProcessPath(processId) is not { } path)
                {
                    return true;
                }

                if (!census.TryGetValue(path, out var windows))
                {
                    windows = [];
                    census[path] = windows;
                }

                windows.Add(hwnd);
                return true;
            },
            0);

        return census;
    }

    /// <summary>Rebuilds the window census from scratch.</summary>
    private void Rescan()
    {
        var fresh = Census();
        _nextSeen = WindowOrder.Note(_firstSeen, [.. fresh.Values.SelectMany(windows => windows)], _nextSeen);

        var changed = !SameAsBefore(fresh);
        if (changed)
        {
            _windowsByPath.Clear();
            _windowsByFileName.Clear();
            foreach (var (path, windows) in fresh)
            {
                _windowsByPath[path] = windows;

                var fileName = System.IO.Path.GetFileName(path);
                if (_windowsByFileName.TryGetValue(fileName, out var sameName))
                {
                    sameName.AddRange(windows);
                }
                else
                {
                    _windowsByFileName[fileName] = [.. windows];
                }
            }
        }

        var closing = NoteClosing(fresh);

        // Asked whenever anything has changed — or has not, as far as the census can tell, when
        // a window went to another folder. A window just closed goes from its folder at once,
        // and one just opened joins its folder when Explorer has said which it is.
        if (_windowsByFileName.ContainsKey(ExplorerFileName))
        {
            _foldersWanted.Set();
        }
        else
        {
            _tabs = [];
        }

        if (SortFolders() || changed || closing)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Hands the closing programs what this census shows: a program that had windows and has
    /// none, with its processes as they were, and every program that has windows now.
    /// </summary>
    /// <remarks>
    /// Not File Explorer: its process is the shell's, and never exits, so the last folder window
    /// closed would hold the File Explorer pin as closing for the whole of the limit.
    /// </remarks>
    /// <returns>True when what is closing changed.</returns>
    private bool NoteClosing(Dictionary<string, List<nint>> fresh)
    {
        var lost = _processesByPath
            .Where(entry => !fresh.ContainsKey(entry.Key)
                && !System.IO.Path.GetFileName(entry.Key).Equals(ExplorerFileName, StringComparison.OrdinalIgnoreCase))
            .Select(entry => (entry.Key, (IReadOnlyList<nint>)[.. entry.Value.Select(WindowsApi.OpenToWatch).Where(process => process != 0)]))
            .ToList();

        _processesByPath = fresh.ToDictionary(
            entry => entry.Key,
            entry => entry.Value
                .Select(window => WindowsApi.GetWindowThreadProcessId(window, out var processId) == 0 ? 0u : processId)
                .Where(processId => processId != 0)
                .ToHashSet(),
            StringComparer.OrdinalIgnoreCase);

        var changed = _closing.Note(lost, fresh.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase), DateTime.UtcNow);
        if (_closing.Any)
        {
            _closingWatch.Start();
        }

        return changed;
    }

    /// <summary>
    /// Reads which folders Explorer's windows' tabs show, whenever it is asked to, and hands
    /// the answer to the dock's thread — never on the dock's thread: see the remarks above.
    /// </summary>
    /// <remarks>
    /// Anything thrown loses that reading and no more, as <c>HandleWindow</c>'s reading does: an
    /// exception on a background thread would take the dock down with it.
    /// </remarks>
    private void ReadFolders(Dispatcher dock)
    {
        while (true)
        {
            _foldersWanted.WaitOne();
            if (_disposed)
            {
                return;
            }

            try
            {
                var tabs = ExplorerWindows.Tabs();
                dock.BeginInvoke(() => OnTabsRead(tabs));
            }
            catch (Exception)
            {
                // See the remarks: the dots keep what they last showed until the next reading.
            }
        }
    }

    private void OnTabsRead(IReadOnlyList<ExplorerTab> tabs)
    {
        if (_disposed)
        {
            return;
        }

        _tabs = tabs;
        if (SortFolders())
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The tab of <paramref name="window"/> showing a folder pin's folder, as Explorer last said —
    /// the one in front, if that is one — or null for anything else.
    /// </summary>
    /// <param name="window">A window of the pin's, as <see cref="NextWindow"/> gives one.</param>
    /// <param name="target">The pin's running target: a folder's, or an executable's, which has no tabs.</param>
    public ExplorerTab? TabShowing(nint window, string? target)
    {
        ExplorerTab? showing = null;
        foreach (var tab in _tabs)
        {
            if (tab.Window != window || !string.Equals(tab.Folder, target, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ExplorerWindows.IsInFront(tab))
            {
                return tab;
            }

            showing ??= tab;
        }

        return showing;
    }

    /// <summary>
    /// Puts Explorer's windows in the census under the folders Explorer last said their tabs show.
    /// </summary>
    /// <returns>Whether any folder's windows are not what they were.</returns>
    private bool SortFolders()
    {
        var explorer = _windowsByFileName.TryGetValue(ExplorerFileName, out var windows) ? windows : [];
        var sorted = WindowsByFolder(explorer, _tabs);
        if (SameWindows(sorted, _windowsByFolder))
        {
            return false;
        }

        _windowsByFolder = sorted;
        return true;
    }

    /// <summary>
    /// The windows showing each folder, in any of their tabs: those of <paramref name="windows"/>,
    /// in their order, that a tab of <paramref name="tabs"/> is in — keyed by the folder, in any
    /// case, and each window under a folder once, however many of its tabs show it.
    /// </summary>
    /// <remarks>
    /// The census's windows, not Explorer's answer, decide which windows there are: an answer
    /// read before a window closed still names it, and one of Explorer's own dialogs — a copy's
    /// progress, say — is in the census and shows no folder.
    /// </remarks>
    public static Dictionary<string, List<nint>> WindowsByFolder(
        IReadOnlyList<nint> windows, IReadOnlyList<ExplorerTab> tabs)
    {
        var byFolder = new Dictionary<string, List<nint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in windows)
        {
            foreach (var tab in tabs)
            {
                if (tab.Window != window)
                {
                    continue;
                }

                if (!byFolder.TryGetValue(tab.Folder, out var showing))
                {
                    showing = [];
                    byFolder[tab.Folder] = showing;
                }

                if (!showing.Contains(window))
                {
                    showing.Add(window);
                }
            }
        }

        return byFolder;
    }

    private static bool SameWindows(Dictionary<string, List<nint>> fresh, Dictionary<string, List<nint>> old) =>
        fresh.Count == old.Count
        && fresh.All(entry => old.TryGetValue(entry.Key, out var existing) && existing.SequenceEqual(entry.Value));

    /// <summary>
    /// Compares the new census with the old one so an unchanged desktop does not repaint the
    /// dock. Window handles are compared, not just counts — closing one window and opening
    /// another between two scans would otherwise look like nothing happened.
    /// </summary>
    private bool SameAsBefore(Dictionary<string, List<nint>> fresh)
    {
        if (fresh.Count != _windowsByPath.Count)
        {
            return false;
        }

        foreach (var (path, windows) in fresh)
        {
            if (!_windowsByPath.TryGetValue(path, out var existing)
                || existing.Count != windows.Count
                || !existing.SequenceEqual(windows))
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debounce.Stop();
        _closingWatch.Stop();
        _closing.Clear();

        // Wakes the reading thread to see it is over. The event is not disposed: the thread may
        // be inside a reading still, and waits on it once more after one.
        _foldersWanted.Set();

        foreach (var hook in _hooks)
        {
            WindowsApi.UnhookWinEvent(hook);
        }

        _hooks.Clear();
    }
}
