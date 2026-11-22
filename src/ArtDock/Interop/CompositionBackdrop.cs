using System.Numerics;
using System.Runtime.InteropServices;
using ArtDock.Dock;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace ArtDock.Interop;

/// <summary>
/// The bar, drawn by the compositor: a blurred sheet of whatever is behind a window, cut to a
/// rounded rectangle of any radius, with the bar's fill, rim and shadow drawn with it.
/// </summary>
/// <remarks>
/// <para>
/// The composition API rather than <c>SetWindowCompositionAttribute</c>, for one reason:
/// shape. The accent policy blurs the whole window rect and there is no way to tell it
/// otherwise — a window region is accepted, and <c>GetWindowRgnBox</c> reads the rounded
/// shape back, but DWM composes straight over it. That left a square-cornered slab of blur
/// poking out of a stadium-ended bar, and the only way round it was to shrink the sheet
/// until its corners cleared, which trades the poking corners for unblurred gaps under the
/// bar's ends.
/// </para>
/// <para>
/// A composition visual takes a <see cref="CompositionGeometricClip"/>, which does take a
/// radius. The sheet is then exactly the bar's shape at any roundness, with nothing given
/// up at the ends.
/// </para>
/// <para>
/// The bar itself is drawn here as well, and not by the dock's own window, because the two
/// cannot be made to reach the screen together. Changes made here go to DWM straight from the
/// thread that makes them; the dock's window is drawn by WPF's render thread and handed over
/// with <c>UpdateLayeredWindow</c>. Captured frame by frame, the blur's edge arrived a frame or
/// two ahead of the bar's while it grew or shrank, and by no fixed amount — so no delay could
/// keep two edges that have to coincide on the same frame. Drawn here, the bar's outline, the
/// blur under it and the shadow around it are set in one batch and cannot come apart. Only the
/// icons are left to the dock's window: they sit well inside the bar, where arriving a frame
/// apart moves nothing into view, and the dock tells the sheet of the bar's shape late enough
/// that, most frames, they arrive together (<c>DockBar.RenderedBarRect</c>).
/// </para>
/// <para>
/// The window shadow DWM would give a framed window is no use: DWM shadows a window's rect,
/// so a rectangular window holding a pill-shaped visual would cast a rectangular shadow. The
/// shadow drawn here is the dock's own, ring for ring (<see cref="BarShadow"/>).
/// </para>
/// </remarks>
internal sealed class CompositionBackdrop : IDisposable
{
    private readonly Compositor _compositor;
    private readonly DesktopWindowTarget _target;
    private readonly ContainerVisual _root;

    /// <summary>The shadow's rings, one shape each, under everything else.</summary>
    private readonly ShapeVisual _shadow;
    private readonly CompositionRoundedRectangleGeometry[] _rings;
    private readonly CompositionColorBrush[] _ringBrushes;

    /// <summary>Holds the blur and its wash, sized to the bar and clipped to its shape.</summary>
    private readonly InsetClip _span;
    private readonly ContainerVisual _sheet;
    private readonly CompositionRoundedRectangleGeometry _shape;
    private readonly CompositionGeometricClip _clip;
    private readonly SpriteVisual _blur;
    private readonly SpriteVisual _tint;

    /// <summary>The bare frosting the blur is by default: Windows' own blur of the desktop.</summary>
    private readonly CompositionBrush _frost;

    /// <summary>
    /// The taskbar's acrylic and what it is made from, while the bar matches the taskbar — see
    /// <see cref="SetMaterial"/>. Made the first time it is asked for, so a dock that never
    /// matches the taskbar never makes them.
    /// </summary>
    private CompositionEffectBrush? _acrylic;

    /// <summary>
    /// The shadow with the bar's shape cut out of it, for under the material — see
    /// <see cref="CutShadow"/>. A layer holding the shadow while it is cut, the shape cut out,
    /// and the effect that does the cutting.
    /// </summary>
    private LayerVisual? _cutLayer;

    private ShapeVisual? _cut;
    private CompositionRoundedRectangleGeometry? _cutShape;
    private CompositionSpriteShape? _cutSprite;
    private CompositionColorBrush? _cutBrush;
    private CompositionEffectBrush? _cutEffect;
    private bool _shadowCut;

    private CompositionBackdropBrush? _behind;
    private CompositionImages? _images;
    private CompositionDrawingSurface? _grainSurface;
    private CompositionSurfaceBrush? _grain;
    private bool _grainTried;

    /// <summary>The bar's fill and rim, over the blur.</summary>
    private readonly ShapeVisual _face;
    private readonly CompositionRoundedRectangleGeometry _outline;
    private readonly CompositionSpriteShape _faceShape;
    private readonly CompositionColorBrush _fill;
    private readonly CompositionColorBrush _rim;

    /// <summary>
    /// The sheet's clip, the bar's face and the shadow's cut as paths, for every corner shape
    /// but round — which the compositor's rounded rectangle cannot draw. Made the first time they are
    /// wanted; the rounded rectangles above are kept up to date alongside, and swapped back in
    /// for round corners.
    /// </summary>
    private CompositionPathGeometry? _shapePath;

    private CompositionPathGeometry? _outlinePath;
    private CompositionPathGeometry? _cutPath;

    /// <summary>The corners drawn: anything but round means the paths are.</summary>
    private BarCorners _corners;

    /// <summary>Set once a path has failed to build, after which every shape is drawn round.</summary>
    private bool _noPaths;

    /// <summary>The bar last set, in device pixels, for a cut made after it — see <see cref="TryMakeCut"/>.</summary>
    private (double X, double Y, double Width, double Height, double Radius) _bar;

    private CompositionBackdrop(Compositor compositor, DesktopWindowTarget target, Color tint)
    {
        _compositor = compositor;
        _target = target;

        _root = compositor.CreateContainerVisual();
        _root.RelativeSizeAdjustment = Vector2.One;

        // What reaches past the display's sides is cut off there, as the dock's own drawing is:
        // see SetSpan.
        _span = compositor.CreateInsetClip();
        _root.Clip = _span;

        // The shadow, the rings stacked as the dock stacks them. Nothing punches the bar out of
        // them here: the blur laid over the bar next is opaque, and hides whatever is under it.
        _shadow = compositor.CreateShapeVisual();
        _shadow.RelativeSizeAdjustment = Vector2.One;
        _ringBrushes = [.. BarShadow.Passes.Select(pass => compositor.CreateColorBrush(Color.FromArgb(pass.Alpha, 0, 0, 0)))];
        _rings = new CompositionRoundedRectangleGeometry[BarShadow.RingCount];

        Span<BarShadow.Ring> layout = stackalloc BarShadow.Ring[BarShadow.RingCount];
        BarShadow.Rings(default, 0, 1, layout);
        for (var i = 0; i < _rings.Length; i++)
        {
            _rings[i] = compositor.CreateRoundedRectangleGeometry();
            var ring = compositor.CreateSpriteShape(_rings[i]);
            ring.FillBrush = _ringBrushes[layout[i].Pass];
            _shadow.Shapes.Add(ring);
        }

        // The blur, as a sheet the size of the bar, cut to its shape. Sized to the bar rather
        // than to the window, so DWM is not asked to blur the window's whole width for the
        // sake of the part the clip keeps.
        _sheet = compositor.CreateContainerVisual();
        _shape = compositor.CreateRoundedRectangleGeometry();
        _clip = compositor.CreateGeometricClip(_shape);
        _sheet.Clip = _clip;

        // Blurs what is behind the window, which is what the taskbar shows and what the web
        // component's backdrop-filter does.
        _blur = compositor.CreateSpriteVisual();
        _blur.RelativeSizeAdjustment = Vector2.One;
        _frost = compositor.CreateHostBackdropBrush();
        _blur.Brush = _frost;

        // The host backdrop is bare frosting; without a wash over it the bar reads as washed
        // out rather than as a material.
        _tint = compositor.CreateSpriteVisual();
        _tint.RelativeSizeAdjustment = Vector2.One;
        _tint.Brush = compositor.CreateColorBrush(tint);

        _sheet.Children.InsertAtTop(_blur);
        _sheet.Children.InsertAtTop(_tint);

        // The bar's face: its fill, and its rim drawn over the fill just inside its edge, as
        // the dock draws them with the blur off.
        _face = compositor.CreateShapeVisual();
        _face.RelativeSizeAdjustment = Vector2.One;
        _fill = compositor.CreateColorBrush(default);
        _rim = compositor.CreateColorBrush(default);
        _outline = compositor.CreateRoundedRectangleGeometry();
        _faceShape = compositor.CreateSpriteShape(_outline);
        _faceShape.FillBrush = _fill;
        _faceShape.StrokeBrush = _rim;
        _face.Shapes.Add(_faceShape);

        _root.Children.InsertAtTop(_shadow);
        _root.Children.InsertAtTop(_sheet);
        _root.Children.InsertAtTop(_face);
        target.Root = _root;
    }

    /// <summary>
    /// Attaches a backdrop to a window, or returns null if this machine will not give us one.
    /// </summary>
    /// <param name="tint">Wash drawn over the blur, to keep it from reading as bare frosting.</param>
    public static CompositionBackdrop? TryCreate(nint hwnd, Color tint)
    {
        try
        {
            EnsureDispatcherQueue();

            // Without this the host backdrop brush comes back solid black: it was built for
            // windows that are handed the desktop's blur, and a plain Win32 window has to
            // opt in before DWM will sample anything for it.
            DesktopComposition.EnableHostBackdropSampling(hwnd);

            var compositor = new Compositor();

            // The only route from a Compositor to a target on an ordinary HWND.
            var interop = compositor.As<ICompositorDesktopInterop>();
            interop.CreateDesktopWindowTarget(hwnd, isTopmost: false, out var abi);
            var target = MarshalInterface<DesktopWindowTarget>.FromAbi(abi);
            Marshal.Release(abi);

            return new CompositionBackdrop(compositor, target, tint);
        }
        catch (Exception e) when (e is COMException or InvalidCastException or UnauthorizedAccessException
            or TypeLoadException or DllNotFoundException or EntryPointNotFoundException)
        {
            // No composition on this machine, or the interop moved, or the compositor refused this
            // thread — which it does, as access denied, on a thread with no dispatcher queue. The
            // caller falls back.
            return null;
        }
    }

    /// <summary>
    /// Puts the bar — blur, fill, rim and shadow — at a rounded rectangle inside the window.
    /// Everything is in device pixels, relative to the window's top-left.
    /// </summary>
    /// <param name="scale">Device pixels to a DIP, for the rim's width and the shadow's reach,
    /// which are the dock's in DIPs.</param>
    /// <remarks>
    /// Every part at once, in one batch, which is the point: the compositor sends a batch to
    /// DWM whole, so no frame can show the bar at one place and its blur or shadow at another.
    /// Fractions of a pixel are kept — the bar's width drifts by well under a pixel a frame
    /// near the ends of a wave, and an edge that followed it in whole pixels would step.
    /// </remarks>
    /// <param name="corners">The corners' shape. All but round ones are drawn as paths, and drawn
    /// round where this machine will not build one.</param>
    public void SetShape(
        double x, double y, double width, double height, double radius, double scale, BarCorners corners = BarCorners.Round)
    {
        _bar = (x, y, width, height, radius);
        var shaped = corners != BarCorners.Round && !_noPaths;
        _sheet.Offset = new Vector3((float)x, (float)y, 0);
        _sheet.Size = new Vector2((float)width, (float)height);
        SetRoundedRect(_shape, 0, 0, width, height, radius);

        // The rim is a DIP wide and drawn half a DIP inside the edge, so it lands exactly
        // inside the bar, as the dock's own pen does.
        var inset = scale / 2;
        SetRoundedRect(_outline, x + inset, y + inset, width - scale, height - scale, radius);
        _faceShape.StrokeThickness = (float)scale;

        if (_cutShape is not null)
        {
            SetRoundedRect(_cutShape, x, y, width, height, radius);
        }

        SetCorners(shaped ? corners : BarCorners.Round, x, y, width, height, radius, inset, scale);

        Span<BarShadow.Ring> rings = stackalloc BarShadow.Ring[BarShadow.RingCount];
        BarShadow.Rings(
            new System.Windows.Rect(x, y, width, height),
            BarOutline.EnclosedBy(radius, shaped ? corners : BarCorners.Round), scale, rings);
        for (var i = 0; i < _rings.Length; i++)
        {
            var ring = rings[i];
            SetRoundedRect(_rings[i], ring.Bounds.X, ring.Bounds.Y, ring.Bounds.Width, ring.Bounds.Height, ring.Radius);
        }
    }

    /// <summary>
    /// Draws the clip, the face and the cut as paths of the corners' shape, or puts the rounded
    /// rectangles back for round ones. The shadow's rings stay rounded rectangles, of a radius
    /// that keeps them inside the shape (<see cref="BarOutline.EnclosedBy"/>): soft as the shadow
    /// is, the difference does not show, and a path for each ring on every frame of the wave would.
    /// </summary>
    private void SetCorners(BarCorners corners, double x, double y, double width, double height, double radius, double inset, double scale)
    {
        var squircle = corners != BarCorners.Round;
        if (squircle)
        {
            try
            {
                _shapePath ??= _compositor.CreatePathGeometry();
                _outlinePath ??= _compositor.CreatePathGeometry();
                _shapePath.Path = Path(0, 0, width, height, radius, corners);
                _outlinePath.Path = Path(x + inset, y + inset, width - scale, height - scale, radius, corners);
                if (_cutSprite is not null)
                {
                    _cutPath ??= _compositor.CreatePathGeometry();
                    _cutPath.Path = Path(x, y, width, height, radius, corners);
                }
            }
            catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException
                or NotSupportedException or UnauthorizedAccessException)
            {
                _noPaths = true;
                squircle = false;
            }
        }

        _corners = squircle ? corners : BarCorners.Round;
        _clip.Geometry = squircle ? _shapePath : (CompositionGeometry)_shape;
        _faceShape.Geometry = squircle ? _outlinePath : (CompositionGeometry)_outline;
        if (_cutSprite is not null)
        {
            _cutSprite.Geometry = squircle && _cutPath is not null ? _cutPath : (CompositionGeometry?)_cutShape;
        }
    }

    private static CompositionPath Path(double x, double y, double width, double height, double radius, BarCorners corners) =>
        new(new D2DOutline(new System.Windows.Rect(x, y, Math.Max(0, width), Math.Max(0, height)), radius, corners));

    /// <summary>
    /// Cuts everything off outside <paramref name="left"/> to <paramref name="right"/> across
    /// the window, in device pixels — the part of it on the dock's display, so a bar whose wave
    /// reaches past the display's side is not drawn on the display next door.
    /// </summary>
    /// <param name="width">The window's width, which the right inset is measured from.</param>
    public void SetSpan(double left, double right, double width)
    {
        _span.LeftInset = (float)Math.Max(0, left);
        _span.RightInset = (float)Math.Max(0, width - right);
    }

    /// <summary>The bar's fill, at its opacity, and its rim.</summary>
    public void SetColors(Color fill, Color rim)
    {
        _fill.Color = fill;
        _rim.Color = rim;
    }

    /// <summary>
    /// How much of the whole sheet shows, 0 to 1 — blur, wash, fill, rim and shadow together:
    /// for a sheet that comes and goes by degrees, as the one under an open group does.
    /// </summary>
    public void SetOpacity(double opacity) => _root.Opacity = (float)Math.Clamp(opacity, 0, 1);

    /// <summary>Whether the shadow is drawn round the shape: not under what casts its own.</summary>
    public void ShowShadow(bool show) => _shadow.IsVisible = show;

    /// <summary>
    /// Makes the sheet the taskbar's own material, or puts it back to the dock's: the bare blur
    /// under a wash, for a fill of the dock's choosing to go over.
    /// </summary>
    /// <param name="material">The taskbar's material, or null for the dock's own.</param>
    /// <param name="scale">Device pixels to a DIP, which the material's blur grows with.</param>
    /// <returns>
    /// True when the sheet now draws the material, and wants nothing painted over it. False when
    /// it is the dock's own blur — asked for, or because the material is a plain colour, or
    /// because this machine's compositor would not build it — and the fill is the caller's.
    /// </returns>
    /// <remarks>
    /// The material takes the blur's place and the wash's both: it blurs for itself, by its own
    /// amount, and a wash over it would be a second tint the taskbar does not have.
    /// </remarks>
    public bool SetMaterial(TaskbarMaterial? material, double scale)
    {
        // The shadow first: the material shows what is under it, so it cannot be had with the
        // shadow lying there.
        var acrylic = material is { Opaque: false } taskbar && CutShadow(true)
            ? TaskbarAcrylic.TryCreate(
                _compositor, taskbar, scale, _behind ??= _compositor.CreateBackdropBrush(), Grain())
            : null;

        if (acrylic is null)
        {
            CutShadow(false);
        }

        _blur.Brush = acrylic ?? _frost;
        _tint.IsVisible = acrylic is null;

        _acrylic?.Dispose();
        _acrylic = acrylic;
        return acrylic is not null;
    }

    /// <summary>
    /// Cuts the bar's shape out of the shadow, or puts the shadow back whole. True when it is
    /// as asked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whole, the shadow runs on under the bar — its rings are whole rounded rectangles — and
    /// nothing shows it, since the dock's own blur is opaque. The taskbar's material is not:
    /// it works on whatever is behind it <i>in this window too</i>, and with the shadow there
    /// it came out three or four levels darker than the taskbar, measured. So under the
    /// material the shadow has a hole the shape of the bar.
    /// </para>
    /// <para>
    /// The compositor clips to a shape and not to everything but a shape, so the hole is made
    /// with arithmetic. The shadow and the bar's shape — in opaque red, over it — are flattened
    /// into a layer, and the layer's effect makes each pixel black at the layer's alpha less
    /// its red: where only the shadow is, the shadow; where the shape is, nothing; and along
    /// the shape's softened edge, the share of the shadow the bar leaves showing. Still in the
    /// one batch with everything else, so the hole cannot be a frame behind the bar.
    /// </para>
    /// <para>
    /// The shadow is moved into the layer only while it is wanted. The dock's own blur has
    /// the tree it always had.
    /// </para>
    /// </remarks>
    private bool CutShadow(bool cut)
    {
        if (cut == _shadowCut)
        {
            return true;
        }

        if (!cut)
        {
            _cutLayer!.Children.RemoveAll();
            _root.Children.Remove(_cutLayer);
            _root.Children.InsertAtBottom(_shadow);
            _shadowCut = false;
            return true;
        }

        if (_cutLayer is null && !TryMakeCut())
        {
            return false;
        }

        _root.Children.Remove(_shadow);
        _cutLayer!.Children.InsertAtBottom(_shadow);
        _cutLayer.Children.InsertAtTop(_cut);
        _root.Children.InsertAtBottom(_cutLayer);
        _shadowCut = true;
        return true;
    }

    private bool TryMakeCut()
    {
        try
        {
            // Rows are what goes in — red, green, blue, alpha, one — and columns what comes
            // out: no colour, and alpha less red. Applied to the values as they are stored,
            // already multiplied by their alpha, which is what makes the softened edge right.
            float[] alphaLessRed =
            [
                0, 0, 0, -1,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 1,
                0, 0, 0, 0
            ];

            var effect = new D2DEffect(
                D2DEffect.ColorMatrix,
                [alphaLessRed, ColorMatrixStraight, false],
                new CompositionEffectSourceParameter("Layer"));

            _cutEffect = _compositor.CreateEffectFactory(effect).CreateBrush();
        }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException
            or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }

        _cutBrush = _compositor.CreateColorBrush(Color.FromArgb(0xFF, 0xFF, 0, 0));
        _cutShape = _compositor.CreateRoundedRectangleGeometry();
        _cutShape.Offset = _shape.Offset + new Vector2(_sheet.Offset.X, _sheet.Offset.Y);
        _cutShape.Size = _shape.Size;
        _cutShape.CornerRadius = _shape.CornerRadius;

        _cutSprite = _compositor.CreateSpriteShape(_cutShape);
        _cutSprite.FillBrush = _cutBrush;

        // Made after the bar was last set, so given its shape now: nothing may set it again
        // before the cut shows.
        if (_corners != BarCorners.Round)
        {
            try
            {
                _cutPath ??= _compositor.CreatePathGeometry();
                _cutPath.Path = Path(_bar.X, _bar.Y, _bar.Width, _bar.Height, _bar.Radius, _corners);
                _cutSprite.Geometry = _cutPath;
            }
            catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException
                or NotSupportedException or UnauthorizedAccessException)
            {
                // The cut stays round: a sliver of shadow, at most, under the corners.
            }
        }

        _cut = _compositor.CreateShapeVisual();
        _cut.RelativeSizeAdjustment = Vector2.One;
        _cut.Shapes.Add(_cutSprite);

        // The layer's own picture is what the effect's one unnamed input is given.
        _cutLayer = _compositor.CreateLayerVisual();
        _cutLayer.RelativeSizeAdjustment = Vector2.One;
        _cutLayer.Effect = _cutEffect;
        return true;
    }

    /// <summary>Direct2D's colour matrix told to leave the values as they are stored.</summary>
    private const uint ColorMatrixStraight = 2;

    /// <summary>
    /// The material's grain, as a brush that tiles from the sheet's corner at its own size —
    /// or null where it cannot be had, and the material goes without its texture.
    /// </summary>
    /// <remarks>
    /// Tried once. It takes a Direct3D device to hand the compositor a picture, and a machine
    /// that refuses one will refuse it again on the next slider tick.
    /// </remarks>
    private CompositionSurfaceBrush? Grain()
    {
        if (_grainTried)
        {
            return _grain;
        }

        _grainTried = true;
        _images = CompositionImages.TryCreate(_compositor);
        _grainSurface = _images?.Upload(TaskbarAcrylic.Grain());
        if (_grainSurface is null)
        {
            return null;
        }

        _grain = _compositor.CreateSurfaceBrush(_grainSurface);
        _grain.Stretch = CompositionStretch.None;
        _grain.HorizontalAlignmentRatio = 0;
        _grain.VerticalAlignmentRatio = 0;
        return _grain;
    }

    /// <summary>
    /// Clamped, because a radius over half the shorter side makes the geometry invalid rather
    /// than simply rounder.
    /// </summary>
    private static void SetRoundedRect(
        CompositionRoundedRectangleGeometry geometry, double x, double y, double width, double height, double radius)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        var corner = (float)Math.Clamp(radius, 0, Math.Min(width, height) / 2);

        geometry.Offset = new Vector2((float)x, (float)y);
        geometry.Size = new Vector2((float)width, (float)height);
        geometry.CornerRadius = new Vector2(corner, corner);
    }

    public void Dispose()
    {
        _root.Children.RemoveAll();
        _face.Shapes.Clear();
        _shadow.Shapes.Clear();
        _faceShape.Dispose();
        _outline.Dispose();
        _fill.Dispose();
        _rim.Dispose();
        _face.Dispose();
        _sheet.Children.RemoveAll();
        _tint.Dispose();
        _blur.Dispose();
        _acrylic?.Dispose();
        _cutLayer?.Children.RemoveAll();
        _cut?.Shapes.Clear();
        _cutSprite?.Dispose();
        _cutShape?.Dispose();
        _cutPath?.Dispose();
        _shapePath?.Dispose();
        _outlinePath?.Dispose();
        _cutBrush?.Dispose();
        _cut?.Dispose();
        _cutLayer?.Dispose();
        _cutEffect?.Dispose();
        _frost.Dispose();
        _behind?.Dispose();
        _grain?.Dispose();
        _grainSurface?.Dispose();
        _images?.Dispose();
        _sheet.Clip = null;
        _clip.Dispose();
        _shape.Dispose();
        _sheet.Dispose();

        foreach (var ring in _rings)
        {
            ring.Dispose();
        }

        foreach (var brush in _ringBrushes)
        {
            brush.Dispose();
        }

        _shadow.Dispose();
        _root.Clip = null;
        _span.Dispose();
        _root.Dispose();
        _target.Dispose();
        _compositor.Dispose();
    }

    /// <summary>
    /// A <c>Compositor</c> can only be built on a thread that has a dispatcher queue, and
    /// WPF's message loop is not one — so one is made, the first time a sheet is made on a
    /// thread that has none.
    /// </summary>
    /// <remarks>
    /// Per thread, since a queue belongs to the thread that made it. It used to be made once for
    /// the whole process, and a sheet made on any thread but the first found none: the compositor
    /// refused with access denied, which the fallback did not catch, so the sheet's constructor
    /// threw. The dock makes its sheet on its one UI thread and never met that; tests, each on a
    /// thread of its own, did at once.
    /// </remarks>
    internal static void EnsureDispatcherQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null)
        {
            return;
        }

        var options = new DispatcherQueueOptions
        {
            Size = Marshal.SizeOf<DispatcherQueueOptions>(),
            ThreadType = 2,      // DQTYPE_THREAD_CURRENT
            ApartmentType = 2    // DQTAT_COM_STA
        };

        CreateDispatcherQueueController(options, out _controller);
    }

    /// <summary>
    /// The controller of this thread's queue, held — never released — so the queue lasts as long
    /// as its thread.
    /// </summary>
    [ThreadStatic]
    private static nint _controller;

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    [DllImport("CoreMessaging.dll", PreserveSig = false)]
    private static extern void CreateDispatcherQueueController(
        DispatcherQueueOptions options, out nint controller);

    [ComImport]
    [Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(
            nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, out nint target);

        void EnsureOnThread(uint threadId);
    }
}
