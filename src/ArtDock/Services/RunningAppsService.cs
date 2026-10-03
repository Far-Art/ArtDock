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
/// </remarks>
public sealed class RunningAppsService : IDisposable
{
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

    private readonly DispatcherTimer _debounce = new()
    {
        Interval = TimeSpan.FromMilliseconds(150)
    };

    /// <summary>Held as a field so the GC cannot collect the delegate the hook still calls.</summary>
    private readonly WindowsApi.WinEventProc _hookCallback;

    private readonly List<nint> _hooks = [];
    private readonly Dictionary<string, int> _cycleIndex = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When each open window was first seen, for <see cref="WindowsOf"/>.</summary>
    private readonly Dictionary<nint, long> _firstSeen = [];

    private long _nextSeen;

    private bool _disposed;

    public RunningAppsService()
    {
        _hookCallback = OnWinEvent;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Rescan();
        };
    }

    /// <summary>Raised after the set of running applications changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Begins watching, and takes a first census of what is already open.</summary>
    public void Start()
    {
        Hook(WindowsApi.EVENT_SYSTEM_FOREGROUND, WindowsApi.EVENT_SYSTEM_FOREGROUND);
        Hook(WindowsApi.EVENT_OBJECT_CREATE, WindowsApi.EVENT_OBJECT_DESTROY);
        Rescan();
    }

    /// <summary>True when <paramref name="executablePath"/> has at least one open window.</summary>
    public bool IsRunning(string? executablePath) => FindWindows(executablePath).Count > 0;

    /// <summary>
    /// The open windows of a pinned target, in the order they were first seen — the order the
    /// window previews show them in, which does not change as the user moves between them.
    /// </summary>
    /// <remarks>A copy: the census replaces its lists, but a caller may hold this one.</remarks>
    public IReadOnlyList<nint> WindowsOf(string? executablePath) =>
        WindowOrder.Arrange(FindWindows(executablePath), _firstSeen);

    /// <summary>
    /// Open windows for a pinned target: an exact image-path match if there is one, otherwise
    /// a match on executable file name to cover Windows' launcher stubs.
    /// </summary>
    private IReadOnlyList<nint> FindWindows(string? executablePath)
    {
        if (executablePath is not { Length: > 0 } path)
        {
            return [];
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
    public nint NextWindow(string? executablePath)
    {
        var windows = FindWindows(executablePath);
        if (windows.Count == 0)
        {
            return 0;
        }

        var path = executablePath!;
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

        if (SameAsBefore(fresh))
        {
            return;
        }

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

        Changed?.Invoke(this, EventArgs.Empty);
    }

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

        foreach (var hook in _hooks)
        {
            WindowsApi.UnhookWinEvent(hook);
        }

        _hooks.Clear();
    }
}
