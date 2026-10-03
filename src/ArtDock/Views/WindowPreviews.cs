using System.Windows;
using ArtDock.Controls;
using ArtDock.Dock;
using ArtDock.Interop;
using ArtDock.Services;

namespace ArtDock.Views;

/// <summary>Where the window previews of an item go: the display, and the point they stand over.</summary>
/// <param name="WorkArea">The dock's display's work area, in physical pixels.</param>
/// <param name="Scale">That display's scale.</param>
/// <param name="AnchorX">The held icon's middle, in physical pixels.</param>
/// <param name="Floor">The top of the dock's hover zone, in physical pixels: the line they stay above.</param>
internal readonly record struct PreviewPlace(Int32Rect WorkArea, double Scale, int AnchorX, int Floor);

/// <summary>
/// The previews of an app's windows, from the dock's side: when they open, for which item, what
/// they show, and everything that closes them.
/// </summary>
/// <remarks>
/// <para>
/// Fed by the dock's own look at the pointer (<see cref="DockBar.Polled"/>), some thirty times a
/// second, and by <see cref="Dismiss"/> from everything in <see cref="DockWindow"/> that has to
/// close them — a click on an icon, a menu, a drag, the keyboard, the dock hiding or standing
/// aside, a change of settings or of display. One place to close them, so a new way of closing
/// is one more call rather than one more copy of the rules.
/// </para>
/// <para>
/// While they are open the item is held under them (<see cref="DockBar.HoldForPreviews"/>) and
/// the dock is held on screen, as a menu holds it; the panel is kept above the dock and its
/// sheet. What a window is called and how big it is are read again on every look, and the panel
/// laid out again only when either has changed — every part of it checks before acting.
/// </para>
/// </remarks>
internal sealed class WindowPreviews(
    DockBar dock,
    RunningAppsService running,
    Func<string, DockItem?> findItem,
    Func<DockItem, PreviewPlace?> placeFor,
    Action<bool, bool> holdRevealed) : IDisposable
{
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkMButton = 0x04;

    private readonly PreviewDwell _dwell = new();
    private readonly PreviewMetrics _metrics = new();
    private readonly Dictionary<nint, bool> _canClose = [];

    private PreviewWindow? _window;

    /// <summary>
    /// The panel a new one is taking over from, left on the screen until the new one is on it:
    /// gone first, it would leave a frame or two with no panel at all.
    /// </summary>
    private PreviewWindow? _outgoing;
    private DockItem? _item;
    private PreviewPlace _place;
    private IReadOnlyList<Size> _sizes = [];
    private bool _enabled = true;
    private bool _solid;
    private bool _animate = true;
    private bool _holding;

    /// <summary>
    /// Takes the settings in force: whether previews are wanted at all, how long the pointer
    /// rests before they open and may be away before they close, whether the panel may blur, and
    /// whether it slides.
    /// </summary>
    public void Configure(bool enabled, int delayMs, int leaveMs, bool solid, bool animate)
    {
        _animate = animate;
        _dwell.HoverTime = Math.Max(0, delayMs);
        _dwell.LeaveTime = Math.Max(0, leaveMs);

        if (solid != _solid)
        {
            // The panel's material is chosen when it is made.
            _solid = solid;
            Dismiss();
        }

        _enabled = enabled;
        if (!enabled)
        {
            Dismiss();
        }
    }

    /// <summary>Whether the previews are open.</summary>
    public bool IsOpen => _window is not null;

    /// <summary>One look at the pointer.</summary>
    /// <param name="allowed">Whether the dock may show previews now: shown, settled, and neither a menu nor the keyboard holding it.</param>
    public void Poll(bool allowed)
    {
        if (!_enabled || !allowed)
        {
            if (_window is not null || _holding)
            {
                Dismiss();
            }
            else
            {
                // Not dismissed, which would keep the item under the pointer shut once the dock
                // is free again; only the wait so far is forgotten.
                _dwell.Look(Environment.TickCount64, under: null, onPanel: false, pressed: false);
            }

            return;
        }

        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var under = RunningUnder();
        var pressed = IsDown(VkLButton) || IsDown(VkRButton) || IsDown(VkMButton);

        switch (_dwell.Look(Environment.TickCount64, under?.Id, IsOnPanel(cursor.X, cursor.Y), pressed))
        {
            case PreviewMove.Open:
            case PreviewMove.Switch:
                Show(_dwell.Showing);
                break;

            case PreviewMove.Close:
                Close(lingers: false);
                return;
        }

        if (_window is not null)
        {
            Refresh();
            _window?.PointAt(cursor.X, cursor.Y);
        }
    }

    /// <summary>
    /// Closes the previews for anything but the pointer — and keeps the item under the pointer
    /// from opening them again until the pointer has left it.
    /// </summary>
    public void Dismiss()
    {
        _dwell.Dismiss(dock.ItemUnderCursor()?.Id);
        Close(lingers: true);
    }

    /// <summary>
    /// Another window has come to the front. The previews stay if the pointer is on them or on
    /// their item — closing a window from its card brings another forward, under the pointer
    /// still choosing — and close otherwise: a click somewhere else on the screen.
    /// </summary>
    public void ForegroundChanged()
    {
        if (_window is null || !NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        if (!IsOnPanel(cursor.X, cursor.Y) && RunningUnder()?.Id != _dwell.Showing)
        {
            Dismiss();
        }
    }

    /// <summary>Keeps the panel over the dock's windows, when it is not already.</summary>
    public void KeepOnTop(ReadOnlySpan<nint> dockWindows)
    {
        if (_window is { } window
            && (!WindowChrome.IsTopmostWindow(window.Hwnd) || WindowChrome.IsUnderAny(window.Hwnd, dockWindows)))
        {
            window.BringToTop();
        }
    }

    /// <summary>Closes at once, without the fade: the dock is going.</summary>
    public void Dispose() => Close(lingers: true, fade: false);

    private DockItem? RunningUnder() =>
        dock.IsPointerOverDock() && dock.ItemUnderCursor() is { IsRunning: true, RunningTarget: not null } item ? item : null;

    /// <summary>Whether a point is on the panel, or in the gap between it and the dock.</summary>
    private bool IsOnPanel(int x, int y)
    {
        if (_window is null)
        {
            return false;
        }

        var panel = _window.Bounds;
        var bottom = Math.Max(panel.Y + panel.Height, _place.Floor);
        return x >= panel.X && x < panel.X + panel.Width && y >= panel.Y && y < bottom;
    }

    private void Show(string? id)
    {
        if (id is null || findItem(id) is not { } item || placeFor(item) is not { } place)
        {
            _dwell.Dismiss(null);
            Close(lingers: true);
            return;
        }

        var entries = EntriesFor(item);
        if (entries.Count == 0)
        {
            _dwell.Dismiss(id);
            Close(lingers: true);
            return;
        }

        // A panel is made for one display: another one's is a new panel (see PreviewWindow).
        if (_window is not null && (place.Scale != _place.Scale || place.WorkArea != _place.WorkArea))
        {
            _window.Dispose();
            _window = null;
        }

        _item = item;
        _place = place;
        _sizes = [];

        if (!_holding)
        {
            _holding = true;
            holdRevealed(true, true);
        }

        dock.HoldForPreviews(item);

        // Another item's panel is not changed into this one's: a new panel takes over from it,
        // made at its own size and coming in from the old one's place while the old one fades
        // out. Changing it in place meant resizing a window on the screen, and Windows showed the
        // space it gained, and the old pictures, for a frame or two before WPF had drawn the new
        // cards — a blink at the start of every switch, the worse the wider the new panel.
        Int32Rect? followsFrom = null;
        var previous = _window;
        if (previous is not null)
        {
            followsFrom = previous.Placed;
            previous.Chosen -= OnChosen;
            previous.CloseRequested -= OnCloseRequested;

            // One panel on its way out at a time: a third app in quick succession sends the first
            // on its way at once.
            ReleaseOutgoing();
            _outgoing = previous;
            _window = null;
        }

        // It rises the whole gap it keeps from the dock, and no further: see PreviewWindow.
        var rise = (int)Math.Round(_metrics.Lift * place.Scale);
        _window = new PreviewWindow(Arrange(entries), entries, _solid, _animate, rise, followsFrom);
        _window.Chosen += OnChosen;
        _window.CloseRequested += OnCloseRequested;

        // The old one fades out as the new one fades in over it — or, not animating, goes the
        // moment the new one is there.
        if (previous is not null)
        {
            _window.Shown += (_, _) =>
            {
                if (ReferenceEquals(_outgoing, previous))
                {
                    ReleaseOutgoing();
                }
            };
        }

        // The windows' real shapes are DWM's to say, once their pictures are registered — before
        // anything has been drawn, so the first frame is already the right one.
        _sizes = _window.SourceSizes();
        _window.Update(Arrange(entries), entries);
    }

    /// <summary>Reads the windows, their titles and their sizes again, and lays out again on any change.</summary>
    private void Refresh()
    {
        if (_window is not { } window || _item is not { } held)
        {
            return;
        }

        // The item may have gone from the dock, or been replaced by a rebuild.
        var item = findItem(held.Id);
        if (item is null || placeFor(item) is not { } place)
        {
            Dismiss();
            return;
        }

        var entries = EntriesFor(item);
        if (entries.Count == 0)
        {
            Dismiss();
            return;
        }

        if (place.Scale != _place.Scale || place.WorkArea != _place.WorkArea)
        {
            Show(item.Id);
            return;
        }

        var sizes = window.SourceSizes();
        var same = place == _place
            && ReferenceEquals(item, _item)
            && entries.SequenceEqual(window.Entries)
            && sizes.SequenceEqual(_sizes);
        if (same)
        {
            return;
        }

        _item = item;
        _place = place;
        _sizes = sizes;
        window.Update(Arrange(entries), entries);

        // A window opened or closed changes which pictures are registered, and so their sizes.
        var registered = window.SourceSizes();
        if (!registered.SequenceEqual(_sizes))
        {
            _sizes = registered;
            window.Update(Arrange(entries), entries);
        }
    }

    private List<PreviewEntry> EntriesFor(DockItem item)
    {
        var windows = running.WindowsOf(item.RunningTarget);
        var entries = new List<PreviewEntry>(windows.Count);
        foreach (var window in windows)
        {
            if (!_canClose.TryGetValue(window, out var canClose))
            {
                canClose = WindowsApi.CanClose(window);
                _canClose[window] = canClose;
            }

            entries.Add(new PreviewEntry(window, WindowsApi.GetWindowTitle(window), item.Icon, canClose));
        }

        return entries;
    }

    private PreviewArrangement Arrange(IReadOnlyList<PreviewEntry> entries)
    {
        var sizes = entries.Count == _sizes.Count ? _sizes : [.. entries.Select(_ => Size.Empty)];
        return PreviewLayout.Arrange(sizes, _metrics, _place.Scale, _place.WorkArea, _place.AnchorX, _place.Floor);
    }

    private void OnChosen(object? sender, nint window)
    {
        // A folder's window comes forward on the folder's tab, which may be behind another: the
        // card was chosen under that folder. Asked before closing, which lets go of the item.
        var tab = running.TabShowing(window, _item?.RunningTarget);

        // Closed first: the window coming forward is the answer, and the panel over it is not.
        Dismiss();

        if (tab is { } shown)
        {
            ExplorerWindows.Activate(shown, System.Windows.Threading.Dispatcher.CurrentDispatcher);
        }
        else
        {
            AppLauncher.Activate(window);
        }
    }

    private void OnCloseRequested(object? sender, nint window)
    {
        // The card goes when the census sees the window go, which a program asking about unsaved
        // work may never do; until then it stays, as on the taskbar.
        WindowsApi.RequestClose(window);
    }

    /// <summary>Sends the panel being taken over from on its way: faded out, or at once without animation.</summary>
    private void ReleaseOutgoing(bool fade = true)
    {
        if (_outgoing is not { } outgoing)
        {
            return;
        }

        _outgoing = null;
        if (fade)
        {
            outgoing.FadeAway();
        }
        else
        {
            outgoing.Dispose();
        }
    }

    private void Close(bool lingers, bool fade = true)
    {
        ReleaseOutgoing(fade);

        if (_window is { } window)
        {
            window.Chosen -= OnChosen;
            window.CloseRequested -= OnCloseRequested;
            if (fade)
            {
                window.FadeAway();
            }
            else
            {
                window.Dispose();
            }

            _window = null;
        }

        _item = null;
        _sizes = [];
        _canClose.Clear();
        dock.HoldForPreviews(null);

        if (_holding)
        {
            _holding = false;
            holdRevealed(false, lingers);
        }
    }

    private static bool IsDown(int key) => WindowsApi.GetAsyncKeyState(key) < 0;
}
