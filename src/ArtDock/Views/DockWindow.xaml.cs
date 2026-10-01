using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ArtDock.Controls;
using Microsoft.Win32;
using ArtDock.Dock;
using ArtDock.IconSets;
using ArtDock.Interop;
using ArtDock.Localization;
using ArtDock.Services;

namespace ArtDock.Views;

/// <summary>Whether the dock can be seen, and if not, why not.</summary>
public enum DockPresence
{
    OnScreen,

    /// <summary>Slid off the edge by auto-hide; the cursor at that edge brings it back.</summary>
    AutoHidden,

    /// <summary>Put away from the tray menu while nothing was hiding it, so only the tray brings it back.</summary>
    HiddenFromTray,

    /// <summary>Shown, but on no display that is connected.</summary>
    OffScreen
}

/// <summary>
/// The floating dock window.
///
/// Transparency, the frameless look and topmost come from WPF window properties in the
/// XAML; this file adds the Win32 traits WPF has no property for (tool window, no
/// activation), sizes the window to fit a fully magnified wave, and parks it on the
/// display's bottom edge.
/// </summary>
public sealed partial class DockWindow : Window
{
    private readonly SettingsStore _settings;
    private readonly PinnedAppsService _pinnedApps = new();
    private readonly RunningAppsService _runningApps = new();
    private readonly DockBar _dock;

    private IReadOnlyList<DockItem> _items = [];

    /// <summary>
    /// The icon set the items are drawn from, or null for their own icons. Looked up when the
    /// items are rebuilt, which a change of set is — see <see cref="SignatureOf"/>.
    /// </summary>
    private IconSet? _iconSet;

    private WindowChrome? _chrome;
    private AutoHideController? _autoHide;
    private BackdropWindow? _backdrop;
    private RecycleBinWatch? _binWatch;
    private readonly MenuHost _menus;
    private readonly ForegroundApp _foreground = new();

    /// <summary>
    /// The whole of the display the dock is on, in physical pixels, as it was last placed —
    /// what a program has to cover for the edge to stand down for it.
    /// </summary>
    private Rect _display = Rect.Empty;

    /// <summary>
    /// The usable part of that display, in physical pixels, as it was last placed — the handle
    /// sits just above its bottom, which is the top of the taskbar.
    /// </summary>
    private Rect _workArea = Rect.Empty;

    /// <summary>
    /// The scale of that display, for the handle's own dimensions. Taken from the display, not
    /// from the dock's window, which is no guide to it while the dock is hidden — see
    /// <see cref="MonitorDpi.ScaleAt"/>.
    /// </summary>
    private double _handleScale = 1;

    /// <summary>The handle that marks the dock while it is out of sight; made the first time it is wanted.</summary>
    private HandleWindow? _handle;

    /// <summary>
    /// Looks, a few times a second, at what is in front of the dock's display — see
    /// <see cref="CheckFront"/>.
    /// </summary>
    /// <remarks>
    /// A quarter of a second: a window going fullscreen or being maximized is not something the
    /// dock has to beat — the maximize animation takes about as long — a change of foreground
    /// is heard at once by the hook besides, and the look is a handful of calls with no program
    /// resolved unless the Exclusions page lists any.
    /// </remarks>
    private readonly DispatcherTimer _frontWatch = new() { Interval = TimeSpan.FromMilliseconds(250) };

    /// <summary>Held as a field so the collector cannot take the delegate the hook still calls.</summary>
    private readonly WindowsApi.WinEventProc _onForeground;

    /// <summary>The hook that reports a change of foreground, or 0 while there is none.</summary>
    private nint _foregroundHook;

    /// <summary>
    /// The window the dock has stood aside for — put under, and out of the topmost band — while
    /// it fills the dock's display in front; 0 when there is none.
    /// </summary>
    private nint _asideFor;

    /// <summary>
    /// True while the window in front has the whole of the dock's display — fullscreen, or a
    /// program on the Exclusions page filling it — when the handle is not shown over it.
    /// </summary>
    private bool _fullscreenInFront;

    /// <summary>
    /// True while the dock is on screen and nothing of its bar can be seen, for the windows
    /// lying over it — see <see cref="CheckSight"/>.
    /// </summary>
    private bool _outOfSight;

    /// <summary>
    /// Set once the window has closed, so a late settings preview — the dialog closing after
    /// the dock, on the way out — does not make a handle for a dock that is gone.
    /// </summary>
    private bool _closed;

    /// <summary>
    /// What the shell posts to this window when the Recycle Bin's icon may have changed.
    /// </summary>
    private const int WM_RECYCLEBINICON = NativeMethods.WM_APP + 1;

    /// <summary>
    /// The settings currently in force, which is not the same as the settings on disk.
    /// </summary>
    /// <remarks>
    /// The settings dialog shows a change by pushing it here without saving it, so anything
    /// that re-applies settings of its own accord — a change of scale, a change of system
    /// colours, the dock asking for more room — has to re-apply <i>these</i> rather than the
    /// stored ones. Reading the stored ones silently reverted whatever was being previewed,
    /// and moving the dock to another display did exactly that in a loop: the move changed
    /// the scale, the scale change re-applied the stored display, and the dock went back to
    /// the one it came from.
    /// </remarks>
    private DockSettings _applied;

    public DockWindow(SettingsStore settings, MenuHost menus)
    {
        InitializeComponent();

        _settings = settings;
        _applied = settings.Current;
        _menus = menus;
        _dock = new DockBar(settings.Current.Metrics);
        _dock.Activated += OnItemActivated;
        _dock.Reordered += OnItemReordered;

        // The dock decides how big a window it needs — a drop preview adds a slot, a taller
        // label wants more headroom — and neither is a settings change, so neither reaches
        // ApplySettings.
        _dock.PreferredSizeChanged += (_, _) => SizeToDock(_applied);

        // The acrylic sheet is a separate window, so it has to be told where the bar is
        // every time the bar moves or changes width. So does the handle, which is placed under
        // the bar — though only its resting shape, so a wave moves it not at all.
        _dock.BarRectChanged += (_, _) =>
        {
            SyncBackdrop();
            SyncHandle();
        };
        LocationChanged += (_, _) =>
        {
            SyncBackdrop();
            SyncHandle();
        };

        // The sheet is the window's twin, so it follows a change of size as well as of place:
        // see BackdropWindow.Follow.
        SizeChanged += (_, _) => SyncBackdrop();

        // Settings are applied from OnSourceInitialized, before the dock has been laid out
        // and while its bar is therefore still nonsense. One sync once the first frame is
        // on screen is what puts the sheet under a bar that actually exists.
        ContentRendered += (_, _) =>
        {
            SyncBackdrop();
            SyncHandle();

            // Showing a window puts it at the top of its band, over whatever is in front —
            // an excluded game included, which is what a dock restarted in the middle of one
            // used to be drawn over until something moved it.
            CheckFront();
        };
        Root.Children.Add(_dock);

        _frontWatch.Tick += (_, _) => CheckFront();
        _onForeground = (_, _, _, _, _, _, _) => CheckFront(foregroundChanged: true);

        _runningApps.Changed += OnRunningAppsChanged;
        _settings.Changed += (_, updated) => Dispatcher.Invoke(() => ApplySettings(updated));

        // Resolution changes and a monitor being unplugged shift the display out from under the
        // dock, and the taskbar taking or giving back room shifts the work area; re-applying
        // settings re-anchors it. They are two notifications, not one — see the second.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        Closed += (_, _) =>
        {
            _closed = true;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _runningApps.Dispose();
            _backdrop?.Dispose();
            _binWatch?.Dispose();
            _handle?.Dispose();
            _frontWatch.Stop();
            UnhookForeground();
        };
    }

    /// <summary>Raised when the user asks for the settings dialog from the dock's own menu.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>True when the dock is on screen rather than hidden at the edge.</summary>
    public bool IsDockShown => _autoHide?.Visibility is not (DockVisibility.Hidden or DockVisibility.Hiding);

    /// <summary>
    /// True when the dock has settled on screen: shown, and not sliding either way — what it
    /// takes for the dock to answer the pointer and take clicks.
    /// </summary>
    /// <remarks>
    /// Stricter than <see cref="IsDockShown"/>, which counts a dock on its way back as shown —
    /// rightly, for the tray and a second launch, which ask whether the dock is coming or
    /// going. Not so for the pointer: the edge that brought the dock back has the pointer right
    /// under it, and the wave would start while the bar was still rising.
    /// </remarks>
    private bool IsSettled => _autoHide is { Visibility: DockVisibility.Shown };

    /// <summary>Whether the dock can be seen where it stands — what a second launch asks.</summary>
    /// <remarks>
    /// Shown is not the same as seen: a window on no connected display is as good as hidden,
    /// so that is asked of Windows as well. Being covered by other windows is not detected —
    /// a dock that does not float can be buried — which is why a second launch lifts it with
    /// <see cref="Raise"/> whatever this says.
    /// </remarks>
    public DockPresence Presence
    {
        get
        {
            if (_autoHide is { } autoHide && !IsDockShown)
            {
                // With auto-hide on, the tray's Hide is the same slide the timer does, and the
                // edge brings it back either way.
                if (autoHide.IsPutAway)
                {
                    return DockPresence.HiddenFromTray;
                }

                if (autoHide.IsEnabled)
                {
                    return DockPresence.AutoHidden;
                }

                // Away only because a window in front fills the display. The notice this is
                // asked for takes the foreground from that window, and the dock comes back for
                // anything that does not fill the display — so by the time it is read, the dock
                // is where the rest of this looks for it.
            }

            return IsVisible && MonitorDpi.IsOnAnyDisplay(new WindowInteropHelper(this).Handle)
                ? DockPresence.OnScreen
                : DockPresence.OffScreen;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // The HWND only exists from here on, so the ex-style work cannot run any earlier.
        _chrome = new WindowChrome(this);
        _chrome.ApplyDockStyles();

        // So the pointer on a window lying over the dock is that window's, not the dock's — and
        // anywhere at all is someone else's until the dock has settled on screen. The pointer
        // is polled, not delivered, so a dock that still had some of itself on the screen while
        // it counted as hidden waved and labelled its icons under a pointer that was on the
        // taskbar; reported after a wake on 2026-09-30. On its way back it is no different: the
        // pointer that brought it is right there at the edge, and the wave started under a bar
        // still sliding up — asked the same day to wait until the slide has landed.
        var chrome = _chrome;
        _dock.IsCoveredAt = (x, y) => !IsSettled || chrome.IsCoveredAt(x, y);

        // Clicks have to come from the window procedure: the dock is deliberately
        // WS_EX_NOACTIVATE, and WPF does not route mouse input to a non-activating window.
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        source.AddHook(OnWindowMessage);
        _autoHide = new AutoHideController(
            this, _chrome, _dock.IsPointerOverDock, IsCovered, Raise, HoldAbove, Lower,
            IsFullscreenAppInFront, RestingBarOnScreen);

        // The handle arrives as the dock slides away and goes as it slides back. Whether the
        // dock can be seen is only asked of a dock that is on screen, so that is asked again
        // too — a dock that has just arrived may have arrived under something.
        _autoHide.VisibilityChanged += (_, _) =>
        {
            if (!CheckSight())
            {
                SyncHandle();
            }
        };

        // What is in front is watched for as long as the dock is up, whatever the settings: the
        // dock hides for anything that fills its display, and the handle comes up over it. The
        // hook is out of context, so its callback arrives on this thread through the message
        // loop, which is running by the time anything can change the foreground.
        _frontWatch.Start();
        HookForeground();

        // Before the first ApplySettings, which is where the bin's icon is first read: a
        // change landing between the two is then either in that read or announced after it.
        _binWatch = RecycleBinWatch.Start(source.Handle, WM_RECYCLEBINICON);

        ApplySettings(_settings.Current);

        // Started after the window exists so the WinEvent hooks land on a thread with a
        // running message loop.
        _runningApps.Start();
        RefreshRunningState();
    }

    /// <summary>
    /// What the dock's contents depend on. Geometry is deliberately absent: a change to icon
    /// size or gap must not count as a change of contents.
    /// </summary>
    private string? _itemsSignature;

    /// <summary>Applies settings to the live dock; safe to call repeatedly.</summary>
    public void ApplySettings(DockSettings settings)
    {
        _applied = settings;

        var signature = SignatureOf(settings);
        if (signature != _itemsSignature)
        {
            _itemsSignature = signature;
            _iconSet = IconSetLibrary.Installed.Find(settings.IconSet);
            _items = ResolveItems(settings);
            _dock.SetItems(_items);
            RefreshRunningState();
        }

        // Not in ApplyAppearance: that path is what a slider drag takes, and this is about
        // what the dock will let you do rather than about how it looks.
        _dock.ReorderEnabled = !settings.LockItemOrder;

        ApplyAppearance(settings);
        ApplyAlwaysOnTop(settings.AlwaysOnTop);

        if (_autoHide is { } autoHide)
        {
            autoHide.HideDelay = TimeSpan.FromMilliseconds(settings.HideDelayMs);
            autoHide.RevealDelay = TimeSpan.FromMilliseconds(settings.RevealDelayMs);
            autoHide.SetEnabled(settings.AutoHide);
        }

        // After auto-hide has been told: turning it off brings the dock back, and the handle
        // goes with that — but turning the handle itself on or off changes nothing auto-hide
        // hears about.
        SyncHandle();

        // Last, after ApplyAlwaysOnTop has set the band: the list may have changed, and a
        // re-apply for any other reason — a display change, a preview — must leave a dock that
        // is standing aside where it is. It also asks again whether the dock can be seen, for
        // the handle, which a change of display, of size or of the handle's own setting can
        // each change.
        CheckFront();
    }

    /// <summary>
    /// Applies only the settings that change how the dock looks, not what is in it.
    /// </summary>
    /// <remarks>
    /// This is the path a slider drag takes, so it must stay cheap: it touches geometry and
    /// colour and nothing else. Rebuilding the items here — which is what used to happen —
    /// meant re-reading every icon from the shell on each tick.
    /// </remarks>
    public void ApplyAppearance(DockSettings settings)
    {
        // The sheet is placed once, after both the row and the window have moved. Moving the
        // dock along its edge re-lays the row out inside the window and moves the window, and
        // the sheet follows each of those on its own — the first time against the other's old
        // position, which parked the blur a whole slider tick away from the bar for as long as
        // DWM took to compose the second.
        _holdBackdrop = true;
        try
        {
            _dock.UpdateMetrics(settings.Metrics);
            _dock.RowAlignment = settings.EdgeAlignment;
            _dock.SetBarAppearance(EffectiveBarColor(settings), settings.BarOpacity);
            ApplyBackdrop(settings.BlurBackground);

            if (_previewing)
            {
                _dock.PreviewSweep(settings.PreviewSweep);
            }

            // The system animation setting is honoured on its own; the stored flag is an
            // additional, explicit opt-out.
            _dock.MagnificationEnabled = !settings.ReduceMotion && SystemParameters.ClientAreaAnimation;

            SizeToDock(settings);
        }
        finally
        {
            _holdBackdrop = false;
        }

        SyncBackdrop();

        // The handle's width can follow the icon size, and its colour follows the bar's.
        SyncHandle();
    }

    /// <summary>
    /// Set while the dock is being moved in more than one step, so the sheet behind it is
    /// placed once at the end rather than after each.
    /// </summary>
    private bool _holdBackdrop;

    /// <summary>
    /// The settings a change to the dock's own contents should be written into.
    /// </summary>
    /// <remarks>
    /// The items come from what is on screen; everything else from what is on disk. The
    /// settings dialog shows an order without saving it, so the indices a drag on the dock
    /// produces refer to the order being shown — applying them to the stored one moves the
    /// wrong icon. The rest has to stay as stored, or an unaccepted preview would be
    /// persisted by an unrelated reorder and Cancel would have nothing to put back.
    /// </remarks>
    private DockSettings ContentsToSave()
    {
        var settings = _settings.Current.Clone();
        settings.PinnedApps = _applied.Clone().PinnedApps;
        return settings;
    }

    /// <summary>
    /// The colour the bar is actually painted: the taskbar's, when that is what was asked
    /// for, and otherwise the one that was chosen.
    /// </summary>
    private static string EffectiveBarColor(DockSettings settings) =>
        settings.UseTaskbarColor
            ? BarPalette.ToHex(TaskbarColour.Current())
            : settings.BarColor;

    /// <summary>
    /// Brings the acrylic sheet up or takes it down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sheet is created the first time it is asked for rather than at startup, so a
    /// dock with the blur turned off never makes a second window at all. Once made it is
    /// hidden rather than destroyed, because building it again costs a DWM round trip and
    /// the setting is a toggle people flip while watching the dock.
    /// </para>
    /// <para>
    /// Taken down, it is not hidden at once: it has been drawing the bar, and the dock takes
    /// the bar back through WPF's render thread, which was measured reaching the screen a frame
    /// or two after anything the sheet does. Hidden straight away, the bar would be gone for
    /// those frames. So the dock draws the bar again first, and the sheet goes once a few of the
    /// dock's frames have been rendered — which costs, at most, a frame or two of the bar drawn
    /// twice.
    /// </para>
    /// </remarks>
    private void ApplyBackdrop(bool enabled)
    {
        if (!enabled)
        {
            _dock.DrawsShadow = true;

            if (!_dock.DrawsBar)
            {
                _dock.DrawsBar = true;
                HideBackdropOnceDrawn();
            }
            else if (!_backdropLeaving)
            {
                _backdrop?.Hide();
            }

            return;
        }

        if (_backdrop is null)
        {
            _backdrop = new BackdropWindow();
            _backdrop.SetTopmost(Topmost);
            _backdrop.Show();
        }
        else if (!_backdrop.IsVisible)
        {
            _backdrop.Show();
        }

        SyncBackdrop();
    }

    /// <summary>True while the sheet is waiting to be hidden — see <see cref="HideBackdropOnceDrawn"/>.</summary>
    private bool _backdropLeaving;

    /// <summary>How many of the dock's frames to wait before the sheet goes.</summary>
    /// <remarks>
    /// The dock's frames were measured reaching the screen up to two frames after the
    /// sheet's changes do; one more for good measure.
    /// </remarks>
    private const int BackdropLeavingFrames = 3;

    /// <summary>
    /// Hides the sheet once the dock has had time to put the bar it took back on the screen.
    /// </summary>
    private void HideBackdropOnceDrawn()
    {
        if (_backdropLeaving)
        {
            return;
        }

        _backdropLeaving = true;
        var frames = 0;

        void OnRendering(object? sender, EventArgs e)
        {
            if (++frames < BackdropLeavingFrames && !_closed)
            {
                return;
            }

            CompositionTarget.Rendering -= OnRendering;
            _backdropLeaving = false;

            // Unless the blur came back on while it waited, in which case the sheet is wanted.
            if (!_applied.BlurBackground)
            {
                _backdrop?.Hide();
            }
        }

        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>
    /// Puts the sheet under the bar — and, where the sheet draws the bar, hands the bar to it.
    /// </summary>
    /// <remarks>
    /// The measuring is the sheet's own (<see cref="BackdropWindow.Follow"/>). The bar is only
    /// handed over once the sheet has drawn it, and only while the blur is on: a sync that
    /// arrives while the sheet is on its way out must not take the bar back off the dock.
    /// </remarks>
    private void SyncBackdrop()
    {
        if (_holdBackdrop
            || !_applied.BlurBackground
            || _backdrop is not { IsVisible: true } backdrop
            || _chrome is not { } chrome
            || !backdrop.Follow(_dock, chrome.Hwnd))
        {
            return;
        }

        _dock.DrawsBar = !backdrop.DrawsBar;

        // Re-asserted here rather than once at startup: showing a topmost window puts it at
        // the top of the topmost band, and that is where this one kept ending up — over the
        // icons it is supposed to be behind.
        RestackBackdrop();

        // Two shadows for one object is one too many.
        _dock.DrawsShadow = !backdrop.HasNativeShadow;
    }

    /// <summary>
    /// Keeps the dock immediately above its own backdrop.
    /// </summary>
    /// <remarks>
    /// Both windows are topmost, and topmost only says "above the rest" — it says nothing
    /// about the order among themselves. Placing one directly above the other by handle is
    /// the only way to be sure the sheet does not surface over the icons it is behind.
    /// </remarks>
    private void RestackBackdrop()
    {
        if (_chrome is not { } chrome || _backdrop is not { IsVisible: true } backdrop
            || backdrop.Hwnd == 0)
        {
            return;
        }

        // Already directly under the dock, so leave it alone. This runs on every appearance
        // change, and re-ordering two overlapping windows sixty times a second is what made
        // dragging a slider flicker: each call re-composes both of them, and for a moment
        // the sheet is drawn without the bar that belongs on top of it.
        if (NativeMethods.GetWindow(backdrop.Hwnd, NativeMethods.GW_HWNDPREV) == chrome.Hwnd)
        {
            return;
        }

        // The sheet is moved below the dock, rather than the dock above the sheet: the
        // dock's own z-position is managed by auto-hide and by BringToTop, and nudging it
        // from here fights them.
        NativeMethods.SetWindowPos(
            backdrop.Hwnd, chrome.Hwnd, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Puts the dock, and the sheet behind it, in the right z-order band.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both have to move together: they are stacked against each other by handle afterwards,
    /// and that only orders windows within a band. Leaving the sheet topmost while the dock
    /// dropped out would put the blur over every other window with no dock on it.
    /// </para>
    /// <para>
    /// The setting floats the dock only while it is not standing aside for a window filling its
    /// display (<see cref="StandAside"/>). That is decided here, where the band is set, and not
    /// by moving the windows behind this method's back: it runs on every re-apply of the
    /// settings — a display change, a preview — and the sheet re-asserts whatever band it is
    /// given each time, so a stand-aside it did not know about would lift the blur back over
    /// the game at the next one.
    /// </para>
    /// </remarks>
    private void ApplyAlwaysOnTop(bool onTop)
    {
        var floats = onTop && _asideFor == 0;

        Topmost = floats;

        if (_chrome is { } chrome)
        {
            chrome.Topmost = floats;
        }

        // Floating by setting supersedes a lift's hold, which would otherwise let it go later
        // and take a floating dock out of the band. Not floating, a held dock keeps its sheet
        // up with it: this runs on every settings change, and dropping the sheet under the
        // windows the dock is held over would leave the bar there without its blur.
        if (floats)
        {
            _heldAbove = false;
        }

        _backdrop?.SetTopmost(floats || _heldAbove);
        RestackBackdrop();
    }

    /// <summary>
    /// Everything about the pins that would require rebuilding the dock's items — and the icon
    /// set, which changes every picture on the dock without changing a pin.
    /// </summary>
    private static string SignatureOf(DockSettings settings) =>
        settings.IconSet + '' + string.Join(
            '',
            settings.PinnedApps.Select(pin => string.Join(
                '',
                pin.Id,
                pin.Label,
                pin.TargetPath,
                pin.Aumid,
                pin.IconPath,
                pin.UseIconNotThumbnail ? "icon" : null,
                pin.FontFamily,
                pin.FontSize?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                pin.FontStyle,
                pin.IsSeparator ? "sep" : null)));

    /// <summary>
    /// Turns raw mouse messages into dock presses.
    /// </summary>
    /// <remarks>
    /// Only messages the dock actually acts on are marked handled, so a click that lands on
    /// the dock's window but not on an icon is left alone rather than silently eaten.
    /// </remarks>
    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // A dock that has not settled on screen — hidden, or sliding either way — starts
        // nothing. Hidden it is click-through as well, but that is a window style, and a hidden
        // dock should not depend on one flag to be out of the way; sliding back it is not, and a
        // click there would land on an icon still on its way to where it is drawn. A button
        // coming up is still let through, to finish a gesture begun while the dock was settled.
        if (!IsSettled && msg is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONUP)
        {
            return 0;
        }

        switch (msg)
        {
            case NativeMethods.WM_LBUTTONDOWN:
                _dock.BeginPress();

                // Held for the length of the gesture. Without it the button-up lands on
                // whatever window the cursor has wandered onto, and a reorder drag that
                // strayed off the bar would never be seen to finish — leaving the icon
                // stuck to the pointer.
                if (_dock.HoveredItem is not null)
                {
                    NativeMethods.SetCapture(hwnd);
                }

                break;

            case NativeMethods.WM_LBUTTONUP:
                // Resolved before the capture is dropped, not after: ReleaseCapture posts
                // WM_CAPTURECHANGED synchronously, and the handler below would cancel the
                // very gesture that is about to be applied.
                handled = _dock.EndPress();
                NativeMethods.ReleaseCapture();
                break;

            case NativeMethods.WM_CAPTURECHANGED:
                // Capture taken away mid-gesture — a system dialog, Alt-Tab, anything.
                // The drag cannot be completed, so it is abandoned rather than left live.
                _dock.CancelPress();
                break;

            case NativeMethods.WM_RBUTTONUP:
                handled = ShowItemMenu();
                break;

            case NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED:
            case NativeMethods.WM_SETTINGCHANGE:
                // The accent, or light and dark, has just moved under us. A dock that was
                // told to match the taskbar has to follow it there.
                TaskbarColour.Invalidate();
                if (_applied.UseTaskbarColor)
                {
                    ApplyAppearance(_applied);
                }

                break;

            case WM_RECYCLEBINICON:
                if (_binWatch?.IconChanged(wParam, lParam) == true)
                {
                    RefreshRecycleBinIcon();
                }

                handled = true;
                break;
        }

        return 0;
    }

    /// <summary>
    /// Redraws the Recycle Bin with the icon the shell has for it now.
    /// </summary>
    /// <remarks>
    /// Swapped in place rather than by rebuilding the items, which is what re-reading it used
    /// to go through. Nothing about the dock has changed but one picture, and this runs
    /// whenever anything on the machine changes the bin — so it must not re-lay the row out,
    /// or drop a press that happens to be in progress, over something the user did elsewhere.
    /// </remarks>
    private void RefreshRecycleBinIcon()
    {
        foreach (var item in _items.Where(pin => DockPresets.IsRecycleBin(pin.TargetPath)))
        {
            item.Icon = PinnedAppsService.LoadIcon(item, _iconSet);
            _dock.RefreshIcon(item);
        }
    }

    /// <summary>
    /// Accepts files, and places such as This PC, dragged onto the dock, pinning them where
    /// they were dropped.
    /// </summary>
    /// <remarks>
    /// Worth noting that this works at all: the dock is <c>WS_EX_NOACTIVATE</c>, which
    /// stops WPF routing mouse input here entirely (hence clicks coming from the window
    /// procedure). Drag and drop is unaffected, because it is not mouse routing — WPF
    /// registers an OLE drop target against the HWND, and OLE drives it from the drag
    /// loop in the *source* process.
    /// </remarks>
    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);

        // Whatever DragLeave may have just claimed, the drag is plainly still here.
        _dropLeaving = false;

        e.Effects = PrepareDrop(e) ? DroppedItems.Effect(e.AllowedEffects) : DragDropEffects.None;
        var pinnable = e.Effects != DragDropEffects.None;
        e.Handled = true;

        if (pinnable)
        {
            _dock.ShowGhosts(_dropGhosts, PointToScreen(e.GetPosition(this)));
        }
        else
        {
            _dock.ClearGhosts();
        }

        // A refused drop shows no preview, so without this the dock's answer to a deliberate
        // gesture is nothing at all.
        _dock.ShowDropNotice(pinnable ? null : _dropRefusal);
    }

    /// <summary>
    /// Takes the preview back down when the drag leaves, so a drag that wanders off does
    /// not leave a phantom icon on the dock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deferred, because <c>DragLeave</c> is a bubbling event: it is raised on whatever the
    /// drag was over and arrives here indistinguishable from the pointer leaving the dock
    /// entirely. A real leave is one that no <c>DragOver</c> follows, and both are raised
    /// from the same OLE callback, so a background-priority check runs after whichever of
    /// them was going to arrive already has.
    /// </para>
    /// <para>
    /// That deferral is a guard, not the cure. What actually made previews flicker was the
    /// icons being hit-testable: the row rebuild that splices a preview in removes the
    /// element under the cursor, which raises <c>DragLeave</c> from inside this very
    /// handler's own work — too late for any <c>DragOver</c> to contradict, because the next
    /// one is a mouse-move away. <see cref="Controls.DockItemVisual"/> is out of the
    /// hit-test now, so the only thing a drag can be over is the bar itself, which no
    /// rebuild removes.
    /// </para>
    /// </remarks>
    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);

        _dropLeaving = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                if (_dropLeaving)
                {
                    ForgetDrop();
                }
            }));
    }

    /// <summary>Set by a <c>DragLeave</c> that a <c>DragOver</c> has not yet contradicted.</summary>
    private bool _dropLeaving;

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        e.Handled = true;

        // The effect is the source's to act on, so it says what was done — a copy or a link,
        // and never the move the source may also have offered.
        e.Effects = PrepareDrop(e) ? DroppedItems.Effect(e.AllowedEffects) : DragDropEffects.None;
        var pinnable = e.Effects != DragDropEffects.None;

        // Where the preview was sitting, which is where the user was told it would land —
        // read before it is cleared, since clearing forgets the slot.
        var index = _dock.IsShowingGhosts
            ? _dock.GhostDropIndex
            : _dock.DropIndexAt(PointToScreen(e.GetPosition(this)));

        var pins = _dropPins;
        _dropLeaving = false;
        ForgetDrop();

        if (!pinnable)
        {
            return;
        }

        var settings = ContentsToSave();
        index = Math.Clamp(index, 0, settings.PinnedApps.Count);

        foreach (var pin in pins)
        {
            settings.PinnedApps.Insert(index, pin);
            index++;
        }

        // Saving is what applies it: the store raises Changed, and ApplySettings
        // rebuilds the dock from the new list. The preview is cleared first, in the same
        // message, so the two never render as separate frames.
        _settings.Save(settings);
    }

    // ---- drop preview ---------------------------------------------------------

    /// <summary>What the hovering drag holds, so it is resolved once rather than per message.</summary>
    private string? _dropSignature;

    /// <summary>The pins the current drag would add, and the faded items previewing them.</summary>
    private List<PinnedAppSetting> _dropPins = [];
    private List<DockItem> _dropGhosts = [];

    /// <summary>Why the current drag would be refused, when it would be. Null when it would not.</summary>
    private string? _dropRefusal;

    /// <summary>Said of a drag holding nothing the dock can pin — which is to say, no file.</summary>
    private static string NotPinnableNotice => Localizer.Get("Drop.NotPinnable");

    /// <summary>Said of any drag at all while the contents are locked.</summary>
    private static string LockedNotice => Localizer.Get("Drop.Locked");

    /// <summary>Said when everything the drag holds is already pinned.</summary>
    private static string AlreadyPinnedNotice => Localizer.Get("Drop.AlreadyPinned");

    /// <summary>
    /// Resolves what the hovering drag would pin, reusing the last answer while the drag
    /// still holds the same thing.
    /// </summary>
    /// <remarks>
    /// <c>DragOver</c> arrives on every mouse move, and answering it properly means hitting
    /// the filesystem and the shell's icon factory. The pins are kept rather than rebuilt at
    /// drop time so what lands is exactly what was previewed, identity included.
    /// </remarks>
    /// <returns>True when the drop holds something worth pinning.</returns>
    private bool PrepareDrop(DragEventArgs e)
    {
        // Ahead of the signature cache, so a lock ticked while a drag is already hovering
        // bites on the next mouse move rather than on the next drag. Refused with a notice
        // rather than ignored: a drop that silently does nothing reads as a dock that has
        // stopped working, which is the whole reason the refusals say why.
        if (_applied.LockItemContents)
        {
            _dropSignature = null;
            _dropPins = [];
            _dropGhosts = [];
            _dropRefusal = LockedNotice;
            return false;
        }

        var signature = DropSignature(e);
        if (signature is null)
        {
            _dropSignature = null;
            _dropPins = [];
            _dropGhosts = [];
            _dropRefusal = NotPinnableNotice;
            return false;
        }

        if (signature == _dropSignature)
        {
            return _dropPins.Count > 0;
        }

        _dropSignature = signature;
        (_dropPins, _dropRefusal) = PlanDrop(e);
        _dropGhosts = [.. _dropPins.Select(PinnedAppsService.ToDockItem)];
        _pinnedApps.ResolveIcons(_dropGhosts, _iconSet);

        return _dropPins.Count > 0;
    }

    /// <summary>Clears the preview and everything resolved for it.</summary>
    private void ForgetDrop()
    {
        _dropSignature = null;
        _dropPins = [];
        _dropGhosts = [];
        _dropRefusal = null;
        _dock.ClearGhosts();
        _dock.ShowDropNotice(null);
    }

    /// <summary>Identifies a drag by its contents, for deciding whether to resolve it again.</summary>
    private static string? DropSignature(DragEventArgs e) => DroppedItems.Signature(e.Data);

    /// <summary>
    /// What the drop would add, and — if it would add nothing — what to say about it.
    /// </summary>
    /// <remarks>
    /// Anything the dock already has is dropped from the plan rather than pinned twice, and
    /// that includes the same path arriving twice in one drop. A drag holding a mixture of
    /// new and already-pinned things pins the new ones and says nothing about the rest: the
    /// preview shows exactly what will land, which is the answer to the question.
    /// </remarks>
    private (List<PinnedAppSetting> Pins, string? Refusal) PlanDrop(DragEventArgs e)
    {
        if (DroppedItems.Targets(e.Data) is not { } targets)
        {
            return ([], NotPinnableNotice);
        }

        // What the dock is showing, not what is stored. With the settings dialog open the
        // two differ, and it is the shown one the drop is being aimed at.
        var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _items)
        {
            if (PinnedAppsService.TargetKey(item.TargetPath) is { } existing)
            {
                pinned.Add(existing);
            }
        }

        var pins = new List<PinnedAppSetting>();
        var duplicates = 0;

        foreach (var target in targets)
        {
            if (DroppedItems.Pin(target) is not { } pin)
            {
                continue;
            }

            if (PinnedAppsService.TargetKey(pin.TargetPath) is { } key && !pinned.Add(key))
            {
                duplicates++;
                continue;
            }

            pins.Add(pin);
        }

        if (pins.Count > 0)
        {
            return (pins, null);
        }

        return ([], duplicates > 0 ? AlreadyPinnedNotice : NotPinnableNotice);
    }

    /// <summary>
    /// What stands in the menu where the dock's own commands would be, while they are
    /// locked away. One word, because it is a state and not a command.
    /// </summary>
    private static string LockedMenuNote => Localizer.Get("Menu.Locked");

    /// <summary>
    /// Offers the right-click menu for the icon under the cursor.
    /// </summary>
    /// <returns>True when the menu was shown.</returns>
    private bool ShowItemMenu() => ShowMenuFor(_dock.HoveredItem);

    /// <summary>
    /// Shows the right-click menu, for one item or for the dock itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A WPF menu, shown through <see cref="MenuHost"/> rather than by this window. It used
    /// to be a Win32 <c>TrackPopupMenu</c>, because this window is <c>WS_EX_NOACTIVATE</c>
    /// and a menu it owned could not be reliably clicked — but the menu does not have to be
    /// owned by the window it was opened from, and once it is owned by something that can
    /// take the foreground the objection goes away and it can be themed like everything
    /// else.
    /// </para>
    /// <para>
    /// Right-clicking the bar between icons gets the same menu without the per-item
    /// commands, which is the only way to add anything to a dock that has been emptied —
    /// unless the contents are locked, in which case the menu carries no commands at all
    /// beyond <em>Dock settings…</em>, and that is the way back.
    /// </para>
    /// </remarks>
    private bool ShowMenuFor(DockItem? item)
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return false;
        }

        var menu = new ContextMenu();

        // What the item can *do* comes first, above what can be done *to* it. Only the
        // Recycle Bin has anything here so far.
        //
        // It survives the contents lock, and has to: that lock is about what is on the dock,
        // and emptying the bin changes nothing about that. A locked dock still launches what
        // you click, and this is the same kind of act — using an item rather than editing
        // the dock.
        if (item is not null && DockPresets.IsRecycleBin(item.TargetPath))
        {
            menu.Items.Add(EmptyRecycleBinEntry());
            menu.Items.Add(new Separator());
        }

        // Every remaining command changes the dock, so the lock takes all of them and
        // leaves Dock settings… — which is what keeps the lock one right-click from being
        // undone. The commands go rather than grey, because a menu of disabled entries
        // invites the click that will not work; what replaces them is one greyed word saying
        // why the menu is nearly empty, which greying five entries would say five times.
        if (_applied.LockItemContents)
        {
            menu.Items.Add(MenuHost.Note(LockedMenuNote, MenuGlyph.Locked));
            menu.Items.Add(new Separator());
        }
        else
        {
            // What you right-clicked comes first, then what it sits on. The two were
            // interleaved — edit, add, settings, remove — so the two things you can do to
            // one icon sat at opposite ends with the dock's own entries between them.
            if (item is not null)
            {
                // A separator has no name, no icon and nothing to launch, so it has nothing
                // the edit dialog could act on.
                if (!item.IsSeparator)
                {
                    menu.Items.Add(MenuHost.Item(
                        Localizer.Get("Menu.EditItem"), MenuGlyph.Edit, () => ShowEditDialog(item)));
                }

                // Unpin rather than a bin: nothing is deleted, and the bin is the Empty
                // Recycle Bin entry's, which can be in this same menu.
                menu.Items.Add(MenuHost.DangerItem(
                    Localizer.Get("Menu.RemoveFromDock"), MenuGlyph.Unpin, () => ConfirmRemove(item)));
                menu.Items.Add(new Separator());
            }

            var add = new MenuItem
            {
                Header = Localizer.Get("Menu.Add"),
                Icon = MenuIcons.Glyph(MenuGlyph.Add)
            };
            var insertAt = InsertIndexAfter(item);
            foreach (var group in DockPresets.Menu())
            {
                if (add.Items.Count > 0)
                {
                    add.Items.Add(new Separator());
                }

                foreach (var preset in group)
                {
                    var key = preset.Key;
                    add.Items.Add(MenuHost.Item(
                        preset.Label, MenuIcons.For(preset), () => AddPreset(key, insertAt)));
                }
            }

            menu.Items.Add(add);
        }

        menu.Items.Add(MenuHost.Item(
            Localizer.Get("Menu.DockSettings"),
            MenuGlyph.Settings,
            () => SettingsRequested?.Invoke(this, EventArgs.Empty)));

        // Hold the icon the menu belongs to: magnified, label showing, so it is obvious
        // which one is about to be edited or removed. Released when the menu goes, whether
        // anything was chosen or not.
        HoldRevealed(true);
        var claim = item is null ? 0 : _dock.FocusItem(item);

        menu.Closed += (_, _) =>
        {
            // By claim, because this runs after the entry's action has already taken the
            // focus for itself — see DockBar._focusClaim. The reveal is a counter and needs
            // no such care: whoever took the next hold has their own.
            _dock.ReleaseFocus(claim);
            HoldRevealed(false);
        };

        _menus.ShowMenu(menu, cursor.X, cursor.Y);
        return true;
    }

    /// <summary>
    /// The entry that empties the Recycle Bin, greyed when there is nothing in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Greyed rather than absent, which is the opposite of what the contents lock does to
    /// the entries around it — and right for the opposite reason. The lock is a standing
    /// decision, so the commands it governs are gone until it is lifted; an empty bin is a
    /// passing fact, and an entry that vanished with it would leave the menu changing shape
    /// under the pointer for reasons that are nobody's decision.
    /// </para>
    /// <para>
    /// Not in the theme's critical colour, though it deletes permanently. That colour means
    /// one thing in this app — it is on <em>Remove from dock</em> and on the settings
    /// dialog's <em>Remove</em>, so that the two ways of unpinning an icon look alike — and
    /// spending it on a second meaning would cost the first. Explorer draws its own entry in
    /// plain text too, and the confirmation is the guard.
    /// </para>
    /// </remarks>
    private MenuItem EmptyRecycleBinEntry() =>
        RecycleBin.IsEmpty()
            ? MenuHost.Note(EmptyRecycleBinHeader, MenuGlyph.Delete)
            : MenuHost.Item(EmptyRecycleBinHeader, MenuGlyph.Delete, EmptyRecycleBin);

    private static string EmptyRecycleBinHeader => Localizer.Get("Menu.EmptyRecycleBin");

    /// <summary>
    /// Hands the emptying to the shell.
    /// </summary>
    /// <remarks>
    /// The icon is not re-read here. It is re-read when the shell announces the bin's image
    /// has changed (<see cref="RecycleBinWatch"/>), the same as for an empty made in Explorer
    /// — reading it the moment the call returned is what left a full bin on the dock.
    /// </remarks>
    private void EmptyRecycleBin() =>
        RecycleBin.EmptyAsync(new WindowInteropHelper(this).Handle);

    /// <summary>
    /// Holds one item's label open on the dock, or lets it go again.
    /// </summary>
    /// <remarks>
    /// For the settings dialog's copy of the item editor. Editing from the dock's own menu
    /// has always done this — see <see cref="ShowEditDialog"/> — and the held label is the
    /// point of the exercise: the font controls describe a label, so there has to be one on
    /// screen for them to describe. Opened from the settings window they had nothing.
    /// </remarks>
    public void HoldItemLabel(DockItem? item)
    {
        if (item is null)
        {
            _dock.ClearFocus();
            HoldRevealed(false);
            return;
        }

        HoldRevealed(true);
        _dock.FocusItem(item);
    }

    /// <summary>Shows an edit in progress on the held label.</summary>
    public void PreviewItemLabel(
        string? label, string? fontFamily, double? fontSize, string? fontStyle) =>
        _dock.PreviewLabelStyle(label, fontFamily, fontSize, fontStyle);

    /// <summary>
    /// Where an addition made from an item's menu belongs: immediately after that item, so
    /// it lands where the user was pointing rather than at the end of the dock.
    /// </summary>
    private int InsertIndexAfter(DockItem? item)
    {
        var pins = _applied.PinnedApps;
        if (item is null)
        {
            return pins.Count;
        }

        var index = pins.FindIndex(pin => pin.Id == item.Id);
        return index < 0 ? pins.Count : index + 1;
    }

    /// <summary>Adds one of the ready-made entries, or browses for a target.</summary>
    private void AddPreset(string key, int index)
    {
        var pin = key == DockPresets.BrowseKey ? BrowseForPin() : DockPresets.Create(key);
        if (pin is null)
        {
            return;
        }

        var settings = ContentsToSave();
        settings.PinnedApps.Insert(Math.Clamp(index, 0, settings.PinnedApps.Count), pin);
        _settings.Save(settings);
    }

    /// <summary>Asks for a file to pin.</summary>
    /// <remarks>See <see cref="FileFilters.Pinnable"/> for why everything is offered first.</remarks>
    private PinnedAppSetting? BrowseForPin()
    {
        var dialog = new OpenFileDialog
        {
            Title = Localizer.Get("Pin.Browse.Title"),
            Filter = FileFilters.Pinnable,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };

        HoldRevealed(true);
        try
        {
            return dialog.ShowDialog() == true
                ? PinnedAppsService.CreateChosenPin(dialog.FileName)
                : null;
        }
        finally
        {
            HoldRevealed(false);
        }
    }

    /// <summary>Opens the edit dialog for one item.</summary>
    private void ShowEditDialog(DockItem item)
    {

        var editor = new EditPinWindow(item, _iconSet);

        // Style changes land on the dock as they are made, so the preview is the real
        // thing rather than a swatch in a dialog — and on the item being edited, which is
        // the one whose label the user is actually looking at.
        editor.PreviewChanged += (_, _) => _dock.PreviewLabelStyle(
            editor.EditedLabel, editor.EditedFontFamily, editor.EditedFontSize, editor.EditedFontStyle);

        // The dialog is modal, so this blocks until it closes; the hold and the focus both
        // have to be released either way, including if it throws.
        bool saved;
        HoldRevealed(true);
        var claim = _dock.FocusItem(item);
        _dock.PreviewLabelStyle(item.Label, item.FontFamily, item.FontSize, item.FontStyle);
        try
        {
            saved = editor.ShowDialog() == true;
        }
        finally
        {
            _dock.ReleaseFocus(claim);
            HoldRevealed(false);
        }

        if (!saved)
        {
            return;
        }

        var settings = ContentsToSave();
        var pin = settings.PinnedApps.FirstOrDefault(candidate => candidate.Id == item.Id);
        if (pin is null)
        {
            return;
        }

        // The name, the icon and what it opens belong to this item.
        pin.Label = editor.EditedLabel;
        pin.IconPath = editor.EditedIconPath;
        pin.UseIconNotThumbnail = editor.EditedUseIconNotThumbnail;
        pin.TargetPath = editor.EditedTargetPath;
        pin.Aumid = editor.EditedAumid;

        // The lettering does not. A dock whose labels are set in different faces reads as a
        // dock that was assembled rather than designed, and the per-item choice was work the
        // user had to redo for every icon to get the one result they wanted.
        foreach (var target in settings.PinnedApps)
        {
            target.FontFamily = editor.EditedFontFamily;
            target.FontSize = editor.EditedFontSize;
            target.FontStyle = editor.EditedFontStyle;
        }

        _settings.Save(settings);
    }

    /// <summary>
    /// Asks before taking an item off the dock, and takes it off when told to.
    /// </summary>
    /// <remarks>
    /// The item is held as the edit dialog holds it — magnified, label showing — so the icon
    /// the question is about is the one standing out on the dock. Its own claim, because the
    /// menu's <c>Closed</c> fires while this dialog is up and releases the menu's.
    /// </remarks>
    private void ConfirmRemove(DockItem item)
    {
        var dialog = new ConfirmRemoveWindow(item);

        bool confirmed;
        HoldRevealed(true);
        var claim = _dock.FocusItem(item);
        try
        {
            confirmed = dialog.ShowDialog() == true;
        }
        finally
        {
            _dock.ReleaseFocus(claim);
            HoldRevealed(false);
        }

        if (confirmed)
        {
            OnItemUnpinned(this, item);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.Invoke(() => ApplySettings(_applied));

    /// <summary>
    /// Re-anchors when the taskbar takes or gives back room on the dock's display.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A display change does not cover this. Across a wake from sleep the displays come back
    /// first and the taskbar claims its strip afterwards, so the re-apply a display change
    /// prompts can read a work area that is the whole screen — and the dock stayed where that
    /// put it, the bottom of the bar under the taskbar, until something else moved it.
    /// </para>
    /// <para>
    /// Through <see cref="SystemEvents"/>, like the display change, rather than the window
    /// procedure: the work area is announced by a broadcast <c>WM_SETTINGCHANGE</c>, and
    /// <see cref="SystemEvents"/> hears broadcasts on a window of its own — this one WPF gives
    /// an owner, and whether a broadcast reaches an owned window has not been shown here. It
    /// arrives as the Desktop category, which a new wallpaper is too — hence asking whether the
    /// work area is actually a new one.
    /// </para>
    /// </remarks>
    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.Desktop)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            var screen = Screens.Resolve(_applied.ScreenDeviceName, _applied.ScreenDevicePath);
            if (screen.WorkArea != _workArea)
            {
                ApplySettings(_applied);
            }
        });
    }

    /// <summary>
    /// Re-anchors after a DPI change. Every dimension the dock uses is in DIPs, so the layout
    /// itself is scale-independent; only the work area it sits in has moved.
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        // Posted rather than run here.
        //
        // WPF finishes the scale change after this notification, so everything measured now
        // is measured against the old scale — including the window's own, which is what the
        // dock is sized from. Applying settings at that moment placed the dock for the
        // display it was leaving, and the correct placement a few milliseconds later then
        // had to undo it. One dispatcher turn later the scale is the new one and the dock is
        // laid out once, correctly.
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => ApplySettings(_applied)));
    }

    /// <summary>
    /// Keeps the dock on screen while a configuration window is open, whatever auto-hide
    /// would otherwise do.
    /// </summary>
    public void HoldRevealed(bool hold) => _autoHide?.HoldRevealed(hold);

    /// <summary>
    /// Puts the dock into the state the settings dialog wants: the wave up and moving, and
    /// the window held at a fixed size so dragging a slider does not resize it.
    /// </summary>
    public void PreviewMagnification(bool active)
    {
        _previewing = active;
        _dock.PreviewMagnification(active);
        _dock.PreviewSweep(active && _applied.PreviewSweep);

        // Takes up or releases the held size.
        SizeToDock(_applied);

        // The handle is shown under the dock for as long as the dialog is open, so its width
        // can be seen while it is being set — the dock is held up then, and the handle would
        // otherwise never be on screen at the same time as the slider that sizes it.
        SyncHandle();
    }

    /// <summary>True while the settings dialog is open and showing the dock off.</summary>
    private bool _previewing;

    /// <summary>
    /// Lifts the dock, and the sheet behind it, above whatever is covering it.
    /// </summary>
    /// <remarks>
    /// Also what auto-hide calls, both to bring a hidden dock back and to lift a covered one
    /// when the cursor is held against the edge or arrives on the dock.
    /// Not above the window in the foreground, though, unless this process holds the
    /// foreground at the time: Windows declines that silently. The lift checks, and falls back
    /// on <see cref="HoldAbove"/>; a reveal, which cannot check, holds regardless.
    /// </remarks>
    /// <returns>
    /// The window the dock was directly under, for <see cref="Lower"/> to put it back under —
    /// or 0 when nothing was over it.
    /// </returns>
    public nint Raise()
    {
        var under = _chrome?.WindowAbove() ?? 0;

        _chrome?.BringToTop();

        // Raised on its own, the dock leaves the sheet where it was, under whatever it has
        // just risen above: the bar would be drawn over that window rather than over a blur.
        RestackBackdrop();
        CheckSight();
        KeepHandleOnTop();

        return under;
    }

    /// <summary>
    /// True when a window of another program lies over the dock — what a lift asks before it
    /// lifts, and again after raising to see whether that got through.
    /// </summary>
    private bool IsCovered() =>
        _chrome is { } chrome && chrome.IsCovered(RestingBarOnScreen());

    /// <summary>
    /// The bar at rest, in physical screen pixels where the window is now — empty before the
    /// dock has been laid out, when nothing about it can be measured.
    /// </summary>
    /// <remarks>
    /// What a lift asks is covered; and, across, where the edge brings the dock up and the strip
    /// below a dock that is up keeps it there (<see cref="AutoHideController.IsUnderDock(double, Rect, Rect)"/>)
    /// — as wide as the dock, measured as the handle's width is.
    /// </remarks>
    private Rect RestingBarOnScreen()
    {
        if (PresentationSource.FromVisual(_dock) is null || _dock.ActualWidth <= 0 || _dock.ActualHeight <= 0)
        {
            return Rect.Empty;
        }

        var bar = _dock.RestingBarRect;
        if (bar.Width <= 0 || bar.Height <= 0)
        {
            return Rect.Empty;
        }

        return new Rect(_dock.PointToScreen(bar.TopLeft), _dock.PointToScreen(bar.BottomRight));
    }

    /// <summary>
    /// True while the dock and its sheet are held in the topmost band by a lift, rather than
    /// floating there because the settings say so.
    /// </summary>
    private bool _heldAbove;

    /// <summary>
    /// Holds the dock, and the sheet behind it, in the topmost band until <see cref="Lower"/>
    /// lets them go.
    /// </summary>
    /// <remarks>
    /// For when <see cref="Raise"/> did not get the dock above what covers it: Windows is
    /// entitled to refuse to put a background program's window over the one in the
    /// foreground, and refuses silently. Entering the topmost band is not refused — it is
    /// how a dock set to float stays up — so the dock goes there for as long as the pointer
    /// is on it, or auto-hide has it revealed. The sheet first, so the dock, going second,
    /// lands above it.
    /// </remarks>
    public void HoldAbove()
    {
        if (_chrome is not { } chrome || Topmost)
        {
            return;
        }

        if (_backdrop is { IsVisible: true } backdrop && backdrop.Hwnd != 0)
        {
            NativeMethods.SetWindowPos(
                backdrop.Hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        NativeMethods.SetWindowPos(
            chrome.Hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        _heldAbove = true;
        RestackBackdrop();
        CheckSight();
        KeepHandleOnTop();
    }

    /// <summary>Lets go of a <see cref="HoldAbove"/>: both windows back to the ordinary band.</summary>
    private void LetDown()
    {
        if (!_heldAbove || _chrome is not { } chrome)
        {
            return;
        }

        _heldAbove = false;

        if (_backdrop is { IsVisible: true } backdrop && backdrop.Hwnd != 0)
        {
            NativeMethods.SetWindowPos(
                backdrop.Hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        NativeMethods.SetWindowPos(
            chrome.Hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        RestackBackdrop();
    }

    /// <summary>
    /// Puts the dock, and the sheet behind it, back under the window a <see cref="Raise"/>
    /// lifted it over — if that is still a move down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sheet goes first. Moved second, it would be left over the window for a frame with
    /// the dock already gone from on top of it: a patch of blur, floating on its own. The
    /// other way round, the frame in between has the bar without its blur, which is what a
    /// raise shows too and reads as nothing.
    /// </para>
    /// <para>
    /// Then a look at what is in front, since the dock may be coming down in front of a window
    /// that fills its display: it does not stand aside for one while it is up on request, and
    /// waiting for the next look would leave it over the game for up to a quarter of a second.
    /// So the caller has to have stopped counting the dock as up before it calls this.
    /// </para>
    /// </remarks>
    public void Lower(nint window)
    {
        LetDown();

        if (_chrome is { } chrome && chrome.CanGoUnder(window))
        {
            if (_backdrop is { IsVisible: true } backdrop && backdrop.Hwnd != 0)
            {
                NativeMethods.SetWindowPos(
                    backdrop.Hwnd, window, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }

            chrome.GoUnder(window);
            RestackBackdrop();
        }

        CheckFront();
    }

    /// <summary>
    /// True while a program the dock stays down for is in front and covers the dock's display,
    /// which is when holding the edge is to do nothing.
    /// </summary>
    /// <remarks>
    /// The list in force, not the stored one, so a program added in the settings dialog is
    /// honoured before Save — and one removed there stops being honoured on Cancel. Asked
    /// a few times a second by <see cref="CheckFront"/>, and whenever the pointer is down at
    /// the edge; an empty list, the usual case, costs nothing either way.
    /// </remarks>
    private bool IsFullscreenAppInFront() =>
        _applied.NoRevealApps is { Count: > 0 } apps
        && _foreground.FillingDisplay(_display) is { } program
        && FullscreenApps.Contains(apps, program);

    // ---- what is in front -----------------------------------------------------------------

    /// <summary>
    /// Looks at what is in front of the dock's display, and puts the dock and its handle where
    /// that wants them: away while a window fills the display, back once there is none, and the
    /// handle over whatever is not fullscreen — a program on the Exclusions page included, which
    /// counts only when it fills the display. The rules are
    /// <see cref="DockFront"/>'s; this reads what they are asked and moves the windows they
    /// answer for.
    /// </summary>
    /// <param name="foregroundChanged">
    /// True when this is the foreground changing, which is when a window that floats may have
    /// come up over the handle — see <see cref="HandleWindow.KeepOnTop"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The dock is not on top of anything that fills its display</b> — a game, a video, a
    /// presentation, or a window maximized with the taskbar showing — whatever <em>Always on
    /// top</em> says; asked on 2026-09-30, fullscreen first and maximized the same day. It hides
    /// as auto-hide would (<see cref="AutoHideController.Yield"/>): slides away, pops back up
    /// from the bottom when the pointer is held against the edge, and slides away again once
    /// the pointer has left. It went under such a window at first, which left nothing to see
    /// when it came back up; hiding was asked for the same day, for the animation. Only the
    /// foreground is asked, as the Exclusions page has always asked it: overlays are never the
    /// foreground, and a window that is has the user's attention — so a maximized window left
    /// behind while the user works on the other display has the dock back over it.
    /// </para>
    /// <para>
    /// <b>A program on the Exclusions page outranks all of that</b>: the dock goes under it as
    /// well as away — under first, so the slide happens behind the game rather than over it —
    /// and the edge does not bring it back: the pointer's ways of bringing the dock up stand
    /// down for such a program by themselves.
    /// </para>
    /// <para>
    /// A timer and a hook both. The hook answers a change of foreground the moment it happens —
    /// alt-tabbing back into a game with the dock set to float would otherwise show the dock
    /// over it for up to a tick — and the timer catches what no foreground change announces: a
    /// window maximized, restored or made fullscreen while already in front, a window that
    /// raised the dock, a window moved over the dock or away from it.
    /// </para>
    /// <para>
    /// Safe to call as often as anything likes: it acts only on a change, and checks where the
    /// windows actually are before moving one — re-placing a window where it already is still
    /// recomposes it.
    /// </para>
    /// </remarks>
    private void CheckFront(bool foregroundChanged = false)
    {
        if (_closed || _chrome is null)
        {
            return;
        }

        // The taskbar, Alt+Tab, Start and the like come and go over whatever is in front, and
        // everything holds still for them — see ForegroundApp.IsPassingShellInFront.
        var marked = false;
        if (!ForegroundApp.IsPassingShellInFront())
        {
            var excluded = IsFullscreenAppInFront();

            // The general test is not asked once the list has answered, which settles it: a
            // listed program counts only when it fills the display.
            var fill = excluded ? FrontFill.Fullscreen : ForegroundApp.Filling(_display, _workArea);
            var action = DockFront.Decide(excluded, filledInFront: fill != FrontFill.None);

            // Before the dock is moved, since its sliding away is what brings the handle up, and
            // the handle reads this to know whether it may.
            var fullscreen = fill == FrontFill.Fullscreen;
            marked = fullscreen != _fullscreenInFront;
            _fullscreenInFront = fullscreen;

            switch (action)
            {
                case FrontAction.HideOutranked:
                    StandAside(WindowsApi.GetForegroundWindow());
                    _autoHide?.Yield(true);
                    break;

                case FrontAction.Hide:
                    StepBack();
                    _autoHide?.Yield(true);
                    break;

                case FrontAction.Show:
                    StepBack();
                    _autoHide?.Yield(false);
                    break;
            }
        }

        if (!CheckSight() && marked)
        {
            SyncHandle();
        }

        KeepHandleOnTop(overOthers: foregroundChanged);
        KeepHiddenInPlace();
    }

    /// <summary>
    /// Keeps the handle at the top of the topmost band, over the dock's own windows as well as
    /// everything else — see <see cref="HandleWindow.KeepOnTop"/>.
    /// </summary>
    /// <remarks>
    /// Asked by every look at what is in front, and straight after the dock puts itself at the
    /// top of the band — a raise or a hold — since that lands it over the handle, and the
    /// bar's shadow, which the dock draws in its own window, falls across the handle's strip.
    /// The band changing with <em>Always on top</em> is covered by the look that every re-apply
    /// of the settings ends with.
    /// </remarks>
    private void KeepHandleOnTop(bool overOthers = false)
    {
        if (_handle is not { } handle)
        {
            return;
        }

        ReadOnlySpan<nint> dock = [_chrome?.Hwnd ?? 0, _backdrop?.Hwnd ?? 0];
        handle.KeepOnTop(overOthers, dock);
    }

    /// <summary>
    /// Takes the dock, and the sheet behind it, out of the topmost band and puts them under
    /// <paramref name="game"/>, a program on the Exclusions page filling the dock's display in
    /// front.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Out of the band through <see cref="ApplyAlwaysOnTop"/>, which is what decides the band,
    /// so everything that re-applies the settings keeps it that way; a hold is let go as well,
    /// every time — no lift gets the dock over a listed program, but one held up when the
    /// program came to the front would otherwise stay over it. Then under the game, if the
    /// dock is not already: leaving the band puts a window at the top of the ordinary one, over
    /// the game. The sheet goes first, as in <see cref="Lower"/>, so no frame shows the blur
    /// over the game without the dock.
    /// </para>
    /// <para>
    /// Only for the list's programs since the dock began to hide for anything else that fills
    /// the display; it hides for these as well, and being under the game is what keeps its
    /// slide away out of sight.
    /// </para>
    /// </remarks>
    private void StandAside(nint game)
    {
        if (_chrome is not { } chrome || game == 0)
        {
            return;
        }

        LetDown();

        if (_asideFor == 0)
        {
            _asideFor = game;
            ApplyAlwaysOnTop(_applied.AlwaysOnTop);
        }

        _asideFor = game;

        if (!chrome.CanGoUnder(game))
        {
            return;
        }

        if (_backdrop is { IsVisible: true } backdrop && backdrop.Hwnd != 0)
        {
            NativeMethods.SetWindowPos(
                backdrop.Hwnd, game, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        chrome.GoUnder(game);
        RestackBackdrop();
    }

    /// <summary>
    /// Ends a stand-aside: a dock set to float goes back to floating. One that is not set to
    /// float is left where it is, as any window is when another comes to the front.
    /// </summary>
    private void StepBack()
    {
        if (_asideFor == 0)
        {
            return;
        }

        _asideFor = 0;
        ApplyAlwaysOnTop(_applied.AlwaysOnTop);
    }

    private void HookForeground()
    {
        if (_foregroundHook != 0)
        {
            return;
        }

        // Out of context, so the callback arrives on this thread through its message loop.
        // Not skipping this process: the settings dialog coming to the front is a change of
        // foreground like any other, and the dock should float again for it.
        _foregroundHook = WindowsApi.SetWinEventHook(
            WindowsApi.EVENT_SYSTEM_FOREGROUND,
            WindowsApi.EVENT_SYSTEM_FOREGROUND,
            0,
            _onForeground,
            idProcess: 0,
            idThread: 0,
            WindowsApi.WINEVENT_OUTOFCONTEXT);
    }

    private void UnhookForeground()
    {
        if (_foregroundHook == 0)
        {
            return;
        }

        WindowsApi.UnhookWinEvent(_foregroundHook);
        _foregroundHook = 0;
    }

    // ---- the handle -----------------------------------------------------------

    /// <summary>
    /// Shows the handle while it has a dock out of sight to mark, and puts it under where that
    /// dock will come up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called wherever the sheet behind the bar is synced — the bar changing shape, the window
    /// moving, a settings change — whenever auto-hide moves the dock between shown and hidden,
    /// and whenever the dock goes out of sight or comes back into it, or a fullscreen window
    /// comes to the front or leaves it. Whether the dock can be seen is not
    /// asked here but read from what <see cref="CheckSight"/> last found, since this runs far
    /// too often for a walk of the windows. Most of those are nothing to the handle, so it is
    /// cheap when there is no handle to show and does nothing when there is one already in
    /// place: <see cref="HandleWindow"/> checks before it moves, fades or recolours anything, as
    /// every window here must, since this runs on every frame of the dock's slide and of the
    /// dialog's demonstration wave.
    /// </para>
    /// <para>
    /// The settings in force, not the stored ones, so the dialog previews it like everything
    /// else.
    /// </para>
    /// </remarks>
    private void SyncHandle()
    {
        if (_closed)
        {
            return;
        }

        // A maximized window keeps it — the dock has hidden for that, and the edge brings it
        // back up over it — and a fullscreen one does not, nor a program on the Exclusions page.
        if (!MarksDock())
        {
            _handle?.Hide();
            return;
        }

        // Nothing can be measured before the dock has been laid out — see SyncBackdrop.
        if (PresentationSource.FromVisual(_dock) is null || _dock.ActualWidth <= 0 || _workArea.IsEmpty)
        {
            return;
        }

        // Only across: the dock's window is below the bottom of the screen while it is hidden,
        // and on its way there or back, and neither is anything to the handle, which sits on the
        // work area. The width comes through the same conversion as the sheet's, so a handle as
        // wide as the dock is exactly as wide as the bar the sheet is cut to.
        var bar = _dock.RestingBarRect;
        var bounds = DockHandle.Place(
            _dock.PointToScreen(bar.TopLeft).X,
            _dock.PointToScreen(bar.TopRight).X,
            _workArea.Bottom,
            _handleScale,
            _applied.HandleMatchesDock,
            _applied.HandleWidth);

        _handle ??= new HandleWindow();
        _handle.SetLook(fallback: BarPalette.Parse(EffectiveBarColor(_applied)));
        _handle.Place(bounds);
        _handle.Show();
    }

    /// <summary>
    /// True when there is a dock for the handle to mark: it is asked for, and the dock is out of
    /// sight — hidden as auto-hide hides it, or on screen under the windows in front — or the
    /// settings dialog is open; and the window in front does not have the whole display —
    /// fullscreen, or a program on the Exclusions page filling it. The rule is
    /// <see cref="DockFront.Marks"/>'s.
    /// </summary>
    /// <remarks>
    /// Whether auto-hide is on or not, since 2026-09-30. It used to mark only a dock that
    /// auto-hide had put away, and the setting greyed out without it — but a dock hides for a
    /// maximized or fullscreen window in front whatever the setting, and one that does not
    /// float is out of sight under any window over it. Hidden counts when the dock hides itself
    /// and was not put away from the tray. It only marks: the edge is what brings the dock up.
    /// Since 2026-10-01 not over a fullscreen window, so as not to sit over a video; a dock
    /// hidden for a maximized one is still marked.
    /// </remarks>
    private bool MarksDock() =>
        _autoHide is { } autoHide
        && DockFront.Marks(
            _applied.ShowHandle,
            _fullscreenInFront,
            _previewing,
            hides: autoHide.Hides && !autoHide.IsPutAway,
            autoHide.Visibility,
            _outOfSight);

    /// <summary>
    /// Asks again whether the dock can be seen, and brings the handle up to date if that has
    /// changed.
    /// </summary>
    /// <returns>True when it changed, and the handle has been brought up to date.</returns>
    /// <remarks>
    /// <para>
    /// Asked a few times a second by <see cref="CheckFront"/>, and straight after anything the
    /// dock does to its own place in the z-order — a lift, a hold, a lowering — so the handle
    /// goes as the dock comes up and returns as it goes back under, rather than up to a quarter
    /// of a second later. Only of a dock that is shown, settled and wanting a handle: a dock on
    /// its way somewhere is marked, or not, by auto-hide's own state, and a walk of the windows
    /// above the dock is not paid for a handle that is turned off.
    /// </para>
    /// <para>
    /// Out of sight means none of the bar can be seen, not some of it — see
    /// <see cref="WindowChrome.IsHiddenAt"/>. A dock half under a window is plain to see, and
    /// the pointer brings it up by going to the half that shows.
    /// </para>
    /// </remarks>
    private bool CheckSight()
    {
        var outOfSight = !_closed
            && _applied.ShowHandle
            && _autoHide is { Visibility: DockVisibility.Shown }
            && IsBarOutOfSight();

        if (outOfSight == _outOfSight)
        {
            return false;
        }

        _outOfSight = outOfSight;
        SyncHandle();
        return true;
    }

    /// <summary>How many points along the bar are looked at to decide whether it can be seen.</summary>
    private const int SightPoints = 5;

    /// <summary>
    /// True when nothing of the bar at rest can be seen, for the windows of other programs lying
    /// over it.
    /// </summary>
    /// <remarks>
    /// Points along its middle, spread evenly and kept off its ends: the ends are rounded, and
    /// a window stopping just short of one would otherwise leave a sliver of dock that the point
    /// under it calls covered.
    /// </remarks>
    private bool IsBarOutOfSight()
    {
        if (_chrome is not { } chrome)
        {
            return false;
        }

        var bar = RestingBarOnScreen();
        if (bar.IsEmpty)
        {
            return false;
        }

        Span<Point> points = stackalloc Point[SightPoints];
        var y = bar.Top + (bar.Height / 2);
        for (var i = 0; i < SightPoints; i++)
        {
            points[i] = new Point(bar.Left + (bar.Width * (i + 0.5) / SightPoints), y);
        }

        return chrome.IsHiddenAt(points);
    }

    /// <summary>Toggles the dock between shown and hidden, from the tray menu.</summary>
    public void ToggleVisibility()
    {
        if (_autoHide is not { } autoHide)
        {
            return;
        }

        if (IsDockShown)
        {
            autoHide.Hide();
        }
        else
        {
            autoHide.Reveal();
        }
    }

    /// <summary>Turns stored pins into dock items with their icons resolved.</summary>
    private IReadOnlyList<DockItem> ResolveItems(DockSettings settings)
    {
        var items = settings.PinnedApps.Select(PinnedAppsService.ToDockItem).ToList();
        _pinnedApps.ResolveIcons(items, _iconSet);
        return items;
    }

    /// <summary>
    /// Click behaviour: raise the app if it already has a window, otherwise start it.
    /// Clicking an app that is already in front moves to its next window.
    /// </summary>
    private void OnItemActivated(object? sender, DockItem item)
    {
        var window = _runningApps.NextWindow(item.RunningTarget);
        if (window != 0 && AppLauncher.Activate(window))
        {
            return;
        }

        // Flash only on a real launch. Raising a window that was already open is not
        // something the dock needs to announce — and macOS does not bounce for it either.
        if (AppLauncher.Launch(item))
        {
            _dock.FlashItem(item);
        }
    }

    /// <summary>Persists a drag that moved an icon to a new position.</summary>
    private void OnItemReordered(object? sender, (int From, int To) move)
    {
        var settings = ContentsToSave();
        if (move.From < 0 || move.From >= settings.PinnedApps.Count)
        {
            return;
        }

        var pin = settings.PinnedApps[move.From];
        settings.PinnedApps.RemoveAt(move.From);
        settings.PinnedApps.Insert(Math.Clamp(move.To, 0, settings.PinnedApps.Count), pin);

        _settings.Save(settings);
    }

    /// <summary>Persists an icon removed from the dock's own menu.</summary>
    private void OnItemUnpinned(object? sender, DockItem item)
    {
        var settings = ContentsToSave();
        var removed = settings.PinnedApps.RemoveAll(pin => pin.Id == item.Id);
        if (removed > 0)
        {
            _settings.Save(settings);
        }
    }

    private void OnRunningAppsChanged(object? sender, EventArgs e) => RefreshRunningState();

    private void RefreshRunningState()
    {
        var changed = false;
        foreach (var item in _items)
        {
            var running = _runningApps.IsRunning(item.RunningTarget);
            if (item.IsRunning != running)
            {
                item.IsRunning = running;
                changed = true;
            }
        }

        if (changed)
        {
            _dock.RefreshRunningState();
        }
    }

    /// <summary>
    /// Sizes the window to the widest the bar ever gets, so a magnifying wave never has to
    /// resize the window — resizing a layered window mid-gesture is both visible and slow.
    /// </summary>
    private void SizeToDock(DockSettings settings)
    {
        var wanted = _dock.PreferredSize();

        // While the settings dialog is open the window only ever grows.
        //
        // It used to jump straight to the largest the tuning values could ask for, so that
        // dragging a slider never resized it — but that made opening and closing the dialog
        // a large resize in itself, and a resized layered window shows its previous bitmap
        // inside its new bounds until it renders again. Growing on demand costs nothing at
        // all to open or close, and a drag resizes only when it asks for more room than the
        // window already has rather than on every tick. A window bigger than it needs to be
        // is invisible: it is transparent where the dock does not draw, and the bar lands in
        // the same place on the screen either way — see the placement below.
        //
        // Held in the dock's own units rather than read back off the window. The window's
        // size means different numbers on displays of different scales, so feeding it back
        // in made the held size grow every time the dock crossed between them — which is
        // what sent the window off to fill the screen.
        if (_previewing)
        {
            wanted = new Size(
                Math.Max(wanted.Width, _heldSize.Width),
                Math.Max(wanted.Height, _heldSize.Height));
        }

        _heldSize = _previewing ? wanted : default;

        // The work area comes from the display the dock is being sent to; the scale comes
        // from the display it is on at this instant.
        //
        // That pairing looks wrong and is the whole of the fix. WPF preserves a window's
        // size in device-independent units across a change of scale, so sizing the window
        // in the target's scale while it still sits at the old one makes WPF record the
        // wrong number and restore the window to it the moment the scale changes — a fight
        // this code cannot win. Sized in the scale it currently has, WPF's own rescale
        // produces exactly the size wanted, and the re-place that follows the change then
        // runs with the two scales equal and puts it precisely.
        //
        // The scale is asked of Windows rather than of WPF for the same reason: WPF learns
        // it from a message, and a layout computed in the gap before that message arrives
        // is computed for the display the dock has already left.
        var screen = Screens.Resolve(settings.ScreenDeviceName, settings.ScreenDevicePath);
        _display = screen.Bounds;
        _workArea = screen.WorkArea;
        _handleScale = MonitorDpi.ScaleAt(
            (int)(screen.Bounds.Left + (screen.Bounds.Width / 2)),
            (int)(screen.Bounds.Top + (screen.Bounds.Height / 2)));
        var scale = _chrome is { } current
            ? MonitorDpi.ScaleForWindow(current.Hwnd)
            : VisualTreeHelper.GetDpi(this).DpiScaleX;

        var width = (int)Math.Round(wanted.Width * scale);
        var height = (int)Math.Round(wanted.Height * scale);

        // Position by where the bar appears, not by the window's bounds: the window extends
        // below the bar to give its shadow somewhere to fall, and that slack should not read
        // as extra margin above the screen edge.
        var margin = (settings.BottomMargin - DockBar.BottomInset) * scale;

        // Along the edge, the window is slid the same fraction of the way along the screen as
        // the dock puts its row along the window. Aligning a box within a box composes, so
        // the row lands where the setting says whatever size the window is — which matters,
        // because the settings dialog holds the window wider than the dock, and a row placed
        // any other way would wander along the edge as that happened.
        //
        // The window's ends are kept the bottom margin inside the screen's, less the slack
        // it carries beside the bar, so a dock moved all the way along stops with its widest
        // wave as far from the side of the screen as the bar is from the bottom. Centred, all
        // of this is the middle of the work area, which is where the dock always sat.
        var side = (settings.BottomMargin - DockBar.SideInset) * scale;
        var left = (int)Math.Round(DockLayout.Align(
            screen.WorkArea.Left + side,
            screen.WorkArea.Width - (2 * side),
            width,
            settings.EdgeAlignment));
        var top = (int)Math.Round(screen.WorkArea.Bottom - height - margin);

        // Where it hides: the whole window below where its bottom is when shown, and a little
        // more, worked out here in the same physical pixels and the same scale as the shown
        // place. It used to be left to auto-hide, as the shown top plus the window's height in
        // WPF's units — and WPF's idea of the height lags a change of scale like its idea of
        // the scale does. After a wake on 2026-09-30 the dock had been counted on the 4K display
        // for a moment, and hid at 1266 + 142 ÷ 1.5 + 4 = 1364 instead of 1412: the top of the
        // bar left showing over the taskbar, for as long as it stayed hidden.
        var hidden = top + height + (int)Math.Round(HiddenGap * scale);
        _hiddenPlace = (left, hidden, width, height);

        // A hidden dock is put where it hides as surely as a shown one where it shows, rather
        // than left where it happens to be — which is what kept that wrong place. One on its
        // way out is left to its slide.
        var y = _autoHide?.Visibility switch
        {
            DockVisibility.Hidden => hidden,
            DockVisibility.Hiding => CurrentTop(),
            _ => top
        };

        Place(left, y, width, height);

        // At either end that leaves the window reaching past the side of the screen by the
        // slack beside the bar, which is transparent — but a label is not, and it is kept
        // inside the window. So the dock is told which part of it is actually on the screen.
        _dock.OnScreen = (
            (screen.WorkArea.Left - left) / scale,
            (screen.WorkArea.Right - left) / scale);

        // Auto-hide works in the window's own units, which are the target display's once it
        // has arrived there — all but across, where the edge answers under the bar, which is
        // read off the screen in physical pixels, and within the display in the same.
        //
        // The reveal edge is the display's bottom, not the work area's — the work area
        // stops at the top of the taskbar, and anchoring there made the whole taskbar
        // count as "at the edge". Its sides are the display's too: a bar wider than its
        // screen must not reach onto the display next door.
        _autoHide?.AnchorTo(top / scale, hidden / scale, screen.Bounds.Bottom / scale, screen.Bounds);
    }

    /// <summary>
    /// How far below where its bottom is when shown the window's top goes when it hides, in
    /// DIPs — so nothing of it, shadow included, is left on the screen.
    /// </summary>
    private const double HiddenGap = 4;

    /// <summary>
    /// Where the window goes when hidden, in physical pixels, as it was last worked out — see
    /// <see cref="KeepHiddenInPlace"/>.
    /// </summary>
    private (int X, int Y, int Width, int Height)? _hiddenPlace;

    /// <summary>
    /// Puts a hidden dock back where it hides, if anything has moved it since.
    /// </summary>
    /// <remarks>
    /// Asked by every look at what is in front, a few times a second, and it costs one
    /// <c>GetWindowRect</c> while the dock is hidden and nothing otherwise. The re-placing that
    /// every change of display and scale runs puts a hidden dock right; this is for a move that
    /// comes after the last of them, or without one — Windows moving windows about as the
    /// displays come back from sleep, say. A dock left partly on the screen while it counts as
    /// hidden is the worst of both: seen, and in the way, while nothing about it expects the
    /// pointer. Within a pixel, since the slide away lands through WPF's units.
    /// </remarks>
    private void KeepHiddenInPlace()
    {
        if (_autoHide is not { Visibility: DockVisibility.Hidden }
            || _hiddenPlace is not { } place
            || _chrome is not { } chrome
            || !NativeMethods.GetWindowRect(chrome.Hwnd, out var actual))
        {
            return;
        }

        if (Math.Abs(actual.Left - place.X) <= 1 && Math.Abs(actual.Top - place.Y) <= 1
            && Math.Abs(actual.Right - actual.Left - place.Width) <= 1
            && Math.Abs(actual.Bottom - actual.Top - place.Height) <= 1)
        {
            return;
        }

        Place(place.X, place.Y, place.Width, place.Height);
    }

    /// <summary>
    /// The size the settings dialog is holding the window at, in the dock's own units.
    /// </summary>
    private Size _heldSize;

    /// <summary>Where the window is now, for leaving a dock on its way out to its slide.</summary>
    private int CurrentTop() =>
        _chrome is { } chrome && NativeMethods.GetWindowRect(chrome.Hwnd, out var actual)
            ? actual.Top
            : 0;

    /// <summary>
    /// Moves and resizes the window in one go.
    /// </summary>
    /// <remarks>
    /// Not through <c>Left</c>, <c>Top</c>, <c>Width</c> and <c>Height</c>: WPF turns each of
    /// those into its own <c>SetWindowPos</c>, and the window is composed between them. The
    /// held preview size is wider than the screen, so its left edge is off screen to the
    /// left — and closing the settings dialog shrank the width before moving the window,
    /// putting the dock in the bottom-left corner for a frame on the way back to the middle.
    /// One call cannot be caught halfway.
    /// </remarks>
    /// <summary>Where the window was last put, so it is not put there again.</summary>
    private (int X, int Y, int Width, int Height)? _placed;

    private void Place(int x, int y, int width, int height)
    {
        if (_chrome is not { } chrome)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = x / dpi.DpiScaleX;
            Top = y / dpi.DpiScaleY;
            Width = width / dpi.DpiScaleX;
            Height = height / dpi.DpiScaleY;
            return;
        }

        // Checked against where the window actually is as well as against what was asked
        // for, so a move by anything else is still corrected. Every appearance change came
        // through here, and moving a layered window to where it already is still costs it a
        // recomposition — which is what the colour and opacity sliders were flickering.
        if (_placed == (x, y, width, height)
            && NativeMethods.GetWindowRect(chrome.Hwnd, out var actual)
            && actual.Left == x && actual.Top == y
            && actual.Right - actual.Left == width
            && actual.Bottom - actual.Top == height)
        {
            return;
        }

        _placed = (x, y, width, height);

        NativeMethods.SetWindowPos(
            chrome.Hwnd, 0, x, y, width, height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
    }
}
