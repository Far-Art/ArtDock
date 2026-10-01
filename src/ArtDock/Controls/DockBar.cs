using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArtDock.Dock;
using ArtDock.Interop;

namespace ArtDock.Controls;

/// <summary>
/// The dock's rendering surface: paints the translucent bar, its icons' tooltips and
/// running-app indicators, and drives the magnification wave.
/// </summary>
/// <remarks>
/// <para>
/// Per frame this control touches only render state — each icon's scale and translate
/// transforms, plus a redraw of the bar. Nothing invalidates layout, so magnification never
/// runs a measure or arrange pass. That is the design the web original filed under "Tier 3,
/// previously rejected as too complex"; with positions computed analytically rather than
/// inherited from flexbox, it is simply the direct way to do it.
/// </para>
/// <para>
/// The wave is driven by polling the cursor rather than by mouse events, because the window
/// is transparent and hit-tests only where the dock draws — mouse events cut out over the
/// gaps between magnified icons, and the wave would collapse and re-form as the pointer
/// crossed one.
/// </para>
/// </remarks>
public sealed class DockBar : Canvas
{
    private const double EnterRampMs = 140;
    private const double LeaveRampMs = 200;

    /// <summary>
    /// How long the launch flash runs, covering both of its pulses.
    /// </summary>
    /// <remarks>
    /// Slow enough that each pulse reads as the icon breathing rather than as a glitch.
    /// At 620ms — where this started — the two pulses were quick enough to look like a
    /// dropped frame, which is the opposite of the reassurance the flash exists to give.
    /// </remarks>
    private const double FlashDurationMs = 1150;

    /// <summary>
    /// The least headroom kept above the bar for the tooltip, whatever its label measures.
    /// </summary>
    private const double MinTooltipReserve = 34;

    /// <summary>Gap between an icon's top and the bubble above it.</summary>
    private const double TooltipGap = 8;

    /// <summary>Padding inside the tooltip bubble.</summary>
    private const double TooltipPadX = 9;
    private const double TooltipPadY = 4;

    /// <summary>
    /// Headroom actually reserved above the bar, measured from the labels in the dock.
    /// </summary>
    /// <remarks>
    /// It used to be a flat 34px, which is about what a 12pt label needs and nothing more.
    /// A fully magnified icon reaches exactly the top of that reserve, so the bubble sat
    /// against the window's edge and the window clips its own content — anything taller
    /// than the default label lost its top edge. Sizing the reserve to the labels present
    /// means the window grows to fit them instead.
    /// </remarks>
    private double _tooltipReserve = MinTooltipReserve;

    /// <summary>
    /// Slack on each side of the widest the bar ever gets. Without it the bar's rounded caps
    /// sit exactly on the window edge at peak magnification, leaving nowhere for the drop
    /// shadow to fall.
    /// </summary>
    private const double HorizontalSlack = 24;

    // ---- shadow ---------------------------------------------------------------

    /// <summary>One brush per pass of <see cref="BarShadow"/>, which says what the shadow is.</summary>
    private static readonly Brush[] ShadowBrushes =
        [.. BarShadow.Passes.Select(pass => Frozen(Color.FromArgb(pass.Alpha, 0x00, 0x00, 0x00)))];

    /// <summary>
    /// Room below the bar for the shadow to fall into. The window clips its own content, so
    /// with the bar flush against the window's bottom edge the shadow is sliced off in a hard
    /// flat line — and so does the acrylic sheet, which is the same size as the window and
    /// draws the shadow itself while the blur is on.
    /// </summary>
    private static readonly double ShadowSlack = BarShadow.Reach;

    /// <summary>The bar's rim: a pixel of white drawn over its fill, just inside its edge.</summary>
    public static Color BarBorder { get; } = Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF);

    private static readonly Brush BarBorderBrush = Frozen(BarBorder);

    /// <summary>
    /// What stands in for the bar while the sheet behind it draws it: a single step of alpha,
    /// which no eye can tell from nothing and Windows can — see <see cref="DrawsBar"/>.
    /// </summary>
    private static readonly Brush StandInBrush = Frozen(Color.FromArgb(0x01, 0x00, 0x00, 0x00));
    private static readonly Brush DotBrush = Frozen(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));

    /// <summary>The label bubble. Keep it all but opaque: the labels are ClearType, which
    /// is blended against this colour, and any of the desktop showing through would show
    /// through with coloured fringes.</summary>
    private static readonly Brush TooltipFill = Frozen(Color.FromArgb(0xF0, 0x14, 0x16, 0x20));
    private static readonly Brush TooltipText = Frozen(Colors.White);

    private readonly Pen _barBorder;
    private readonly List<DockItemVisual> _items = [];

    private Color _barColor = BarPalette.Default;
    private double _barOpacity = 0.76;
    private Brush _barFill = Frozen(Color.FromArgb(0x80, 0xCC, 0xD2, 0xFF));

    /// <summary>The bar's fill as it is painted: its colour, at its opacity.</summary>
    public Color BarFill => ((SolidColorBrush)_barFill).Color;

    private DockLayout _layout;

    /// <summary>Pointer position in the resting frame; null when the pointer is away.</summary>
    /// <remarks>
    /// Where the wave's driver wants it, which is not quite where the wave is: see
    /// <see cref="WaveCentre"/> and <see cref="_handoverLag"/>.
    /// </remarks>
    private double? _pointer;

    /// <summary>What the wave can be following.</summary>
    private enum WaveDriver
    {
        /// <summary>Nothing: the pointer is away and no preview is holding it.</summary>
        None,

        /// <summary>The cursor, whether it is hovering or dragging an icon.</summary>
        Pointer,

        /// <summary>The settings dialog's demonstration, walking the wave across the dock.</summary>
        Sweep,

        /// <summary>An item held for a menu, an edit dialog, or the dialog's preview.</summary>
        Parked
    }

    /// <summary>What set <see cref="_pointer"/> last.</summary>
    /// <remarks>
    /// Only so that a change of hands can be told from the same hand moving. The wave has
    /// several possible drivers and they take it from each other at unrelated moments —
    /// the pointer arriving on a dock the settings dialog is demonstrating, and leaving it
    /// again, being the pair the user actually watches.
    /// </remarks>
    private WaveDriver _driver;

    /// <summary>
    /// How far the wave is still behind where its driver wants it, easing to zero.
    /// </summary>
    /// <remarks>
    /// A driver taking over used to move the wave to its own position on the frame it took
    /// it, which is a teleport across however much dock lay between the two: pointing at a
    /// dock the dialog was demonstrating snapped the wave to the cursor from wherever the
    /// sweep had got to, and taking the cursor away snapped it back to the middle icon.
    ///
    /// Carried as a lag rather than as an eased position on purpose. The wave still moves
    /// with the cursor exactly, one pixel per pixel, while the gap left by the handover
    /// closes underneath — so the smoothing is never felt as the dock lagging the pointer,
    /// which is the one thing it must not do.
    /// </remarks>
    private double _handoverLag;

    /// <summary>
    /// Time constant for closing that gap.
    /// </summary>
    /// <remarks>
    /// A little slower than the slot slide, because a handover can be the whole width of the
    /// dock rather than one pitch, and the same rate over that distance reads as a snap.
    /// </remarks>
    private const double HandoverEaseMs = 70;

    /// <summary>
    /// Where the wave is actually centred: its driver's position, less whatever is left of
    /// the last handover. Null when nothing is driving it.
    /// </summary>
    private double? WaveCentre => _pointer is { } centre ? centre + _handoverLag : null;

    /// <summary>
    /// Moves the wave's centre, easing the move if the wave is changing hands — or when asked
    /// to, for a parked wave sent on to another icon, which nobody's hand is moving either.
    /// </summary>
    private void SetWaveCentre(double centre, WaveDriver driver, bool ease = false)
    {
        if (driver != _driver || ease)
        {
            // Keep the wave where it is being drawn and let the gap close on its own.
            _handoverLag = WaveCentre is { } drawn ? drawn - centre : 0;
            _driver = driver;
        }

        _pointer = centre;
    }

    /// <summary>Takes the wave away from whatever was driving it.</summary>
    private void ReleaseWaveCentre()
    {
        _pointer = null;
        _handoverLag = 0;
        _driver = WaveDriver.None;
    }

    /// <summary>Closes what is left of a handover.</summary>
    private void AdvanceHandover(double deltaMs)
    {
        if (_handoverLag == 0 || deltaMs <= 0)
        {
            return;
        }

        _handoverLag *= Math.Exp(-deltaMs / HandoverEaseMs);

        // Arriving exactly matters: a lag left running is a wave that follows the cursor a
        // fraction of a pixel off for ever, and it would hold the render loop open.
        if (Math.Abs(_handoverLag) < 0.3)
        {
            _handoverLag = 0;
        }
    }

    /// <summary>Wave amplitude, 0 (flat) to 1 (full).</summary>
    private double _progress;

    private double _rampFrom;
    private double _rampTo;
    private double _rampElapsedMs;
    private double _rampDurationMs;
    private bool _rampRunning;

    private TimeSpan _lastFrame;
    private bool _renderingHooked;

    private int _hoveredIndex = -1;
    private int _pressedIndex = -1;

    /// <summary>Item currently playing the launch flash, or -1.</summary>
    private int _flashIndex = -1;
    private double _flashElapsedMs;

    // ---- drop preview ---------------------------------------------------------

    /// <summary>The dock's real contents, without any drop preview mixed in.</summary>
    private IReadOnlyList<DockItem> _pinnedItems = [];

    /// <summary>What a drop would add, shown faded in place until it lands.</summary>
    private IReadOnlyList<DockItem> _ghostItems = [];

    /// <summary>Where the preview sits in <see cref="_pinnedItems"/>, or -1 for none.</summary>
    private int _ghostIndex = -1;

    /// <summary>True while any icon is still sliding toward its slot.</summary>
    private bool _slotsMoving;

    /// <summary>True while an arriving icon is still growing into its slot.</summary>
    private bool _iconsGrowing;

    /// <summary>
    /// How many slots wide the bar is currently drawn, which lags the row's real count while
    /// the bar opens up for a new icon or closes over a departed one. Negative until the
    /// first row arrives, so a dock does not grow out of nothing on startup.
    /// </summary>
    private double _barSlots = -1;

    /// <summary>
    /// Time constant of that growth, in milliseconds.
    /// </summary>
    /// <remarks>
    /// The same as the slide between slots, deliberately: the bar and the icons inside it
    /// are one movement, and a bar that settled at a different rate from its contents reads
    /// as the two coming apart.
    /// </remarks>
    private const double BarGrowthEaseMs = 60;

    /// <summary>Below this, in slots, the bar has arrived at its width.</summary>
    private const double BarSlotEpsilon = 0.002;

    /// <summary>
    /// How many slots the bar is drawn for, which is never fewer than one.
    /// </summary>
    /// <remarks>
    /// An empty dock still draws a slot's worth of bar — see <see cref="EmptyBarWidth"/> —
    /// so the growth has to start from one slot rather than from nothing. Easing toward the
    /// item count instead made the bar collapse to a stub and swell back out when the first
    /// icon was added: the width came out as one slot minus a whole pitch, because the icon
    /// was being counted as opening a slot the bar was already drawn as having. Removing the
    /// last icon is the same in reverse — the bar stays where it is and the icon leaves it.
    /// </remarks>
    private int BarSlotTarget => Math.Max(1, _items.Count);

    /// <summary>True while the bar is still opening or closing over a change of contents.</summary>
    private bool BarIsResizing =>
        _barSlots >= 0 && Math.Abs(BarSlotTarget - _barSlots) > BarSlotEpsilon;

    /// <summary>Icon count the window was last sized for, so a reorder does not resize it.</summary>
    private int _sizedForCount = -1;

    // ---- drag to reorder ------------------------------------------

    /// <summary>
    /// How far the cursor must travel before a press becomes a drag.
    /// </summary>
    /// <remarks>
    /// Without a threshold every slightly shaky click would nudge the dock's order.
    /// </remarks>
    private const double DragThreshold = 8;

    /// <summary>Item being dragged, or -1.</summary>
    private int _dragIndex = -1;

    /// <summary>Slot the dragged item would land in.</summary>
    private int _dragTarget = -1;

    /// <summary>Where the press started, for measuring against the threshold.</summary>
    private double _pressX;

    private bool _dragging;

    /// <summary>
    /// The hovered item's label, laid out once and reused.
    /// </summary>
    /// <remarks>
    /// <see cref="FormattedText"/> runs full text layout in its constructor, and the tooltip
    /// is redrawn on every frame of the wave — so building it per frame means shaping the
    /// same handful of characters sixty times a second for no reason.
    /// </remarks>
    /// <remarks>
    /// Keyed rather than single-slot because the reserve above the bar is measured from
    /// every label in the dock, not only the one on screen — a one-entry cache would be
    /// thrown away and rebuilt on each of those measurements.
    /// </remarks>
    private readonly Dictionary<string, FormattedText> _tooltipLayouts = [];

    /// <summary>
    /// Item the dock is holding magnified, or -1 when the pointer is in charge.
    /// </summary>
    /// <remarks>
    /// Used whenever something outside the dock needs a particular icon held up for review:
    /// the settings dialog pins the item selected on its Items page, or the middle one, so its
    /// appearance sliders have something to act on, and the item menu and edit dialog pin the
    /// icon they were opened for, so it stays the subject of whatever the user does next.
    /// </remarks>
    private int _focusIndex = -1;

    /// <summary>Whether the focused item's label is pinned open alongside it.</summary>
    private bool _focusShowsLabel;

    /// <summary>
    /// True when the held item is the settings dialog's preview rather than a particular
    /// item someone is working on.
    /// </summary>
    /// <remarks>
    /// The two want opposite things when the contents change. A menu or edit dialog holds
    /// <em>that</em> item and must keep holding it; the preview holds <em>the middle</em>,
    /// and has to be re-centred or it drifts toward the end as items are removed — which
    /// narrows the bar, because an icon near the end has fewer neighbours to lift, and makes
    /// the dock look like it is resizing by the wrong amount each time. Or it holds the item
    /// selected in the dialog, which it has to find again wherever the change has put it.
    /// </remarks>
    private bool _focusIsPreview;

    /// <summary>Whether the settings dialog is open and showing the dock off.</summary>
    /// <remarks>
    /// What the dock goes back to when a menu or a dialog lets go of the item it was holding:
    /// the preview, rather than nothing. Without it, an icon's menu opened while the settings
    /// dialog was up left the dock flat once it closed, the demonstration gone until the
    /// dialog was opened again.
    /// </remarks>
    private bool _previewing;

    /// <summary>
    /// The item the settings dialog's preview holds up — the row selected on its Items page —
    /// or null for the middle icon.
    /// </summary>
    /// <remarks>
    /// Kept by id, not by index, and looked for again on every rebuild: the dialog previews each
    /// change to the list as it is made, so the item moves under it — and the dock may not have
    /// it yet at all, since a row added in the dialog is selected before the dock is told of it.
    /// </remarks>
    private string? _previewId;

    /// <summary>
    /// Whether the wave is being walked back and forth across the dock by itself.
    /// </summary>
    /// <remarks>
    /// Magnification and influence range describe the wave, and the wave only exists while
    /// a pointer is over the dock — so with the settings dialog holding the pointer's
    /// attention, both sliders looked dead. Sweeping shows what they do without asking the
    /// user to put the mouse back on the dock and lose sight of the slider.
    /// </remarks>
    private bool _sweeping;

    private double _sweepElapsedMs;

    /// <summary>
    /// True while the sweep has stood down because the pointer is on the dock, or because the
    /// settings dialog has an item selected for the preview to hold up instead.
    /// </summary>
    /// <remarks>
    /// The demonstration exists to show what the wave does when nobody is pointing at the
    /// dock. The moment somebody is, they outrank it — and it picks up again, from where
    /// they left off, when they go away. A selected item is somebody pointing by other means.
    /// </remarks>
    private bool _sweepYielded;

    /// <summary>
    /// One full there-and-back. Slow enough to follow, quick enough to show both ends of
    /// the dock while a slider is being adjusted.
    /// </summary>
    private const double SweepPeriodMs = 4200;

    /// <summary>
    /// Candidate styling for the focused item's label, or null to use the item's own.
    /// </summary>
    /// <remarks>
    /// Set while the edit dialog is open so the styling is judged on the dock itself, at the
    /// size and against the background it will actually be used at.
    /// </remarks>
    private DockItem? _labelStyle;

    /// <summary>
    /// The sizes last actually painted, so a frame that would change nothing can be skipped.
    /// </summary>
    /// <remarks>
    /// The per-frame loop keeps running whenever the wave is up — including while it is held
    /// still for the settings preview. Without this the dock repaints a layered window sixty
    /// times a second to produce an identical image, which is both wasted work and a drag on
    /// how quickly it responds to anything else.
    /// </remarks>
    private double[] _paintedSizes = [];

    /// <summary>Live geometry of the last rendered frame, reused for hit-testing and tooltips.</summary>
    /// <remarks>
    /// Indexed by item, which is not the same thing as indexed by slot while a drag is in
    /// flight — see <see cref="_displaySlots"/>. These are the slot arrays scattered into
    /// item order, because everything downstream of the wave asks about an item.
    /// </remarks>
    private double[] _sizes = [];
    private double[] _offsets = [];

    /// <summary>The same geometry in the order the row is drawn.</summary>
    private double[] _slotSizes = [];
    private double[] _slotOffsets = [];

    /// <summary>
    /// The slot each item occupies on screen.
    /// </summary>
    /// <remarks>
    /// Identity except during a reorder drag, when the row shows an order the model has not
    /// been told about yet. Keeping the two apart is what lets the wave be computed for the
    /// row as displayed while the item list stays untouched until the drop.
    /// </remarks>
    private int[] _displaySlots = [];
    private double _barLeft;
    private double _barWidth;

    /// <summary>
    /// Left edge of the icon block. The bar no longer wraps the icons tightly, so they are
    /// centred within it instead of defining it — see <see cref="DockLayout.SteadyBarWidth"/>.
    /// </summary>
    private double _iconsLeft;

    /// <summary>
    /// Watches for the cursor arriving over the dock.
    /// </summary>
    /// <remarks>
    /// A <c>MouseEnter</c> handler is not enough on its own: the window hit-tests only where
    /// the dock actually draws, so WPF's mouse events are unreliable at exactly the moments
    /// that matter. Polling costs one <c>GetCursorPos</c> per tick and never misses.
    /// </remarks>
    private readonly DispatcherTimer _cursorWatch = new()
    {
        Interval = TimeSpan.FromMilliseconds(32)
    };

    public DockBar(DockMetrics metrics)
    {
        _layout = new DockLayout(metrics);
        _barBorder = new Pen(BarBorderBrush, 1);
        _barBorder.Freeze();

        // No Background: WPF only hit-tests where something is drawn, so the empty space
        // above the bar stays click-through for the window underneath.
        Background = null;

        // Labels in ClearType, as Windows draws its own text. The dock window is
        // AllowsTransparency, where WPF turns ClearType off — subpixel coverage cannot be
        // composited against per-pixel alpha — and falls back to grayscale, which smooths
        // only horizontally: the tops and bottoms of every curve stay hard steps. The hint
        // puts ClearType back, and it is safe here because every word the dock draws sits
        // on a bubble (TooltipFill) all but opaque. The rendering mode is left alone, so the
        // system's own font smoothing still decides. Animated hinting was tried first: it
        // smooths both ways, but unhinted, and the labels came out soft and heavy.
        RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);

        // Ideal formatting places glyphs at their true sub-pixel positions instead of
        // snapping them to the pixel grid, which matters because the tooltip slides with
        // the wave: snapped text visibly steps as it moves.
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

        _cursorWatch.Tick += OnCursorWatchTick;
        Loaded += (_, _) => _cursorWatch.Start();
        Unloaded += (_, _) =>
        {
            _cursorWatch.Stop();
            UnhookRendering();
        };
    }

    /// <summary>Raised when an icon is activated by click.</summary>
    public event EventHandler<DockItem>? Activated;

    /// <summary>Raised when a drag has moved an icon to a new position.</summary>
    public event EventHandler<(int From, int To)>? Reordered;

    /// <summary>
    /// Raised when the window this dock needs has changed size — a drop preview appearing,
    /// or a label tall enough to want more headroom.
    /// </summary>
    public event EventHandler? PreferredSizeChanged;

    /// <summary>
    /// Raised when the drawn bar changes shape, so anything sitting under it can follow —
    /// with <see cref="RenderedBarRect"/>, as the frame after the one that drew it begins.
    /// </summary>
    /// <remarks>
    /// Not once per frame, despite the bar being redrawn that often: the raised-cosine
    /// falloff sums to a constant across the middle of the dock, so the bar's width holds
    /// steady once the wave is up and only moves during the entry and exit ramps, and a
    /// little at the ends. That is what makes it practical for a second window to track it —
    /// which, while the blur is on, is what draws the bar, so it is told of every change and
    /// not only of whole pixels: see <see cref="AnnounceDrawnBar"/>.
    /// </remarks>
    public event EventHandler<Rect>? BarRectChanged;

    /// <summary>
    /// Whether the bar draws its own shadow. Off when something behind it is casting a
    /// real one, since two shadows for one object is one too many.
    /// </summary>
    public bool DrawsShadow
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            InvalidateVisual();
        }
    } = true;

    /// <summary>
    /// Whether this control paints the bar — its fill, its rim and its shadow. Off while the
    /// acrylic sheet behind the dock paints them instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sheet draws the bar so that the bar's outline and the blur under it are one thing
    /// on screen. Drawn here, they were two: this window's frames reach the screen through
    /// WPF's render thread and <c>UpdateLayeredWindow</c>, the sheet's straight from this
    /// thread through the compositor, and measured frame by frame the blur's edge ran ahead of
    /// the bar's by one frame or two — a strip of blur past the bar's end while it grew, and of
    /// bar without blur while it shrank, plain to see over a white window.
    /// </para>
    /// <para>
    /// Off, the bar is not simply left out: a window with <c>AllowsTransparency</c> is
    /// hit-tested by the alpha of what it paints, and a bar that painted nothing would pass
    /// every click, right-click and drop through to whatever is behind — the lesson of the empty
    /// dock. So the area the bar and its shadow cover is painted with a single step of alpha,
    /// which keeps it the dock's without anyone being able to see it.
    /// </para>
    /// </remarks>
    public bool DrawsBar
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            InvalidateVisual();
        }
    } = true;

    /// <summary>The item under the cursor, or null if the cursor is not on one.</summary>
    public DockItem? HoveredItem =>
        _hoveredIndex >= 0 && _hoveredIndex < _items.Count ? _items[_hoveredIndex].Item : null;

    /// <summary>
    /// Whether icons magnify at all. Hover, tooltips and clicks keep working when this is
    /// off — only the wave's amplitude is suppressed, which is what "reduce motion" should
    /// mean for a dock.
    /// </summary>
    public bool MagnificationEnabled { get; set; } = true;

    /// <summary>
    /// Whether a drag along the bar may rearrange the icons.
    /// </summary>
    /// <remarks>
    /// Off, a press still presses and still activates — only its promotion into a drag is
    /// refused, so an icon held with an unsteady hand launches rather than doing nothing at
    /// all. The lock can also arrive mid-gesture, since the settings dialog previews its
    /// checkbox the moment it is ticked, so turning it off puts back a drag already in
    /// flight rather than leaving it to land in a slot that is no longer allowed.
    /// </remarks>
    public bool ReorderEnabled
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;

            if (!value)
            {
                CancelPress();
            }
        }
    } = true;

    /// <summary>
    /// The wave's amplitude this frame, 0 to 1.
    /// </summary>
    /// <remarks>
    /// Reduce-motion suppresses the amplitude only; <c>_progress</c> keeps ramping so
    /// tooltips and hover still behave normally. Said once because the geometry and the
    /// hover zone are both built on it, and a zone computed for a wave the dock is not
    /// drawing is a zone whose edges are somewhere else.
    /// </remarks>
    private double WaveAmplitude => MagnificationEnabled ? _progress : 0;

    public DockMetrics Metrics => _layout.Metrics;

    /// <summary>
    /// How far the bar's bottom edge sits above the window's, so the window can be positioned
    /// by where the bar appears rather than by its own bounds.
    /// </summary>
    public static double BottomInset => ShadowSlack;

    /// <summary>
    /// The least room the window keeps beside the widest the bar gets, at either end — the
    /// same use as <see cref="BottomInset"/>, along the edge rather than across it.
    /// </summary>
    public static double SideInset => HorizontalSlack;

    /// <summary>The bar's bottom edge, inset from the window's by the shadow's room.</summary>
    private double BarBottom => ActualHeight - ShadowSlack;

    /// <summary>
    /// Where the row sits across the window: 0 against its left, ½ centred, 1 against its
    /// right.
    /// </summary>
    /// <remarks>
    /// The dock's place along its edge, said inside the window. <c>DockWindow</c> slides the
    /// window the same fraction of the way along the screen, and aligning a box within a box
    /// composes (<see cref="DockLayout.Align"/>), so the row lands where the setting puts it
    /// whatever size the window happens to be. What it changes in here is which way the row
    /// grows when its width changes — see <see cref="DockLayout.RestingLeft"/>. The wave is
    /// untouched by it.
    /// </remarks>
    public double RowAlignment
    {
        get;
        set
        {
            var aligned = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.5;
            if (aligned == field)
            {
                return;
            }

            field = aligned;

            // What a change of metrics does, for the same reason: the row moves under a wave
            // that must not drop, and a held item has to be re-found where it now is.
            PinFocusPointer(claim: false);
            LayoutAtRest();
            InvalidateVisual();
        }
    } = 0.5;

    /// <summary>Where a resting row of the given width starts, in this element's coordinates.</summary>
    private double RowLeft(double restingWidth) =>
        _layout.RestingLeft(
            restingWidth, HorizontalSlack, ActualWidth - (2 * HorizontalSlack), RowAlignment);

    /// <summary>
    /// The part of this element that is on the screen, left to right, in its own coordinates.
    /// </summary>
    /// <remarks>
    /// Labels are kept inside it as well as inside the window. The two were the same thing
    /// for as long as the dock was centred; one moved to the end of its edge has its window
    /// reaching past the side of the screen by the slack beside the bar, and a long name on
    /// the end icon was drawn partly off the screen — or onto the display next door.
    /// </remarks>
    public (double Left, double Right) OnScreen
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            InvalidateVisual();
        }
    } = (double.NegativeInfinity, double.PositiveInfinity);

    /// <summary>
    /// Where a bubble <paramref name="width"/> wide goes to be centred on
    /// <paramref name="centreX"/>, kept inside the window and on the screen.
    /// </summary>
    private double BubbleLeft(double centreX, double width)
    {
        var min = Math.Max(2, OnScreen.Left + 2);
        var max = Math.Min(ActualWidth, OnScreen.Right) - width - 2;

        // A bubble wider than the room has nowhere to go but the start of it, and Math.Clamp
        // throws when its bounds cross.
        return Math.Clamp(centreX - (width / 2), min, Math.Max(min, max));
    }

    /// <summary>
    /// Replaces the tuning values, re-using the existing icon visuals.
    /// </summary>
    /// <remarks>
    /// Deliberately does not go through <see cref="SetItems"/>. Rebuilding every visual for
    /// what is only a geometry change means discarding and re-creating seven elements on
    /// each tick of a slider, and that is what made the appearance settings feel laggy.
    /// </remarks>
    public void UpdateMetrics(DockMetrics metrics)
    {
        // Value equality on a record, so this is the whole comparison. The appearance path
        // is shared by every control in the settings dialog, and the colour and opacity ones
        // do not touch geometry at all — re-laying the row out for them threw away the
        // painted-size cache and forced a full repaint on every tick of a slider that had
        // not moved a single icon.
        if (_layout.Metrics == metrics)
        {
            return;
        }

        _layout = new DockLayout(metrics);

        foreach (var visual in _items)
        {
            visual.SetRestingSize(metrics.BaseSize);
        }

        // Re-pin first: new metrics move where the held item sits, and laying out against a
        // pointer still expressed in the old geometry would jerk the wave sideways.
        PinFocusPointer(claim: false);
        LayoutAtRest();
        _paintedSizes = [];
        InvalidateVisual();
    }

    /// <summary>Replaces the dock's contents.</summary>
    public void SetItems(IReadOnlyList<DockItem> items)
    {
        _pinnedItems = items;
        RebuildVisuals();
    }

    /// <summary>
    /// Rebuilds the row from the pinned items plus whatever drop preview is showing, so a
    /// preview occupies a real slot and everything else moves aside for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Visuals are carried over by identity rather than recreated. Clearing the canvas and
    /// building fresh elements is what made a moving drop preview flicker — every icon in
    /// the dock was discarded and re-rastered on each slot the preview passed through — and
    /// it also threw away the animation state that makes the row slide rather than jump.
    /// </para>
    /// <para>
    /// An icon that changes position gets a new <b>target</b> slot and eases to it; only
    /// icons that were not there before are placed outright.
    /// </para>
    /// </remarks>
    private void RebuildVisuals()
    {
        var display = ComposeDisplay();

        // Carried by id, so the same icon keeps its element — and its slide — across a
        // rebuild even though settings hand over freshly built items each time.
        var carried = new Dictionary<string, DockItemVisual>(StringComparer.Ordinal);
        foreach (var visual in _items)
        {
            carried.TryAdd(VisualKey(visual.Item, visual.IsGhost), visual);
        }

        var hovered = _hoveredIndex >= 0 && _hoveredIndex < _items.Count
            ? _items[_hoveredIndex]
            : null;

        var next = new List<DockItemVisual>(display.Count);
        for (var slot = 0; slot < display.Count; slot++)
        {
            var (item, isGhost) = display[slot];

            if (carried.Remove(VisualKey(item, isGhost), out var visual))
            {
                visual.Rebind(item);
                visual.SetRestingSize(Metrics.BaseSize);
                visual.MoveToSlot(slot);
            }
            else
            {
                visual = new DockItemVisual(item, Metrics.BaseSize, isGhost);
                visual.SnapToSlot(slot);

                // A drop preview grows into the slot the row is opening for it, rather than
                // being placed in it whole. Reduce-motion covers this the way it covers the
                // launch pulse: it is motion, and nothing depends on seeing it.
                if (isGhost && MagnificationEnabled)
                {
                    visual.BeginEntry();
                }

                Children.Add(visual);
            }

            next.Add(visual);
        }

        // Whatever is left was removed from the dock.
        foreach (var stale in carried.Values)
        {
            Children.Remove(stale);
        }

        _items.Clear();
        _items.AddRange(next);

        // The row that was just composed is the order to draw, so nothing is displaced.
        ResetDisplayOrder();

        // The settings dialog's preview holds the item selected there, wherever the change has
        // put it, or the middle icon, which moves when the contents do. Either can be in
        // another slot now, and the wave travels to it rather than jumping.
        var previewMoved = false;
        if (PreviewHolds)
        {
            var preview = PreviewIndex();
            previewMoved = preview != _focusIndex;
            _focusIndex = preview;
            _focusIsPreview = preview >= 0;
        }

        // The held item may have just been removed, or the row shortened past it. Left
        // dangling, the wave stays parked off the end of the bar and the bar narrows around
        // a pointer position that no longer refers to anything.
        if (_focusIndex >= _items.Count)
        {
            _focusIndex = _items.Count - 1;
        }

        // Hover survives a rebuild now that the elements do: a settings change while the
        // pointer rests on an icon should not blink its label off and on.
        _hoveredIndex = hovered is null ? -1 : _items.IndexOf(hovered);
        _pressedIndex = -1;

        PinFocusPointer(claim: false, ease: previewMoved);
        LayoutAtRest();
        _paintedSizes = [];
        InvalidateVisual();
        BeginSlotAnimation();

        // Against the count the window is sized for, not the number of icons on the bar:
        // splicing a preview in changes the second and not the first, which is the whole
        // point of reserving the slot.
        var countChanged = SizedCount != _sizedForCount;
        _sizedForCount = SizedCount;

        // Only when the window actually has to change size. Re-sizing it for a reorder
        // would move the whole dock sideways mid-drag for no reason.
        if (RefreshTooltipReserve() || countChanged)
        {
            RaisePreferredSizeChanged();
        }
    }

    /// <summary>The row as it should read: the pinned items with any preview spliced in.</summary>
    private List<(DockItem Item, bool IsGhost)> ComposeDisplay()
    {
        var display = new List<(DockItem, bool)>(_pinnedItems.Count + _ghostItems.Count);
        var ghostAt = _ghostItems.Count > 0
            ? Math.Clamp(_ghostIndex, 0, _pinnedItems.Count)
            : -1;

        for (var i = 0; i <= _pinnedItems.Count; i++)
        {
            if (i == ghostAt)
            {
                foreach (var ghost in _ghostItems)
                {
                    display.Add((ghost, true));
                }
            }

            if (i < _pinnedItems.Count)
            {
                display.Add((_pinnedItems[i], false));
            }
        }

        return display;
    }

    /// <summary>What makes two entries "the same icon" across a rebuild.</summary>
    private static string VisualKey(DockItem item, bool isGhost) =>
        isGhost ? "\u001fghost:" + item.Id : item.Id;

    private void RaisePreferredSizeChanged() =>
        PreferredSizeChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Starts the per-frame loop if anything has a slot to slide into or fill.</summary>
    private void BeginSlotAnimation()
    {
        _slotsMoving = _items.Exists(visual => visual.IsSlotMoving);
        _iconsGrowing = _items.Exists(visual => visual.IsEntering);

        // The first row is placed rather than grown into.
        if (_barSlots < 0)
        {
            _barSlots = BarSlotTarget;
        }

        if (_slotsMoving || _iconsGrowing || BarIsResizing)
        {
            EnsureRendering();
        }
    }

    /// <summary>Sends every icon back to the slot its position in the row implies.</summary>
    private void SettleSlots()
    {
        for (var i = 0; i < _items.Count; i++)
        {
            _items[i].MoveToSlot(i);
        }

        BeginSlotAnimation();
    }

    /// <summary>Advances both of the movements an icon can be making of its own: the slide
    /// between slots, and the growth of one that has just arrived.</summary>
    private void AdvanceIcons(double deltaMs)
    {
        var moving = false;
        var growing = false;
        foreach (var visual in _items)
        {
            moving |= visual.AdvanceSlot(deltaMs);
            growing |= visual.AdvanceEntry(deltaMs);
        }

        _slotsMoving = moving;
        _iconsGrowing = growing;

        AdvanceBarWidth(deltaMs);
    }

    /// <summary>Eases the bar toward the width its contents now ask for.</summary>
    private void AdvanceBarWidth(double deltaMs)
    {
        if (_barSlots < 0)
        {
            _barSlots = BarSlotTarget;
            return;
        }

        if (!BarIsResizing)
        {
            _barSlots = BarSlotTarget;
            return;
        }

        if (deltaMs > 0)
        {
            _barSlots += (BarSlotTarget - _barSlots) * (1 - Math.Exp(-deltaMs / BarGrowthEaseMs));
            if (!BarIsResizing)
            {
                _barSlots = BarSlotTarget;
            }
        }
    }

    /// <summary>
    /// How far an icon is displaced from the slot it has been given, because it is still
    /// sliding into it or riding the cursor.
    /// </summary>
    /// <remarks>
    /// Measured against the display slot rather than the item index, because the slot is
    /// what <see cref="RecomputeGeometry"/> has already placed the icon at. The two differ
    /// only during a drag, and counting the displacement twice there would leave every icon
    /// a slot ahead of where the wave put it.
    /// </remarks>
    private double SlotShift(int index) =>
        index >= 0 && index < _items.Count
            ? (_items[index].CurrentSlot - DisplaySlot(index)) * Metrics.Pitch
            : 0;

    /// <summary>The slot an item is drawn in.</summary>
    private int DisplaySlot(int index) =>
        index >= 0 && index < _displaySlots.Length ? _displaySlots[index] : index;

    /// <summary>Puts the row back in item order, which is where it sits outside a drag.</summary>
    private void ResetDisplayOrder()
    {
        if (_displaySlots.Length != _items.Count)
        {
            _displaySlots = new int[_items.Count];
        }

        for (var i = 0; i < _displaySlots.Length; i++)
        {
            _displaySlots[i] = i;
        }
    }

    /// <summary>The window size this dock needs, including room for a fully magnified wave.</summary>
    /// <remarks>
    /// The width is also enough for the row to sit against either end with its wave's reach
    /// still inside the window, which is what a dock moved along its edge asks of it. For a
    /// row long enough to hold a whole wave the two are the same number; a shorter one lifts
    /// by less than the reach it is given, so it gets a little more room than it can fill.
    /// </remarks>
    public Size PreferredSize() =>
        new(
            Math.Max(
                _layout.MaxBarWidth(SizedCount),
                _layout.RestingWidth(SizedCount) + (2 * _layout.WaveReach))
            + (HorizontalSlack * 2),
            Metrics.MaxSize + Metrics.PaddingY + _tooltipReserve + ShadowSlack);

    /// <summary>
    /// The number of icons the window is sized for: always one more than the dock holds.
    /// </summary>
    /// <remarks>
    /// The slot a drop preview opens is paid for in advance, because a window that grew to
    /// fit it would have to shrink again the moment the drag moved away — and each of those
    /// is a re-place of a layered window, which shows its old bitmap inside its new bounds
    /// until it repaints. Waving a file over the dock made it flinch by half a pitch every
    /// time the preview came and went, and a drop did it twice in one message: the preview
    /// came down, the window shrank, the pin went in, the window grew back.
    ///
    /// Costs nothing to look at. The window is transparent where the dock does not draw, and
    /// where the bar lands does not depend on the window's width — see
    /// <see cref="RowAlignment"/> — so the reserve is invisible: the same reasoning that lets
    /// the window stay oversized while the settings dialog is open.
    /// </remarks>
    private int SizedCount => Math.Max(_items.Count, _pinnedItems.Count + 1);

    /// <summary>Repaints the indicator dots after a change in which apps are running.</summary>
    public void RefreshRunningState() => InvalidateVisual();

    /// <summary>
    /// Repaints an item whose <see cref="DockItem.Icon"/> has been swapped in place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Needed because WPF keeps what an element drew: a visual goes on showing the picture
    /// it rendered until it is told to render again, whatever its item now holds.
    /// </para>
    /// <para>
    /// For the Recycle Bin, whose icon changes while nothing else about the dock does. A
    /// rebuild through <see cref="SetItems"/> would show it too, and would also re-lay the
    /// row out and drop a press in progress — for a change that arrives whenever anything on
    /// the machine touches the bin, not when the user did something here.
    /// </para>
    /// </remarks>
    public void RefreshIcon(DockItem item)
    {
        foreach (var visual in _items)
        {
            if (ReferenceEquals(visual.Item, item))
            {
                visual.InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Puts the dock into the settings dialog's demonstration, or takes it out: the wave held
    /// up on the item the dialog has selected (<see cref="PreviewItem"/>), or on the middle icon
    /// while it has none, so the appearance sliders have something visible to act on.
    /// </summary>
    public void PreviewMagnification(bool active)
    {
        _previewing = active;

        if (!active)
        {
            // The next dialog opens with nothing selected, and so starts from the middle.
            _previewId = null;
            ClearFocus();
            return;
        }

        FocusPreview();
    }

    /// <summary>
    /// Holds up the item the settings dialog has selected — magnified, and labelled as a
    /// hovered one is — or, given null, goes back to the middle icon.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pointer outranks it as it outranks the rest of the demonstration, and the sweep
    /// stands aside for it: an item picked out of the list is the one being looked at.
    /// </para>
    /// <para>
    /// Kept until the dialog says otherwise, through anything else that holds an item for a
    /// while. A menu or an edit dialog holding one of its own outranks the preview, and the
    /// preview comes back to this item when it lets go.
    /// </para>
    /// </remarks>
    /// <param name="id">The item's <see cref="DockItem.Id"/>, or null for none.</param>
    public void PreviewItem(string? id)
    {
        if (id == _previewId)
        {
            return;
        }

        _previewId = id;

        if (PreviewHolds)
        {
            FocusPreview();
        }
    }

    /// <summary>
    /// The item the settings dialog's preview is holding up because it is selected there; null
    /// while it holds the middle icon, while a menu or a dialog holds an item of its own, and
    /// while the dialog is closed.
    /// </summary>
    /// <remarks>
    /// The condition <see cref="DrawTooltip"/> labels the preview on, said once for the same
    /// reason <see cref="IsHoldingLabel"/> is.
    /// </remarks>
    public DockItem? PreviewedItem =>
        _focusIsPreview && IsPreviewItem(_focusIndex) ? _items[_focusIndex].Item : null;

    /// <summary>
    /// True while the preview is what holds the wave: the dialog is open, and no menu or dialog
    /// holds an item of its own. With nothing held at all only while the dock is empty.
    /// </summary>
    private bool PreviewHolds => _previewing && (_focusIsPreview || _focusIndex < 0);

    /// <summary>
    /// Where the preview holds the wave: the selected item, if the dock has it, and otherwise
    /// the middle icon. -1 for an empty dock.
    /// </summary>
    private int PreviewIndex()
    {
        if (_previewId is not null)
        {
            var selected = _items.FindIndex(visual => !visual.IsGhost && visual.Item.Id == _previewId);
            if (selected >= 0)
            {
                return selected;
            }
        }

        return _items.Count > 0 ? _items.Count / 2 : -1;
    }

    /// <summary>Whether the icon at an index is the item the dialog has selected.</summary>
    private bool IsPreviewItem(int index) =>
        _previewId is not null
        && index >= 0
        && index < _items.Count
        && !_items[index].IsGhost
        && _items[index].Item.Id == _previewId;

    /// <summary>Hands the wave to the preview, on the icon <see cref="PreviewIndex"/> names.</summary>
    /// <remarks>
    /// No label of its own: the selected item is labelled through <see cref="PreviewedItem"/>,
    /// as a hovered one is, so the pointer can take the label with the wave.
    /// </remarks>
    private void FocusPreview()
    {
        _focusIsPreview = true;
        FocusIndex(PreviewIndex(), showLabel: false);
    }

    /// <summary>Walks the wave from one end of the dock to the other and back, forever.</summary>
    public void PreviewSweep(bool active)
    {
        if (_sweeping == active)
        {
            return;
        }

        _sweeping = active;
        _sweepElapsedMs = 0;
        _sweepYielded = false;

        if (active)
        {
            EnsureRendering();
        }
    }

    /// <summary>Moves the sweep on, and parks the pointer where it has got to.</summary>
    private void AdvanceSweep(double deltaMs)
    {
        if (!_sweeping || _sweepYielded || PreviewedItem is not null || _items.Count < 2)
        {
            return;
        }

        _sweepElapsedMs = (_sweepElapsedMs + deltaMs) % SweepPeriodMs;

        // A raised cosine rather than a triangle: it eases to a stop at each end instead of
        // reversing on the spot, which is what the pointer does when a person is looking at
        // an icon.
        var phase = (1 - Math.Cos(_sweepElapsedMs / SweepPeriodMs * 2 * Math.PI)) / 2;

        var from = _layout.RestingCentre(0);
        var to = _layout.RestingCentre(_items.Count - 1);
        SetWaveCentre(from + ((to - from) * phase), WaveDriver.Sweep);
    }

    /// <summary>
    /// Winds the sweep to the phase that puts it where the pointer just was, so handing
    /// back is a continuation rather than a jump.
    /// </summary>
    private void ResumeSweepFrom(double? pointer)
    {
        if (_items.Count < 2 || pointer is not { } x)
        {
            _sweepElapsedMs = 0;
            return;
        }

        var from = _layout.RestingCentre(0);
        var to = _layout.RestingCentre(_items.Count - 1);
        if (Math.Abs(to - from) < 0.001)
        {
            _sweepElapsedMs = 0;
            return;
        }

        // Inverting the raised cosine. Two phases give any position — the outbound and the
        // return leg — and the outbound one is taken, so the wave always leaves in the same
        // direction it would have been travelling from a standing start.
        var phase = Math.Clamp((x - from) / (to - from), 0, 1);
        _sweepElapsedMs = Math.Acos(1 - (2 * phase)) / (2 * Math.PI) * SweepPeriodMs;
    }

    /// <summary>
    /// Which claim on the held item is current.
    /// </summary>
    /// <remarks>
    /// A context menu and the dialog it opens hold the same item one after the other, and
    /// the menu's release arrives <b>after</b> the dialog has taken over: the entry's action
    /// is deferred so the popup can finish tearing itself down, and WPF raises the menu's
    /// <c>Closed</c> later still — measured, after the dialog was already up. Releasing
    /// unconditionally therefore took the label down in front of the dialog that had just
    /// asked for it. A claim lets the late release discover it is stale and do nothing.
    /// </remarks>
    private int _focusClaim;

    /// <summary>
    /// Holds one item magnified with its label open — the item a menu or edit dialog was
    /// opened for, so it stays the subject of what happens next.
    /// </summary>
    /// <returns>
    /// The claim on the focus, to hand back to <see cref="ReleaseFocus"/>; zero when there
    /// was no such item to hold.
    /// </returns>
    public int FocusItem(DockItem item)
    {
        var index = _items.FindIndex(visual => ReferenceEquals(visual.Item, item));
        if (index < 0)
        {
            index = _items.FindIndex(visual => visual.Item.Id == item.Id);
        }

        // Asked of the index rather than of what ends up held: with the settings dialog open,
        // letting go of the old hold puts the preview back, which is no claim of this caller's.
        if (index < 0)
        {
            ClearFocus();
            return 0;
        }

        _focusIsPreview = false;
        FocusIndex(index, showLabel: true);

        return _focusClaim;
    }

    /// <summary>
    /// True while an item's label is being held open for a menu or a dialog.
    /// </summary>
    /// <remarks>
    /// The same condition <see cref="DrawTooltip"/> paints on, said once so the two cannot
    /// disagree about what "holding a label" means.
    /// </remarks>
    public bool IsHoldingLabel => _focusIndex >= 0 && _focusShowsLabel && !_focusIsPreview;

    /// <summary>Releases a claim taken by <see cref="FocusItem"/>, if it is still the current one.</summary>
    public void ReleaseFocus(int claim)
    {
        if (claim != 0 && claim == _focusClaim)
        {
            ClearFocus();
        }
    }

    /// <summary>
    /// Releases the held item: back to the settings dialog's preview while the dialog is open,
    /// and otherwise to the pointer.
    /// </summary>
    public void ClearFocus()
    {
        _labelStyle = null;

        if (_previewing && PreviewIndex() >= 0)
        {
            // What the dock was showing before the hold, and still should be: the dialog is
            // open behind whatever held the item. Supersedes the old claim as it takes its own.
            FocusPreview();
        }
        else
        {
            // Superseded, so anything still holding the old claim can no longer act on it.
            _focusClaim++;

            _focusIndex = -1;
            _focusShowsLabel = false;
            _focusIsPreview = false;

            // Hand control back to the pointer; the wave eases down if it is not over the dock.
            EndGesture();
        }

        // A previewed font may have been taller than anything actually pinned; dropping it
        // gives the window its headroom back.
        if (RefreshTooltipReserve())
        {
            RaisePreferredSizeChanged();
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Renames and restyles the focused item's label, for previewing an edit in progress.
    /// Does nothing if nothing is focused.
    /// </summary>
    /// <remarks>
    /// The name is previewed alongside the font because the label on the dock is the whole
    /// of what the editor is describing: typing a new name and seeing the old one still
    /// sitting there reads as the preview having stopped working.
    /// </remarks>
    public void PreviewLabelStyle(
        string? label, string? fontFamily, double? fontSize, string? fontStyle)
    {
        if (_focusIndex < 0 || _focusIndex >= _items.Count)
        {
            return;
        }

        var source = _items[_focusIndex].Item;
        _labelStyle = new DockItem
        {
            Id = source.Id,
            Label = string.IsNullOrWhiteSpace(label) ? source.Label : label,
            FontFamily = fontFamily,
            FontSize = fontSize,
            FontStyle = fontStyle
        };

        _focusShowsLabel = true;

        // A bigger label needs more room above the bar, and the preview is the whole point
        // of this call — showing it clipped would defeat it.
        if (RefreshTooltipReserve())
        {
            RaisePreferredSizeChanged();
        }

        InvalidateVisual();
    }

    private void FocusIndex(int index, bool showLabel)
    {
        if (index < 0 || index >= _items.Count)
        {
            ClearFocus();
            return;
        }

        // A new hold supersedes whatever was holding the label before it.
        _focusClaim++;

        // Another icon than the one held — the preview following the dialog's selection down
        // the list, say — and the wave travels there rather than jumping.
        var moved = index != _focusIndex;

        _focusIndex = index;
        _focusShowsLabel = showLabel;
        _labelStyle = null;
        PinFocusPointer(ease: moved);
        InvalidateVisual();
    }

    /// <summary>
    /// Parks the wave on the focused item. Re-applied whenever the items are rebuilt, since
    /// a change to icon size or gap moves where that item sits.
    /// </summary>
    /// <param name="claim">
    /// Whether this may take the wave off whatever is driving it now. False for the re-pin
    /// after a rebuild, which is only bringing the parked position up to date with geometry
    /// that has moved: a rebuild that claimed the wave would take it off the cursor and hand
    /// it straight back on the next tick, and since a handover is eased that is a visible
    /// wobble on every tick of a settings slider.
    /// </param>
    /// <param name="ease">
    /// Whether a parked wave travels to the new place rather than being put there: true when
    /// the focus has moved to another icon. False for the same icon moved by new geometry,
    /// which the wave has to stay on exactly, or it trails the slider being dragged.
    /// </param>
    private void PinFocusPointer(bool claim = true, bool ease = false)
    {
        if (_focusIndex < 0 || _focusIndex >= _items.Count)
        {
            return;
        }

        if (!claim && _driver is WaveDriver.Pointer or WaveDriver.Sweep)
        {
            // The cursor's position is expressed against the resting bar, which the new
            // metrics have just moved. Re-reading it costs one GetCursorPos and keeps the
            // layout this rebuild is about to run from being computed against the old.
            if (_driver == WaveDriver.Pointer && TryGetLocalCursor(out var cursor))
            {
                _pointer = cursor.X - RestingBarLeft();
            }

            return;
        }

        SetWaveCentre(_layout.RestingCentre(_focusIndex), WaveDriver.Parked, ease);
        EnsureRendering();
        StartRamp(1, EnterRampMs);
    }

    /// <summary>Sets the bar's fill colour and how opaque it is, 0 to 1.</summary>
    public void SetBarAppearance(string? color, double opacity)
    {
        var resolved = BarPalette.Parse(color);
        var clamped = Math.Clamp(opacity, 0, 1);

        if (resolved == _barColor && Math.Abs(clamped - _barOpacity) < 0.001)
        {
            return;
        }

        _barColor = resolved;
        _barOpacity = clamped;

        var alpha = (byte)Math.Clamp(clamped * 255, 0, 255);
        _barFill = Frozen(Color.FromArgb(alpha, resolved.R, resolved.G, resolved.B));
        InvalidateVisual();
    }

    /// <summary>
    /// The index a drop at <paramref name="screenPoint"/> should insert at, from 0 to
    /// the item count. Compares against each icon's live centre, so the insertion point
    /// follows what the user can actually see rather than resting geometry.
    /// </summary>
    public int DropIndexAt(Point screenPoint)
    {
        if (PresentationSource.FromVisual(this) is null || _items.Count == 0)
        {
            return _items.Count;
        }

        var local = PointFromScreen(screenPoint);
        for (var i = 0; i < _items.Count; i++)
        {
            if (local.X < _iconsLeft + _offsets[i] + (_sizes[i] / 2))
            {
                return i;
            }
        }

        return _items.Count;
    }

    /// <summary>
    /// Shows what a drop would add, at the slot it would land in.
    /// </summary>
    /// <remarks>
    /// The preview is a real item in a real slot — same icon, same size, same spreading —
    /// only faded, so the dock answers "where will this go and what will it look like"
    /// before the drop rather than after it. Rebuilding costs a handful of visuals and
    /// happens only when the slot changes, not on every drag-over message.
    /// </remarks>
    /// <returns>True when the preview moved or appeared.</returns>
    public bool ShowGhosts(IReadOnlyList<DockItem> ghosts, Point screenPoint)
    {
        if (ghosts.Count == 0)
        {
            return ClearGhosts();
        }

        var index = GhostIndexAt(screenPoint);
        if (ReferenceEquals(ghosts, _ghostItems) && index == _ghostIndex)
        {
            return false;
        }

        // A drag owns the dock while it is over it: no press can be in flight, and the
        // wave is held flat so the slot the preview sits in does not move underneath it.
        _pressedIndex = -1;
        _dragging = false;
        _dragIndex = -1;

        _ghostItems = ghosts;
        _ghostIndex = index;
        RebuildVisuals();
        return true;
    }

    /// <summary>Takes the drop preview back down.</summary>
    /// <returns>True when there was one to remove.</returns>
    public bool ClearGhosts()
    {
        if (_ghostItems.Count == 0)
        {
            return false;
        }

        _ghostItems = [];
        _ghostIndex = -1;
        RebuildVisuals();
        return true;
    }

    /// <summary>True while a drop preview is on the dock.</summary>
    public bool IsShowingGhosts => _ghostItems.Count > 0;

    /// <summary>What the dock is currently refusing to accept, and why. Null when nothing.</summary>
    private DockItem? _dropNotice;

    /// <summary>
    /// The notice's laid-out text, kept for the length of the refusal.
    /// </summary>
    /// <remarks>
    /// Not taken from the tooltip cache, which the labels share: the notice is ellipsised to
    /// the window's width, and that is a setting on the layout itself.
    /// </remarks>
    private FormattedText? _dropNoticeLayout;

    /// <summary>The scale it was laid out at, so a move between displays re-does it.</summary>
    private double _dropNoticeDpi;

    /// <summary>
    /// Says why a drag hovering over the dock will not be accepted, in place of the label
    /// the pointer would otherwise be showing.
    /// </summary>
    /// <remarks>
    /// A refused drop shows no preview by definition, so without this the dock answers a
    /// deliberate gesture with nothing at all — the cursor changes, in the source
    /// application, and that is the whole of it. The message is drawn in the dock's own
    /// label styling and at the top of the reserve those labels are measured for, so it
    /// needs no more room than the tallest of them already has.
    /// </remarks>
    public void ShowDropNotice(string? message)
    {
        if (string.Equals(_dropNotice?.Label, message, StringComparison.Ordinal))
        {
            return;
        }

        _dropNoticeLayout = null;

        if (message is null)
        {
            _dropNotice = null;
            InvalidateVisual();
            return;
        }

        // Styled like the labels rather than like a system message: they are the only text
        // this dock draws, and the reserve above the bar is measured from them.
        var style = _labelStyle ?? _items.Find(visual => !visual.IsGhost)?.Item;
        _dropNotice = new DockItem
        {
            Id = "drop-notice",
            Label = message,
            FontFamily = style?.FontFamily,
            FontSize = style?.FontSize,
            FontStyle = style?.FontStyle
        };

        InvalidateVisual();
    }

    /// <summary>Where a drop would insert, in the dock's real contents.</summary>
    public int GhostDropIndex =>
        _ghostIndex < 0 ? _pinnedItems.Count : Math.Clamp(_ghostIndex, 0, _pinnedItems.Count);

    /// <summary>
    /// The slot a drop at <paramref name="screenPoint"/> belongs in, expressed against the
    /// real contents rather than the preview-inclusive layout.
    /// </summary>
    /// <remarks>
    /// Hovering over the preview itself maps back to where the preview already is, so the
    /// slot does not oscillate as the cursor crosses the gap the preview opened.
    /// </remarks>
    private int GhostIndexAt(Point screenPoint)
    {
        var display = DropIndexAt(screenPoint);
        var ghosts = _ghostItems.Count;
        if (ghosts == 0 || _ghostIndex < 0)
        {
            return display;
        }

        if (display <= _ghostIndex)
        {
            return display;
        }

        return display >= _ghostIndex + ghosts ? display - ghosts : _ghostIndex;
    }

    /// <summary>
    /// True when the cursor is inside the dock's hover zone. Auto-hide asks this rather than
    /// tracking the pointer itself, so "over the dock" means exactly one thing.
    /// </summary>
    public bool IsPointerOverDock() =>
        TryGetLocalCursor(out var local) && IsPointingAtDock(local);

    /// <summary>
    /// Asks whether another window lies over the dock at a point on the screen, in physical
    /// pixels. Null, as in a test, answers no.
    /// </summary>
    /// <remarks>
    /// The cursor is polled rather than delivered, so without this the dock answers it
    /// wherever it is — including under a window that covers the dock, where the wave ran
    /// out of sight and the visible end of the bar lifted its icons for a pointer that was
    /// on something else entirely.
    /// </remarks>
    public Func<int, int, bool>? IsCoveredAt { get; set; }

    /// <summary>The cursor in physical screen pixels, as <see cref="TryGetLocalCursor"/> last read it.</summary>
    private NativeMethods.NativePoint _screenCursor;

    /// <summary>
    /// True when the pointer is inside the hover zone and on the dock itself, not on a window
    /// that lies over it there. Call after <see cref="TryGetLocalCursor"/>, whose reading it uses.
    /// </summary>
    private bool IsPointingAtDock(Point local) =>
        IsInsideHoverZone(local) && IsCoveredAt?.Invoke(_screenCursor.X, _screenCursor.Y) != true;

    /// <summary>
    /// Places every icon at its resting position once. Magnification only ever adjusts the
    /// transforms from here, so these coordinates stay put for the life of the dock.
    /// </summary>
    private void LayoutAtRest()
    {
        var restingLeft = RestingBarLeft();
        var top = BarBottom - Metrics.PaddingY - Metrics.BaseSize;

        for (var i = 0; i < _items.Count; i++)
        {
            SetLeft(_items[i], restingLeft + _layout.RestingCentre(i) - (Metrics.BaseSize / 2));
            SetTop(_items[i], top);
        }

        // Deliberately does not flatten the icons to their resting size. Doing that left the
        // dock un-magnified until the next rendering tick, which is invisible for a one-off
        // relayout but shows as a flicker when relayouts come in a stream — dragging a
        // settings slider produces one per change. Re-applying the current wave here means
        // the geometry changes underneath a wave that never drops.
        RecomputeGeometry();
        ApplyWaveTransforms();
    }

    /// <summary>Pushes this frame's geometry onto the icons' transforms.</summary>
    /// <remarks>
    /// Two displacements, added: the wave's spreading, which is a property of where the
    /// pointer is, and the slot slide, which is a property of where the icon is going. They
    /// are independent, so a row that is still settling from a reorder magnifies correctly
    /// while it settles, and a settled row adds nothing.
    /// </remarks>
    private void ApplyWaveTransforms()
    {
        var restingLeft = RestingBarLeft();
        for (var i = 0; i < _items.Count; i++)
        {
            // Where the icon's magnified box sits now, against where it was laid out at
            // rest. The scale is anchored at bottom-centre, so this translation carries
            // the spreading and the scale carries the growth.
            var target = _iconsLeft + _offsets[i] + ((_sizes[i] - Metrics.BaseSize) / 2);
            var resting = restingLeft + _layout.RestingCentre(i) - (Metrics.BaseSize / 2);
            _items[i].ApplyWave(_sizes[i], target - resting + SlotShift(i));
        }
    }

    private double RestingBarLeft() => RowLeft(_layout.RestingWidth(_items.Count));

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        LayoutAtRest();
        InvalidateVisual();
    }

    // ---- input ---------------------------------------------------------------

    /// <summary>Wakes the wave as soon as the cursor is over the dock.</summary>
    private void OnCursorWatchTick(object? sender, EventArgs e)
    {
        // A drop preview holds the dock flat and drives its own redraws; waking the wave
        // here would only start a ramp the next tick immediately cancels.
        if (_renderingHooked || _ghostItems.Count > 0 || !TryGetLocalCursor(out var local))
        {
            return;
        }

        if (_pressedIndex >= 0 || IsPointingAtDock(local))
        {
            BeginGesture();
        }
    }

    /// <summary>
    /// Presses whichever icon the cursor is over.
    /// </summary>
    /// <remarks>
    /// Driven by the window procedure rather than by WPF's <c>MouseLeftButtonDown</c>, which
    /// never fires here: <c>WS_EX_NOACTIVATE</c> stops WPF routing mouse input to this window
    /// at all. The messages still reach the HWND, and the hovered icon is already known from
    /// the cursor poll, so this needs nothing WPF's routing would have supplied.
    /// </remarks>
    public void BeginPress()
    {
        _pressedIndex = _hoveredIndex;
        _dragging = false;
        _dragIndex = -1;
        _dragTarget = -1;

        if (TryGetLocalCursor(out var local))
        {
            _pressX = local.X;
        }

    }

    /// <summary>Releases a press, activating the icon if the cursor never left it.</summary>
    /// <returns>True when the press was consumed — an activation or a reorder.</returns>
    public bool EndPress()
    {
        var pressed = _pressedIndex;
        _pressedIndex = -1;

        if (_dragging)
        {
            return EndDrag();
        }


        // Only a press and release on the same icon counts, matching every other button.
        if (pressed < 0 || pressed != _hoveredIndex || pressed >= _items.Count)
        {
            return false;
        }

        var item = _items[pressed].Item;
        if (item.IsDisabled || item.IsSeparator || _items[pressed].IsGhost)
        {
            return false;
        }

        Activated?.Invoke(this, item);
        return true;
    }

    /// <summary>
    /// Abandons a press or drag without acting on it, for when the gesture is taken away
    /// rather than finished — losing mouse capture, say.
    /// </summary>
    public void CancelPress()
    {
        if (_pressedIndex < 0 && !_dragging)
        {
            return;
        }

        _pressedIndex = -1;
        _dragging = false;
        _dragIndex = -1;
        _dragTarget = -1;

        ResetDisplayOrder();
        SettleSlots();
    }

    /// <summary>
    /// Promotes a held press into a drag once the cursor has travelled far enough,
    /// and tracks which slot the icon would land in.
    /// </summary>
    private void UpdateDrag(Point local)
    {
        if (_pressedIndex < 0 || !ReorderEnabled)
        {
            return;
        }

        if (!_dragging)
        {
            if (Math.Abs(local.X - _pressX) < DragThreshold)
            {
                return;
            }

            _dragging = true;
            _dragIndex = _pressedIndex;
        }

        _dragTarget = SlotAt(local.X);
    }

    /// <summary>The slot the cursor sits over, clamped to the dock.</summary>
    /// <remarks>
    /// Measured against the row as it is drawn rather than as it rests. The wave stays up
    /// through a drag, so the icon under the pointer is no longer the one the resting pitch
    /// would name — reading the slot from the resting frame made the gap open a place or two
    /// away from where the cursor was. The boundary is still an icon's leading edge, which
    /// is what the resting arithmetic worked out to, so the gesture needs the same travel to
    /// change slots as it always did.
    /// </remarks>
    private int SlotAt(double localX)
    {
        var count = _items.Count;
        if (count == 0)
        {
            return 0;
        }

        if (_slotOffsets.Length != count)
        {
            var resting = (int)Math.Floor(
                (localX - RestingBarLeft() - Metrics.PaddingX) / Metrics.Pitch);

            return Math.Clamp(resting, 0, count - 1);
        }

        for (var slot = count - 1; slot > 0; slot--)
        {
            if (localX >= _iconsLeft + _slotOffsets[slot])
            {
                return slot;
            }
        }

        return 0;
    }

    /// <summary>Ends a drag, moving the icon to whatever slot it was let go over.</summary>
    /// <remarks>
    /// A drag has one outcome now. Letting go clear of the bar used to unpin the icon, and
    /// that gesture is gone: it fired on a drag that wandered rather than on one that meant
    /// it, and losing a pin to a slip of the hand is a poor trade for a shortcut that the
    /// right-click menu and the settings dialog both already offer. Dragging away and letting
    /// go now simply puts the icon back in the row.
    /// </remarks>
    private bool EndDrag()
    {
        var from = _dragIndex;
        var to = _dragTarget;

        _dragging = false;
        _dragIndex = -1;
        _dragTarget = -1;

        if (from < 0 || from >= _items.Count)
        {
            return false;
        }

        // The row goes back to one icon per slot — including the dragged one, which slides
        // from wherever it was let go into the slot it earned.
        ResetDisplayOrder();
        SettleSlots();

        if (to != from && to >= 0)
        {
            Reordered?.Invoke(this, (from, to));
        }

        // Either way the gesture is spent, and the press that started it must not also
        // count as a click.
        return true;
    }

    /// <summary>Starts the wave and the per-frame cursor poll that sustains it.</summary>
    private void BeginGesture()
    {
        EnsureRendering();
        TrackCursor();
        StartRamp(1, EnterRampMs);
    }

    private void EnsureRendering()
    {
        if (_renderingHooked)
        {
            return;
        }

        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
        _renderingHooked = true;
    }

    /// <summary>
    /// Pulses an icon twice to acknowledge that a launch actually started.
    /// </summary>
    /// <remarks>
    /// Rides the same per-frame tick as the wave rather than owning a timer, and holds
    /// that tick open while it runs — a flash usually fires as the pointer is leaving,
    /// so the wave's own ramp cannot be relied on to keep the loop alive.
    /// </remarks>
    public void FlashItem(DockItem item)
    {
        // Reduce-motion covers this too: it is motion, and it is not load-bearing.
        if (!MagnificationEnabled)
        {
            return;
        }

        var index = _items.FindIndex(visual => ReferenceEquals(visual.Item, item));
        if (index < 0)
        {
            return;
        }

        _flashIndex = index;
        _flashElapsedMs = 0;
        EnsureRendering();
    }

    /// <summary>Advances the flash, shaped as two humps of a sine.</summary>
    private void AdvanceFlash(double deltaMs)
    {
        if (_flashIndex < 0)
        {
            return;
        }

        _flashElapsedMs += deltaMs;
        var t = Math.Min(_flashElapsedMs / FlashDurationMs, 1);

        // Two humps: |sin| over a full turn peaks twice and lands back on zero.
        var intensity = t >= 1 ? 0 : Math.Abs(Math.Sin(t * Math.PI * 2));

        for (var i = 0; i < _items.Count; i++)
        {
            _items[i].FlashIntensity = i == _flashIndex ? intensity : 0;
        }

        if (t >= 1)
        {
            _flashIndex = -1;
        }
    }

    private void EndGesture() => StartRamp(0, LeaveRampMs);

    private void UnhookRendering()
    {
        if (!_renderingHooked)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _renderingHooked = false;
    }

    /// <summary>
    /// Ramps the wave's amplitude only. The pointer keeps updating independently, so moving
    /// the cursor during the entry ramp neither restarts nor stalls the animation.
    /// </summary>
    private void StartRamp(double target, double fullDurationMs)
    {
        if (Math.Abs(_progress - target) < 0.0001)
        {
            _rampRunning = false;
            _progress = target;
            _rampTo = target;
            return;
        }

        _rampFrom = _progress;
        _rampTo = target;
        _rampElapsedMs = 0;
        _rampDurationMs = fullDurationMs * Math.Abs(target - _rampFrom);
        _rampRunning = true;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args)
        {
            return;
        }

        var deltaMs = _lastFrame == TimeSpan.Zero
            ? 0
            : (args.RenderingTime - _lastFrame).TotalMilliseconds;
        _lastFrame = args.RenderingTime;

        // First, while the shape is still the one the last frame drew.
        _frame++;
        AnnounceDrawnBar();

        _inFrame = true;
        try
        {
            TrackCursor();

            // After TrackCursor, which would otherwise put the real pointer back.
            AdvanceSweep(deltaMs);

            // After both, so a handover either of them has just started still draws this frame
            // at the position it is taking the wave from.
            AdvanceHandover(deltaMs);

            AdvanceRamp(deltaMs);
            AdvanceFlash(deltaMs);
            AdvanceIcons(deltaMs);
            ApplyWave();
        }
        finally
        {
            _inFrame = false;
        }

        // Once the wave is flat, the pointer has gone and nothing is animating, there is
        // nothing left to do; the cursor watch takes over until it comes back — once the last
        // shape drawn has been announced, which takes a frame after it.
        if (!_rampRunning && _progress <= 0 && _pointer is null && _flashIndex < 0
            && !_dragging && _focusIndex < 0 && !_slotsMoving && !_iconsGrowing
            && !BarIsResizing && !_sweeping && _announceAt is null)
        {
            UnhookRendering();
        }
    }

    /// <summary>Reads the cursor in this control's coordinates.</summary>
    private bool TryGetLocalCursor(out Point local)
    {
        local = default;
        if (PresentationSource.FromVisual(this) is null || !NativeMethods.GetCursorPos(out var cursor))
        {
            return false;
        }

        _screenCursor = cursor;
        local = PointFromScreen(new Point(cursor.X, cursor.Y));
        return true;
    }

    /// <summary>Reads the cursor and either updates the wave's position or ends the gesture.</summary>
    private void TrackCursor()
    {
        // A drop preview holds the dock flat. Sizes changing under the pointer would move
        // the very slot boundaries the drop is aiming at — the same reason a reorder drag
        // suppresses the wave.
        if (_ghostItems.Count > 0)
        {
            ReleaseWaveCentre();
            UpdateHovered(local: null);
            if (_rampTo > 0)
            {
                EndGesture();
            }

            return;
        }

        // While an item is held for a menu or an edit dialog, the wave is parked on it and
        // the real cursor is ignored — otherwise moving the mouse to reach the dialog would
        // drag it away. The settings dialog's preview is the opposite case: it is there to
        // be looked at, so a pointer arriving on the dock takes it over.
        if (_focusIndex >= 0 && !_focusIsPreview)
        {
            return;
        }

        if (!TryGetLocalCursor(out var local))
        {
            return;
        }

        UpdateDrag(local);

        if (_dragging)
        {
            // The wave stays up: a dock that flattens the moment you start moving an icon
            // reads as though the drag broke it, and the magnified neighbours are what make
            // the gap the icon is about to drop into legible. The slot the drop aims at is
            // read from the same geometry — see SlotAt — so the two agree.
            //
            // Hover is deliberately left alone. It still names the icon being dragged, whose
            // label therefore travels with it, and re-testing it against a cursor that is
            // holding an icon would only ever answer with that same icon anyway.
            SetWaveCentre(local.X - RestingBarLeft(), WaveDriver.Pointer);
            if (_rampTo < 1)
            {
                StartRamp(1, EnterRampMs);
            }

            return;
        }

        if (IsPointingAtDock(local))
        {
            // Whoever is actually pointing at the dock outranks the demonstration.
            _sweepYielded = true;

            SetWaveCentre(local.X - RestingBarLeft(), WaveDriver.Pointer);
            UpdateHovered(local);
            if (_rampTo < 1)
            {
                StartRamp(1, EnterRampMs);
            }

            return;
        }

        UpdateHovered(local: null);

        if (_sweeping && PreviewedItem is null)
        {
            // Nobody is pointing at it, so the demonstration takes over again — picking up
            // at the point the pointer left it rather than jumping back to its own phase.
            if (_sweepYielded)
            {
                ResumeSweepFrom(WaveCentre);
                _sweepYielded = false;
            }

            if (_rampTo < 1)
            {
                StartRamp(1, EnterRampMs);
            }

            return;
        }

        // A preview that is not sweeping goes back to holding its icon: the item the dialog has
        // selected, or the middle one.
        if (_focusIsPreview)
        {
            // The sweep stands aside for a selected item as it does for the pointer, and
            // picks up from it in the same way once the selection goes.
            _sweepYielded |= _sweeping;

            PinFocusPointer();
            return;
        }

        if (_rampTo > 0)
        {
            EndGesture();
        }
        else if (!_rampRunning && _progress <= 0)
        {
            ReleaseWaveCentre();
        }
    }

    /// <summary>Finds the icon under the cursor, using the live (magnified) geometry.</summary>
    private void UpdateHovered(Point? local)
    {
        var found = -1;

        if (local is { } point)
        {
            var iconBottom = BarBottom - Metrics.PaddingY;
            var barTop = BarBottom - Metrics.BarHeight;

            for (var i = 0; i < _items.Count; i++)
            {
                // Including the slide, so an icon still easing into a slot is picked up
                // where it is drawn rather than where it is heading.
                var left = _iconsLeft + _offsets[i] + SlotShift(i);
                if (point.X < left || point.X > left + _sizes[i])
                {
                    continue;
                }

                // An icon's column runs the full height of the bar, not just the glyph's own
                // box: the padding strip below it — where the running indicator sits — is part
                // of the target, otherwise the bottom edge of the dock is a dead zone. Above
                // the bar, the reachable area is however far the icon is currently magnified.
                var top = Math.Min(barTop, iconBottom - _sizes[i]);
                if (point.Y >= top && point.Y <= BarBottom)
                {
                    found = i;
                }

                break;
            }
        }


        if (found == _hoveredIndex)
        {
            return;
        }

        _hoveredIndex = found;
        InvalidateVisual();
    }

    /// <summary>
    /// The area that keeps the wave alive: the bar at this frame's amplitude, widened by
    /// half a gap, and extended upward by however far icons currently overflow it.
    /// </summary>
    /// <remarks>
    /// The horizontal half comes from <see cref="DockLayout.HoverSpan"/>, which takes the
    /// amplitude but not the pointer: a zone measured from the live bar moves whenever the
    /// wave moves, and near the ends that let the wave and a stationary pointer chase each
    /// other. See <see cref="DockLayout.HoverSpan"/> for what that looked like.
    /// </remarks>
    private bool IsInsideHoverZone(Point local)
    {
        if (_items.Count == 0)
        {
            // The empty bar is a target like any other — it is what a drop or a right-click
            // has to land on to refill the dock, and what auto-hide has to count as "over
            // the dock" so an emptied one can still be revealed and kept out.
            var empty = BarRect;
            var edge = Metrics.Gap / 2;

            return local.X >= empty.Left - edge
                && local.X <= empty.Right + edge
                && local.Y >= empty.Top
                && local.Y <= ActualHeight;
        }

        var overflow = Math.Max(0, Metrics.MaxSize - Metrics.BaseSize);
        var barTop = BarBottom - Metrics.BarHeight;

        // While the bar is opening or closing over a change of contents this runs a slot
        // ahead of it for a few frames, which is the safe direction: a zone that lagged the
        // bar would drop the pointer part-way through the animation.
        var (left, right) = _layout.HoverSpan(_items.Count, WaveAmplitude, RestingBarLeft());

        return local.X >= left
            && local.X <= right
            && local.Y >= barTop - overflow
            // Down to the window's edge rather than the bar's: the strip the shadow falls
            // into still reads as "on the dock" to anyone moving the pointer there.
            && local.Y <= ActualHeight;
    }

    private void AdvanceRamp(double deltaMs)
    {
        if (!_rampRunning)
        {
            return;
        }

        _rampElapsedMs += deltaMs;
        var t = _rampDurationMs <= 0 ? 1 : Math.Min(_rampElapsedMs / _rampDurationMs, 1);

        // Cubic ease-out, matching the web original's 1 - (1 - t)^3.
        var eased = 1 - Math.Pow(1 - t, 3);
        _progress = _rampFrom + ((_rampTo - _rampFrom) * eased);

        if (t >= 1)
        {
            _progress = _rampTo;
            _rampRunning = false;
            if (_rampTo <= 0)
            {
                ReleaseWaveCentre();
            }
        }
    }

    /// <summary>Recomputes this frame's sizes and positions from the current pointer state.</summary>
    /// <summary>True when this frame's geometry matches what is already on screen.</summary>
    private bool SizesUnchanged()
    {
        if (_paintedSizes.Length != _sizes.Length)
        {
            return false;
        }

        for (var i = 0; i < _sizes.Length; i++)
        {
            if (Math.Abs(_paintedSizes[i] - _sizes[i]) > 0.01)
            {
                return false;
            }
        }

        return true;
    }

    private void RecomputeGeometry()
    {
        var amplitude = WaveAmplitude;

        // Computed for slots rather than for items, so the wave describes the row as it is
        // drawn. The two orders are the same except during a reorder drag, and it is the
        // drawn one that has to magnify — the pointer is over a position, not over an entry
        // in a list.
        _slotSizes = _layout.Sizes(_items.Count, WaveCentre, amplitude);

        _slotOffsets = _layout.Offsets(_slotSizes);

        // The bar wraps whatever the icons occupy right now, so it follows them as the
        // wave moves instead of snapping to one width on entry and sitting there. Kept as its
        // two parts, the row and what the wave adds to it, because they are placed
        // differently — see below.
        var resting = _layout.RestingWidth(_items.Count);
        var lift = _layout.BarWidth(_slotSizes) - resting;

        // A slot that has just appeared is not paid for all at once. The wave's own
        // contribution stays instant — the bar has to keep wrapping the icons exactly as it
        // passes — but the slot itself is worth a pitch, and that is taken off in proportion
        // to how far the bar still has to grow. Everything else follows from it: the bar is
        // placed by what it is drawn as, and the icons are laid out from its left edge, so
        // the icons already in the dock drift apart with it instead of jumping half a pitch
        // the instant a preview appears.
        if (BarIsResizing)
        {
            resting -= (BarSlotTarget - _barSlots) * Metrics.Pitch;
        }

        // The row is anchored where the dock has been put along its edge, and grows away from
        // the end it is nearest; the wave grows about the row's middle wherever that is. On a
        // centred dock the two come to the same thing, which is the bar as it always was.
        _barWidth = resting + lift;
        _barLeft = RowLeft(resting) - (lift / 2);
        _iconsLeft = _barLeft;

        if (_sizes.Length != _items.Count)
        {
            _sizes = new double[_items.Count];
            _offsets = new double[_items.Count];
        }

        for (var i = 0; i < _items.Count; i++)
        {
            var slot = Math.Clamp(DisplaySlot(i), 0, _items.Count - 1);
            _sizes[i] = _slotSizes[slot];
            _offsets[i] = _slotOffsets[slot];
        }

        // Noted from here rather than from ApplyWave, which only runs while something is
        // animating. Removing an item from the settings dialog with the pointer away from the
        // dock changes the bar without a frame of the wave being run, and the acrylic sheet
        // behind it was left at the old size until something else woke the dock up.
        NoteBarShape();
    }

    /// <summary>
    /// Gives every icon the slot a drop right now would put it in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The display order changes at once; the slide toward it is the ordinary slot
    /// animation, which now measures itself against the new slot and so starts one slot out
    /// and eases in. Icons therefore make room by moving rather than by teleporting.
    /// </para>
    /// <para>
    /// The wave keeps running through all of this. It is computed from the pointer alone,
    /// never from the drag's own target, so the slot boundaries this aims at cannot be
    /// moved by the answer it produces.
    /// </para>
    /// </remarks>
    private void AssignDragSlots()
    {
        var count = _items.Count;
        if (_dragIndex < 0 || _dragIndex >= count)
        {
            return;
        }

        // Display order: the dragged item lifted out and put back at its target slot.
        var order = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            if (i != _dragIndex)
            {
                order.Add(i);
            }
        }

        order.Insert(Math.Clamp(_dragTarget, 0, order.Count), _dragIndex);

        if (_displaySlots.Length != count)
        {
            _displaySlots = new int[count];
        }

        for (var slot = 0; slot < order.Count; slot++)
        {
            var item = order[slot];
            _displaySlots[item] = slot;

            // The dragged one is placed against the cursor instead, once the wave for this
            // frame is known — see PinDraggedToCursor.
            if (item != _dragIndex)
            {
                _items[item].MoveToSlot(slot);
            }
        }
    }

    /// <summary>Puts the dragged icon under the cursor, whatever the wave is doing around it.</summary>
    /// <remarks>
    /// Solved against the position <see cref="ApplyWaveTransforms"/> is about to derive, so
    /// the icon sits centred on the pointer rather than merely near it. Doing that needs the
    /// frame's sizes, which is why this runs after the geometry rather than with the rest of
    /// the slot assignment.
    /// </remarks>
    private void PinDraggedToCursor(Point local)
    {
        var index = _dragIndex;
        if (index < 0 || index >= _items.Count || index >= _sizes.Length)
        {
            return;
        }

        var slot = DisplaySlot(index);
        var pitch = Metrics.Pitch;
        if (pitch <= 0)
        {
            _items[index].SnapToSlot(slot);
            return;
        }

        var size = _sizes[index];

        // Where the slot alone would put the icon's left edge, and where the pointer wants it.
        var resting = _iconsLeft + _offsets[index];
        var wanted = _layout.ClampIconToBar(local.X - (size / 2), size, _barLeft, _barWidth);

        // Snapped rather than eased: it tracks the cursor exactly instead of lagging it —
        // within the bar, which ClampIconToBar is what keeps it inside.
        _items[index].SnapToSlot(slot + ((wanted - resting) / pitch));
    }

    private void ApplyWave()
    {
        if (_items.Count == 0)
        {
            // No wave to compute, but the empty bar still has a shape, and the acrylic sheet
            // behind it follows whatever is announced from here. NoteBarShape notes
            // nothing for an unchanged rectangle, so this costs nothing on the frames where it
            // has settled.
            NoteBarShape();
            return;
        }

        var dragCursor = default(Point);
        var dragging = _dragging && TryGetLocalCursor(out dragCursor);
        if (dragging)
        {
            AssignDragSlots();
        }

        RecomputeGeometry();

        if (dragging)
        {
            PinDraggedToCursor(dragCursor);
        }

        // A held wave produces the same geometry every frame; there is nothing to repaint.
        // The flash, the slot slide and an arriving icon's growth all move without the sizes
        // changing, so any of them forces a frame — as does a live drag, whose icon tracks
        // the cursor by snapping to it rather than by easing, and so never registers as a
        // slide in progress.
        if (!_dragging && _flashIndex < 0 && !_slotsMoving && !_iconsGrowing && !BarIsResizing
            && SizesUnchanged())
        {
            return;
        }

        _paintedSizes = (double[])_sizes.Clone();
        ApplyWaveTransforms();
        InvalidateVisual();
    }

    /// <summary>
    /// The bar as this control has drawn it: its shape as of the last frame rendered, in this
    /// element's coordinates. <see cref="Rect.Empty"/> until it has been drawn once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <see cref="BarRect"/>, which is the shape being computed for the frame to come, and
    /// is on the screen only once WPF has rendered that frame and handed it over. What follows
    /// the bar from another window — the acrylic sheet, which draws the bar while the blur is on
    /// — has to follow this instead, or it runs ahead of the icons.
    /// </para>
    /// <para>
    /// Measured frame by frame: the sheet's changes reach the screen about a frame after they
    /// are made, straight from this thread through the compositor; this window's reach it two
    /// or three frames after the frame that computed them, through WPF's render thread and
    /// <c>UpdateLayeredWindow</c>. A sheet told of a shape as soon as it was computed drew the
    /// bar a frame or two ahead of its own icons — the end icon's margin inside the bar swung
    /// from 10 to 25 pixels while a slot opened, where it holds at 17 or 18 when one window
    /// draws both. Told of it at the start of the frame after the one that drew it, the sheet's
    /// shorter road makes up the difference.
    /// </para>
    /// </remarks>
    public Rect RenderedBarRect { get; private set; } = Rect.Empty;

    /// <summary>
    /// The corner radius that went with <see cref="RenderedBarRect"/>.
    /// </summary>
    /// <remarks>
    /// Part of the shape, and not implied by the rectangle: turning the roundness slider
    /// changes what the bar looks like without moving a single edge of it. Left out, the
    /// sheet behind the bar kept whatever corners it had until something else happened to
    /// resize the dock.
    /// </remarks>
    public double RenderedBarRadius { get; private set; } = -1;

    /// <summary>Frames of the render loop so far, counted as each begins.</summary>
    private long _frame;

    /// <summary>True while a frame of the render loop is being computed.</summary>
    private bool _inFrame;

    /// <summary>
    /// The frame at whose start the bar's latest change will have been drawn, and can be
    /// announced; null while nothing is waiting to be.
    /// </summary>
    private long? _announceAt;

    /// <summary>
    /// The shape last noted, so a frame that computes the same shape again notes nothing — the
    /// geometry is recomputed on every frame of the wave, most of them changing nothing, and a
    /// note keeps the render loop running.
    /// </summary>
    private (Rect Rect, double Radius) _noted = (Rect.Empty, -1);

    /// <summary>
    /// Notes that the bar has changed shape, to be announced once it has been drawn — see
    /// <see cref="RenderedBarRect"/>.
    /// </summary>
    private void NoteBarShape()
    {
        var rect = BarRect;
        var radius = Metrics.BarRadius;
        if (SameShape(rect, radius, _noted.Rect, _noted.Radius))
        {
            return;
        }

        _noted = (rect, radius);

        // Nothing follows the bar, so there is nothing to keep in step: the shape is taken as
        // drawn at once, and no frame is run to announce it.
        if (BarRectChanged is null)
        {
            (RenderedBarRect, RenderedBarRadius) = (rect, radius);
            return;
        }

        // A change made while a frame is being computed is drawn with that frame; one made
        // between frames — a settings change, a rebuild, a layout — with the next. Either way it
        // is announced as the frame after that begins. (Announcing the second kind a frame
        // sooner was tried, measured, and left a size slider's steps less well matched.)
        var drawnIn = _inFrame ? _frame : _frame + 1;
        _announceAt = Math.Max(_announceAt ?? 0, drawnIn + 1);
        EnsureRendering();
    }

    /// <summary>
    /// At the start of a frame, before it changes anything: tells whatever follows the bar the
    /// shape the frames before this one drew — by any amount worth drawing.
    /// </summary>
    /// <remarks>
    /// Any amount: it used to take half a DIP to count, which was fine for a sheet hidden under
    /// a bar drawn here. With the blur on the sheet draws the bar itself, and a bar that moved
    /// in half-DIP steps would visibly step where the wave drifts it by less than a pixel a
    /// frame — and come to rest up to half a DIP from where the icons say it is, the last small
    /// move never being announced.
    /// </remarks>
    private void AnnounceDrawnBar()
    {
        if (_announceAt is not { } at || _frame < at)
        {
            return;
        }

        _announceAt = null;

        var rect = BarRect;
        var radius = Metrics.BarRadius;
        if (SameShape(rect, radius, RenderedBarRect, RenderedBarRadius))
        {
            return;
        }

        (RenderedBarRect, RenderedBarRadius) = (rect, radius);
        BarRectChanged?.Invoke(this, rect);
    }

    /// <summary>The same shape, to a hundredth of a DIP — which is to say, drawn the same.</summary>
    private static bool SameShape(Rect a, double aRadius, Rect b, double bRadius) =>
        !a.IsEmpty && !b.IsEmpty
        && Math.Abs(aRadius - bRadius) < 0.01
        && Math.Abs(a.X - b.X) < 0.01
        && Math.Abs(a.Y - b.Y) < 0.01
        && Math.Abs(a.Width - b.Width) < 0.01
        && Math.Abs(a.Height - b.Height) < 0.01;


    // ---- rendering -----------------------------------------------------------

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (_items.Count == 0)
        {
            // The bar and nothing else: there are no icons to indicate, and no label or drop
            // notice to put over them. Painting it is what keeps an emptied dock visible,
            // right-clickable and droppable — see EmptyBarWidth.
            DrawBar(drawingContext);
            return;
        }

        DrawBar(drawingContext);
        DrawRunningIndicators(drawingContext);

        // A refusal outranks a label: the pointer is over an icon whose name the user is
        // not asking for, and two bubbles at once would sit on top of each other.
        if (!DrawDropNotice(drawingContext))
        {
            DrawTooltip(drawingContext);
        }
    }

    /// <summary>
    /// The width of the bar when nothing is pinned: one empty slot.
    /// </summary>
    /// <remarks>
    /// An emptied dock used to draw nothing at all, and that did not merely look like an
    /// absent dock — it was one. This window is <c>AllowsTransparency</c>, so Windows
    /// hit-tests it by the per-pixel alpha of what has been painted. With no bar there were
    /// no opaque pixels anywhere, every click went straight through to whatever was behind,
    /// and the dock's own right-click menu — which <see cref="DockWindow.ShowMenuFor"/>
    /// describes as the only way to add anything back to an emptied dock — could not be
    /// reached at all. Nor could a drop land. The tray icon was the only way out.
    /// One slot is what <see cref="SizedCount"/> already keeps the window wide enough for,
    /// so an empty bar costs no extra room.
    /// </remarks>
    private double EmptyBarWidth => _layout.SteadyBarWidth(1, 0);

    /// <summary>The bar as it is currently drawn, in this element's coordinates.</summary>
    /// <remarks>
    /// Derived rather than read back from the eased geometry while the dock is empty:
    /// <see cref="RecomputeGeometry"/> does not run without icons, so nothing would have left
    /// <c>_barWidth</c> correct and the last non-empty width would linger.
    /// </remarks>
    public Rect BarRect
    {
        get
        {
            if (_items.Count > 0)
            {
                return new Rect(_barLeft, BarBottom - Metrics.BarHeight, _barWidth, Metrics.BarHeight);
            }

            var width = EmptyBarWidth;
            return new Rect(
                RowLeft(width), BarBottom - Metrics.BarHeight, width, Metrics.BarHeight);
        }
    }

    /// <summary>
    /// The bar as it is at rest — no wave, and no slot still easing open — in this element's
    /// coordinates.
    /// </summary>
    /// <remarks>
    /// What the handle an auto-hidden dock leaves behind is measured from
    /// (<see cref="DockHandle"/>). It marks where the dock will come up, and a mark that
    /// breathed with the wave would be following something the dock is not doing while it is
    /// away — or, while the settings dialog demonstrates the wave, wobbling under a dock that is
    /// plainly not going anywhere.
    /// </remarks>
    public Rect RestingBarRect
    {
        get
        {
            var width = _items.Count > 0 ? _layout.RestingWidth(_items.Count) : EmptyBarWidth;
            return new Rect(RowLeft(width), BarBottom - Metrics.BarHeight, width, Metrics.BarHeight);
        }
    }

    /// <summary>
    /// The widest the bar ever gets, in this element's coordinates.
    /// </summary>
    /// <remarks>
    /// The envelope the window is sized around, with room beside it for the shadow: the
    /// acrylic sheet is the same size as the window and draws the bar and its shadow while the
    /// blur is on, so anything of either outside the window would be cut off. It used to be the
    /// sheet's own size, when the sheet only blurred and the bar was drawn here.
    /// </remarks>
    public Rect MaxBarRect
    {
        get
        {
            // The same envelope the window is sized for, and never narrower than the bar is
            // drawn at this instant, which matters while the bar is closing over an icon that
            // has gone: the drawn width outlives the count by a few frames.
            var slots = Math.Max(SizedCount, (int)Math.Ceiling(_barSlots));
            var resting = _layout.RestingWidth(slots);
            var width = _layout.SteadyBarWidth(slots, 1);

            // The widest row, grown about its middle the way the wave grows the live bar —
            // which puts every bar this dock can draw inside it, wherever the row is anchored.
            return new Rect(
                RowLeft(resting) - ((width - resting) / 2),
                BarBottom - Metrics.BarHeight,
                width,
                Metrics.BarHeight);
        }
    }

    private void DrawBar(DrawingContext drawingContext)
    {
        var radius = Metrics.BarRadius;
        var bar = BarRect;

        if (!DrawsBar)
        {
            DrawStandIn(drawingContext, bar, radius);
            return;
        }

        if (DrawsShadow)
        {
            DrawShadow(drawingContext, bar, radius);
        }

        // Inset by half the pen so the 1px border lands inside the bar's bounds.
        var bounds = new Rect(bar.X + 0.5, bar.Y + 0.5, bar.Width - 1, bar.Height - 1);
        drawingContext.DrawRoundedRectangle(_barFill, _barBorder, bounds, radius, radius);
    }

    /// <summary>
    /// Paints, invisibly, what the bar and its shadow cover, while the sheet behind the dock
    /// draws them — so they go on taking clicks and drops exactly where they did when they
    /// were drawn here. See <see cref="DrawsBar"/>.
    /// </summary>
    /// <remarks>
    /// The shadow's footprint included, because it always was the dock's: every ring of it is
    /// painted, faint as it is, so a right-click in the strip below the bar opened the dock's
    /// menu, and a drop there landed. One figure per pass, filled as a union, so nowhere is
    /// painted twice and nowhere reaches more than the one step of alpha.
    /// </remarks>
    private void DrawStandIn(DrawingContext drawingContext, Rect bar, double radius)
    {
        var area = new GeometryGroup { FillRule = FillRule.Nonzero };
        area.Children.Add(new RectangleGeometry(bar, radius, radius));

        if (DrawsShadow)
        {
            for (var pass = 0; pass < BarShadow.Passes.Count; pass++)
            {
                var ring = BarShadow.Outermost(bar, radius, pass);
                area.Children.Add(new RectangleGeometry(ring.Bounds, ring.Radius, ring.Radius));
            }
        }

        area.Freeze();
        drawingContext.DrawGeometry(StandInBrush, pen: null, area);
    }

    /// <summary>
    /// Approximates a soft drop shadow with a stack of concentric rounded rectangles whose
    /// alpha accumulates towards the bar — see <see cref="BarShadow"/> for its shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A real <c>DropShadowEffect</c> would have to hang off a live element and blur it again
    /// every frame the bar changes width, which is every frame of a wave. A stack of cheap
    /// rounded rectangles costs nothing to redraw.
    /// </para>
    /// <para>
    /// The bar itself is clipped out, because a shadow is not visible through the object
    /// casting it; without that, the accumulated black sits behind a translucent bar and dirties
    /// its colour. The sheet, when it draws the shadow, gets the same effect for nothing: the
    /// blur it lays over the bar is opaque.
    /// </para>
    /// </remarks>
    private static void DrawShadow(DrawingContext drawingContext, Rect bar, double radius)
    {
        // Even-odd over two figures punches the bar out of the region: the standard donut,
        // and far cheaper per frame than a boolean geometry combine.
        var region = new GeometryGroup { FillRule = FillRule.EvenOdd };
        region.Children.Add(new RectangleGeometry(Rect.Inflate(bar, BarShadow.Reach, BarShadow.Reach)));
        region.Children.Add(new RectangleGeometry(bar, radius, radius));
        region.Freeze();

        drawingContext.PushClip(region);

        Span<BarShadow.Ring> rings = stackalloc BarShadow.Ring[BarShadow.RingCount];
        BarShadow.Rings(bar, radius, scale: 1, rings);

        foreach (var ring in rings)
        {
            drawingContext.DrawRoundedRectangle(
                ShadowBrushes[ring.Pass], pen: null, ring.Bounds, ring.Radius, ring.Radius);
        }

        drawingContext.Pop();
    }

    /// <summary>
    /// Draws the dot under each running app. Deliberately painted here rather than inside the
    /// icon: the dots are a fixed size and must not grow with the wave.
    /// </summary>
    private void DrawRunningIndicators(DrawingContext drawingContext)
    {
        var y = BarBottom - (Metrics.PaddingY / 2);

        for (var i = 0; i < _items.Count; i++)
        {
            if (!_items[i].Item.IsRunning)
            {
                continue;
            }

            var centreX = _iconsLeft + _offsets[i] + (_sizes[i] / 2) + SlotShift(i);
            drawingContext.DrawEllipse(DotBrush, pen: null, new Point(centreX, y), 2, 2);
        }
    }

    /// <summary>
    /// Draws the reason a hovering drag is being refused, centred over the bar.
    /// </summary>
    /// <remarks>
    /// Placed at the top of the tooltip reserve rather than above an icon, because the icons
    /// under a hovering drag are magnified and there is no one of them the message belongs
    /// to. A message too long for the window is ellipsised; the dock is as wide as its
    /// contents make it, and a narrow one still has to say something.
    /// </remarks>
    /// <returns>True when there was a notice to draw.</returns>
    private bool DrawDropNotice(DrawingContext drawingContext)
    {
        if (_dropNotice is null)
        {
            return false;
        }

        var text = NoticeLayout(_dropNotice);

        var width = text.Width + (TooltipPadX * 2);
        var height = text.Height + (TooltipPadY * 2);

        // Over the bar, not the middle of the window: the two part company once the dock has
        // been moved along its edge, and further still while the settings dialog is holding
        // the window wide.
        var bar = BarRect;
        var left = BubbleLeft(bar.X + (bar.Width / 2), width);

        drawingContext.DrawRoundedRectangle(
            TooltipFill, pen: null, new Rect(left, 2, width, height), 7, 7);
        drawingContext.DrawText(text, new Point(left + TooltipPadX, 2 + TooltipPadY));
        return true;
    }

    /// <summary>Lays the notice out, once per message and per scale.</summary>
    private FormattedText NoticeLayout(DockItem notice)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (_dropNoticeLayout is { } cached && Math.Abs(_dropNoticeDpi - pixelsPerDip) < 0.0001)
        {
            return cached;
        }

        var layout = new FormattedText(
            notice.Label,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            TypefaceFor(notice),
            Math.Clamp(notice.FontSize ?? DefaultTooltipSize, 6, 48),
            TooltipText,
            pixelsPerDip)
        {
            // One line, cut short with an ellipsis rather than allowed to spill out of a
            // narrow dock's window — which clips its own content.
            MaxTextWidth = Math.Max(1, ActualWidth - (TooltipPadX * 2) - 8),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };

        _dropNoticeLayout = layout;
        _dropNoticeDpi = pixelsPerDip;
        return layout;
    }

    private void DrawTooltip(DrawingContext drawingContext)
    {
        // A held item outranks the pointer: its label stays up whether or not anything is
        // hovered, so it can be judged while a menu or dialog has focus. The settings
        // preview holds none: it lets a hovered label through like any other, and labels its
        // own item only as a hovered one would be.
        if (_focusIndex >= 0 && !_focusIsPreview)
        {
            if (_focusShowsLabel && _focusIndex < _items.Count)
            {
                DrawTooltipFor(drawingContext, _focusIndex, _labelStyle ?? _items[_focusIndex].Item);
            }

            return;
        }

        // The item selected in the settings dialog is labelled as a hovered one would be, for
        // as long as the wave is parked on it: a pointer on the dock takes the wave, and the
        // label goes with it.
        var labelled = _hoveredIndex >= 0 ? _hoveredIndex
            : PreviewedItem is not null && _driver == WaveDriver.Parked ? _focusIndex
            : -1;

        if (labelled < 0 || labelled >= _items.Count || _progress < 0.5)
        {
            return;
        }

        DrawTooltipFor(drawingContext, labelled, _items[labelled].Item);
    }

    /// <summary>Draws one item's label bubble above it.</summary>
    private void DrawTooltipFor(DrawingContext drawingContext, int index, DockItem item)
    {
        if (index < 0 || index >= _sizes.Length
            || item.IsSeparator
            || string.IsNullOrWhiteSpace(item.Label))
        {
            return;
        }

        var text = GetTooltipLayout(item);

        var iconTop = BarBottom - Metrics.PaddingY - _sizes[index];
        var centreX = _iconsLeft + _offsets[index] + (_sizes[index] / 2) + SlotShift(index);

        var width = text.Width + (TooltipPadX * 2);
        var height = text.Height + (TooltipPadY * 2);

        // Keep the bubble inside the window, and on the screen, even for a long name on an end
        // icon.
        var left = BubbleLeft(centreX, width);

        // The reserve above the bar is sized to fit this, so the clamp should never bite;
        // it is here because the window clips its own content, and a label losing its top
        // edge is worse than one sitting a little closer to its icon.
        var top = Math.Max(2, iconTop - height - TooltipGap);

        drawingContext.DrawRoundedRectangle(
            TooltipFill, pen: null, new Rect(left, top, width, height), 7, 7);
        drawingContext.DrawText(text, new Point(left + TooltipPadX, top + TooltipPadY));
    }

    /// <summary>
    /// Re-measures how much headroom the labels in the dock need, including whatever
    /// styling is currently being previewed.
    /// </summary>
    /// <returns>True when the reserve changed, which means the window has to grow or shrink.</returns>
    private bool RefreshTooltipReserve()
    {
        var tallest = 0d;
        foreach (var visual in _items)
        {
            // Previews are never labelled, so their names must not push the window
            // taller for the length of a drag.
            if (visual.IsGhost
                || visual.Item.IsSeparator
                || string.IsNullOrWhiteSpace(visual.Item.Label))
            {
                continue;
            }

            tallest = Math.Max(tallest, GetTooltipLayout(visual.Item).Height);
        }

        if (_labelStyle is { Label.Length: > 0 } style)
        {
            tallest = Math.Max(tallest, GetTooltipLayout(style).Height);
        }

        // The bubble, the gap under it, and a couple of pixels so it is not flush against
        // the window edge the content is clipped to.
        var reserve = Math.Max(
            MinTooltipReserve, tallest + (TooltipPadY * 2) + TooltipGap + 2);

        if (Math.Abs(reserve - _tooltipReserve) < 0.5)
        {
            return false;
        }

        _tooltipReserve = reserve;
        return true;
    }

    /// <summary>Default typeface for labels, when an item does not override it.</summary>
    /// <remarks>
    /// Internal rather than private so the item editor can preview a label the same way the
    /// dock draws one. Two copies of these numbers would drift, and the editor's whole job
    /// is to show what the dock is about to do.
    /// </remarks>
    internal const string DefaultTooltipFont = "Segoe UI";

    /// <summary>Default label size, when an item does not override it.</summary>
    internal const double DefaultTooltipSize = 14;

    /// <summary>
    /// Default emphasis, when an item does not override it.
    /// </summary>
    /// <remarks>
    /// Bold, because a label is read against whatever happens to be behind the dock — a
    /// desktop, a document, a video — and at label sizes over a translucent bubble the
    /// regular weight is the first thing to lose. <c>Regular</c> remains available as an
    /// explicit choice for anyone who wants it.
    /// </remarks>
    internal const string DefaultTooltipStyle = "Bold";

    /// <summary>
    /// Lays the tooltip label out, reusing the previous result while nothing about it has
    /// changed.
    /// </summary>
    /// <remarks>
    /// The cache key covers the font as well as the text and DPI: two items can share a
    /// label and still need different layouts.
    /// </remarks>
    private FormattedText GetTooltipLayout(DockItem item)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var signature = string.Join(
            '',
            item.Label,
            item.FontFamily,
            item.FontSize?.ToString(CultureInfo.InvariantCulture),
            item.FontStyle,
            pixelsPerDip.ToString(CultureInfo.InvariantCulture));

        if (_tooltipLayouts.TryGetValue(signature, out var cached))
        {
            return cached;
        }

        // Previewing a font size walks through a new signature per slider tick, so the
        // cache is emptied rather than allowed to accumulate one layout per value tried.
        if (_tooltipLayouts.Count > 64)
        {
            _tooltipLayouts.Clear();
        }

        var layout = new FormattedText(
            item.Label,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            TypefaceFor(item),
            Math.Clamp(item.FontSize ?? DefaultTooltipSize, 6, 48),
            TooltipText,
            pixelsPerDip);

        _tooltipLayouts[signature] = layout;
        return layout;
    }

    /// <summary>Builds an item's typeface, falling back to the dock's default.</summary>
    private static Typeface TypefaceFor(DockItem item)
    {
        var family = string.IsNullOrWhiteSpace(item.FontFamily)
            ? new FontFamily(DefaultTooltipFont)
            : new FontFamily(item.FontFamily);

        // An item that has never been styled takes the dock's default rather than the
        // typeface's own, which is what makes the default something other than regular.
        var style = string.IsNullOrWhiteSpace(item.FontStyle) ? DefaultTooltipStyle : item.FontStyle;

        var italic = style is "Italic" or "BoldItalic";
        var bold = style is "Bold" or "BoldItalic";

        return new Typeface(
            family,
            italic ? FontStyles.Italic : FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
