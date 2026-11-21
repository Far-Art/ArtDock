using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ArtDock.Controls;
using ArtDock.Dock;
using ArtDock.Interop;
using ArtDock.Localization;
using ArtDock.Services;

namespace ArtDock.Views;

/// <summary>
/// An open group of the dock's own: what it holds, as a grid of icons with their names in a
/// flyout above it — as Windows 11's Start menu opens a folder of its pins.
/// </summary>
/// <remarks>
/// <para>
/// A WPF popup in the theme's own flyout colours, hung off <see cref="MenuHost"/> for the reason
/// a menu is: the dock never takes the foreground, and a popup that is not in the foreground's
/// window is not told of a click outside it. It closes as a menu does — a click anywhere else,
/// Escape, or a choice made from it.
/// </para>
/// <para>
/// A right-click on an icon asks for that item's menu, which opens over it with the flyout held
/// open under it (<see cref="ShowMenu"/>): a menu takes the mouse from a popup that closes on any
/// click elsewhere, and the popup would otherwise close under it.
/// </para>
/// <para>
/// An icon pressed and pulled is dragged out (<see cref="DragOut"/>): the dock carries it on —
/// onto the dock, into another group, or off for removal — while the flyout stays open under the
/// drag, the icon's tile faded where it was, until the drag is over (<see cref="EndDrag"/>).
/// </para>
/// <para>
/// Opened with the room for the group's icon under it (<c>morphIcon</c>), it is the icon, lifted
/// off the dock: its card starts on the icon's tile, the group's picture over it, and eases up
/// into place as what it holds comes up and the picture goes; closing, it shrinks back down onto
/// the tile and only then goes. One surface, moved by its transform and never laid out again on
/// the way. That needs it to be told of a click elsewhere rather than closing on one, so it stays
/// open by itself and is closed by whoever hears of the click — the dock, and the window the menus
/// hang off losing the foreground.
/// </para>
/// </remarks>
public sealed class GroupFlyout
{
    /// <summary>Each icon's side, in DIPs: the size Start draws a folder's pins at.</summary>
    private const double IconSize = 40;

    /// <summary>Each tile's width, in DIPs: room for a name of a word or two under the icon.</summary>
    private const double TileWidth = 84;

    /// <summary>The most icons in a row; a group holding more goes on downwards.</summary>
    private const int MostColumns = 4;

    /// <summary>
    /// The most rows shown at once; a group holding more scrolls, with half of the next row in
    /// sight to say so.
    /// </summary>
    private const double MostRows = 4.5;

    /// <summary>
    /// The room kept at the right of a scrolling group's icons for its scroll bar, and how far
    /// of that lies out in the card's own padding: the bar stands clear of the icons and of the
    /// card's edge both.
    /// </summary>
    private const double ScrollBarLane = 15;

    private const double ScrollBarReach = 9;

    /// <summary>Clear room around the card for its shadow, and below it a gap above the icon.</summary>
    private static readonly Thickness ShadowRoom = new(16, 16, 16, 12);

    /// <summary>How long the icon takes to open out into the group, easing to a stop.</summary>
    public const double OpenMs = 300;

    /// <summary>How long the group takes to close back down onto the icon.</summary>
    public const double CloseMs = 200;

    /// <summary>
    /// The side of the group's icon on the dock, magnified, in DIPs — the box the card grows out
    /// of and back into, under the card — or 0 for a flyout that simply appears and goes.
    /// </summary>
    private readonly double _morphIcon;

    /// <summary>
    /// How far to either side of its resting place the dock's wave can carry the group's icon, in
    /// DIPs: the room the flyout keeps at its sides to follow the icon into.
    /// </summary>
    private readonly double _morphReach;

    /// <summary>The card's transform on its way from the icon and back: scaled to the tile, and moved onto it.</summary>
    private readonly ScaleTransform _scale = new(1, 1);

    private readonly TranslateTransform _shift = new(0, 0);

    /// <summary>The card's corners on the way, rounded as the tile's while it is the tile's size.</summary>
    private readonly RectangleGeometry _clip = new();

    /// <summary>
    /// The group's four icons over the card, as the picture on the dock has them, there while the
    /// card is the icon and fading as it opens (<see cref="GroupArt.IconsOnly"/>).
    /// </summary>
    private Image? _picture;

    /// <summary>
    /// The resting tile's pale face, as a layer of the card: the card is the tile while it is the
    /// icon's size, in its own corners, and its face gives way to the card's as it opens.
    /// </summary>
    private Border? _face;

    /// <summary>The card's own shadow, which grows in as the card opens out and up off the dock.</summary>
    private DropShadowEffect? _shadow;

    /// <summary>
    /// The card's own shadow's strength, spread and drop while the card is the icon's size, and
    /// open. None of its own as the icon: there it has the icon's (<see cref="_iconShadow"/>).
    /// </summary>
    private static readonly (double Opacity, double Blur, double Depth) ShadowAsIcon = (0, 5, 1.5);

    /// <summary>
    /// The icon's shadow, under the card while the card is the icon: the picture the dock draws
    /// under the icon, placed about the card as the dock places it about the icon, so the card on
    /// the icon's place casts exactly what the icon does. It goes with the card as the card
    /// leaves, growing with it, and gives way to the card's own shadow.
    /// </summary>
    /// <remarks>
    /// See <see cref="DockItemVisual.ShadowAmount"/> for what it replaced, and why the dock and
    /// the flyout share the shadow out between them while both have the icon.
    /// </remarks>
    private Image? _iconShadow;

    /// <summary>Where the icon's shadow is drawn, and how large: set on every frame of the way.</summary>
    private readonly MatrixTransform _iconShadowPlace = new();

    /// <summary>The dock's shadow for the group's icon, or null when the dock draws none.</summary>
    private IconShadow? _iconShadowCast;

    /// <summary>The side of the icon's shadow's element before it is placed: any size, being stretched to its place.</summary>
    private const double ShadowSide = 64;

    /// <summary>The openness by which the icon's shadow has given way to the card's own.</summary>
    private const double ShadowsCrossBy = 0.5;

    /// <summary>How much of the icon's shadow is the flyout's to draw, 0 to 1; the rest is the dock's.</summary>
    private double _ownShadow;

    /// <summary>Whether the flyout is taking the icon's shadow over from the dock, the icon having just lifted.</summary>
    private bool _takingShadow;

    /// <summary>Whether the card has landed, and is handing its place back to the icon.</summary>
    private bool _landed;

    /// <summary>How long the icon's shadow takes to pass from the dock to the flyout as the icon lifts.</summary>
    private const double ShadowHandMs = 50;

    /// <summary>How much of the icon's shadow the dock was last told to draw; -1 before the first.</summary>
    private double _shadowOnDock = -1;

    /// <summary>
    /// The card's ground — a menu's — as a layer of its own on the way, under the tile's face:
    /// not there while the card is the icon, whose tile on the dock has only the dock under it.
    /// </summary>
    /// <remarks>
    /// The tile's face lets a twentieth of what is under it through. Over the card's ground
    /// rather than the dock, the card as the icon was a few shades off the icon itself, and the
    /// tile was seen to change shade as the icon took the card's place — measured on 2026-11-21.
    /// </remarks>
    private Border? _backdrop;

    /// <summary>How much rounder the open card's corners are than the icon's tile's, and the most they are rounded, in DIPs.</summary>
    private const double OpenCornerGrowth = 2;

    private const double MostOpenCorner = 24;

    private static readonly (double Opacity, double Blur, double Depth) ShadowOpen = (0.22, 16, 4);

    /// <summary>What the card shows — title and icons — faded in as it opens and out as it closes.</summary>
    private FrameworkElement? _content;

    /// <summary>The card's place, unmoved by its transform: where the card stands once open.</summary>
    private FrameworkElement? _cardFrame;

    /// <summary>Set once closing has begun, on its way down onto the icon.</summary>
    private bool _closing;

    /// <summary>Set while it is on its way up out of the icon, before it has settled open.</summary>
    private bool _opening;

    /// <summary>
    /// The corners, as drawn, all the way from the icon to the card and back, and of the card and
    /// its tiles once open: the icon's tile's, so the shape keeps the corners it had on the dock.
    /// </summary>
    private double _corner;

    /// <summary>
    /// How far open the card is drawn: 0 the icon on the dock, 1 open. Everything of the way
    /// between — where the card is, how big, how much of it shows, its corners, its shadow, what
    /// is on it — is worked out from this on every frame (<see cref="Draw"/>), against where the
    /// icon is on that frame.
    /// </summary>
    private double _openness;

    /// <summary>The leg of the way under way: from and to which openness, over how long, how far in.</summary>
    private double _legFrom;

    private double _legTo;

    private double _legMs;

    private double _legElapsedMs;

    private bool _legRunning;

    /// <summary>What to do on arriving at the end of the leg.</summary>
    private Action? _legDone;

    /// <summary>Whether the card is being drawn frame by frame (<see cref="Step"/>).</summary>
    private bool _stepping;

    private long _lastStep;

    /// <summary>The window of the card the tile's layers were last laid out for, so they are laid out again only when it changes.</summary>
    private Rect _tileLaidFor = Rect.Empty;

    /// <summary>The tile's face, shaded across the window: one brush, moved with the window.</summary>
    private readonly LinearGradientBrush _faceBrush = new(GroupArt.FaceTop, GroupArt.FaceBottom, 90)
    {
        MappingMode = BrushMappingMode.Absolute
    };

    /// <summary>
    /// The way's pace: off from rest and in to rest, so the card leaves the icon and lands on it
    /// without a step — a cubic Bézier, as CSS's <c>cubic-bezier(0.3, 0.05, 0.2, 1)</c>.
    /// </summary>
    private static readonly KeySpline Pace = MakePace();

    private static KeySpline MakePace()
    {
        var pace = new KeySpline(0.3, 0.05, 0.2, 1);
        pace.Freeze();
        return pace;
    }

    /// <summary>The shortest a leg takes, however little of the way it covers: a turn back near either end.</summary>
    private const double ShortestLegMs = 120;

    /// <summary>
    /// The openness over which the tile — its face and four icons — gives way, and over which
    /// what the card holds comes up: crossing, so the card is never neither.
    /// </summary>
    private const double TileWholeUntil = 0.08;

    private const double TileGoneBy = 0.5;

    private const double ContentFrom = 0.12;

    private const double ContentWholeBy = 0.7;

    /// <summary>
    /// Counts the ways set out on, opening or closing, so one turned back part of the way does not
    /// finish after all — closing the flyout under a reopened card, or settling a closing one.
    /// </summary>
    private int _journey;

    private readonly Popup _popup;

    /// <summary>The name being typed, for a group opened to be named; null otherwise.</summary>
    private TextBox? _nameBox;

    /// <summary>Set when Escape takes the flyout down: the name typed, if any, is not kept.</summary>
    private bool _cancelled;

    /// <summary>
    /// Whether its icons may be dragged out and its name changed — not while the dock's contents
    /// are locked.
    /// </summary>
    private readonly bool _canEdit;

    /// <summary>Where the title stands: the name, or the box it is typed in while it is being changed.</summary>
    private readonly ContentControl _titleHost = new() { Focusable = false, IsTabStop = false };

    /// <summary>The flyout's outermost element, which takes the keys when no tile can.</summary>
    private FrameworkElement? _root;

    /// <summary>The card the content stands on, inside the room kept round it for its shadow.</summary>
    private Border? _card;

    /// <summary>Set by the keys that ask for a tile's menu, so it opens under the tile rather than at the pointer.</summary>
    private bool _menuFromKeys;

    /// <summary>The icon the left button went down on, and where, until it comes up or a drag starts.</summary>
    private (DockItem Item, Point At)? _press;

    /// <summary>Set while an icon is being dragged out of it, when nothing in it is clicked.</summary>
    private bool _draggedOut;

    /// <summary>The tile of the icon being dragged out, faded while it is.</summary>
    private UIElement? _draggedTile;

    /// <summary>How the tile of an icon being dragged out is drawn: the place it left.</summary>
    private const double DraggedTileOpacity = 0.3;

    /// <summary>How an icon that cannot be opened is drawn, as the dock draws one.</summary>
    private const double UnopenableOpacity = 0.45;

    /// <param name="group">The group, with what it holds and their icons.</param>
    /// <param name="naming">
    /// Opened for a group just made: its title is a box with the name it was given selected, to
    /// be typed over (<see cref="BeginRename"/>).
    /// </param>
    /// <param name="canEdit">Whether its icons may be dragged out of it, and its name changed.</param>
    /// <param name="morphIcon">
    /// The side of the group's icon on the dock, in DIPs, for the flyout to open out of it and
    /// close back into it, the icon's room kept under the card; 0 — for reduced motion — and it
    /// simply appears and goes.
    /// </param>
    /// <param name="morphReach">
    /// How far to either side of where it rests the wave can carry that icon, in DIPs: the card
    /// follows the icon there as it closes.
    /// </param>
    /// <param name="blurred">
    /// Whether the card stands on a blur of what is behind it, see-through, rather than on a
    /// menu's plain ground: where Windows' transparency effects are on and there is a GPU to do it.
    /// </param>
    public GroupFlyout(
        DockItem group,
        bool naming = false,
        bool canEdit = true,
        double morphIcon = 0,
        double morphReach = 0,
        bool blurred = false)
    {
        Group = group;
        _canEdit = canEdit;
        _morphIcon = Math.Max(0, morphIcon);
        _morphReach = Math.Max(0, morphReach);
        _blurred = blurred && Sheet() is not null;
        _holder = new Decorator { Child = Build(group, naming) };
        _popup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = Morphs,
            PopupAnimation = PopupAnimation.None,
            Child = _holder
        };

        // Laid out and placed, but drawn only once the card is on the icon.
        if (Morphs && _root is { } hidden)
        {
            hidden.Opacity = 0;
        }

        // Lifted off the icon the moment it is open: laid out there and then, rather than on the
        // next turn of WPF's own, which kept it out of sight for a frame or more after the click.
        _popup.Opened += (_, _) =>
        {
            WatchActivation();
            KeepSheetUnder();
            _root?.UpdateLayout();
            if (Morphs)
            {
                BeginOpening();
            }
            else
            {
                DrawSheetAtRest();
            }
        };
        _popup.Opened += (_, _) => _popup.Dispatcher.InvokeAsync(
            () =>
            {
                if (_nameBox is { } box)
                {
                    FocusNameBox(box);
                }
                else
                {
                    FocusContent();
                }
            },
            System.Windows.Threading.DispatcherPriority.Input);
        _popup.Closed += (_, _) =>
        {
            StopStepping();
            StopResizing();
            LetSheetGo();

            // A name being typed when the flyout goes is kept, as a click elsewhere keeps it in
            // Explorer — unless Escape took the flyout down.
            if (!_cancelled && _nameBox?.Text.Trim() is { Length: > 0 } name && name != Group.Label)
            {
                Renamed?.Invoke(this, name);
            }

            Closed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>The group it shows.</summary>
    public DockItem Group { get; private set; }

    /// <summary>Whether it is on screen.</summary>
    public bool IsOpen => _popup.IsOpen;

    /// <summary>The popup, for <see cref="MenuHost.ShowPopup"/> to place.</summary>
    public Popup Popup => _popup;

    /// <summary>
    /// What the popup holds, for the life of the popup: the card is rebuilt inside it, never in
    /// the popup's place. Handed a new child, the popup placed itself from a size measured before
    /// that child was laid out — some two hundred pixels to the right and down over the dock —
    /// and was put back a frame later: a jump each time an icon dragged out was removed, seen
    /// frame by frame on 2026-11-21. With the same child, it only grows or shrinks, in place.
    /// </summary>
    private readonly Decorator _holder;

    /// <summary>
    /// Raised when the flyout's own window stops being the active one. A click in it can make it
    /// that, in place of the window the menus hang off, and a click elsewhere is then heard of
    /// only here.
    /// </summary>
    public event EventHandler? Deactivated;

    /// <summary>Raised as a mouse button goes down anywhere in the flyout.</summary>
    public event EventHandler? Pressed;

    private void WatchActivation()
    {
        if (PresentationSource.FromVisual(_popup.Child) is HwndSource source)
        {
            // The source goes with the popup's window, and the hook with it.
            source.AddHook(OnWindowMessage);
        }
    }

    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_ACTIVATE = 0x0006;
        if (msg == WM_ACTIVATE && ((long)wParam & 0xFFFF) == 0)
        {
            Deactivated?.Invoke(this, EventArgs.Empty);
        }

        return 0;
    }

    /// <summary>Whether the card stands on a blur of what is behind it (<see cref="Sheet"/>).</summary>
    private readonly bool _blurred;

    /// <summary>How much of the card's ground shows over the blur: enough to read on, the rest see-through.</summary>
    private const double GroundOnBlur = 0.72;

    /// <summary>
    /// The blurred sheet under the open group: a window of its own straight under the flyout's,
    /// as the dock's bar has one, since nothing composes a blur behind a see-through WPF window.
    /// One for the life of the program — only one group is open at a time, and making one takes
    /// long enough to hold a group up as it opens (<see cref="WarmUp"/>).
    /// </summary>
    private static BackdropWindow? _sheet;

    private static bool _sheetTried;

    /// <summary>The flyout the sheet is under now, so one closing late does not take it from the next.</summary>
    private static GroupFlyout? _sheetOwner;

    /// <summary>Where the sheet's window is, in device pixels: where the flyout's own is.</summary>
    private NativeMethods.NativeRect _sheetAt;

    /// <summary>The sheet, made the first time it is asked for; null where it cannot be cut to a shape.</summary>
    private static BackdropWindow? Sheet()
    {
        if (_sheetTried)
        {
            return _sheet;
        }

        _sheetTried = true;
        try
        {
            // No wash and no shadow: the card over it is its tint, and casts its own.
            var sheet = new BackdropWindow(Colors.Transparent, shadow: false);
            if (sheet.DrawsBar)
            {
                _sheet = sheet;
            }
            else
            {
                sheet.Dispose();
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No window to be had: the card keeps its plain ground.
        }

        return _sheet;
    }

    /// <summary>How long the card takes to grow or shrink to what it holds when that changes.</summary>
    private const double ResizeMs = 220;

    /// <summary>Whether the card's frame is on its way between two sizes (<see cref="ResizeStep"/>).</summary>
    private bool _resizing;

    private Size _resizeFrom;

    private Size _resizeTo;

    private double _resizeElapsedMs;

    private long _resizeLast;

    /// <summary>The card's shape while it changes size, so what it holds is cut to it.</summary>
    private readonly RectangleGeometry _resizeClip = new();

    /// <summary>
    /// Has the card, rebuilt at a new size, go there from the size it had: its frame — ground,
    /// rim, blur — grows or shrinks round what it now holds, which stands at its new size from
    /// the start, under the title at the top. The flyout's window is held at the larger of the
    /// two meanwhile, so nothing moves but the card's edge, and lets go once it is there.
    /// </summary>
    private void ResizeFrom(Size from, Size holderWas)
    {
        if (_card is not { Child: FrameworkElement body } card || from.IsEmpty)
        {
            PlaceAgain();
            return;
        }

        // The new card's own size, measured with the window held where it was.
        _holder.MinWidth = holderWas.Width;
        _holder.MinHeight = holderWas.Height;
        _holder.UpdateLayout();
        var to = new Size(card.ActualWidth, card.ActualHeight);
        if (Math.Abs(to.Width - from.Width) < 0.5 && Math.Abs(to.Height - from.Height) < 0.5)
        {
            EndResize();
            return;
        }

        // What it holds, at its new size, from the top, the frame going round it.
        body.Width = body.ActualWidth;
        body.Height = body.ActualHeight;
        body.HorizontalAlignment = HorizontalAlignment.Center;
        body.VerticalAlignment = VerticalAlignment.Top;

        _holder.MinWidth = holderWas.Width + Math.Max(0, to.Width - from.Width);
        _holder.MinHeight = holderWas.Height + Math.Max(0, to.Height - from.Height);

        _resizeFrom = from;
        _resizeTo = to;
        _resizeElapsedMs = 0;
        card.Clip = _resizeClip;
        SizeCard(card, from);
        _holder.UpdateLayout();
        PlaceAgain();
        DrawSheetAtRest();

        if (!_resizing)
        {
            _resizing = true;
            CompositionTarget.Rendering += ResizeStep;
        }

        _resizeLast = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// One frame of the card's change of size. Only the card is laid out again: the flyout's
    /// window is held at its size till the end, so no window moves from here.
    /// </summary>
    private void ResizeStep(object? sender, EventArgs e)
    {
        if (_card is not { } card || !_popup.IsOpen)
        {
            StopResizing();
            return;
        }

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        _resizeElapsedMs += Math.Min(50, System.Diagnostics.Stopwatch.GetElapsedTime(_resizeLast, now).TotalMilliseconds);
        _resizeLast = now;

        var time = Math.Min(1, _resizeElapsedMs / ResizeMs);
        var eased = Pace.GetSplineProgress(time);
        SizeCard(card, new Size(
            _resizeFrom.Width + ((_resizeTo.Width - _resizeFrom.Width) * eased),
            _resizeFrom.Height + ((_resizeTo.Height - _resizeFrom.Height) * eased)));
        _holder.UpdateLayout();
        DrawSheetAtRest();

        if (time >= 1)
        {
            EndResize();
        }
    }

    private void SizeCard(Border card, Size size)
    {
        card.Width = size.Width;
        card.Height = size.Height;
        _resizeClip.Rect = new Rect(size);
        _resizeClip.RadiusX = _resizeClip.RadiusY = Math.Max(0, card.CornerRadius.TopLeft - (card.BorderThickness.Left / 2));
    }

    /// <summary>Lets the card and the window take their own sizes again, the card where it is.</summary>
    private void EndResize()
    {
        StopResizing();
        if (_card is { } card)
        {
            card.ClearValue(FrameworkElement.WidthProperty);
            card.ClearValue(FrameworkElement.HeightProperty);
            if (ReferenceEquals(card.Clip, _resizeClip))
            {
                card.Clip = null;
            }

            if (card.Child is FrameworkElement body)
            {
                body.ClearValue(FrameworkElement.WidthProperty);
                body.ClearValue(FrameworkElement.HeightProperty);
                body.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
                body.ClearValue(FrameworkElement.VerticalAlignmentProperty);
            }
        }

        _holder.ClearValue(FrameworkElement.MinWidthProperty);
        _holder.ClearValue(FrameworkElement.MinHeightProperty);
        _holder.UpdateLayout();
        PlaceAgain();
        DrawSheetAtRest();

        // And again once the window has taken its own size, which it does after this.
        _popup.Dispatcher.BeginInvoke(
            () =>
            {
                if (!_resizing && !_stepping && _popup.IsOpen)
                {
                    DrawSheetAtRest();
                }
            },
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void StopResizing()
    {
        if (_resizing)
        {
            _resizing = false;
            CompositionTarget.Rendering -= ResizeStep;
        }
    }

    /// <summary>
    /// Places the flyout again from what it now holds: centred over the group's place, standing
    /// on it, at the size it is now.
    /// </summary>
    /// <remarks>
    /// The popup places itself again when what it holds is replaced, but from a size measured
    /// before the new card was laid out: the open group jumped some two hundred pixels to the
    /// right and down over the dock as an icon dragged out of it was removed, in one frame —
    /// seen frame by frame on 2026-11-21, the window it is placed from where it had been. So
    /// it is laid out first, and then asked to place itself again, which a change of offset does.
    /// </remarks>
    private void PlaceAgain()
    {
        if (!_popup.IsOpen || _popup.Child is not UIElement child)
        {
            return;
        }

        child.UpdateLayout();
        var offset = _popup.HorizontalOffset;
        _popup.HorizontalOffset = offset + 1;
        _popup.HorizontalOffset = offset;
        KeepSheetUnder();
    }

    /// <summary>
    /// Puts the blurred sheet where the flyout's window is and straight under it: as it opens,
    /// when it changes size, and whenever the dock has put the flyout back over itself.
    /// </summary>
    /// <remarks>
    /// The sheet's window covers the whole display the group is on, and so stays where it is
    /// while the group is open, whatever size the group is. Kept to the flyout's own window, it
    /// was moved each time the group changed size, and a sheet's window moved shows its blur at
    /// the whole of the window for a frame before its shape is drawn again: a blink of the
    /// group as an item went from it (reported 2026-11-21). The blur is drawn only under the
    /// card, so the room round it costs nothing, and the window passes clicks by.
    /// </remarks>
    public void KeepSheetUnder()
    {
        if (!_blurred || _sheet is not { } sheet || !_popup.IsOpen
            || PresentationSource.FromVisual(_popup.Child) is not HwndSource source
            || MonitorDpi.DisplayOfWindow(source.Handle) is not { } at)
        {
            return;
        }

        _sheetOwner = this;
        _sheetAt = at;
        sheet.Cover(at.Left, at.Top, at.Right - at.Left, at.Bottom - at.Top, source.Handle);
    }

    /// <summary>
    /// Draws the blur under the card: where the card shows, in the flyout's own units, rounded
    /// as it is on screen, and that much of it.
    /// </summary>
    private void DrawSheet(Rect shown, double corner, double opacity)
    {
        if (!ReferenceEquals(_sheetOwner, this) || _sheet is not { } sheet || _root is not { } root
            || PresentationSource.FromVisual(root) is not { CompositionTarget: { } target }
            || ScreenSpace.ToScreen(root, shown.TopLeft) is not { } near
            || ScreenSpace.ToScreen(root, shown.BottomRight) is not { } far)
        {
            return;
        }

        var on = new Rect(near, far);
        var scale = target.TransformToDevice.M11;
        sheet.DrawSheet(
            on.Left - _sheetAt.Left, on.Top - _sheetAt.Top, on.Width, on.Height, corner * scale, scale, opacity);
    }

    /// <summary>Draws the blur under the card standing open, all of it.</summary>
    private void DrawSheetAtRest()
    {
        if (_card is not { ActualWidth: > 0 } card || _root is not { } root
            || PresentationSource.FromVisual(root) is null)
        {
            return;
        }

        DrawSheet(
            new Rect(card.TranslatePoint(new Point(0, 0), root), new Size(card.ActualWidth, card.ActualHeight)),
            card.CornerRadius.TopLeft,
            1);
    }

    private void LetSheetGo()
    {
        if (ReferenceEquals(_sheetOwner, this))
        {
            _sheetOwner = null;
            _sheet?.Hide();
        }
    }

    /// <summary>
    /// Gives a part of the card its ground: a menu's — or, over the blur, that much of it
    /// see-through, as the theme has it now.
    /// </summary>
    private void SetGround(Border on)
    {
        if (_blurred && on.TryFindResource("ContextMenuBackground") is SolidColorBrush solid)
        {
            var ground = new SolidColorBrush(solid.Color) { Opacity = solid.Opacity * GroundOnBlur };
            ground.Freeze();
            on.Background = ground;
            return;
        }

        on.SetResourceReference(Border.BackgroundProperty, "ContextMenuBackground");
    }

    /// <summary>Raised, once it has closed, when one of its items was clicked, with what the click asks for.</summary>
    public event EventHandler<(DockItem Item, DockClick Click)>? Chosen;

    /// <summary>
    /// Raised when one of its items is right-clicked, with the tile it was clicked on: the item's
    /// menu is wanted, to be shown by <see cref="ShowMenu"/>.
    /// </summary>
    public event EventHandler<(DockItem Item, UIElement Tile)>? MenuAsked;

    /// <summary>
    /// Raised when one of its items is pressed and pulled: it is to be dragged out of the group,
    /// the left button still down. With where the flyout stands, in device pixels, inside which
    /// letting go puts it back. The flyout stays open, and is told when the drag is over
    /// (<see cref="EndDrag"/>).
    /// </summary>
    public event EventHandler<(DockItem Item, Rect Bounds)>? DragOut;

    /// <summary>
    /// Raised with a new name typed for the group: on Enter, or as the flyout goes while one is
    /// being typed — not when Escape called it off.
    /// </summary>
    public event EventHandler<string>? Renamed;

    /// <summary>Raised when it has gone, however it went.</summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Raised as its card leaves the icon's place, opening: the icon on the dock is to go, the card
    /// being it now, until the flyout has closed back onto it.
    /// </summary>
    public event EventHandler? Lifted;

    /// <summary>
    /// Raised by a click on the group's place on the dock that the flyout, reaching down over it,
    /// took itself: the group is to be turned round, as the dock would for a click there.
    /// </summary>
    public event EventHandler? PlaceClicked;

    /// <summary>
    /// Raised as the card, closing, has come down onto the icon's place: the icon is to be back on
    /// the dock, the flyout going once it is.
    /// </summary>
    public event EventHandler? Landed;

    /// <summary>
    /// Where the group's icon is drawn now, on screen in device pixels — its place, while the card
    /// is it — or null when the dock has it no longer. What the card opens out of and lands on.
    /// </summary>
    public Func<Rect?>? IconOnScreen { get; set; }

    /// <summary>
    /// Told how much of the group's icon's shadow the dock is to draw itself, 0 to 1, as that
    /// changes: the rest of it the flyout draws, under the card.
    /// </summary>
    public Action<double>? ShadowOnDock { get; set; }

    /// <summary>The shadow the dock draws under the group's icon, or null when it draws none.</summary>
    public Func<IconShadow?>? IconShadowOnDock { get; set; }

    /// <summary>
    /// Raised as it starts to close back into the icon: the dock lets go of what it held still
    /// for the open group, and the card follows the icon wherever that takes it.
    /// </summary>
    public event EventHandler? Closing;

    /// <summary>Whether it opens out of the group's icon and closes back into it.</summary>
    public bool Morphs => _morphIcon > 0;

    /// <summary>Whether it is on its way back down onto the icon.</summary>
    public bool IsClosing => _closing;

    /// <summary>
    /// Where the group's icon is, on screen in device pixels: the middle of its foot, as the popup
    /// was placed from it. What the card grows out of and goes back into.
    /// </summary>
    public Point Anchor { get; set; }

    /// <summary>Takes it down — back into the icon first, when it opened out of it.</summary>
    public void Close()
    {
        if (!_popup.IsOpen || _closing)
        {
            return;
        }

        if (Morphs)
        {
            Closing?.Invoke(this, EventArgs.Empty);
        }

        if (!Morphs || !Measure(out _, out _))
        {
            _popup.IsOpen = false;
            return;
        }

        // From wherever it stands: part of the way out, it turns back down.
        _closing = true;
        _opening = false;
        var leg = ++_journey;
        Dress();
        Go(0, CloseMs, () =>
        {
            if (leg != _journey)
            {
                return;
            }

            // The icon back on the dock first, under the card — without its shadow, which is the
            // flyout's yet, and passes to the dock as the card fades off the icon (Step).
            _landed = true;
            TellDockShadow();
            Landed?.Invoke(this, EventArgs.Empty);
            HandOver(leg);
        });
    }

    /// <summary>How long the card, landed, stays whole over the icon while the dock draws the icon under it.</summary>
    private const double LandedHoldMs = 50;

    /// <summary>How long the card then takes to fade off the icon.</summary>
    private const double LandedFadeMs = 80;

    /// <summary>
    /// Hands the place back to the icon on the dock, the card having landed on it: the card stays
    /// over it for a few frames, then fades off it, and only then goes.
    /// </summary>
    /// <remarks>
    /// The dock's window reaches the screen a frame or two after it is asked for the icon, and
    /// the flyout's goes at once: closed as it landed, the place stood empty for a frame between
    /// the two — every time, seen frame by frame on 2026-11-21, as a flicker at the end of the
    /// closing. The card and the icon are a pixel or two and a few shades apart, so the one over
    /// the other is nothing to see, and faded rather than cut, neither is the change between them.
    /// The card goes on being drawn on the icon, frame by frame, until it has gone.
    /// </remarks>
    private void HandOver(int leg)
    {
        var hold = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Normal, _popup.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(LandedHoldMs)
        };
        hold.Tick += (_, _) =>
        {
            hold.Stop();
            if (leg != _journey || !_popup.IsOpen || _root is not { } root)
            {
                return;
            }

            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(LandedFadeMs));
            fade.Completed += (_, _) =>
            {
                if (leg == _journey)
                {
                    _popup.IsOpen = false;
                }
            };
            root.BeginAnimation(UIElement.OpacityProperty, fade);
        };
        hold.Start();
    }

    /// <summary>
    /// Turns a closing flyout back up, from wherever it is on its way down, as if it had never
    /// started to close.
    /// </summary>
    /// <returns>False when it was not closing, or could not turn back.</returns>
    public bool Reopen()
    {
        if (!_popup.IsOpen || !_closing || !Morphs || !Measure(out _, out _))
        {
            return false;
        }

        _closing = false;
        _opening = true;
        var leg = ++_journey;

        // Whole again, if it had begun to fade off the icon it landed on.
        if (_root is { } root)
        {
            root.BeginAnimation(UIElement.OpacityProperty, null);
            root.Opacity = 1;
        }

        _landed = false;
        _takingShadow = _ownShadow < 1;
        Dress();
        Go(1, OpenMs, () =>
        {
            if (leg == _journey)
            {
                Settle();
            }
        });
        Lifted?.Invoke(this, EventArgs.Empty);
        TellDockShadow();
        return true;
    }

    /// <summary>Takes it down at once, where it stands: for the dock changing under it.</summary>
    public void CloseNow()
    {
        _closing = true;
        _popup.IsOpen = false;
    }

    /// <summary>
    /// Keeps it open whatever is clicked, or lets a click elsewhere close it again — for one that
    /// closes itself; one that opens out of the icon is always kept, and closed by its owner.
    /// </summary>
    private void HoldOpen(bool held)
    {
        if (!Morphs && _popup.IsOpen)
        {
            _popup.StaysOpen = held;
        }
    }

    /// <summary>
    /// Where the card stands once open, and where the group's icon is drawn on this frame, both
    /// in the flyout's own units. False before the flyout is laid out.
    /// </summary>
    /// <remarks>
    /// The icon is measured as the dock draws it now (<see cref="IconOnScreen"/>), on every frame
    /// of the way: not taken for a fully magnified one, nor for where it was as the way began.
    /// Closing, the dock lets go of its wave, and the icon moves and changes size with the
    /// pointer; a card sent to where the icon had been landed beside it and bigger, over its
    /// neighbour, and crossed to it as a double image — seen frame by frame on 2026-11-21.
    /// </remarks>
    private bool Measure(out Rect card, out Rect icon)
    {
        card = icon = Rect.Empty;
        if (_root is not { } root || _cardFrame is not { } frame || _card is not { } face
            || PresentationSource.FromVisual(root) is null || frame.ActualWidth <= 0 || face.ActualWidth <= 0)
        {
            return false;
        }

        card = new Rect(
            frame.TranslatePoint(new Point(ShadowRoom.Left, ShadowRoom.Top), root),
            new Size(face.ActualWidth, face.ActualHeight));

        // To the fraction of a pixel (ScreenSpace): the card is this size while it is the icon.
        if (IconOnScreen?.Invoke() is { IsEmpty: false } drawn
            && ScreenSpace.FromScreen(root, drawn.TopLeft) is { } topLeft
            && ScreenSpace.FromScreen(root, drawn.BottomRight) is { } bottomRight)
        {
            icon = new Rect(topLeft, bottomRight);
        }
        else
        {
            var foot = root.PointFromScreen(Anchor);
            icon = new Rect(foot.X - (_morphIcon / 2), foot.Y - _morphIcon, _morphIcon, _morphIcon);
        }

        return icon.Width > 0 && icon.Height > 0;
    }

    /// <summary>
    /// A click on the flyout but not on the card — round it, or in the icon's room under it. On the
    /// group's own place on the dock it is a click on the group (<see cref="PlaceClicked"/>);
    /// anywhere else, it closes the flyout, as a click beside an open folder on a phone does.
    /// </summary>
    private void OnClickBesideCard(FrameworkElement root, MouseButtonEventArgs e)
    {
        // A click on the card, or on anything on it, is the card's.
        if (e.OriginalSource is DependencyObject source && _card is { } card
            && (ReferenceEquals(source, card) || card.IsAncestorOf(source)))
        {
            return;
        }

        e.Handled = true;

        // Where the group's place is, asked now: the flyout stands where it was put by then,
        // pushed aside by the screen's edge or not.
        var at = e.GetPosition(root);
        var foot = root.PointFromScreen(Anchor);
        var onPlace = Math.Abs(at.X - foot.X) <= _morphIcon / 2 && at.Y >= foot.Y - _morphIcon;
        if (onPlace && e.ChangedButton == MouseButton.Left)
        {
            PlaceClicked?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Close();
        }
    }

    /// <summary>
    /// What puts a card standing at <paramref name="card"/> onto <paramref name="tile"/>, scaled
    /// evenly — so nothing on it is stretched — about its top-left corner: the scale, the offset,
    /// and the window of the card, in its own units, that shows there: the tile's shape, as big as
    /// the card holds, in its middle.
    /// </summary>
    public static (double Scale, double X, double Y, Rect Window) MorphFrom(Rect card, Rect tile)
    {
        var width = Math.Max(1, card.Width);
        var height = Math.Max(1, card.Height);
        var scale = Math.Max(tile.Width / width, tile.Height / height);
        var window = new Rect(
            (width - (tile.Width / scale)) / 2,
            (height - (tile.Height / scale)) / 2,
            tile.Width / scale,
            tile.Height / scale);
        return (scale, tile.Left - card.Left - (scale * window.Left), tile.Top - card.Top - (scale * window.Top), window);
    }

    /// <summary>Puts the card on the icon and opens it out of there.</summary>
    private void BeginOpening()
    {
        if (!_popup.IsOpen || _closing || _root is not { } root)
        {
            return;
        }

        if (!Measure(out _, out var icon))
        {
            root.Opacity = 1;
            return;
        }

        var leg = ++_journey;
        _opening = true;

        // The card, and the tiles on it, rounded after the icon's tile — rounder, being so much
        // the larger — and from the one to the other all the way between (Draw).
        var tileCorner = GroupArt.TileRadiusIn(icon);
        _corner = Math.Max(tileCorner, Math.Min(MostOpenCorner, tileCorner * OpenCornerGrowth));
        root.Resources["OverlayCornerRadius"] = new CornerRadius(_corner);
        root.UpdateLayout();

        // The icon is the dock's yet, shadow and all, until the card has been drawn over it.
        _openness = 0;
        _ownShadow = 0;
        _takingShadow = false;
        _landed = false;
        Dress();
        Draw();
        TellDockShadow();
        root.Opacity = 1;
        Go(1, OpenMs, () =>
        {
            if (leg == _journey)
            {
                Settle();
            }
        });

        // The icon goes from the dock once the card, standing on it and looking as it does, has
        // been drawn: the two for a frame is nothing to see, neither for a frame is a blink.
        _popup.Dispatcher.BeginInvoke(
            () =>
            {
                if (_popup.IsOpen && !_closing)
                {
                    Lifted?.Invoke(this, EventArgs.Empty);
                    _takingShadow = true;
                }
            },
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Sets the card up to be drawn part of the way: clipped to the window of it that shows, moved
    /// by its transform, the tile's layers on it.
    /// </summary>
    private void Dress()
    {
        if (_card is not { } card || _picture is not { } picture || _face is not { } face)
        {
            return;
        }

        card.Clip = _clip;
        if (card.RenderTransform is not TransformGroup)
        {
            card.RenderTransform = new TransformGroup { Children = { _scale, _shift } };
        }

        face.Background = _faceBrush;
        face.Visibility = Visibility.Visible;
        picture.Visibility = Visibility.Visible;
        _tileLaidFor = Rect.Empty;

        // The card's ground as the layer that comes and goes under the tile's face — rounded as
        // the card rounds its own, inside its border. Square, its corners stood out past the
        // card's, cut off short by the window the card shows through, and were seen to round off
        // as the card settled: a corner that changed once the opening had ended (2026-11-21).
        card.Background = null;
        if (_backdrop is { } backdrop)
        {
            backdrop.CornerRadius = new CornerRadius(
                Math.Max(0, card.CornerRadius.TopLeft - (card.BorderThickness.Left / 2)));
            backdrop.Visibility = Visibility.Visible;
        }

        _iconShadowCast = IconShadowOnDock?.Invoke();
        if (_iconShadow is { } iconShadow)
        {
            iconShadow.Source = _iconShadowCast?.Image;
        }
    }

    /// <summary>
    /// Sets out for an openness — 1 open, 0 the icon — from wherever the card stands, off from
    /// rest and in to rest, taking as much of <paramref name="fullMs"/> as it has of the way to go.
    /// </summary>
    /// <remarks>
    /// Turned back part of the way, the card stops where it is and goes back from there, by the
    /// same pace: nothing of it steps, since all of it is drawn from the one openness.
    /// </remarks>
    private void Go(double to, double fullMs, Action? done)
    {
        _legFrom = _openness;
        _legTo = to;
        _legMs = Math.Max(ShortestLegMs, fullMs * Math.Abs(to - _legFrom));
        _legElapsedMs = 0;
        _legDone = done;
        _legRunning = true;

        if (!_stepping)
        {
            _stepping = true;
            CompositionTarget.Rendering += Step;
        }

        _lastStep = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    private void StopStepping()
    {
        _legRunning = false;
        _legDone = null;
        if (_stepping)
        {
            _stepping = false;
            CompositionTarget.Rendering -= Step;
        }
    }

    /// <summary>
    /// One frame of the way: the openness moved on by the time that has passed, and the card drawn
    /// for it against where the icon is now. Nothing here moves a window — only what is drawn in
    /// the flyout's own — so it does not feed the frame clock back on itself, as moving one does.
    /// </summary>
    private void Step(object? sender, EventArgs e)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var passed = System.Diagnostics.Stopwatch.GetElapsedTime(_lastStep, now).TotalMilliseconds;
        _lastStep = now;

        // The icon's shadow, passing from the dock to the flyout once the icon has lifted.
        if (_takingShadow)
        {
            _ownShadow = Math.Min(1, _ownShadow + (Math.Min(passed, 50) / ShadowHandMs));
            _takingShadow = _ownShadow < 1;
        }

        Action? arrived = null;
        if (_legRunning)
        {
            // A frame held up does not make the card leap the time it lost.
            _legElapsedMs += Math.Min(passed, 50);
            var time = Math.Min(1, _legElapsedMs / _legMs);
            _openness = _legFrom + ((_legTo - _legFrom) * Pace.GetSplineProgress(time));
            if (time >= 1)
            {
                _openness = _legTo;
                _legRunning = false;
                arrived = _legDone;
                _legDone = null;
            }
        }

        Draw();
        TellDockShadow();
        arrived?.Invoke();
    }

    /// <summary>
    /// Draws the card for the openness it is at: between the icon's tile, where the dock has it on
    /// this frame, and its own place open.
    /// </summary>
    /// <remarks>
    /// Scaled evenly, so nothing on it is stretched, the card shows only the tile-shaped window of
    /// its middle while it is the icon, and opens out to all of itself on the way, as a phone's
    /// folder does. Its corners on screen go from the tile's as it is now to the card's own, in
    /// step with it; its shadow from an icon's worth to the card's; and the tile's layers cross
    /// with what the card holds.
    /// </remarks>
    private void Draw()
    {
        if (_card is not { } card || _content is not { } content || _picture is not { } picture
            || _face is not { } face || _shadow is not { } shadow || !Measure(out var place, out var icon))
        {
            return;
        }

        var open = Math.Clamp(_openness, 0, 1);
        var (tileScale, x, y, window) = MorphFrom(place, GroupArt.TileIn(icon));

        var scale = tileScale + ((1 - tileScale) * open);
        _scale.ScaleX = _scale.ScaleY = scale;
        _shift.X = x * (1 - open);
        _shift.Y = y * (1 - open);

        _clip.Rect = new Rect(
            window.Left * (1 - open),
            window.Top * (1 - open),
            window.Width + ((place.Width - window.Width) * open),
            window.Height + ((place.Height - window.Height) * open));

        // On screen, from the tile's corners as the dock draws it now to the card's own.
        var tileCorner = GroupArt.TileRadiusIn(icon);
        var corner = (tileCorner + ((_corner - tileCorner) * open)) / Math.Max(0.01, scale);
        _clip.RadiusX = corner;
        _clip.RadiusY = corner;

        // The blur under the card where the card shows on this frame, there as the card's ground
        // is: none of it under the card as the icon, whose tile on the dock has none.
        DrawSheet(
            new Rect(
                place.Left + _shift.X + (_clip.Rect.Left * scale),
                place.Top + _shift.Y + (_clip.Rect.Top * scale),
                _clip.Rect.Width * scale,
                _clip.Rect.Height * scale),
            corner * scale,
            Smooth(open / TileWholeUntil));

        // The card's own shadow comes as the icon's goes, the one for the other: never neither.
        var own = Smooth(open / ShadowsCrossBy);
        shadow.Opacity = ShadowAsIcon.Opacity + ((ShadowOpen.Opacity - ShadowAsIcon.Opacity) * own);
        shadow.BlurRadius = ShadowAsIcon.Blur + ((ShadowOpen.Blur - ShadowAsIcon.Blur) * open);
        shadow.ShadowDepth = ShadowAsIcon.Depth + ((ShadowOpen.Depth - ShadowAsIcon.Depth) * open);
        DrawIconShadow(place, scale, 1 - own);

        if (_backdrop is { } backdrop)
        {
            backdrop.Opacity = Smooth(open / TileWholeUntil);
        }

        var tile = 1 - Smooth((open - TileWholeUntil) / (TileGoneBy - TileWholeUntil));
        picture.Opacity = tile;
        face.Opacity = tile;
        content.Opacity = Smooth((open - ContentFrom) / (ContentWholeBy - ContentFrom));

        // The tile's layers on the window as it is at the icon's size: the four icons where the
        // picture on the dock has them, and the face shaded across it as the tile's is across
        // itself. Laid out again only when the window has changed, which is when the icon has.
        if (tile > 0 && !Near(window, _tileLaidFor))
        {
            _tileLaidFor = window;
            var room = window.Width * GroupArt.ArtRoom;
            Canvas.SetLeft(picture, window.Left - room - card.BorderThickness.Left);
            Canvas.SetTop(picture, window.Top - room - card.BorderThickness.Top);
            picture.Width = window.Width + (2 * room);
            picture.Height = window.Height + (2 * room);
            _faceBrush.StartPoint = new Point(0, window.Top - card.BorderThickness.Top);
            _faceBrush.EndPoint = new Point(0, window.Bottom - card.BorderThickness.Top);
        }
    }

    /// <summary>
    /// Draws the icon's shadow under the card, that much of it: about the card's window as it is
    /// drawn on this frame, as the dock draws it about the icon — the tile standing in from the
    /// icon's box as it does in the picture on the dock.
    /// </summary>
    private void DrawIconShadow(Rect place, double scale, double amount)
    {
        if (_iconShadow is not { } image)
        {
            return;
        }

        var opacity = _iconShadowCast is null ? 0 : amount * _ownShadow;
        image.Opacity = opacity;
        if (opacity <= 0 || _iconShadowCast is not { } cast)
        {
            return;
        }

        var window = new Rect(
            place.Left + _shift.X + (_clip.Rect.Left * scale),
            place.Top + _shift.Y + (_clip.Rect.Top * scale),
            _clip.Rect.Width * scale,
            _clip.Rect.Height * scale);
        window.Inflate(window.Width * GroupArt.ArtRoom, window.Height * GroupArt.ArtRoom);
        var at = cast.Around(window);
        _iconShadowPlace.Matrix = new Matrix(at.Width / ShadowSide, 0, 0, at.Height / ShadowSide, at.Left, at.Top);
    }

    /// <summary>
    /// Tells the dock how much of the icon's shadow is its own to draw: what the flyout does not —
    /// all of it until the icon has lifted, none while the card is out, and more of it again as
    /// the card, landed, fades off the icon.
    /// </summary>
    private void TellDockShadow() =>
        LeaveShadowOnDock(1 - (_ownShadow * (_landed ? _root?.Opacity ?? 0 : 1)));

    /// <summary>Tells the dock how much of the icon's shadow to draw, when that has changed.</summary>
    private void LeaveShadowOnDock(double amount)
    {
        if (amount == _shadowOnDock)
        {
            return;
        }

        _shadowOnDock = amount;
        ShadowOnDock?.Invoke(amount);
    }

    private static double Smooth(double value)
    {
        var x = Math.Clamp(value, 0, 1);
        return x * x * (3 - (2 * x));
    }

    private static bool Near(Rect a, Rect b) =>
        !a.IsEmpty && !b.IsEmpty
        && Math.Abs(a.Left - b.Left) < 0.25 && Math.Abs(a.Top - b.Top) < 0.25
        && Math.Abs(a.Width - b.Width) < 0.25 && Math.Abs(a.Height - b.Height) < 0.25;

    /// <summary>Makes every tile but <paramref name="except"/> deaf to the pointer, or all of them hear it again.</summary>
    private void SetOtherTilesDeaf(UIElement? except, bool deaf)
    {
        if (_content is not { } content)
        {
            return;
        }

        foreach (var tile in Tiles(content))
        {
            if (!ReferenceEquals(tile, except))
            {
                tile.IsHitTestVisible = !deaf;
            }
        }
    }

    private static IEnumerable<Button> Tiles(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button)
            {
                yield return button;
                continue;
            }

            foreach (var inner in Tiles(child))
            {
                yield return inner;
            }
        }
    }

    /// <summary>Opened all the way: the card as it is, with nothing left over it.</summary>
    private void Settle()
    {
        if (_closing || _card is not { } card || _content is not { } content || _picture is not { } picture)
        {
            return;
        }

        _opening = false;
        StopStepping();
        _openness = 1;
        _ownShadow = 1;
        _takingShadow = false;
        LeaveShadowOnDock(0);

        if (_iconShadow is { } iconShadow)
        {
            iconShadow.Opacity = 0;
        }

        // Its own ground again, rounded with it, and the layer that stood in for it gone.
        SetGround(card);
        if (_backdrop is { } backdrop)
        {
            backdrop.Visibility = Visibility.Collapsed;
        }

        if (_face is { } face)
        {
            face.Opacity = 0;
            face.Visibility = Visibility.Collapsed;
        }

        if (_shadow is { } shadow)
        {
            shadow.Opacity = ShadowOpen.Opacity;
            shadow.BlurRadius = ShadowOpen.Blur;
            shadow.ShadowDepth = ShadowOpen.Depth;
        }

        _scale.ScaleX = _scale.ScaleY = 1;
        _shift.X = _shift.Y = 0;
        content.Opacity = 1;
        picture.Opacity = 0;
        picture.Visibility = Visibility.Collapsed;
        card.Clip = null;
        card.RenderTransform = Transform.Identity;
        DrawSheetAtRest();
    }

    /// <summary>
    /// Lets a click elsewhere close it, as a menu does, giving up closing back into the icon: for
    /// when the window the menus hang off did not get the foreground, and would never hear of the
    /// click.
    /// </summary>
    public void CloseOnClickElsewhere()
    {
        if (_popup.IsOpen && !_closing)
        {
            _popup.StaysOpen = false;
        }
    }

    /// <summary>
    /// The drag out of it is over: its tile comes back, and a click elsewhere closes it again.
    /// </summary>
    public void EndDrag()
    {
        _draggedOut = false;
        SetOtherTilesDeaf(null, deaf: false);

        if (_draggedTile is { } tile)
        {
            tile.Opacity = 1;
            _draggedTile = null;
        }

        // Setting it back while open takes the mouse again, as after an item's menu.
        HoldOpen(false);
    }

    /// <summary>
    /// Where it stands on screen, in device pixels — its own room around the card included —
    /// or empty while it is not open.
    /// </summary>
    public Rect ScreenBounds
    {
        get
        {
            if (!_popup.IsOpen || _root is not { } root || PresentationSource.FromVisual(root) is null)
            {
                return Rect.Empty;
            }

            return new Rect(
                root.PointToScreen(new Point(0, 0)),
                root.PointToScreen(new Point(root.ActualWidth, root.ActualHeight)));
        }
    }

    /// <summary>
    /// Shows an item's menu over the tile it was asked for on, keeping the flyout open under it
    /// while it is up, and closing on a click elsewhere again once it has gone.
    /// </summary>
    public void ShowMenu(ContextMenu menu, UIElement tile)
    {
        if (!_popup.IsOpen)
        {
            return;
        }

        HoldOpen(true);
        menu.PlacementTarget = tile;
        menu.FlowDirection = Localizer.FlowDirection;

        // Asked for from the keys, under the tile and with its first entry ready for the arrows;
        // from the mouse, at the pointer.
        if (_menuFromKeys)
        {
            _menuFromKeys = false;
            menu.Placement = PlacementMode.Bottom;
            menu.Opened += (_, _) => menu.Dispatcher.InvokeAsync(
                () => menu.Items.OfType<MenuItem>().FirstOrDefault(entry => entry.IsEnabled)?.Focus(),
                System.Windows.Threading.DispatcherPriority.Input);
        }
        else
        {
            menu.Placement = PlacementMode.MousePoint;
        }

        menu.Closed += (_, _) =>
        {
            // Setting it back while open takes the mouse again, so a click elsewhere closes it
            // as before.
            HoldOpen(false);
        };
        menu.IsOpen = true;
    }

    /// <summary>
    /// Shows the group as it is now, in place: what it holds, after a change made from an item's
    /// menu while it was open. Not while it is being named, which nothing else changes.
    /// </summary>
    public void Refresh(DockItem group)
    {
        if (_nameBox is not null || !_popup.IsOpen)
        {
            return;
        }

        // On its way down, it simply goes; on its way up, the rebuilt card stands open.
        if (_closing)
        {
            CloseNow();
            return;
        }

        var midway = _opening;
        if (_opening)
        {
            _opening = false;
            StopStepping();
            _openness = 1;
            _ownShadow = 1;
            _takingShadow = false;
            LeaveShadowOnDock(0);
        }

        // The card as it stands, to go from to its new size; none mid-way, when it stands open at once.
        var wasOpen = !midway;
        var from = wasOpen && _card is { ActualWidth: > 0 } was ? new Size(was.ActualWidth, was.ActualHeight) : Size.Empty;
        var holderWas = new Size(_holder.ActualWidth, _holder.ActualHeight);
        StopResizing();
        _holder.ClearValue(FrameworkElement.MinWidthProperty);
        _holder.ClearValue(FrameworkElement.MinHeightProperty);

        Group = group;
        FirstTile = null;
        _holder.Child = Build(group, naming: false);
        ResizeFrom(from, holderWas);

        // The keys were in what was just replaced: back to the first icon, so Escape and the
        // arrows go on working.
        _popup.Dispatcher.InvokeAsync(FocusContent, System.Windows.Threading.DispatcherPriority.Input);
    }

    private Button? FirstTile { get; set; }

    /// <summary>Shows the group's name as its title: clicked, it can be changed (<see cref="BeginRename"/>).</summary>
    private void ShowTitle(string label)
    {
        _nameBox = null;
        var title = new TextBlock
        {
            Text = label,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 2, 8, 8),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = MostColumns * TileWidth
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

        // As Start renames a folder: a click on its name. The pointer and a tip say so.
        if (_canEdit)
        {
            title.Cursor = Cursors.IBeam;
            title.ToolTip = Localizer.Get("Group.Rename");
            title.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                BeginRename();
            };
        }

        _titleHost.Content = title;
    }

    /// <summary>
    /// Puts the theme's own text box in the title's place and lettering, the name selected, to be
    /// typed over. Enter keeps the name and leaves the flyout open; Escape puts the name back; a
    /// click elsewhere keeps it as the flyout goes.
    /// </summary>
    private void BeginRename()
    {
        if (!_canEdit || _nameBox is not null)
        {
            return;
        }

        var box = new TextBox
        {
            Text = Group.Label,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 0, 4, 8),
            MinWidth = 2 * TileWidth,
            MaxWidth = MostColumns * TileWidth,
            MaxLength = 64
        };
        AutomationProperties.SetName(box, Localizer.Get("Group.Name"));
        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return)
            {
                e.Handled = true;
                CommitRename();
            }
        };

        _nameBox = box;
        _titleHost.Content = box;

        // Not yet there for a group opened to be named, which is built before its popup; the
        // popup's opening focuses the box then.
        if (_popup?.IsOpen == true)
        {
            _popup.Dispatcher.InvokeAsync(() => FocusNameBox(box), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    /// <summary>Keeps the name typed, shown as the title from now on, and hands it on to be saved.</summary>
    private void CommitRename()
    {
        if (_nameBox is not { } box)
        {
            return;
        }

        var name = box.Text.Trim();
        var keep = name.Length > 0 && name != Group.Label;
        ShowTitle(keep ? name : Group.Label);
        FocusContent();
        if (keep)
        {
            Renamed?.Invoke(this, name);
        }
    }

    /// <summary>Puts the name back as it was.</summary>
    private void CancelRename()
    {
        ShowTitle(Group.Label);
        FocusContent();
    }

    private static void FocusNameBox(TextBox box)
    {
        box.Focus();
        Keyboard.Focus(box);
        box.SelectAll();
    }

    /// <summary>The keys to the first icon, or to the flyout itself when it holds none, so Escape still reaches it.</summary>
    private void FocusContent()
    {
        if (FirstTile is { } tile)
        {
            tile.Focus();
        }
        else if (_root is { } root)
        {
            root.Focus();
        }
    }

    private UIElement Build(DockItem group, bool naming)
    {
        var content = new StackPanel();

        if (_titleHost.Parent is Panel old)
        {
            old.Children.Remove(_titleHost);
        }

        content.Children.Add(_titleHost);
        ShowTitle(group.Label);
        if (naming)
        {
            BeginRename();
        }

        if (group.Children.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = Localizer.Get("Group.Empty"),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 2.5 * TileWidth,
                Margin = new Thickness(8, 0, 8, 6)
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            content.Children.Add(hint);
        }
        else
        {
            var columns = ColumnsFor(group.Children.Count);
            var grid = new UniformGrid { Columns = columns };
            foreach (var child in group.Children)
            {
                var tile = Tile(child);
                FirstTile ??= tile;
                grid.Children.Add(tile);
            }

            // Past a few rows, a scrolling list of them rather than a card taller than the screen.
            var rows = (int)Math.Ceiling(group.Children.Count / (double)columns);
            if (rows > MostRows && FirstTile is { } first)
            {
                first.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var row = first.DesiredSize.Height > 0 ? first.DesiredSize.Height : 100;

                // The scroll bar in a lane of its own, between the icons and the card's edge.
                // Windows draws it over the right of what scrolls, which put it on the last
                // column's tiles, hard against their icons; so the icons stop short of it, and
                // the scrolling part reaches out into the card's own padding to carry it there.
                grid.Margin = new Thickness(0, 0, ScrollBarLane, 0);
                content.Children.Add(new ScrollViewer
                {
                    Content = grid,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    PanningMode = PanningMode.VerticalOnly,
                    Focusable = false,
                    MaxHeight = row * MostRows,
                    Margin = new Thickness(0, 0, -ScrollBarReach, 0)
                });
            }
            else
            {
                content.Children.Add(grid);
            }
        }

        // The tile, as layers of the card for while it is the icon's size: the tile's pale face
        // under what the card holds, and the group's four icons over it, where the picture on the
        // dock has them. Not the picture itself, tile and all: grown with the card, its tile was a
        // rounder, outlined square inside the card's own — a tile within a tile, seen frame by
        // frame on 2026-11-21.
        content.Margin = new Thickness(14, 16, 14, 14);
        var face = new Border
        {
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Opacity = 0
        };
        var picture = new Image
        {
            Source = GroupArt.IconsOnly([.. group.Children.Select(child => child.Icon)]),
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Opacity = 0
        };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);

        // On a canvas, which asks for no room: the picture is sized to the card, never the card
        // to the picture — a bitmap's own size would otherwise stretch the card as it shows.
        var stage = new Canvas { Children = { picture } };

        // The card's ground, for the way (_backdrop): at rest the card has it as its own.
        var backdrop = new Border { Visibility = Visibility.Collapsed };
        SetGround(backdrop);
        _backdrop = backdrop;
        var body = new Grid { Children = { backdrop, face, content, stage } };

        var card = new Border
        {
            Child = body,
            RenderTransformOrigin = new Point(0, 0)
        };
        SetGround(card);
        card.SetResourceReference(Border.BorderBrushProperty, "ContextMenuBorderBrush");
        card.SetResourceReference(Border.BorderThicknessProperty, "ContextMenuBorderThickness");
        card.SetResourceReference(Border.CornerRadiusProperty, "OverlayCornerRadius");
        _card = card;
        _content = content;
        _picture = picture;

        // The card kept where it stands as it changes size standing open — the name being typed
        // — and the blur kept to it. Once the flyout's window has taken its new size, which is
        // after this; and not on the way up or down, when it is drawn with the card every frame.
        card.SizeChanged += (_, _) => card.Dispatcher.BeginInvoke(
            () =>
            {
                if (!_stepping && !_resizing && _popup?.IsOpen == true && ReferenceEquals(card, _card))
                {
                    PlaceAgain();
                    KeepSheetUnder();
                    DrawSheetAtRest();
                }
            },
            System.Windows.Threading.DispatcherPriority.Background);
        _face = face;

        // The shadow is the frame's, cast by the card as it is drawn — clipped and moved — so it
        // follows the card all the way.
        var shadow = new DropShadowEffect
        {
            BlurRadius = ShadowOpen.Blur,
            ShadowDepth = ShadowOpen.Depth,
            Direction = 270,
            Opacity = ShadowOpen.Opacity
        };
        var frame = new Border
        {
            Padding = ShadowRoom,
            Background = Brushes.Transparent,
            Child = card,
            Effect = shadow
        };
        _cardFrame = frame;
        _shadow = shadow;

        // Under the card, the icon's room, which it opens out of. The flyout's window covers it,
        // and Windows hands it the clicks there rather than to the dock beneath; so all of the
        // flyout that is not the card takes a click, and says what it was on: the group's own place
        // on the dock, or somewhere else (OnClickBesideCard).
        var root = new Grid
        {
            Focusable = true,
            FocusVisualStyle = null,
            Background = Morphs ? Brushes.Transparent : null,
            RowDefinitions = { new RowDefinition(), new RowDefinition { Height = new GridLength(_morphIcon) } }
        };
        frame.HorizontalAlignment = HorizontalAlignment.Center;
        frame.VerticalAlignment = VerticalAlignment.Bottom;
        if (Morphs)
        {
            // Under the card, the icon's shadow for while the card is the icon (DrawIconShadow).
            var iconShadow = new Image
            {
                Width = ShadowSide,
                Height = ShadowSide,
                Stretch = Stretch.Fill,
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransform = _iconShadowPlace,
                Source = _iconShadowCast?.Image
            };
            var under = new Canvas { IsHitTestVisible = false, Children = { iconShadow } };
            Grid.SetRowSpan(under, 2);
            root.Children.Add(under);
            _iconShadow = iconShadow;
        }

        root.Children.Add(frame);
        if (Morphs)
        {
            root.MouseDown += (_, e) => OnClickBesideCard(root, e);
        }

        // Rebuilt in place, it keeps the icon's corners it opened with.
        if (_corner > 0)
        {
            root.Resources["OverlayCornerRadius"] = new CornerRadius(_corner);
        }
        root.PreviewKeyDown += (_, e) =>
        {
            // Escape during a drag out is the drag's to answer: it calls the drag off, and the
            // group stays open. While the name is being typed it puts the name back; otherwise it
            // closes the flyout.
            if (e.Key != Key.Escape || _draggedOut)
            {
                return;
            }

            e.Handled = true;
            if (_nameBox is not null)
            {
                CancelRename();
                return;
            }

            _cancelled = true;
            Close();
        };
        _root = root;

        // A click on the card while it is still on its way, up or down, turns it round — a second
        // click on the group, landing on the card that has just left it.
        root.PreviewMouseDown += (_, e) =>
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            if (!Morphs || e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            if (_opening)
            {
                e.Handled = true;
                Close();
            }
            else if (_closing)
            {
                e.Handled = true;
                Reopen();
            }
        };

        // Mid-way out of the icon, a rebuilt card — a change made from an item's menu — stands as
        // it is, open.
        _scale.ScaleX = _scale.ScaleY = 1;
        _shift.X = _shift.Y = 0;

        // A popup is a tree of its own and inherits nothing from the windows around it, so it
        // is given the language's reading direction here, as a menu is (MenuHost).
        root.FlowDirection = Localizer.FlowDirection;
        AutomationProperties.SetName(root, group.Label);
        if (!Morphs || _morphReach <= 0)
        {
            return root;
        }

        // Room at the sides to follow the icon into. Closing, the card goes to the icon wherever
        // the wave has carried it, and under a narrow card that can be past the edge of the
        // flyout's own window, which would cut the card off there. Nothing is drawn in the room
        // and nothing in it takes a click, so a click there goes to the dock under it as before.
        root.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border
        {
            MinWidth = 2 * (_morphReach + (_morphIcon / 2) + ShadowRoom.Left),
            Child = root
        };
    }

    /// <summary>
    /// Builds a flyout once and lays it out, out of sight, so the first group opened is not the
    /// one that waits for its code to be compiled, its tiles' template read and the theme's
    /// styles for its parts found.
    /// </summary>
    /// <param name="blurred">Whether groups open on a blur, and the sheet for it is wanted too.</param>
    public static void WarmUp(bool blurred = false)
    {
        var child = new DockItem { Id = "warm-up-item", Label = "Item" };
        var group = new DockItem { Id = "warm-up", Label = "Group", IsGroup = true, Children = [child] };
        var flyout = new GroupFlyout(group, canEdit: true, morphIcon: 48, blurred: blurred);
        if (flyout._popup.Child is FrameworkElement root)
        {
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            root.Arrange(new Rect(root.DesiredSize));
        }

        _ = MorphFrom(new Rect(0, 0, 100, 140), new Rect(10, 10, 20, 20));
    }

    /// <summary>How many icons to a row: near square for a few, as many as fit for more.</summary>
    public static int ColumnsFor(int count) =>
        Math.Clamp((int)Math.Ceiling(Math.Sqrt(Math.Max(1, count))), 1, MostColumns);

    private Button Tile(DockItem item)
    {
        var icon = new Image { Source = item.Icon, Width = IconSize, Height = IconSize };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

        var name = new TextBlock
        {
            Text = item.Label,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 34,
            Margin = new Thickness(0, 4, 0, 0)
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

        // The dock's own dot under the icon, white, amber or hollow, while it has one.
        var dot = new RunningDot(item)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = RunningDot.Has(item) ? Visibility.Visible : Visibility.Hidden
        };

        // An item that cannot be opened — its program gone — is drawn faded, as on the dock, and
        // a click on it does nothing. It is not disabled: a disabled tile takes no mouse at all,
        // and its menu and dragging it out are how it is taken off.
        var face = new StackPanel { Children = { icon, dot, name } };
        if (!item.IsLaunchable)
        {
            face.Opacity = UnopenableOpacity;
        }

        var tile = new Button
        {
            Template = TileTemplate.Value,
            Width = TileWidth,
            Margin = new Thickness(2),
            Padding = new Thickness(4, 10, 4, 8),
            Content = face
        };
        AutomationProperties.SetName(tile, item.Label);

        // Into sight when the keys reach it, in a group that scrolls.
        tile.GotKeyboardFocus += (_, _) => tile.BringIntoView();

        tile.Click += (_, _) =>
        {
            if (item.IsLaunchable)
            {
                Choose(item, DockClicks.Of(
                    middle: false,
                    control: Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
                    shift: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)));
            }
        };

        tile.MouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                e.Handled = true;
                if (item.IsLaunchable)
                {
                    Choose(item, DockClick.NewWindow);
                }
            }
        };

        tile.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            MenuAsked?.Invoke(this, (item, tile));
        };

        // The menu key, and Shift+F10, ask for the same menu as a right-click — under the tile.
        tile.KeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Apps || (key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
            {
                e.Handled = true;
                _menuFromKeys = true;
                MenuAsked?.Invoke(this, (item, tile));
            }
        };

        // Pressed and pulled further than a click wanders: out of the group, by the dock.
        tile.PreviewMouseLeftButtonDown += (_, e) => _press = (item, e.GetPosition(null));
        tile.PreviewMouseLeftButtonUp += (_, _) => _press = null;
        tile.PreviewMouseMove += (_, e) =>
        {
            if (!_canEdit || _press is not { } press || !ReferenceEquals(press.Item, item)
                || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var moved = e.GetPosition(null) - press.At;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance * 2
                && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance * 2)
            {
                return;
            }

            _press = null;
            e.Handled = true;
            var bounds = ScreenBounds;

            // Held open under the drag, which does not keep the mouse in it. The tile took the
            // mouse as it was pressed: it lets go of it, and of the click it would make when the
            // button comes up, and stands faded where the icon was.
            _draggedOut = true;
            _draggedTile = tile;
            tile.Opacity = DraggedTileOpacity;
            HoldOpen(true);

            // The other tiles deaf to the pointer while the drag lasts. They lit up under the
            // pointer passing over them, and each one redrew the whole flyout — shadow and all, a
            // layered window, so drawn the slow way — and held up the icon being carried, which
            // stuttered for as long as it was over the flyout. Not this tile, nor the flyout itself:
            // made deaf, the tile holding the mouse lost it, and the drag with it.
            SetOtherTilesDeaf(tile, deaf: true);

            tile.ReleaseMouseCapture();
            _popup.Dispatcher.BeginInvoke(
                () => DragOut?.Invoke(this, (item, bounds)), System.Windows.Threading.DispatcherPriority.Input);
        };

        return tile;
    }

    /// <summary>Closes, and then hands on the click — once the popup has gone, as a menu's entry runs once its menu has.</summary>
    private void Choose(DockItem item, DockClick click)
    {
        // An item dragged out is not clicked as well, when the button comes up.
        if (_draggedOut || _closing)
        {
            return;
        }

        // Started at once, the group closing back into its icon as it starts.
        Close();
        _popup.Dispatcher.BeginInvoke(() => Chosen?.Invoke(this, (item, click)));
    }

    /// <summary>
    /// A tile: the icon and its name on nothing, lit in the theme's subtle fills under the pointer
    /// and while pressed, as a flyout's entries are — rounded as the open group's card is, so the
    /// lit tile echoes the card it stands on.
    /// </summary>
    private static readonly Lazy<ControlTemplate> TileTemplate = new(() => (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            TargetType="Button">
            <Border
                x:Name="Face"
                Background="Transparent"
                CornerRadius="{DynamicResource OverlayCornerRadius}"
                Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" />
            </Border>
            <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="Face" Property="Background" Value="{DynamicResource SubtleFillColorSecondaryBrush}" />
                </Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True">
                    <Setter TargetName="Face" Property="Background" Value="{DynamicResource SubtleFillColorSecondaryBrush}" />
                </Trigger>
                <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="Face" Property="Background" Value="{DynamicResource SubtleFillColorTertiaryBrush}" />
                </Trigger>
                <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.45" />
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>
        """));
}
