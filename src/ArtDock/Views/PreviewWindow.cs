using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.Dock;
using ArtDock.Interop;
using ArtDock.Localization;
using Microsoft.Win32;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;
using CompositionColor = Windows.UI.Color;
using CompositionVisual = Windows.UI.Composition.Visual;
using ContainerVisual = Windows.UI.Composition.ContainerVisual;

namespace ArtDock.Views;

/// <summary>One window as the previews show it.</summary>
/// <param name="Window">The window itself.</param>
/// <param name="Title">Its title as it was last read.</param>
/// <param name="Icon">The icon drawn before the title: the item's own.</param>
/// <param name="CanClose">Whether the dock can close it, and so whether it gets a close button.</param>
internal sealed record PreviewEntry(nint Window, string Title, ImageSource? Icon, bool CanClose);

/// <summary>
/// The panel the previews of an app's windows are shown in, above the dock: a card for each
/// window, its title in a row above a live picture of it — and the card they sit on, which
/// glides and changes shape from one app's windows to another's, as Windows 11's taskbar does.
/// </summary>
/// <remarks>
/// <para>
/// <b>A window that never moves or resizes</b>, lying over the dock's display from the top of
/// its work area down to the dock's hover zone (<see cref="PreviewPlace.DockTop"/>) — below the
/// line the card stands above (<see cref="PreviewPlace.Floor"/>), for its shadow — with no
/// redirection bitmap: everything in it is drawn by the compositor, and it is clear
/// wherever nothing is drawn. Resizing a window on the screen every frame cannot be done without
/// Windows showing the space it gained before it is drawn — found as a blink at every switch on
/// 2026-10-03 — so the card moves inside a window that stays put.
/// </para>
/// <para>
/// <b>It lets the pointer through; a second, empty window over the card takes it.</b> The drawing
/// window is layered and transparent, as the dock's sheet is. Cut to the card by a region it took
/// the pointer only there, but a region cuts what the compositor draws as well, and the card's
/// shadow was cut off at its edge (seen 2026-11-20). The input window draws nothing — no
/// redirection bitmap, no visuals — so it is moved to the card every frame with nothing to show late.
/// </para>
/// <para>
/// <b>Everything moves in one batch.</b> The card, its shadow, its titles, the hover's highlight
/// and the close button are composition visuals, set on each frame together with the pictures'
/// rectangles (DWM thumbnails), from one thread — measured on 2026-11-20 to reach
/// the screen on the same frame, every frame (TODO.md item 25). Nothing is drawn by WPF on the
/// screen: WPF's drawing arrives a frame or two apart from the compositor's, and the card glides
/// tens of pixels a frame. WPF draws each title once, into a bitmap the compositor is given
/// (<see cref="CompositionImages"/>).
/// </para>
/// <para>
/// <b>How it moves between apps is Windows'</b>, measured off the taskbar
/// (<see cref="PreviewMotion"/>): it glides to another item's place in a fixed time, every edge
/// on one curve and its bottom still, the old cards cross-fading into the new. Opening and
/// closing are the dock's own: it rises the gap it keeps above the dock and fades in, and fades
/// out gliding up and away — the taskbar's slide from behind itself, tried, rose out of a line
/// in mid-air over the dock. Off with *Reduce motion* or Windows' animations, when it simply
/// appears, changes and goes.
/// </para>
/// <para>
/// <b>The pictures are cut by hand.</b> DWM draws a thumbnail over everything in the window and
/// nothing clips it — not the region, not the compositor's clip — so each is given the part of
/// its window that falls inside the card (<see cref="PreviewMotion.Cut"/>), which matters as a
/// set of a different size cross-fades on the card. The window's own edge does clip it.
/// </para>
/// <para>
/// Made for one display, in physical pixels, and never moved to another: a panel wanted on
/// another display, or above another floor, is a new one (<see cref="Fits"/>).
/// </para>
/// <para>
/// The mouse comes to the window procedure, as the dock's does — clicks there, with the press
/// and the release on the same card, found from <see cref="PreviewLayout"/>'s rectangles.
/// </para>
/// </remarks>
internal sealed class PreviewWindow : IDisposable
{
    private const string ClassName = "ArtDock.Previews";

    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int WM_SETCURSOR = 0x0020;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int MA_NOACTIVATE = 3;

    /// <summary>The card's corner radius, in DIPs: Windows 11's flyouts'.</summary>
    private const double CardRadius = 8;

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Held for as long as the process runs: Windows calls it for every panel.</summary>
    private static readonly NativeMethods.WindowProc Procedure = OnWindowMessage;

    private static readonly Dictionary<nint, PreviewWindow> Open = [];
    private static bool _registered;

    /// <summary>One compositor for every panel on the UI thread, and the way bitmaps reach it.</summary>
    [ThreadStatic]
    private static Compositor? _compositor;

    [ThreadStatic]
    private static CompositionImages? _images;

    private readonly nint _hwnd;

    /// <summary>The empty window over the card that takes the pointer; the drawing window lets it through.</summary>
    private readonly nint _input;

    private readonly PreviewPlace _place;
    private readonly Int32Rect _stage;
    private readonly bool _animate;
    private readonly Palette _palette;

    private readonly DesktopWindowTarget _target;
    private readonly ContainerVisual _root;
    private readonly ShapeVisual _shadow;
    private readonly CompositionRoundedRectangleGeometry[] _rings;
    private readonly ShapeVisual _body;
    private readonly CompositionRoundedRectangleGeometry _bodyShape;
    private readonly CompositionSpriteShape _bodySprite;
    private readonly ContainerVisual _content;
    private readonly CompositionRoundedRectangleGeometry _contentShape;

    /// <summary>The cards shown, and the ones a glide is fading out; null when none is.</summary>
    private CardSet _cards;
    private CardSet? _leaving;

    /// <summary>The card's movement, in physical screen pixels: from where to where, how, and since when.</summary>
    private Int32Rect _from;
    private Int32Rect _to;
    private CubicBezier _curve;
    private double _moveMs;
    private long _moveStart;
    private bool _moving;

    /// <summary>The cross-fade from <see cref="_leaving"/> to <see cref="_cards"/>.</summary>
    private long _fadeStart;

    /// <summary>The whole card's fade in or out: which, from what opacity, how and since when.</summary>
    private bool _fading;
    private bool _fadingIn;
    private double _alphaFrom;
    private CubicBezier _alphaCurve;
    private double _alphaMs;
    private long _alphaStart;

    /// <summary>The card as last drawn, and where the input window was last put, in the drawing window's pixels.</summary>
    private Int32Rect _drawn;
    private Int32Rect? _inputAt;

    /// <summary>What steps the movement; null while nothing moves.</summary>
    private CompositorClock? _clock;

    private bool _shown;
    private bool _closing;
    private bool _disposed;

    /// <summary>The card a button went down on, and which button; -1 for none.</summary>
    private int _pressed = -1;

    private bool _pressedClose;
    private bool _pressedMiddle;

    private PreviewWindow(
        nint hwnd,
        nint input,
        PreviewPlace place,
        Int32Rect stage,
        bool animate,
        Compositor compositor,
        DesktopWindowTarget target,
        IReadOnlyList<PreviewEntry> entries,
        Func<IReadOnlyList<Size>, PreviewArrangement> arrange)
    {
        _hwnd = hwnd;
        _input = input;
        _place = place;
        _stage = stage;
        _animate = animate;
        _target = target;
        _palette = new Palette(!UsesLightTheme());

        _root = compositor.CreateContainerVisual();
        _root.Size = new Vector2(stage.Width, stage.Height);

        // The shadow, ring for ring as the bar's, under the card.
        _shadow = compositor.CreateShapeVisual();
        _shadow.Size = _root.Size;
        _rings = new CompositionRoundedRectangleGeometry[BarShadow.RingCount];
        Span<BarShadow.Ring> layout = stackalloc BarShadow.Ring[BarShadow.RingCount];
        BarShadow.Rings(default, 0, 1, layout);
        var ringBrushes = BarShadow.Passes.Select(pass => compositor.CreateColorBrush(CompositionColor.FromArgb(pass.Alpha, 0, 0, 0))).ToArray();
        for (var i = 0; i < _rings.Length; i++)
        {
            _rings[i] = compositor.CreateRoundedRectangleGeometry();
            var ring = compositor.CreateSpriteShape(_rings[i]);
            ring.FillBrush = ringBrushes[layout[i].Pass];
            _shadow.Shapes.Add(ring);
        }

        // The card: Windows' flyout colour, solid, with its rim.
        _body = compositor.CreateShapeVisual();
        _body.Size = _root.Size;
        _bodyShape = compositor.CreateRoundedRectangleGeometry();
        _bodySprite = compositor.CreateSpriteShape(_bodyShape);
        _bodySprite.FillBrush = compositor.CreateColorBrush(_palette.Card);
        _bodySprite.StrokeBrush = compositor.CreateColorBrush(_palette.Rim);
        _bodySprite.StrokeThickness = (float)place.Scale;
        _body.Shapes.Add(_bodySprite);

        // What is on it, cut to it.
        _content = compositor.CreateContainerVisual();
        _content.Size = _root.Size;
        _contentShape = compositor.CreateRoundedRectangleGeometry();
        _content.Clip = compositor.CreateGeometricClip(_contentShape);

        _root.Children.InsertAtTop(_shadow);
        _root.Children.InsertAtTop(_body);
        _root.Children.InsertAtTop(_content);
        target.Root = _root;

        _cards = CardSet.Make(this, compositor, entries, arrange);
        _content.Children.InsertAtTop(_cards.Visual);

        var place0 = _cards.Arrangement.Panel;
        if (_animate)
        {
            // Up the gap it keeps above the dock, and in.
            Move(Shifted(place0, Rise()), place0, PreviewMotion.Rise, PreviewMotion.RiseMs);
            Fade(fadeIn: true);
        }
        else
        {
            _to = place0;
        }

        Draw(Stopwatch.GetTimestamp());
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNA);
        _shown = true;
        BringToTop();
    }

    /// <summary>
    /// Opens a panel on <paramref name="place"/>'s display showing <paramref name="entries"/>,
    /// laid out by <paramref name="arrange"/> once their windows' sizes are known; null when the
    /// compositor is not to be had.
    /// </summary>
    public static PreviewWindow? TryCreate(
        PreviewPlace place,
        IReadOnlyList<PreviewEntry> entries,
        Func<IReadOnlyList<Size>, PreviewArrangement> arrange,
        bool animate)
    {
        // Down to the dock's hover zone, not only to the line the card stands above: the card's
        // shadow falls below it, and a window ending at that line cut it off (seen 2026-11-20).
        // The window lets the pointer through, so reaching over the held item's label costs
        // nothing; it must not reach into the hover zone, where the dock counts it as covering.
        var stage = new Int32Rect(
            place.WorkArea.X,
            place.WorkArea.Y,
            Math.Max(1, place.WorkArea.Width),
            Math.Max(1, Math.Max(place.Floor, place.DockTop) - place.WorkArea.Y));

        if (SharedCompositor() is not { } compositor)
        {
            return null;
        }

        Register();

        // Click-through, as the dock's sheet is: layered as well as transparent, since transparent
        // alone is not reliably passed by (see WindowChrome.IsClickThrough). A region cannot do
        // it — a region cuts what the compositor draws, and cut the card's shadow off at its edge.
        var hwnd = Popup(
            NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED,
            "ArtDock.Previews",
            stage);

        // A layered window shows nothing until told how; fully opaque, the visuals carrying their own alpha.
        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 0xFF, NativeMethods.LWA_ALPHA);

        // What takes the pointer: an empty window over the card, which draws nothing and so can be
        // moved every frame without anything to show late. Shown once the card is.
        var input = Popup(0, "ArtDock.Previews.Input", new Int32Rect(stage.X, stage.Y, 1, 1));

        DesktopWindowTarget target;
        try
        {
            compositor.As<ICompositorDesktopInterop>().CreateDesktopWindowTarget(hwnd, isTopmost: false, out var abi);
            target = MarshalInterface<DesktopWindowTarget>.FromAbi(abi);
            Marshal.Release(abi);
        }
        catch (Exception e) when (e is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            NativeMethods.DestroyWindow(input);
            NativeMethods.DestroyWindow(hwnd);
            return null;
        }

        var window = new PreviewWindow(hwnd, input, place, stage, animate, compositor, target, entries, arrange);
        Open[input] = window;
        return window;
    }

    /// <summary>
    /// One of the panel's windows: topmost, never active, with no redirection bitmap — clear
    /// wherever the compositor draws nothing — and nothing of DWM's own round it.
    /// </summary>
    private static nint Popup(uint exStyle, string name, Int32Rect rect)
    {
        var hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST
                | NativeMethods.WS_EX_NOREDIRECTIONBITMAP | exStyle,
            ClassName,
            name,
            NativeMethods.WS_POPUP,
            rect.X, rect.Y, rect.Width, rect.Height,
            0, 0, NativeMethods.GetModuleHandle(null), 0);

        if (hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        DesktopComposition.SetRoundedCorners(hwnd, rounded: false);
        var none = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref none, sizeof(int));
        return hwnd;
    }

    /// <summary>A card was clicked: go to its window.</summary>
    public event EventHandler<nint>? Chosen;

    /// <summary>A card's close button, or a middle click on it: close its window.</summary>
    public event EventHandler<nint>? CloseRequested;

    public nint Hwnd => _hwnd;

    /// <summary>Where the card is, or is gliding to, in physical screen pixels.</summary>
    public Int32Rect Bounds => _to;

    /// <summary>The windows shown, in order.</summary>
    public IReadOnlyList<PreviewEntry> Entries => _cards.Entries;

    /// <summary>Whether this panel can show previews at <paramref name="place"/>: the same display, scale and floor.</summary>
    public bool Fits(PreviewPlace place) =>
        place.WorkArea == _place.WorkArea && place.Scale == _place.Scale && place.Floor == _place.Floor
        && place.DockTop == _place.DockTop;

    /// <summary>Each window's size as DWM pictures it, in pixels, for laying the cards out.</summary>
    public IReadOnlyList<Size> SourceSizes() => _cards.SourceSizes();

    /// <summary>
    /// Shows another app's windows: the card glides to where theirs goes and takes its shape, the
    /// cards on it cross-fading into theirs.
    /// </summary>
    public void Show(IReadOnlyList<PreviewEntry> entries, Func<IReadOnlyList<Size>, PreviewArrangement> arrange)
    {
        if (_disposed || _closing || SharedCompositor() is not { } compositor)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var here = CardAt(now);

        // One set on its way out at a time: a third app in quick succession sends the first
        // on its way at once.
        _leaving?.Dispose();
        _leaving = _cards;
        _leaving.Hover(-1, false);

        _cards = CardSet.Make(this, compositor, entries, arrange);
        _content.Children.InsertAtTop(_cards.Visual);
        _pressed = -1;

        if (_animate)
        {
            _fadeStart = now;
            Move(here, _cards.Arrangement.Panel, PreviewMotion.Glide, PreviewMotion.GlideMs);
        }
        else
        {
            _leaving.Dispose();
            _leaving = null;
            _moving = false;
            _to = _cards.Arrangement.Panel;
        }

        Draw(now);
    }

    /// <summary>
    /// Shows the same app's windows anew — one opened or closed, a title changed: the cards are
    /// made again where anything about them changed, and the card glides to its new shape.
    /// </summary>
    public void Update(IReadOnlyList<PreviewEntry> entries, Func<IReadOnlyList<Size>, PreviewArrangement> arrange)
    {
        if (_disposed || _closing)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        _cards.Update(entries, arrange);
        var panel = _cards.Arrangement.Panel;
        if (panel != _to)
        {
            if (!_animate)
            {
                _to = panel;
            }
            else if (_moving)
            {
                // Already on its way: on to the new place, on the same curve, without starting again.
                _to = panel;
            }
            else
            {
                Move(_to, panel, PreviewMotion.Glide, PreviewMotion.GlideMs);
            }
        }

        Draw(now);
    }

    /// <summary>Lights the card under a point on the screen, in physical pixels; none when it is off the panel.</summary>
    public void PointAt(int screenX, int screenY)
    {
        if (_disposed || _closing)
        {
            return;
        }

        var (x, y) = _cards.Local(screenX - _stage.X, screenY - _stage.Y);
        var card = _cards.CardAt(x, y);
        _cards.Hover(card, card >= 0 && Contains(_cards.Arrangement.Cards[card].Close, x, y));
    }

    /// <summary>The window whose card is under a point on the screen, in physical pixels; zero for none, or while closing.</summary>
    public nint WindowAt(int screenX, int screenY)
    {
        if (_disposed || _closing)
        {
            return 0;
        }

        var (x, y) = _cards.Local(screenX - _stage.X, screenY - _stage.Y);
        var card = _cards.CardAt(x, y);
        return card >= 0 && card < _cards.Entries.Count ? _cards.Entries[card].Window : 0;
    }

    /// <summary>The panel's windows: the one it is drawn in and the one that takes the pointer.</summary>
    public (nint Drawing, nint Input) Windows => (_hwnd, _input);

    /// <summary>Keeps the panel above the dock, its sheet and its handle: topmost, and at the top of the topmost band.</summary>
    public void BringToTop()
    {
        if (_disposed)
        {
            return;
        }

        // The input window last, so it is the one on top — though the drawing window lets the
        // pointer through anyway.
        foreach (var window in (ReadOnlySpan<nint>)[_hwnd, _input])
        {
            NativeMethods.SetWindowPos(
                window, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// Closes the panel: the card fades out gliding up, away from the dock, and the panel goes —
    /// or at once when it does not animate. It shows nothing new and answers no clicks meanwhile.
    /// </summary>
    public void Close()
    {
        if (_disposed || _closing)
        {
            return;
        }

        _closing = true;
        if (!_animate || !_shown)
        {
            Dispose();
            return;
        }

        _cards.Hover(-1, false);

        // Nothing to click on any more: the pointer goes straight through to what is under it.
        NativeMethods.ShowWindow(_input, NativeMethods.SW_HIDE);
        _inputAt = null;

        Fade(fadeIn: false);

        // Up and away from the dock as it goes, on the fade's curve and in its time — on from
        // wherever a glide or the rise had got to.
        var here = CardAt(Stopwatch.GetTimestamp());
        Move(here, Shifted(here, -Rise()), PreviewMotion.FadeOut, _alphaMs);
        Draw(Stopwatch.GetTimestamp());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clock?.Dispose();
        _clock = null;
        Open.Remove(_input);

        _leaving?.Dispose();
        _leaving = null;
        _cards.Dispose();

        _target.Root = null;
        _root.Children.RemoveAll();
        _content.Clip = null;
        _shadow.Shapes.Clear();
        _body.Shapes.Clear();
        _target.Dispose();
        NativeMethods.DestroyWindow(_input);
        NativeMethods.DestroyWindow(_hwnd);
    }

    /// <summary>The compositor of this thread, made the first time; null where there is none to be had.</summary>
    private static Compositor? SharedCompositor()
    {
        if (_compositor is not null)
        {
            return _compositor;
        }

        try
        {
            CompositionBackdrop.EnsureDispatcherQueue();
            _compositor = new Compositor();
            return _compositor;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or UnauthorizedAccessException
            or TypeLoadException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The way titles reach the compositor, made again if the last one's device was lost.</summary>
    private static CompositionImages? Images(Compositor compositor)
    {
        if (_images is { IsBroken: true })
        {
            _images.Dispose();
            _images = null;
        }

        return _images ??= CompositionImages.TryCreate(compositor);
    }

    private static void Register()
    {
        if (_registered)
        {
            return;
        }

        var windowClass = new NativeMethods.WindowClass
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.WindowClass>(),
            WindowProc = Marshal.GetFunctionPointerForDelegate(Procedure),
            Instance = NativeMethods.GetModuleHandle(null),
            Cursor = LoadCursor(0, IdcArrow),
            ClassName = ClassName,
        };

        NativeMethods.RegisterClassEx(ref windowClass);
        _registered = true;
    }

    /// <summary>How far below its place the card starts as it opens, in pixels.</summary>
    private int Rise() => (int)Math.Round(PreviewMotion.RiseDips * _place.Scale);

    private static Int32Rect Shifted(Int32Rect rect, int down) => new(rect.X, rect.Y + down, rect.Width, rect.Height);

    /// <summary>Starts the card moving, and the clock that moves it.</summary>
    private void Move(Int32Rect from, Int32Rect to, CubicBezier curve, double ms)
    {
        _from = from;
        _to = to;
        _curve = curve;
        _moveMs = ms;
        _moveStart = Stopwatch.GetTimestamp();
        _moving = true;
        StartClock();
    }

    /// <summary>
    /// Starts the whole card fading in from nothing, or out from as far as it has got — the card,
    /// its shadow, everything on it and the pictures, which the compositor's opacity does not
    /// reach and are given theirs with it.
    /// </summary>
    private void Fade(bool fadeIn)
    {
        var now = Stopwatch.GetTimestamp();
        _alphaFrom = fadeIn ? 0 : CardAlpha(now);
        _fadingIn = fadeIn;
        _alphaCurve = fadeIn ? PreviewMotion.Rise : PreviewMotion.FadeOut;
        _alphaMs = fadeIn ? PreviewMotion.RiseMs : PreviewMotion.FadeOutMs * _alphaFrom;
        _alphaStart = now;
        _fading = true;
        StartClock();
    }

    /// <summary>The whole card's opacity at a time, 0 to 1.</summary>
    private double CardAlpha(long now)
    {
        // Not fading is fully shown: a fade out that has finished has disposed the panel. Not
        // "closing, so nothing" — Close marks the panel closing before it asks how far a fade
        // has got, and that read every fade out as starting from nothing, and so over at once.
        if (!_fading)
        {
            return 1;
        }

        var eased = _alphaCurve.Ease(PreviewMotion.Progress(Stopwatch.GetElapsedTime(_alphaStart, now).TotalMilliseconds, _alphaMs));
        return _fadingIn ? eased : _alphaFrom * (1 - eased);
    }

    private void StartClock() =>
        _clock ??= new CompositorClock(System.Windows.Threading.Dispatcher.CurrentDispatcher, OnFrame);

    /// <summary>Where the card is at a time, in physical screen pixels.</summary>
    private Int32Rect CardAt(long now) =>
        _moving
            ? PreviewMotion.Between(_from, _to, _curve.Ease(PreviewMotion.Progress(Stopwatch.GetElapsedTime(_moveStart, now).TotalMilliseconds, _moveMs)))
            : _to;

    private void OnFrame(long now)
    {
        if (_disposed)
        {
            return;
        }

        Draw(now);
    }

    /// <summary>
    /// Puts everything where it is at a time — the card, its shadow, the cards on it, their
    /// pictures and the input window — in one go, so they reach the screen on the same frame.
    /// </summary>
    private void Draw(long now)
    {
        var moved = _moving ? PreviewMotion.Progress(Stopwatch.GetElapsedTime(_moveStart, now).TotalMilliseconds, _moveMs) : 1;
        var card = CardAt(now);
        if (moved >= 1)
        {
            _moving = false;
        }

        var alpha = CardAlpha(now);
        if (_fading && PreviewMotion.Progress(Stopwatch.GetElapsedTime(_alphaStart, now).TotalMilliseconds, _alphaMs) >= 1)
        {
            _fading = false;
        }

        if (_closing && !_fading)
        {
            Dispose();
            return;
        }

        _root.Opacity = (float)alpha;

        var client = new Int32Rect(card.X - _stage.X, card.Y - _stage.Y, card.Width, card.Height);
        var radius = CardRadius * _place.Scale;

        if (client != _drawn)
        {
            _drawn = client;
            SetRoundedRect(_bodyShape, client.X + (_place.Scale / 2), client.Y + (_place.Scale / 2), client.Width - _place.Scale, client.Height - _place.Scale, radius);
            SetRoundedRect(_contentShape, client.X, client.Y, client.Width, client.Height, radius);

            Span<BarShadow.Ring> rings = stackalloc BarShadow.Ring[BarShadow.RingCount];
            BarShadow.Rings(new Rect(client.X, client.Y, client.Width, client.Height), radius, _place.Scale, rings);
            for (var i = 0; i < _rings.Length; i++)
            {
                var ring = rings[i];
                SetRoundedRect(_rings[i], ring.Bounds.X, ring.Bounds.Y, ring.Bounds.Width, ring.Bounds.Height, ring.Radius);
            }
        }

        // What of the card can be seen: the window's edge cuts it at the floor.
        var visible = Intersect(client, new Int32Rect(0, 0, _stage.Width, _stage.Height));

        var fade = 1.0;
        if (_leaving is { } leaving)
        {
            fade = PreviewMotion.Glide.Ease(PreviewMotion.Progress(Stopwatch.GetElapsedTime(_fadeStart, now).TotalMilliseconds, PreviewMotion.GlideMs));
            if (fade >= 1 || !_animate)
            {
                leaving.Dispose();
                _leaving = null;
                fade = 1;
            }
            else
            {
                leaving.Place(client, visible, 1 - fade, alpha);
            }
        }

        _cards.Place(client, visible, fade, alpha);
        PlaceInput(visible);

        if (!_moving && !_fading && _leaving is null)
        {
            _clock?.Dispose();
            _clock = null;
        }
    }

    /// <summary>
    /// Puts the input window over what can be seen of the card — where the pointer is the
    /// panel's — or hides it when none can, or the panel is closing. Only when that has changed.
    /// </summary>
    private void PlaceInput(Int32Rect visible)
    {
        var wanted = _closing || visible.Width <= 0 || visible.Height <= 0 ? (Int32Rect?)null : visible;
        if (_inputAt == wanted)
        {
            return;
        }

        _inputAt = wanted;
        if (wanted is not { } rect)
        {
            NativeMethods.ShowWindow(_input, NativeMethods.SW_HIDE);
            return;
        }

        NativeMethods.SetWindowPos(
            _input, 0, _stage.X + rect.X, _stage.Y + rect.Y, rect.Width, rect.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | SwpShowWindow);
    }

    private static Int32Rect Intersect(Int32Rect a, Int32Rect b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return right > left && bottom > top ? new Int32Rect(left, top, right - left, bottom - top) : new Int32Rect(0, 0, 0, 0);
    }

    /// <summary>Clamped, because a radius over half the shorter side makes the geometry invalid rather than simply rounder.</summary>
    private static void SetRoundedRect(CompositionRoundedRectangleGeometry geometry, double x, double y, double width, double height, double radius)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        var corner = (float)Math.Clamp(radius, 0, Math.Min(width, height) / 2);
        geometry.Offset = new Vector2((float)x, (float)y);
        geometry.Size = new Vector2((float)width, (float)height);
        geometry.CornerRadius = new Vector2(corner, corner);
    }

    private static bool Contains(Int32Rect rect, int x, int y) =>
        x >= rect.X && y >= rect.Y && x < rect.X + rect.Width && y < rect.Y + rect.Height;

    private static nint OnWindowMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        // Sized in device pixels by the dock, and by nothing else.
        if (msg == NativeMethods.WM_DPICHANGED)
        {
            return 0;
        }

        if (msg == WM_MOUSEACTIVATE)
        {
            return MA_NOACTIVATE;
        }

        if (msg != WM_SETCURSOR && Open.TryGetValue(hwnd, out var window) && window.OnMouse((int)msg, lParam))
        {
            return 0;
        }

        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>The panel's own mouse messages; true when one was answered.</summary>
    private bool OnMouse(int msg, nint lParam)
    {
        if (_disposed || _closing)
        {
            return false;
        }

        switch (msg)
        {
            case WM_MOUSEMOVE:
                if (NativeMethods.GetCursorPos(out var cursor))
                {
                    PointAt(cursor.X, cursor.Y);
                }

                return false;

            case NativeMethods.WM_LBUTTONDOWN:
            case NativeMethods.WM_MBUTTONDOWN:
            {
                var (x, y) = _cards.Local(StagePoint(lParam));
                _pressed = _cards.CardAt(x, y);
                _pressedMiddle = msg == NativeMethods.WM_MBUTTONDOWN;
                _pressedClose = _pressed >= 0 && !_pressedMiddle
                    && _cards.Entries[_pressed].CanClose && Contains(_cards.Arrangement.Cards[_pressed].Close, x, y);
                if (_pressed >= 0)
                {
                    NativeMethods.SetCapture(_input);
                }

                return true;
            }

            case NativeMethods.WM_LBUTTONUP:
            case NativeMethods.WM_MBUTTONUP:
            {
                var (x, y) = _cards.Local(StagePoint(lParam));
                var card = _cards.CardAt(x, y);
                var pressed = _pressed;
                var close = _pressedClose;
                var middle = _pressedMiddle;
                _pressed = -1;
                NativeMethods.ReleaseCapture();

                if (pressed < 0 || card != pressed || middle != (msg == NativeMethods.WM_MBUTTONUP) || pressed >= _cards.Entries.Count)
                {
                    return true;
                }

                var target = _cards.Entries[pressed].Window;
                if (middle || close)
                {
                    // A middle click closes, as on the taskbar — where the dock can.
                    if (close || _cards.Entries[pressed].CanClose)
                    {
                        CloseRequested?.Invoke(this, target);
                    }
                }
                else if (!Contains(_cards.Arrangement.Cards[pressed].Close, x, y) || !_cards.Entries[pressed].CanClose)
                {
                    Chosen?.Invoke(this, target);
                }

                return true;
            }

            case NativeMethods.WM_CAPTURECHANGED:
                _pressed = -1;
                return false;
        }

        return false;
    }

    /// <summary>
    /// A mouse message's point in the drawing window's pixels: given in the input window's, which
    /// lies over the card where it was last put.
    /// </summary>
    private (int X, int Y) StagePoint(nint lParam)
    {
        var value = (int)(lParam.ToInt64() & 0xFFFF_FFFF);
        var origin = _inputAt ?? default;
        return ((short)(value & 0xFFFF) + origin.X, (short)((value >> 16) & 0xFFFF) + origin.Y);
    }

    /// <summary>Whether Windows is in light mode — the taskbar's and Start's setting, which its flyouts follow.</summary>
    private static bool UsesLightTheme() => ReadPersonalize("SystemUsesLightTheme", fallback: false);

    private static bool ReadPersonalize(string name, bool fallback)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(name) is int value ? value != 0 : fallback;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }

    /// <summary>Windows' flyout colours for the light or dark mode.</summary>
    private sealed class Palette(bool dark)
    {
        public bool Dark { get; } = dark;

        /// <summary>The card: solid, as Windows' previews have no blur behind them.</summary>
        public CompositionColor Card { get; } = dark ? CompositionColor.FromArgb(0xFF, 0x2C, 0x2C, 0x2C) : CompositionColor.FromArgb(0xFF, 0xF3, 0xF3, 0xF3);

        /// <summary>Its rim, where DWM drew one round the window before.</summary>
        public CompositionColor Rim { get; } = dark ? CompositionColor.FromArgb(0x1F, 0xFF, 0xFF, 0xFF) : CompositionColor.FromArgb(0x14, 0x00, 0x00, 0x00);

        public CompositionColor Hover { get; } = dark ? CompositionColor.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : CompositionColor.FromArgb(0x12, 0x00, 0x00, 0x00);

        public CompositionColor CloseHover { get; } = CompositionColor.FromArgb(0xFF, 0xC4, 0x2B, 0x1C);

        public Brush Text { get; } = Frozen(dark ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1A, 0x1A, 0x1A));

        public Brush CloseText { get; } = Brushes.White;

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// One app's windows on the card: a card for each, laid out for a panel of their own size,
    /// with the pictures registered against the panel's window. Two are on the card during a
    /// glide, one fading out over the other.
    /// </summary>
    private sealed class CardSet : IDisposable
    {
        private static readonly Typeface TitleFace = new(
            new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private static readonly Typeface SymbolFace = new(
            new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        /// <summary>ChromeClose, in both symbol fonts.</summary>
        private const string CloseGlyph = "";

        private const double TitleSize = 12;
        private const double IconSize = 16;
        private const double HoverRadius = 6;
        private const double CloseRadius = 4;
        private const double CloseGlyphSize = 10;

        private readonly PreviewWindow _owner;
        private readonly Compositor _compositor;
        private readonly List<DwmThumbnail?> _thumbnails = [];
        private readonly List<CardVisual> _visuals = [];

        private int _hovered = -1;
        private bool _hoveredClose;

        /// <summary>Where the set's top left was last put, in the window's pixels.</summary>
        private (int X, int Y) _origin;

        private CardSet(PreviewWindow owner, Compositor compositor)
        {
            _owner = owner;
            _compositor = compositor;
            Visual = compositor.CreateContainerVisual();
            Entries = [];
            Arrangement = new PreviewArrangement(Int32Rect.Empty, false, []);
        }

        public ContainerVisual Visual { get; }

        public IReadOnlyList<PreviewEntry> Entries { get; private set; }

        public PreviewArrangement Arrangement { get; private set; }

        public static CardSet Make(
            PreviewWindow owner, Compositor compositor, IReadOnlyList<PreviewEntry> entries, Func<IReadOnlyList<Size>, PreviewArrangement> arrange)
        {
            var set = new CardSet(owner, compositor);
            set.Update(entries, arrange);
            return set;
        }

        public IReadOnlyList<Size> SourceSizes() =>
            [.. _thumbnails.Select(thumbnail => thumbnail?.SourceSize() ?? Size.Empty)];

        /// <summary>
        /// Takes these windows: pictures registered for the windows new to a card, the cards laid
        /// out for their sizes, and drawn again — every card, since a change of size moves them all.
        /// </summary>
        public void Update(IReadOnlyList<PreviewEntry> entries, Func<IReadOnlyList<Size>, PreviewArrangement> arrange)
        {
            // Registered first, hidden, for their sizes: the layout needs them.
            Register(entries, list: false);
            var arrangement = arrange(SourceSizes());

            // Laid out as a list after all: no pictures.
            if (arrangement.IsList)
            {
                Register(entries, list: true);
            }

            var redraw = !ReferenceEquals(Entries, entries) || arrangement != Arrangement;
            Entries = entries;
            Arrangement = arrangement;
            Visual.Size = new Vector2(arrangement.Panel.Width, arrangement.Panel.Height);
            if (redraw)
            {
                Redraw();
            }
        }

        /// <summary>A point in the window's pixels as a point on this set's cards.</summary>
        public (int X, int Y) Local(int x, int y) => (x - _origin.X, y - _origin.Y);

        public (int X, int Y) Local((int X, int Y) point) => Local(point.X, point.Y);

        public int CardAt(int x, int y)
        {
            for (var i = 0; i < Arrangement.Cards.Count; i++)
            {
                if (Contains(Arrangement.Cards[i].Card, x, y))
                {
                    return i;
                }
            }

            return -1;
        }

        public void Hover(int card, bool onClose)
        {
            if (card == _hovered && onClose == _hoveredClose)
            {
                return;
            }

            _hovered = card;
            _hoveredClose = onClose;
            for (var i = 0; i < _visuals.Count; i++)
            {
                _visuals[i].Hover(i == card, i == card && onClose);
            }
        }

        /// <summary>
        /// Puts the set on the card, in the window's pixels: standing on the card's bottom, centred
        /// across it, at an opacity — and each picture with it, cut to what of the card shows.
        /// </summary>
        /// <param name="card">The card, in the window's pixels.</param>
        /// <param name="visible">What of it can be seen.</param>
        /// <param name="opacity">This set's own, in a cross-fade.</param>
        /// <param name="cardAlpha">The whole card's, as it fades in or out: the compositor applies it
        /// to the visuals, being the card's, and the pictures are given it here.</param>
        public void Place(Int32Rect card, Int32Rect visible, double opacity, double cardAlpha)
        {
            var panel = Arrangement.Panel;
            _origin = PreviewMotion.Anchor(card, panel.Width, panel.Height);
            Visual.Offset = new Vector3(_origin.X, _origin.Y, 0);
            Visual.Opacity = (float)Math.Clamp(opacity, 0, 1);

            var alpha = (byte)Math.Round(255 * Math.Clamp(opacity * cardAlpha, 0, 1));
            var clip = Intersect(card, visible);
            for (var i = 0; i < _thumbnails.Count; i++)
            {
                if (_thumbnails[i] is not { } thumbnail)
                {
                    continue;
                }

                var picture = i < Arrangement.Cards.Count ? Arrangement.Cards[i].Picture : Int32Rect.Empty;
                var where = new Int32Rect(_origin.X + picture.X, _origin.Y + picture.Y, picture.Width, picture.Height);
                if (PreviewMotion.Cut(where, clip, thumbnail.SourceSize()) is { } cut)
                {
                    var whole = cut.Destination == where;
                    thumbnail.Show(cut.Destination, whole ? null : cut.Source, alpha);
                }
                else
                {
                    thumbnail.Show(Int32Rect.Empty);
                }
            }
        }

        public void Dispose()
        {
            foreach (var thumbnail in _thumbnails)
            {
                thumbnail?.Dispose();
            }

            _thumbnails.Clear();
            foreach (var visual in _visuals)
            {
                visual.Dispose();
            }

            _visuals.Clear();
            Visual.Children.RemoveAll();
            Visual.Parent?.Children.Remove(Visual);
            Visual.Dispose();
        }

        /// <summary>A picture for each window, kept where the window in a card has not changed.</summary>
        private void Register(IReadOnlyList<PreviewEntry> entries, bool list)
        {
            for (var i = 0; i < Math.Max(entries.Count, _thumbnails.Count); i++)
            {
                var wanted = i < entries.Count && !list ? entries[i].Window : 0;
                var current = i < _thumbnails.Count ? _thumbnails[i] : null;
                if ((current?.Source ?? 0) == wanted && (wanted == 0 || current is not null))
                {
                    continue;
                }

                current?.Dispose();
                var replacement = wanted == 0 ? null : DwmThumbnail.Register(_owner._hwnd, wanted);
                if (i < _thumbnails.Count)
                {
                    _thumbnails[i] = replacement;
                }
                else
                {
                    _thumbnails.Add(replacement);
                }
            }

            while (_thumbnails.Count > entries.Count)
            {
                _thumbnails[^1]?.Dispose();
                _thumbnails.RemoveAt(_thumbnails.Count - 1);
            }
        }

        private void Redraw()
        {
            foreach (var visual in _visuals)
            {
                visual.Dispose();
            }

            _visuals.Clear();
            Visual.Children.RemoveAll();

            var images = Images(_compositor);
            var scale = _owner._place.Scale;
            var palette = _owner._palette;
            for (var i = 0; i < Arrangement.Cards.Count && i < Entries.Count; i++)
            {
                var visual = new CardVisual(_compositor, images, Arrangement.Cards[i], Entries[i], scale, palette);
                visual.Hover(i == _hovered, i == _hovered && _hoveredClose);
                Visual.Children.InsertAtTop(visual.Root);
                _visuals.Add(visual);
            }

            if (_hovered >= _visuals.Count)
            {
                _hovered = -1;
            }
        }

        /// <summary>
        /// One card's visuals: the hover's highlight, the title — written twice, the full width
        /// and short of the close button, shown as the pointer is off it or on it — and the close
        /// button with its own highlight.
        /// </summary>
        private sealed class CardVisual : IDisposable
        {
            private readonly ShapeVisual _hover;
            private readonly SpriteVisual? _title;
            private readonly SpriteVisual? _shortTitle;
            private readonly ShapeVisual? _closeHover;
            private readonly SpriteVisual? _close;
            private readonly SpriteVisual? _closeLit;
            private readonly List<IDisposable> _owned = [];

            public CardVisual(Compositor compositor, CompositionImages? images, PreviewCard card, PreviewEntry entry, double scale, Palette palette)
            {
                Root = compositor.CreateContainerVisual();

                _hover = Rounded(compositor, card.Card, HoverRadius * scale, palette.Hover);
                _hover.IsVisible = false;
                Root.Children.InsertAtTop(_hover);

                var titleRight = card.Card.X + card.Card.Width - (card.Title.X - card.Card.X);
                var fullWidth = Math.Max(1, titleRight - card.Title.X);
                var shortWidth = Math.Max(1, Math.Min(card.Title.X + card.Title.Width, card.Close.X - (int)Math.Round(4 * scale)) - card.Title.X);

                _title = Image(compositor, images, Title(entry, fullWidth, card.Title.Height, scale, palette.Text), card.Title.X, card.Title.Y);
                if (entry.CanClose)
                {
                    _shortTitle = Image(compositor, images, Title(entry, shortWidth, card.Title.Height, scale, palette.Text), card.Title.X, card.Title.Y);
                    _closeHover = Rounded(compositor, card.Close, CloseRadius * scale, palette.CloseHover);
                    _closeHover.IsVisible = false;
                    Root.Children.InsertAtTop(_closeHover);
                    _close = Image(compositor, images, Glyph(card.Close, scale, palette.Text), card.Close.X, card.Close.Y);
                    _closeLit = Image(compositor, images, Glyph(card.Close, scale, palette.CloseText), card.Close.X, card.Close.Y);
                }

                Hover(false, false);
            }

            public ContainerVisual Root { get; }

            public void Hover(bool hovered, bool onClose)
            {
                _hover.IsVisible = hovered;
                var closeShown = hovered && _close is not null;
                Show(_title, !closeShown);
                Show(_shortTitle, closeShown);
                Show(_close, closeShown && !onClose);
                Show(_closeLit, closeShown && onClose);
                if (_closeHover is not null)
                {
                    _closeHover.IsVisible = closeShown && onClose;
                }
            }

            public void Dispose()
            {
                Root.Children.RemoveAll();
                foreach (var owned in _owned)
                {
                    owned.Dispose();
                }

                Root.Dispose();
            }

            private static void Show(CompositionVisual? visual, bool shown)
            {
                if (visual is not null)
                {
                    visual.IsVisible = shown;
                }
            }

            private ShapeVisual Rounded(Compositor compositor, Int32Rect rect, double radius, CompositionColor color)
            {
                var visual = compositor.CreateShapeVisual();
                visual.Offset = new Vector3(rect.X, rect.Y, 0);
                visual.Size = new Vector2(rect.Width, rect.Height);
                var geometry = compositor.CreateRoundedRectangleGeometry();
                SetRoundedRect(geometry, 0, 0, rect.Width, rect.Height, radius);
                var shape = compositor.CreateSpriteShape(geometry);
                var brush = compositor.CreateColorBrush(color);
                shape.FillBrush = brush;
                visual.Shapes.Add(shape);
                _owned.Add(brush);
                _owned.Add(shape);
                _owned.Add(geometry);
                _owned.Add(visual);
                return visual;
            }

            /// <summary>A bitmap on the card at a place, pixel for pixel; nothing when there is no way to put one there.</summary>
            private SpriteVisual? Image(Compositor compositor, CompositionImages? images, BitmapSource? bitmap, int x, int y)
            {
                if (images is null || bitmap is null || images.Upload(bitmap) is not { } surface)
                {
                    return null;
                }

                var brush = compositor.CreateSurfaceBrush(surface);
                brush.Stretch = CompositionStretch.None;
                var sprite = compositor.CreateSpriteVisual();
                sprite.Brush = brush;
                sprite.Offset = new Vector3(x, y, 0);
                sprite.Size = new Vector2(bitmap.PixelWidth, bitmap.PixelHeight);
                Root.Children.InsertAtTop(sprite);
                _owned.Add(sprite);
                _owned.Add(brush);
                _owned.Add(surface);
                return sprite;
            }

            /// <summary>The title row — the item's icon, then the window's title — drawn by WPF at the display's scale.</summary>
            private static BitmapSource? Title(PreviewEntry entry, int width, int height, double scale, Brush text)
            {
                if (width <= 0 || height <= 0)
                {
                    return null;
                }

                var dipWidth = width / scale;
                var dipHeight = height / scale;
                var drawing = new DrawingVisual();
                using (var context = drawing.RenderOpen())
                {
                    var left = 0.0;
                    if (entry.Icon is { } icon)
                    {
                        context.DrawImage(icon, new Rect(0, (dipHeight - IconSize) / 2, IconSize, IconSize));
                        left = IconSize + 8;
                    }

                    // Left to right whatever the language: the cards' rectangles are DWM's too.
                    // Only the text reads the language's way.
                    var formatted = new FormattedText(
                        string.IsNullOrWhiteSpace(entry.Title) ? " " : entry.Title,
                        CultureInfo.CurrentUICulture,
                        Localizer.FlowDirection,
                        TitleFace,
                        TitleSize,
                        text,
                        scale)
                    {
                        MaxTextWidth = Math.Max(1, dipWidth - left),
                        MaxLineCount = 1,
                        Trimming = TextTrimming.CharacterEllipsis,
                    };

                    context.DrawText(formatted, new Point(left, (dipHeight - formatted.Height) / 2));
                }

                return Render(drawing, width, height, scale);
            }

            private static BitmapSource? Glyph(Int32Rect close, double scale, Brush color)
            {
                if (close.Width <= 0 || close.Height <= 0)
                {
                    return null;
                }

                var drawing = new DrawingVisual();
                using (var context = drawing.RenderOpen())
                {
                    var glyph = new FormattedText(
                        CloseGlyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, SymbolFace, CloseGlyphSize, color, scale);
                    context.DrawText(glyph, new Point(((close.Width / scale) - glyph.Width) / 2, ((close.Height / scale) - glyph.Height) / 2));
                }

                return Render(drawing, close.Width, close.Height, scale);
            }

            private static RenderTargetBitmap Render(DrawingVisual drawing, int width, int height, double scale)
            {
                var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(drawing);
                bitmap.Freeze();
                return bitmap;
            }
        }
    }

    private const int DwmwaBorderColor = 34;
    private const int IdcArrow = 32512;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    private const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll")]
    private static extern nint LoadCursor(nint instance, int name);

    [ComImport]
    [Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, out nint target);

        void EnsureOnThread(uint threadId);
    }
}
