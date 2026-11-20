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
/// <param name="Floor">The line they stay above, in physical pixels: over the held item's label.</param>
/// <param name="DockTop">
/// The top of the dock's hover zone, in physical pixels: where the way up to them starts, which
/// counts as on them — the label's room between it and <paramref name="Floor"/> included.
/// </param>
internal readonly record struct PreviewPlace(Int32Rect WorkArea, double Scale, int AnchorX, int Floor, int DockTop);

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
/// While the pointer is on them or on its way up, the dock is held on screen, as a menu holds it,
/// and the item held under them (<see cref="DockBar.HoldForPreviews"/>) — not while it is
/// anywhere else off the dock, when the previews go after a short wait of their own, or — with
/// the dock due to hide soon after — the dock goes in its own time and takes them with it
/// (<see cref="PreviewDwell.LeaveTimeFor"/>); the panel is kept above the dock and its
/// sheet. What a window is called and how big it is are read again on every look, and the panel
/// laid out again only when either has changed — every part of it checks before acting.
/// </para>
/// </remarks>
internal sealed class WindowPreviews(
    DockBar dock,
    RunningAppsService running,
    Func<string, DockItem?> findItem,
    Func<DockItem, PreviewPlace?> placeFor,
    Action<bool, bool> holdRevealed,
    Func<bool> dockHides,
    Func<nint[]> dockWindows) : IDisposable
{
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkMButton = 0x04;

    /// <summary>
    /// Resting on a card shows its window alone on the desktop, as the taskbar's previews do —
    /// Windows' own peek (<see cref="LivePreview"/>), which does nothing where it is not to be had.
    /// </summary>
    private readonly PreviewPeek _peek = new();

    /// <summary>The windows kept out of the peek while it lasts: the dock's and the panel's.</summary>
    private nint[] _excluded = [];

    /// <summary>
    /// The dock's *Hide delay*: what decides whether the previews go with the dock or before it
    /// (<see cref="PreviewDwell.LeaveTimeFor"/>).
    /// </summary>
    /// <remarks>
    /// When the dock leads, as the user asked on 2026-11-20, the previews close as it starts to
    /// slide (<c>DockWindow</c>'s <c>VisibilityChanged</c> dismisses them). Both used to wait the
    /// hide delay, each on its own clock, and measured from outside the dock over eight hides the
    /// previews started fading 46 to 194 ms before the dock — auto-hide's 32 ms timer waits behind
    /// the dock's drawing, so the gap was not a fixed one a delay could take out.
    /// </remarks>
    private long _dockHideMs = 700;

    private readonly PreviewDwell _dwell = new();
    private readonly PreviewMetrics _metrics = new();
    private readonly Dictionary<nint, bool> _canClose = [];

    private PreviewWindow? _window;
    private DockItem? _item;
    private PreviewPlace _place;
    private IReadOnlyList<Size> _sizes = [];
    private bool _enabled = true;
    private bool _animate = true;
    private bool _holding;

    /// <summary>
    /// Takes the settings in force: whether previews are wanted at all, how long the pointer
    /// rests before they open and may be away before they close, and whether the panel animates.
    /// </summary>
    public void Configure(bool enabled, int delayMs, int leaveMs, bool animate)
    {
        _animate = animate;
        _dwell.HoverTime = Math.Max(0, delayMs);
        _dockHideMs = Math.Max(0, leaveMs);
        _dwell.LeaveTime = PreviewDwell.LeaveTimeFor(_dockHideMs, dockHides: false);

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

        // Their own short wait, or — when the dock is due to hide soon after — the dock's, which
        // takes them with it.
        _dwell.LeaveTime = PreviewDwell.LeaveTimeFor(_dockHideMs, dockHides());

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

        // The wave held on the item only while the pointer is on the previews or on its way up
        // to them, which is what the hold is for: keeping the icon under them as the pointer
        // climbs. Held whenever the pointer was off the dock, it swung the wave back to the item
        // when the pointer left the dock any other way, and held it there until the previews
        // timed out — the wave moving on its own, under a pointer that was elsewhere.
        // Said whenever they are open, with where they reach: the dock tests the pointer against
        // that itself, every frame, so the hold is there the moment the pointer leaves the icon.
        var onPanel = _window is not null && IsOnPanel(cursor.X, cursor.Y);
        dock.HoldForPreviews(_window is not null ? _item : null, Reach());

        // And the dock held up only then too: on the dock the pointer keeps it up itself, and
        // anywhere else it is the dock's to say when it goes — lingering from now, so it waits its
        // hide delay from the pointer leaving the previews, as it would from leaving the dock.
        SetHolding(onPanel);

        // Resting on a card peeks at its window.
        Peek(_window?.WindowAt(cursor.X, cursor.Y) ?? 0);
    }

    /// <summary>One look for the peek: started, moved to another window, or ended, as the pointer says.</summary>
    private void Peek(nint under)
    {
        switch (_peek.Look(Environment.TickCount64, under))
        {
            case PeekMove.Show when _window is { } window && LivePreview.IsAllowed():
                if (!LivePreview.IsActive)
                {
                    // The dock and the panel stay as they are, over the glass, as the taskbar does.
                    var (drawing, input) = window.Windows;
                    _excluded = [.. dockWindows(), drawing, input];
                    LivePreview.Exclude(_excluded, excluded: true);
                }

                if (!LivePreview.Show((nint)_peek.Showing, window.Windows.Drawing))
                {
                    // Not to be had: nothing peeks, from now on, and nothing else changes.
                    EndPeek();
                }

                break;

            case PeekMove.Show:
                _peek.Stop();
                break;

            case PeekMove.End:
                EndPeek();
                break;
        }
    }

    /// <summary>Ends any peek, every window back as it was, and the windows kept out of it let back in.</summary>
    private void EndPeek()
    {
        _peek.Stop();
        LivePreview.End();
        if (_excluded.Length > 0)
        {
            LivePreview.Exclude(_excluded, excluded: false);
            _excluded = [];
        }
    }

    /// <summary>Holds the dock on the screen, or lets it go, when that is not already so.</summary>
    private void SetHolding(bool hold)
    {
        if (hold == _holding)
        {
            return;
        }

        _holding = hold;
        holdRevealed(hold, true);
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

    /// <summary>Closes at once, without the sink: the dock is going.</summary>
    public void Dispose() => Close(lingers: true, animate: false);

    private DockItem? RunningUnder() =>
        dock.IsPointerOverDock() && dock.ItemUnderCursor() is { IsRunning: true, RunningTarget: not null } item ? item : null;

    /// <summary>Whether a point is on the panel, or in the gap between it and the dock.</summary>
    private bool IsOnPanel(int x, int y)
    {
        var reach = Reach();
        return x >= reach.X && x < reach.X + reach.Width && y >= reach.Y && y < reach.Y + reach.Height;
    }

    /// <summary>
    /// The panel and the way up to it from the dock — the label's room included — in physical
    /// screen pixels; empty while there is no panel.
    /// </summary>
    private Int32Rect Reach()
    {
        if (_window is null)
        {
            return Int32Rect.Empty;
        }

        var panel = _window.Bounds;
        var bottom = Math.Max(panel.Y + panel.Height, Math.Max(_place.Floor, _place.DockTop));
        return new Int32Rect(panel.X, panel.Y, panel.Width, Math.Max(0, bottom - panel.Y));
    }

    private void Show(string? id)
    {
        // Another app's windows: none of them is the one peeked at.
        EndPeek();

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

        // A panel is made for one display and the line it stands above: another one's is a new
        // panel (see PreviewWindow), and the old one simply goes.
        if (_window is not null && !_window.Fits(place))
        {
            Release(_window, animate: false);
            _window = null;
        }

        _item = item;
        _place = place;

        if (_window is not null)
        {
            // Another item's windows on the same panel: the card glides over to them and takes
            // their shape, as Windows' previews move between buttons (TODO.md item 25).
            _window.Show(entries, Arrange);
        }
        else
        {
            _window = PreviewWindow.TryCreate(place, entries, Arrange, _animate);
            if (_window is null)
            {
                // No compositor to draw with: no previews, rather than trying again on every look.
                _enabled = false;
                Close(lingers: true, animate: false);
                return;
            }

            _window.Chosen += OnChosen;
            _window.CloseRequested += OnCloseRequested;
        }

        _sizes = _window.SourceSizes();
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

        if (!window.Fits(place))
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

        // A window opened or closed changes which pictures are registered, and so their sizes:
        // the panel registers them first and lays out with what they say.
        _item = item;
        _place = place;
        window.Update(entries, Arrange);
        _sizes = window.SourceSizes();
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

    /// <summary>Lays out windows of these sizes at the place in force — what the panel asks once their pictures say how big they are.</summary>
    private PreviewArrangement Arrange(IReadOnlyList<Size> sizes) =>
        PreviewLayout.Arrange(sizes, _metrics, _place.Scale, _place.WorkArea, _place.AnchorX, _place.Floor);

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

    /// <summary>Sends a panel on its way: sinking out of sight, or at once.</summary>
    private void Release(PreviewWindow window, bool animate)
    {
        window.Chosen -= OnChosen;
        window.CloseRequested -= OnCloseRequested;
        if (animate)
        {
            window.Close();
        }
        else
        {
            window.Dispose();
        }
    }

    private void Close(bool lingers, bool animate = true)
    {
        // Before anything else: a card chosen brings its window forward next, and the desktop
        // must be back as it was by then.
        EndPeek();

        if (_window is { } window)
        {
            Release(window, animate);
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
