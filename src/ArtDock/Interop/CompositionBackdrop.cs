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
    private readonly ContainerVisual _sheet;
    private readonly CompositionRoundedRectangleGeometry _shape;
    private readonly SpriteVisual _blur;
    private readonly SpriteVisual _tint;

    /// <summary>The bar's fill and rim, over the blur.</summary>
    private readonly ShapeVisual _face;
    private readonly CompositionRoundedRectangleGeometry _outline;
    private readonly CompositionSpriteShape _faceShape;
    private readonly CompositionColorBrush _fill;
    private readonly CompositionColorBrush _rim;

    private CompositionBackdrop(Compositor compositor, DesktopWindowTarget target, Color tint)
    {
        _compositor = compositor;
        _target = target;

        _root = compositor.CreateContainerVisual();
        _root.RelativeSizeAdjustment = Vector2.One;

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
        _sheet.Clip = compositor.CreateGeometricClip(_shape);

        // Blurs what is behind the window, which is what the taskbar shows and what the web
        // component's backdrop-filter does.
        _blur = compositor.CreateSpriteVisual();
        _blur.RelativeSizeAdjustment = Vector2.One;
        _blur.Brush = compositor.CreateHostBackdropBrush();

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
    public void SetShape(double x, double y, double width, double height, double radius, double scale)
    {
        _sheet.Offset = new Vector3((float)x, (float)y, 0);
        _sheet.Size = new Vector2((float)width, (float)height);
        SetRoundedRect(_shape, 0, 0, width, height, radius);

        // The rim is a DIP wide and drawn half a DIP inside the edge, so it lands exactly
        // inside the bar, as the dock's own pen does.
        var inset = scale / 2;
        SetRoundedRect(_outline, x + inset, y + inset, width - scale, height - scale, radius);
        _faceShape.StrokeThickness = (float)scale;

        Span<BarShadow.Ring> rings = stackalloc BarShadow.Ring[BarShadow.RingCount];
        BarShadow.Rings(new System.Windows.Rect(x, y, width, height), radius, scale, rings);
        for (var i = 0; i < _rings.Length; i++)
        {
            var ring = rings[i];
            SetRoundedRect(_rings[i], ring.Bounds.X, ring.Bounds.Y, ring.Bounds.Width, ring.Bounds.Height, ring.Radius);
        }
    }

    /// <summary>The bar's fill, at its opacity, and its rim.</summary>
    public void SetColors(Color fill, Color rim)
    {
        _fill.Color = fill;
        _rim.Color = rim;
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
    private static void EnsureDispatcherQueue()
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
