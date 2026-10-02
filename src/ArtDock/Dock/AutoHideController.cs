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

    private readonly Window _window;
    private readonly WindowChrome _chrome;
    private readonly Func<bool> _isPointerOverDock;

    /// <summary>
    /// True when a window of another program lies over the dock — the owner's
    /// <see cref="WindowChrome.IsCovered"/>, which needs the bar's place on the screen.
    /// </summary>
    private readonly Func<bool> _isCovered;

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
    /// The bar at rest on the screen, in physical pixels — the owner's, which measures it the way
    /// the handle is placed from; empty before the dock has been laid out. Only its left and
    /// right are asked: they bound both zones sideways, see <see cref="IsUnderDock(double, Rect, Rect)"/>.
    /// </summary>
    private readonly Func<Rect> _restingBar;

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

    /// <summary>
    /// True while the holds have up a dock that had been put away from the tray, which goes
    /// away again once the last of them lets go — see <see cref="HoldRevealed"/>.
    /// </summary>
    private bool _putBack;

    private DateTime _outsideSince = DateTime.MaxValue;
    private DateTime _atEdgeSince = DateTime.MaxValue;

    private double _shownTop;
    private double _hiddenTop;

    public AutoHideController(
        Window window,
        WindowChrome chrome,
        Func<bool> isPointerOverDock,
        Func<bool> isCovered,
        Func<nint> raise,
        Action holdAbove,
        Action<nint> lower,
        Func<bool> standDown,
        Func<Rect> restingBar)
    {
        _window = window;
        _chrome = chrome;
        _isPointerOverDock = isPointerOverDock;
        _isCovered = isCovered;
        _raise = raise;
        _holdAbove = holdAbove;
        _lower = lower;
        _standDown = standDown;
        _restingBar = restingBar;

        // Always running, whatever the settings. With nothing hiding the dock the edge is still
        // watched, to lift a dock that has been covered — see WatchForLift — and that is wanted
        // for a dock set to float above everything as much as for one that is not: another
        // window that floats can still cover it. While something hides it — auto-hide, or a
        // maximized or fullscreen window in front — the same edge brings it back instead.
        _watch.Tick += OnTick;
        _watch.Start();
    }

    /// <summary>Whether auto-hide is on in the settings. Turning it off reveals the dock.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// True while a window in front fills the dock's display — maximized there or fullscreen —
    /// and the dock hides as auto-hide would, whatever the setting; see <see cref="Yield"/>.
    /// </summary>
    public bool IsYielding { get; private set; }

    /// <summary>
    /// True while the dock hides and reveals itself: auto-hide is on, or it is yielding to a
    /// window in front.
    /// </summary>
    public bool Hides => IsEnabled || IsYielding;

    /// <summary>
    /// True while the dock is away because it was put away on purpose — hidden from the tray
    /// while nothing was hiding it — which only the tray brings back for good.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not the edge, and not a window in front going: a dock put away from the tray while a
    /// maximized window is in front would otherwise come back the moment that window did, or
    /// the moment the pointer brushed the bottom of the screen. Nor is it marked by the handle,
    /// which would be offering something the pointer cannot do.
    /// </para>
    /// <para>
    /// Whatever holds the dock up — the settings dialog, for its preview — brings it back for
    /// as long as it holds it, and no longer; see <see cref="HoldRevealed"/>.
    /// </para>
    /// </remarks>
    public bool IsPutAway { get; private set; }

    /// <summary>
    /// Hides the dock while a window in front fills its display, as auto-hide would — and
    /// brings it back when that window goes, unless something else still has it hidden.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked for on 2026-09-30, in place of the dock going under such a window. Going under
    /// left the dock where it was, so bringing it up from the edge was a change of z-order with
    /// nothing to see; hiding means the edge brings it up the way auto-hide does, sliding in
    /// from the bottom, and it slides away again once the pointer has left — the auto-hide
    /// path, every part of it, for as long as the window is in front.
    /// </para>
    /// <para>
    /// At once, rather than after the hide delay — unless the pointer is on the dock, as it is
    /// when an icon has just been clicked that opens maximized; then it goes when the pointer
    /// leaves, as auto-hide's would. The hide delay is there so that stepping off the bar for a
    /// moment does not send the dock away, and a window taking the display is no such moment.
    /// </para>
    /// <para>
    /// A lift in progress is handed over rather than let go: a dock held above a window that
    /// has just been maximized under the pointer stays held until the slide away has taken it
    /// off the screen, which is where the hiding lets go.
    /// </para>
    /// </remarks>
    public void Yield(bool yielding)
    {
        if (IsYielding == yielding)
        {
            return;
        }

        IsYielding = yielding;
        _outsideSince = DateTime.MaxValue;

        if (yielding)
        {
            _lifted = false;
            _liftedOver = 0;

            if (_holds == 0 && !_isPointerOverDock() && !IsCursorInKeepAliveZone())
            {
                Hide();
            }

            return;
        }

        // Auto-hide carries on as it was, and a dock put away from the tray stays away.
        if (IsEnabled || IsPutAway)
        {
            return;
        }

        if (Visibility is DockVisibility.Shown or DockVisibility.Revealing)
        {
            // Up at the edge's asking when the window went: from here it is a lift like any
            // other, let go of once the pointer has been away for the hide delay.
            _lifted = true;
            _liftedOver = 0;
            return;
        }

        Reveal();
    }

    /// <summary>
    /// True once the current visit to the edge has raised the dock.
    /// </summary>
    /// <remarks>
    /// One raise per visit. Re-asserting the z-order on every tick would re-compose the dock
    /// and the sheet behind it thirty times a second, for a window that is already on top.
    /// </remarks>
    private bool _raised;

    /// <summary>Delay before a cursor at the screen edge brings the dock back or lifts it.</summary>
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

    /// <summary>
    /// The whole of the dock's display, in physical pixels, which bounds both zones sideways
    /// along with the bar — see <see cref="IsUnderDock(double, Rect, Rect)"/>.
    /// </summary>
    private Rect _display = Rect.Empty;

    /// <summary>
    /// Records where the dock sits when shown and when hidden, and where the bottom of its
    /// display is, in DIPs — and the display's whole area in physical pixels, as the bar and the
    /// cursor are measured across. Call after positioning the dock, which puts a hidden one where
    /// it hides.
    /// </summary>
    /// <remarks>
    /// Both worked out by the owner, from the same physical pixels and the same scale. The
    /// hidden place used to be worked out here, as the shown top plus the window's height — and
    /// the height was WPF's, which lags a change of scale, while the top was not: after a wake
    /// that briefly counted the dock on the 4K display, the two were in different scales and
    /// the dock hid 48 pixels short, with the top of its bar over the taskbar. Nor is a hidden
    /// dock moved from here any more, through WPF's units: the owner has already placed it in
    /// physical ones.
    /// </remarks>
    public void AnchorTo(double shownTop, double hiddenTop, double revealEdgeY, Rect display)
    {
        _shownTop = shownTop;
        _hiddenTop = hiddenTop;
        _revealEdgeY = revealEdgeY;
        _display = display;
    }

    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled)
        {
            return;
        }

        IsEnabled = enabled;

        // Whichever way the dock was being held up — a lift with auto-hide off, a reveal with
        // it on — it is let go of here: the other one has it from now on, and neither lets go
        // of a hold the other took. A lift puts the dock back by lowering it and a reveal by
        // hiding it, and a dock left in the topmost band by one would stay there for good.
        _lifted = false;
        _liftedOver = 0;
        _outsideSince = DateTime.MaxValue;
        _lower(0);

        if (enabled)
        {
            // Auto-hide has the dock from here, and the edge brings it back whatever put it away.
            IsPutAway = false;
            return;
        }

        // Still hidden, and rightly, while a window in front fills the display.
        if (IsYielding)
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
    /// <para>
    /// Adjusting settings while the dock keeps sliding away is unworkable — you cannot see
    /// what a change did.
    /// </para>
    /// <para>
    /// A dock put away from the tray comes up for it as well, the settings dialog's preview
    /// being the point of the dialog, and goes away again when the last hold lets go — put
    /// away, as it was, even with a window in front that hides it anyway, so that window going
    /// does not bring it back. Not if the tray has said otherwise meanwhile
    /// (<see cref="Choose"/>), nor once auto-hide has been turned on, which has the dock from
    /// then on. Asked for on 2026-10-02: the dock used to stay up after the dialog closed,
    /// with the tray's entry still offering to show it. The keyboard's hold is one of these
    /// too, so a dialog opened from the dock's menu while it had the keys puts the dock away
    /// when that dialog closes, rather than leaving it up.
    /// </para>
    /// </remarks>
    /// <param name="hold">True to take a hold, false to let one go.</param>
    /// <param name="lingers">
    /// Letting go: false sends a dock that hides straight back, rather than after the hide delay —
    /// for a hold that turned out to be the start of something else, the Windows key and Ctrl
    /// pressed on the way to another shortcut, where the delay would only leave the dock up over it.
    /// </param>
    public void HoldRevealed(bool hold, bool lingers = true)
    {
        _holds = Math.Max(0, _holds + (hold ? 1 : -1));

        if (_holds > 0)
        {
            // Asked before the reveal, which ends the putting away.
            _putBack |= IsPutAway;
            Reveal();
            return;
        }

        if (_putBack)
        {
            _putBack = false;

            if (!IsEnabled)
            {
                Hide(putAway: true);
                return;
            }
        }

        // Released: start the hide timer from now rather than from whenever the pointer
        // last wandered off, so the dock does not vanish the instant a dialog closes — or, not
        // lingering, from long enough ago that the next look sends it away.
        _outsideSince = lingers ? DateTime.UtcNow : DateTime.MinValue;
    }

    /// <summary>
    /// Hides the dock or brings it back because it was asked to in so many words — the tray's
    /// entry, the hotkey, a second launch — rather than by anything that moves it of its own
    /// accord.
    /// </summary>
    /// <remarks>
    /// A choice, and kept: made while something holds the dock up, it is not undone when the
    /// hold lets go. A dock put away, brought up for the settings dialog, then hidden from the
    /// tray and shown again is not put away once more when the dialog closes.
    /// </remarks>
    public void Choose(bool shown)
    {
        _putBack = false;

        if (shown)
        {
            Reveal();
        }
        else
        {
            Hide();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;

        if (!Hides)
        {
            WatchHover();
            WatchForLift(now);
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

        // A dock put away from the tray is not brought back by the edge, even while a window in
        // front has it hiding the way auto-hide would.
        if (IsEdgeHeld() && !IsPutAway)
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
    /// True when the cursor is held against the reveal edge and the edge is not standing
    /// down for a fullscreen program — where both bringing a hidden dock back and lifting a
    /// covered one start.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The edge and nothing else. The handle marks where the dock is and does not bring it: it
    /// did, by resting the pointer on it, until 2026-09-30, when it was made a mark only on
    /// request. It lies over the bottom rows of other windows — status bars, scroll bars —
    /// where the pointer goes on business of its own, whereas the edge is only reached by
    /// pushing past the taskbar.
    /// </para>
    /// <para>
    /// Standing down resets the hold rather than pausing it. The pointer can sit at the edge
    /// for as long as the game is scrolling, and once the game is left the dock should need a
    /// hold of its own, not arrive the instant focus moves because the old one ran out long ago.
    /// </para>
    /// </remarks>
    private bool IsEdgeHeld() => IsCursorAtRevealEdge() && !_standDown();

    /// <summary>
    /// Lifts the dock when the cursor is held against the screen edge.
    /// </summary>
    /// <remarks>
    /// The same edge, and the same dwell, that would summon a hidden dock. Nothing slides —
    /// the dock is already on screen — so all this does is put it back on top: over the
    /// windows that bury a dock that does not float, and over one that floats lying over a dock
    /// that does. A maximized or fullscreen window in front is not one of these: the dock hides
    /// for that, and the edge brings it back by revealing it (<see cref="Yield"/>).
    /// </remarks>
    private void WatchForLift(DateTime now)
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
        if (_standDown() || !_isCovered())
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
        if (_isCovered())
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
    /// The other half of <see cref="WatchForLift"/>, as hiding is of revealing: the
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
            // No longer lifted before it is lowered: lowering ends with the owner's look at what
            // is in front, and that look should find the lift over.
            var over = _liftedOver;
            _lifted = false;
            _liftedOver = 0;
            _outsideSince = DateTime.MaxValue;
            _lower(over);
        }
    }

    /// <summary>
    /// Reads the cursor in screen DIPs, matching the window's own coordinates — and across in
    /// physical pixels, as Windows gives it, which is what the bar is measured across in.
    /// </summary>
    private bool TryGetCursor(out Point cursor, out double across)
    {
        cursor = default;
        across = 0;
        if (!NativeMethods.GetCursorPos(out var raw))
        {
            return false;
        }

        // GetCursorPos is in physical pixels; everything else here is in DIPs, bar the span
        // across that is under the dock.
        var dpi = VisualTreeHelper.GetDpi(_window);
        cursor = new Point(raw.X / dpi.DpiScaleX, raw.Y / dpi.DpiScaleY);
        across = raw.X;
        return true;
    }

    /// <summary>
    /// True when a pointer at <paramref name="x"/> across the screen is under the dock: across
    /// the bar at rest, and on the dock's own display. All three in physical pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// As wide as the bar and no wider, since 2026-09-30. It was the dock's whole window and
    /// 80 DIPs more at each end — the window keeps room beside the bar for the wave to grow
    /// into, and for the slot a drop opens — which came out at about half as wide again as the
    /// dock, so the edge brought the dock up for a pointer well to the side of it. Measured the
    /// way the handle is placed from, so where the handle is as wide as the dock the two line up.
    /// The window's own width was WPF's too, which lags a change of scale; the bar is read off
    /// the screen.
    /// </para>
    /// <para>
    /// Bounded by the display as well. The dock's placement keeps the bar on its screen, but a
    /// bar with more icons than its screen is wide reaches past the side, and on a desktop with
    /// another display beside this one, a cursor over there — on a different screen entirely, at
    /// whatever height this one's bottom edge happens to fall — would summon the dock, or keep
    /// it out.
    /// </para>
    /// <para>
    /// Nothing is under a dock that has not been laid out, or placed on a display, yet.
    /// </para>
    /// </remarks>
    public static bool IsUnderDock(double x, Rect bar, Rect display) =>
        !bar.IsEmpty
        && !display.IsEmpty
        && x >= Math.Max(bar.Left, display.Left)
        && x < Math.Min(bar.Right, display.Right);

    /// <summary>True when a pointer at <paramref name="x"/>, in physical pixels, is under the dock.</summary>
    private bool IsUnderDock(double x) => IsUnderDock(x, _restingBar(), _display);

    /// <summary>
    /// True when the cursor is pressed against the bottom of the display, under the dock.
    /// </summary>
    /// <remarks>
    /// Bounded below as well as above. An open half-plane would also match a cursor on a
    /// display stacked underneath this one, and — when the edge was taken from the work
    /// area rather than the display — matched the entire taskbar, so merely hovering the
    /// taskbar summoned the dock.
    /// </remarks>
    private bool IsCursorAtRevealEdge() =>
        TryGetCursor(out var cursor, out var across)
        && cursor.Y >= _revealEdgeY - RevealZoneHeight
        && cursor.Y <= _revealEdgeY
        && IsUnderDock(across);

    /// <summary>
    /// The region that keeps a revealed dock on screen: the dock itself and everything below
    /// it down to the screen edge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without the strip below the dock, revealing is self-defeating. The cursor summons the
    /// dock by touching the screen edge, but the dock floats a margin above that edge — so
    /// the instant it arrives the cursor is not on it, the hide timer starts, and the dock
    /// slides away again a moment later. The user sees a flicker rather than a dock.
    /// </para>
    /// <para>
    /// As wide as the edge that brings the dock up, and narrowed with it on 2026-09-30: never
    /// narrower, or a dock brought up from the end of the edge would find the pointer outside
    /// what keeps it up, and go again. The dock itself is <see cref="_isPointerOverDock"/>'s,
    /// and reaches as far as the wave does.
    /// </para>
    /// </remarks>
    private bool IsCursorInKeepAliveZone() =>
        TryGetCursor(out var cursor, out var across)
        && cursor.Y >= _shownTop
        && IsUnderDock(across);

    /// <summary>
    /// Slides the dock away. Hidden with nothing hiding it, it is the tray's <em>Hide dock</em>:
    /// put away on purpose.
    /// </summary>
    public void Hide() => Hide(putAway: !Hides);

    private void Hide(bool putAway)
    {
        if (Visibility is DockVisibility.Hiding or DockVisibility.Hidden)
        {
            return;
        }

        // Before the change of visibility, which the handle hears about and asks this.
        IsPutAway = putAway;

        Visibility = DockVisibility.Hiding;
        _outsideSince = DateTime.MaxValue;

        // A dock hidden while lifted — from the tray, with auto-hide off — is not lifted any
        // more: there is nothing on screen to put back under anything.
        _lifted = false;
        _liftedOver = 0;

        // Click-through the moment it starts leaving, so the sliding window never swallows a
        // click meant for whatever is underneath.
        _chrome.SetClickThrough(true);

        Slide(_hiddenTop, () =>
        {
            Visibility = DockVisibility.Hidden;
            _atEdgeSince = DateTime.MaxValue;

            // Out of the topmost band once it is off the screen, and not before: let go of on
            // the way down, the dock would drop under the window it is sliding over, and be
            // seen to.
            _lower(0);
        });
    }

    /// <summary>
    /// Slides the dock back on screen, over whatever is in front.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Held in the topmost band until it hides again, unless it floats there anyway. A raise
    /// alone is what this used to do, and Windows declines that over the window in the
    /// foreground — so with <em>Always on top</em> off it slid up <em>under</em> whatever had
    /// the focus. Found on 2026-09-28 and reported on 2026-09-30. The lift had the same
    /// refusal, and checks whether it got through; a reveal cannot check, since the dock starts
    /// below the bottom of the screen where nothing covers it, and a dock that is being
    /// revealed is wanted on top in any case. It is also what brings the dock up over a
    /// maximized or fullscreen window it is yielding to, which is always the foreground.
    /// </para>
    /// <para>
    /// With nothing hiding the dock — auto-hide off, and no window in front filling its
    /// display — what reveals it is the tray, auto-hide being turned off, such a window going,
    /// or something holding it up, as the settings dialog does — which puts a dock that had
    /// been put away back away when it lets go (<see cref="HoldRevealed"/>); and nothing else
    /// hides it again to let go. So it counts as a lift, which is let go of like one, once the
    /// pointer has been away for the hide delay.
    /// </para>
    /// </remarks>
    public void Reveal()
    {
        if (Visibility is DockVisibility.Revealing or DockVisibility.Shown)
        {
            return;
        }

        IsPutAway = false;
        Visibility = DockVisibility.Revealing;
        _atEdgeSince = DateTime.MaxValue;
        _chrome.SetClickThrough(false);
        _raise();
        _holdAbove();

        if (!Hides && !_lifted)
        {
            _lifted = true;
            _liftedOver = 0;
            _outsideSince = DateTime.MaxValue;
        }

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
