using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace ArtDock.Interop;

/// <summary>
/// A blurred sheet of whatever is behind a window, clipped to a rounded rectangle of any
/// radius.
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
/// The cost is the window shadow: DWM shadows a window's rect, so a rectangular window
/// holding a pill-shaped visual would cast a rectangular shadow. The dock draws its own
/// instead, which follows the bar's real shape.
/// </para>
/// </remarks>
internal sealed class CompositionBackdrop : IDisposable
{
    private readonly Compositor _compositor;
    private readonly DesktopWindowTarget _target;
    private readonly ContainerVisual _root;
    private readonly SpriteVisual _blur;
    private readonly SpriteVisual _tint;
    private readonly CompositionRoundedRectangleGeometry _shape;

    private CompositionBackdrop(
        Compositor compositor,
        DesktopWindowTarget target,
        ContainerVisual root,
        SpriteVisual blur,
        SpriteVisual tint,
        CompositionRoundedRectangleGeometry shape)
    {
        _compositor = compositor;
        _target = target;
        _root = root;
        _blur = blur;
        _tint = tint;
        _shape = shape;
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

            var root = compositor.CreateContainerVisual();
            root.RelativeSizeAdjustment = Vector2.One;

            var shape = compositor.CreateRoundedRectangleGeometry();
            root.Clip = compositor.CreateGeometricClip(shape);

            // Blurs what is behind the window, which is what the taskbar shows and what
            // the web component's backdrop-filter does.
            var blur = compositor.CreateSpriteVisual();
            blur.RelativeSizeAdjustment = Vector2.One;
            blur.Brush = compositor.CreateHostBackdropBrush();

            // The host backdrop is bare frosting; without a wash over it the bar reads as
            // washed out rather than as a material.
            var wash = compositor.CreateSpriteVisual();
            wash.RelativeSizeAdjustment = Vector2.One;
            wash.Brush = compositor.CreateColorBrush(tint);

            root.Children.InsertAtTop(blur);
            root.Children.InsertAtTop(wash);
            target.Root = root;

            return new CompositionBackdrop(compositor, target, root, blur, wash, shape);
        }
        catch (Exception e) when (e is COMException or InvalidCastException
            or TypeLoadException or DllNotFoundException or EntryPointNotFoundException)
        {
            // No composition on this machine, or the interop moved. The caller falls back.
            return null;
        }
    }

    /// <summary>
    /// Cuts the sheet to a rounded rectangle somewhere inside its window. Everything is in
    /// device pixels, relative to the window's top-left.
    /// </summary>
    /// <remarks>
    /// The reason the shape is a clip rather than the window's own bounds: this takes
    /// fractions of a pixel and a window does not. The bar's width drifts by well under a
    /// pixel per frame near the ends of a wave, and a window following that in whole pixels
    /// steps visibly. The window is therefore parked at the widest the bar ever gets and
    /// never moved, and this does the following.
    /// </remarks>
    public void SetClip(double x, double y, double width, double height, double radius)
    {
        _shape.Offset = new Vector2((float)x, (float)y);
        _shape.Size = new Vector2((float)width, (float)height);

        // Clamped, because a radius over half the shorter side makes the geometry invalid
        // rather than simply rounder.
        var limit = (float)(Math.Min(width, height) / 2);
        var corner = Math.Clamp((float)radius, 0, limit);
        _shape.CornerRadius = new Vector2(corner, corner);
    }

    public void Dispose()
    {
        _root.Children.RemoveAll();
        _tint.Dispose();
        _blur.Dispose();
        _shape.Dispose();
        _root.Dispose();
        _target.Dispose();
        _compositor.Dispose();
    }

    /// <summary>
    /// A <c>Compositor</c> can only be built on a thread that has a dispatcher queue, and
    /// WPF's message loop is not one — it has to be created explicitly, once.
    /// </summary>
    private static void EnsureDispatcherQueue()
    {
        if (_dispatcherQueue != 0)
        {
            return;
        }

        var options = new DispatcherQueueOptions
        {
            Size = Marshal.SizeOf<DispatcherQueueOptions>(),
            ThreadType = 2,      // DQTYPE_THREAD_CURRENT
            ApartmentType = 2    // DQTAT_COM_STA
        };

        CreateDispatcherQueueController(options, out _dispatcherQueue);
    }

    private static nint _dispatcherQueue;

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
