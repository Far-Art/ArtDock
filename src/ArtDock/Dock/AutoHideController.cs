using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ArtDock.Interop;

namespace ArtDock.Dock;

/// <summary>Where the dock is in its hide/reveal cycle.</summary>
public enum DockVisibility
{
    Shown,
    Hiding,
    Hidden,
    Revealing
}

/// <summary>
/// Slides the dock off the screen edge when it is not wanted, and back when the cursor
/// reaches that edge.
/// </summary>
/// <remarks>
/// <para>
/// Reveal is detected by polling the cursor rather than by a low-level mouse hook. A
/// <c>WH_MOUSE_LL</c> hook sits in the input path of every application on the desktop, so a
/// stalled handler there degrades the whole machine; a poll costs one <c>GetCursorPos</c>
/// per tick and cannot hurt anything outside this process.
/// </para>
/// <para>
/// Both directions are delayed: reveal briefly, so sweeping the pointer past the bottom of
/// the screen on the way somewhere else does not summon the dock, and hide for longer, so
/// stepping a few pixels off the bar while aiming for an icon does not dismiss it.
/// </para>
/// </remarks>
public sealed class AutoHideController
{
    /// <summary>
    /// How close to the screen edge the cursor must come to summon the dock.
    /// </summary>
    /// <remarks>
    /// Deliberately a few pixels. The zone is anchored to the display's true bottom, not
    /// the work area's, so with a bottom taskbar the cursor only reaches it by pushing
    /// past the taskbar — which is what makes summoning the dock a deliberate act rather
    /// than something that happens whenever the pointer visits the taskbar.
    /// </remarks>
    private const double RevealZoneHeight = 3;

    /// <summary>Horizontal slack around the dock's own width for the reveal zone.</summary>
    private const double RevealZoneSlack = 80;

    private readonly Window _window;
    private readonly WindowChrome _chrome;
    private readonly Func<bool> _isPointerOverDock;

    /// <summary>
    /// Lifts the dock above whatever covers it, and its acrylic sheet with it, and says which
    /// window it was under — 0 if none.
    /// </summary>
    /// <remarks>
    /// The owner's rather than <see cref="WindowChrome.BringToTop"/> alone: the sheet is a
    /// window of its own, and a dock raised without it is drawn straight over the window it
    /// has just risen above, with its blur still underneath that window.
    /// </remarks>
    private readonly Func<nint> _raise;

    /// <summary>
    /// Holds the dock, and its sheet, in the topmost band — for when a lift did not get it
    /// above what covers it.
    /// </summary>
    private readonly Action _holdAbove;

    /// <summary>
    /// Puts the dock, and its sheet, back under a window it was lifted over — letting go of
    /// the topmost band first, if it had been held there. 0 only lets go.
    /// </summary>
    private readonly Action<nint> _lower;

    /// <summary>True from a lift until the dock has been put back.</summary>
    private bool _lifted;

    /// <summary>
    /// The window the edge lifted the dock over, to be put back under once the pointer has
    /// left — or 0 when the dock is where it was.
    /// </summary>
    /// <remarks>
    /// The first lift's, kept until the dock goes back. Held against the edge a second time
    /// before that, the dock is lifted again from on top, and what is above it then is not
    /// where it came from.
    /// </remarks>
    private nint _liftedOver;

    /// <summary>
    /// True while the edge should do nothing, because a program the dock stays down for is
    /// in front and fills its display.
    /// </summary>
    /// <remarks>
    /// Asked only once the pointer is already at the edge, or in the strip below a revealed
    /// dock, or has just arrived on a covered dock — so what it costs is paid while the
    /// pointer is down there and not otherwise.
    /// Everything that brings the dock back on purpose — the tray, a second launch, the
    /// settings dialog — does not go through the edge, and is unaffected.
    /// </remarks>
    private readonly Func<bool> _standDown;

    /// <summary>
    /// True when the pointer is resting on the handle a hidden dock leaves behind — false
    /// whenever there is no handle up.
    /// </summary>
    private readonly Func<bool> _isPointerOnHandle;

    private readonly DispatcherTimer _watch = new()
    {
        Interval = TimeSpan.FromMilliseconds(32)
    };

    /// <summary>
    /// Outstanding requests to keep the dock on screen regardless of the pointer.
    /// </summary>
    /// <remarks>
    /// A count rather than a flag: the settings dialog and an item's edit dialog can be
    /// open at once, and whichever closes second must be the one that releases the hold.
    /// </remarks>
    private int _holds;

    private DateTime _outsideSince = DateTime.MaxValue;
    private DateTime _atEdgeSince = DateTime.MaxValue;

    private double _shownTop;
    private double _hiddenTop;

    public AutoHideController(
        Window window,
        WindowChrome chrome,
        Func<bool> isPointerOverDock,
        Func<nint> raise,
        Action holdAbove,
        Action<nint> lower,
        Func<bool> standDown,
        Func<bool> isPointerOnHandle)
    {
        _window = window;
        _chrome = chrome;
        _isPointerOverDock = isPointerOverDock;
        _raise = raise;
        _holdAbove = holdAbove;
        _lower = lower;
        _standDown = standDown;
        _isPointerOnHandle = isPointerOnHandle;

        // Always running, whatever the settings. With auto-hide off the edge is still watched,
        // to lift a dock that has been covered — see WatchEdgeForRaise — and that is wanted
        // for a dock set to float above everything as much as for one that is not: another
        // window that floats can still cover it.
        _watch.Tick += OnTick;
        _watch.Start();
    }

    /// <summary>Whether auto-hide is active at all. Turning it off reveals the dock.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// True once the current visit to the edge has raised the dock.
    /// </summary>
    /// <remarks>
    /// One raise per visit. Re-asserting the z-order on every tick would re-compose the dock
    /// and the sheet behind it thirty times a second, for a window that is already on top.
    /// </remarks>
    private bool _raised;

    /// <summary>Delay before a cursor at the screen edge brings the dock back.</summary>
    public TimeSpan RevealDelay { get; set; } = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// Delay before a cursor away from the dock sends it away — off the edge with auto-hide,
    /// or back under the window the edge lifted it over.
    /// </summary>
    public TimeSpan HideDelay { get; set; } = TimeSpan.FromMilliseconds(700);

    /// <summary>Duration of the slide in each direction.</summary>
    public TimeSpan SlideDuration { get; set; } = TimeSpan.FromMilliseconds(220);

    public DockVisibility Visibility
    {
        get;
        private set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    } = DockVisibility.Shown;

    /// <summary>
    /// Raised as the dock starts to leave, has gone, starts to come back and is back — so the
    /// handle it leaves behind can arrive as it goes and go as it arrives.
    /// </summary>
    public event EventHandler? VisibilityChanged;

    /// <summary>
    /// The screen y-coordinate (in DIPs) of the display's bottom edge — not the work
    /// area's bottom, which is the top of the taskbar, and not the dock's own bottom,
    /// which floats above both.
    /// </summary>
    private double _revealEdgeY;

    /// <summary>The display's left and right edges, in DIPs, which bound both zones sideways.</summary>
    private double _displayLeft = double.NegativeInfinity;
    private double _displayRight = double.PositiveInfinity;

    /// <summary>Records where the dock sits when shown; call after positioning it.</summary>
    public void AnchorTo(double shownTop, double revealEdgeY, double displayLeft, double displayRight)
    {
        _shownTop = shownTop;
        _revealEdgeY = revealEdgeY;
        _displayLeft = displayLeft;
        _displayRight = displayRight;

        // Far enough down that the whole window, shadow included, clears the screen.
        _hiddenTop = shownTop + _window.Height + 4;

        if (Visibility == DockVisibility.Hidden)
        {
            _window.Top = _hiddenTop;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled)
        {
            return;
        }

        IsEnabled = enabled;

        // Auto-hide takes the dock from here, and hiding is its way of putting it back — but
        // not out of the topmost band, if a lift had to hold it there.
        if (_lifted)
        {
            _lower(0);
            _lifted = false;
        }

        _liftedOver = 0;
        _outsideSince = DateTime.MaxValue;

        if (enabled)
        {
            return;
        }

        Reveal();
    }

    /// <summary>
    /// Keeps the dock on screen while something is being configured, and lets it resume
    /// hiding once nothing is.
    /// </summary>
    /// <remarks>
    /// Adjusting settings while the dock keeps sliding away is unworkable — you cannot see
    /// what a change did.
    /// </remarks>
    public void HoldRevealed(bool hold)
    {
        _holds = Math.Max(0, _holds + (hold ? 1 : -1));

        if (_holds > 0)
        {
            Reveal();
            return;
        }

        // Released: start the hide timer from now rather than from whenever the pointer
        // last wandered off, so the dock does not vanish the instant a dialog closes.
        _outsideSince = DateTime.UtcNow;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;

        if (!IsEnabled)
        {
            WatchHover();
            WatchEdgeForRaise(now);
            WatchForLeaving(now);
            return;
        }

        _raised = false;

        if (_holds > 0)
        {
            _outsideSince = DateTime.MaxValue;
            Reveal();
            return;
        }

        if (Visibility is DockVisibility.Shown or DockVisibility.Revealing)
        {
            // Over a fullscreen program the dock stays down for, only the dock itself keeps
            // it up. The strip beneath it is where a game scrolling down holds the pointer,
            // so a dock that happened to be out when the game started would otherwise sit
            // over it for as long as the scrolling went on.
            if (_isPointerOverDock() || (IsCursorInKeepAliveZone() && !_standDown()))
            {
                _outsideSince = DateTime.MaxValue;
                return;
            }

            if (_outsideSince == DateTime.MaxValue)
            {
                _outsideSince = now;
            }
            else if (now - _outsideSince >= HideDelay)
            {
                Hide();
            }

            return;
        }

        if (IsRevealAsked())
        {
            if (_atEdgeSince == DateTime.MaxValue)
            {
                _atEdgeSince = now;
            }
            else if (now - _atEdgeSince >= RevealDelay)
            {
                Reveal();
            }

            return;
        }

        _atEdgeSince = DateTime.MaxValue;
    }

    /// <summary>
    /// True when the cursor is asking for a hidden dock back: held against the reveal edge, or
    /// resting on the handle the dock left behind — and the edge is not standing down.
    /// </summary>
    /// <remarks>
    /// The handle waits out the same <see cref="RevealDelay"/> as the edge, and for more reason:
    /// it lies over the bottom rows of other windows, where the pointer goes on business of its
    /// own, whereas the edge is only reached by pushing past the taskbar. It yields to a program
    /// the edge stands down for, too. It is not normally up over one — it steps aside for
    /// anything that fills the display — but a maximized window where the taskbar hides itself
    /// fills the display and keeps its handle, and the edge's list counts it all the same.
    /// </remarks>
    private bool IsRevealAsked() =>
        (IsCursorAtRevealEdge() || _isPointerOnHandle()) && !_standDown();

    /// <summary>
    /// True when the cursor is held against the reveal edge and the edge is not standing
    /// down for a fullscreen program — where both bringing the dock back and lifting it start.
    /// </summary>
    /// <remarks>
    /// Standing down resets the hold rather than pausing it. The pointer can sit at the edge
    /// for as long as the game is scrolling, and once the game is left the dock should need a
    /// hold of its own, not arrive the instant focus moves because the old one ran out long ago.
    /// </remarks>
    private bool IsEdgeHeld() => IsCursorAtRevealEdge() && !_standDown();

    /// <summary>
    /// Lifts the dock when the cursor is held against the screen edge.
    /// </summary>
    /// <remarks>
    /// The same edge, and the same dwell, that would summon a hidden dock. Nothing slides —
    /// the dock is already on screen — so all this does is put it back on top.
    /// </remarks>
    private void WatchEdgeForRaise(DateTime now)
    {
        if (!IsEdgeHeld())
        {
            _atEdgeSince = DateTime.MaxValue;
            _raised = false;
            return;
        }

        if (_atEdgeSince == DateTime.MaxValue)
        {
            _atEdgeSince = now;
            return;
        }

        if (!_raised && now - _atEdgeSince >= RevealDelay)
        {
            _raised = true;
            Lift();
        }
    }

    /// <summary>
    /// Lifts a covered dock the moment the pointer arrives on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the dock means on the part of it that can be seen — <see cref="_isPointerOverDock"/>
    /// does not count a pointer on a window lying over the dock. So a dock half under a
    /// window comes up when you go to it, and one entirely under a window does not come up
    /// when you merely work in that window over where it is; the edge is for that.
    /// </para>
    /// <para>
    /// At once, unlike the edge. The edge waits out <see cref="RevealDelay"/> because the
    /// pointer reaches it on the way to all sorts of places; arriving on the dock itself is
    /// already the deliberate act, and a wave that grows while half of it is still under a
    /// window is exactly what the lift is for. Passing over it by accident costs a lift that
    /// the hide delay puts back.
    /// </para>
    /// <para>
    /// Once per stay: an icon clicked meanwhile brings its own window forward, and that
    /// window is where the user is going, so the dock must not be lifted back over it until
    /// the pointer has left and come back.
    /// </para>
    /// </remarks>
    private void WatchHover()
    {
        if (!_isPointerOverDock())
        {
            _hoverHandled = false;
            return;
        }

        if (_hoverHandled)
        {
            return;
        }

        _hoverHandled = true;
        Lift();
    }

    /// <summary>True once the current stay on the dock has lifted it, or found it uncovered.</summary>
    private bool _hoverHandled;

    /// <summary>
    /// Lifts the dock over whatever covers it, and remembers what that was so
    /// <see cref="WatchForLeaving"/> can put it back — unless nothing does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing over it, nothing to do: re-asserting a z-order that is already right still
    /// re-composes the dock and its sheet, and would do it on every visit to the edge or to
    /// the dock. With auto-hide on nothing is remembered, since hiding is its way back.
    /// </para>
    /// <para>
    /// And never over a program the edge stands down for. The edge checks that for itself,
    /// but a hover does not come through the edge: a fullscreen window with see-through or
    /// click-through parts can leave some of the dock showing, and resting the pointer there
    /// must not bring the dock up over the game any more than the edge would.
    /// </para>
    /// </remarks>
    private void Lift()
    {
        if (_standDown() || !_chrome.IsCovered())
        {
            return;
        }

        var under = _raise();

        // Checked rather than trusted. Windows declines to put a background program's window
        // over the one in the foreground, and says nothing when it does — measured on
        // 2026-09-28, every lift over a focused File Explorer window left the dock under it.
        // A window covering the dock without having the focus is lifted over the ordinary
        // way; the one that has it needs the topmost band, which is open to anyone — it is
        // how a dock set to float stays up.
        if (_chrome.IsCovered())
        {
            _holdAbove();
        }

        if (!_lifted)
        {
            _lifted = true;
            _liftedOver = under;
        }
    }

    /// <summary>
    /// Puts a lifted dock back where it was once the pointer has left it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other half of <see cref="WatchEdgeForRaise"/>, as hiding is of revealing: the
    /// same zone keeps it up — the dock, and the strip below it down to the edge — and the
    /// same <see cref="HideDelay"/> has to pass outside it, so stepping off the bar on the
    /// way to an icon does not drop it. Nor does anything holding the dock on screen, such as
    /// the settings dialog.
    /// </para>
    /// <para>
    /// Back, not down: the dock goes under the window it was lifted over, not to the bottom,
    /// so whatever it was already in front of stays behind it. A dock that was not under
    /// anything has nowhere to go back to and is left alone.
    /// </para>
    /// </remarks>
    private void WatchForLeaving(DateTime now)
    {
        if (!_lifted)
        {
            return;
        }

        // The strip below yields to a fullscreen program the edge stands down for, as it does
        // for a revealed dock — see OnTick.
        if (_holds > 0 || _isPointerOverDock() || (IsCursorInKeepAliveZone() && !_standDown()))
        {
            _outsideSince = DateTime.MaxValue;
            return;
        }

        if (_outsideSince == DateTime.MaxValue)
        {
            _outsideSince = now;
            return;
        }

        if (now - _outsideSince >= HideDelay)
        {
            _lower(_liftedOver);
            _lifted = false;
            _liftedOver = 0;
            _outsideSince = DateTime.MaxValue;
        }
    }

    /// <summary>Reads the cursor in screen DIPs, matching the window's own coordinates.</summary>
    private bool TryGetCursor(out Point cursor)
    {
        cursor = default;
        if (!NativeMethods.GetCursorPos(out var raw))
        {
            return false;
        }

        // GetCursorPos is in physical pixels; everything else here is in DIPs.
        var dpi = VisualTreeHelper.GetDpi(_window);
        cursor = new Point(raw.X / dpi.DpiScaleX, raw.Y / dpi.DpiScaleY);
        return true;
    }

    /// <summary>
    /// True when <paramref name="x"/> is across the dock, give or take the slack — and on the
    /// dock's own display.
    /// </summary>
    /// <remarks>
    /// Bounded by the display as well as by the dock. A dock moved to one end of its edge has
    /// its slack reaching past the side of the screen, and on a desktop with another display
    /// beside this one, a cursor over there — on a different screen entirely, at whatever
    /// height this one's bottom edge happens to fall — would summon the dock, or keep it out.
    /// </remarks>
    private bool IsWithinDockColumn(double x) =>
        x >= Math.Max(_displayLeft, _window.Left - RevealZoneSlack)
        && x < Math.Min(_displayRight, _window.Left + _window.Width + RevealZoneSlack);

    /// <summary>
    /// True when the cursor is pressed against the bottom of the display, within the
    /// horizontal span the dock occupies.
    /// </summary>
    /// <remarks>
    /// Bounded below as well as above. An open half-plane would also match a cursor on a
    /// display stacked underneath this one, and — when the edge was taken from the work
    /// area rather than the display — matched the entire taskbar, so merely hovering the
    /// taskbar summoned the dock.
    /// </remarks>
    private bool IsCursorAtRevealEdge() =>
        TryGetCursor(out var cursor)
        && cursor.Y >= _revealEdgeY - RevealZoneHeight
        && cursor.Y <= _revealEdgeY
        && IsWithinDockColumn(cursor.X);

    /// <summary>
    /// The region that keeps a revealed dock on screen: the dock itself and everything below
    /// it down to the screen edge.
    /// </summary>
    /// <remarks>
    /// Without the strip below the dock, revealing is self-defeating. The cursor summons the
    /// dock by touching the screen edge, but the dock floats a margin above that edge — so
    /// the instant it arrives the cursor is not on it, the hide timer starts, and the dock
    /// slides away again a moment later. The user sees a flicker rather than a dock.
    /// </remarks>
    private bool IsCursorInKeepAliveZone() =>
        TryGetCursor(out var cursor)
        && cursor.Y >= _shownTop
        && IsWithinDockColumn(cursor.X);

    public void Hide()
    {
        if (Visibility is DockVisibility.Hiding or DockVisibility.Hidden)
        {
            return;
        }

        Visibility = DockVisibility.Hiding;
        _outsideSince = DateTime.MaxValue;

        // Click-through the moment it starts leaving, so the sliding window never swallows a
        // click meant for whatever is underneath.
        _chrome.SetClickThrough(true);

        Slide(_hiddenTop, () =>
        {
            Visibility = DockVisibility.Hidden;
            _atEdgeSince = DateTime.MaxValue;
        });
    }

    public void Reveal()
    {
        if (Visibility is DockVisibility.Revealing or DockVisibility.Shown)
        {
            return;
        }

        Visibility = DockVisibility.Revealing;
        _atEdgeSince = DateTime.MaxValue;
        _chrome.SetClickThrough(false);
        _raise();

        Slide(_shownTop, () =>
        {
            Visibility = DockVisibility.Shown;
            _outsideSince = DateTime.MaxValue;
        });
    }

    private void Slide(double targetTop, Action onCompleted)
    {
        var animation = new DoubleAnimation
        {
            To = targetTop,
            Duration = SlideDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            // Stop the animation holding the property, then commit the value ourselves so
            // later assignments to Top are not silently ignored.
            _window.BeginAnimation(Window.TopProperty, null);
            _window.Top = targetTop;
            onCompleted();
        };

        _window.BeginAnimation(Window.TopProperty, animation);
    }
}
