using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ArtDock.Controls;
using ArtDock.Interop;

namespace ArtDock.Views;

/// <summary>
/// A blurred sheet of whatever is behind the dock, sitting directly under the bar and cut
/// to the bar's own shape — and, where the compositor is there to do it, the bar itself.
/// </summary>
/// <remarks>
/// <para>
/// A second window because the dock's own cannot do this. <c>AllowsTransparency</c> puts
/// that window on a layered surface, and nothing — not DWM's materials, not the accent
/// policy, not a composition target — composes a backdrop behind a layered window. That is
/// why the bar was a flat fill where the web component it was modelled on has a blur.
/// </para>
/// <para>
/// Deliberately not a WPF <see cref="Window"/>, and deliberately created by hand: it needs
/// <c>WS_EX_NOREDIRECTIONBITMAP</c>, which can only be given at creation and which WPF's own
/// window never has. Without it the window keeps a redirection surface that DWM composes
/// <i>over</i> the composition visual, and the sheet comes out solid black. Nothing else about
/// the setup gives that away, so it is worth stating plainly. Not through WPF's
/// <see cref="HwndSource"/> either, which drops <c>WS_EX_LAYERED</c> from any style it is given.
/// </para>
/// <para>
/// It never takes input, which matters because it lies under the whole of a dock whose design
/// is that you can click through the gaps. <c>WS_EX_TRANSPARENT</c> was once thought enough
/// for that, and is not, reliably: a window with only that can still be where
/// <c>WindowFromPoint</c>, and so a click, lands — and the sheet, parked wider than the bar, was
/// found swallowing clicks beside both of the bar's ends. Layered as well, a window is passed by
/// altogether. The bar still takes clicks while this draws it: the dock paints an invisible
/// stand-in over it — see <see cref="DockBar.DrawsBar"/>.
/// </para>
/// </remarks>
public sealed class BackdropWindow : IDisposable
{
    /// <summary>Wash drawn over the blur, so it reads as a material rather than frosting.</summary>
    private static readonly Windows.UI.Color Tint =
        Windows.UI.Color.FromArgb(0x38, 0x14, 0x16, 0x20);

    private const string ClassName = "ArtDock.Backdrop";

    /// <summary>Held for as long as the process runs: Windows calls it for every sheet.</summary>
    private static readonly NativeMethods.WindowProc Procedure = OnWindowMessage;

    private static bool _registered;

    private readonly nint _hwnd;

    private CompositionBackdrop? _composition;
    private bool _acrylic;
    private bool _visible;
    private bool _disposed;
    private bool _topmost = true;

    private int _left;
    private int _top;
    private int _width;
    private int _height;

    public BackdropWindow()
    {
        Register();

        _hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_NOACTIVATE
                | NativeMethods.WS_EX_TRANSPARENT
                | NativeMethods.WS_EX_LAYERED
                | NativeMethods.WS_EX_NOREDIRECTIONBITMAP,
            ClassName,
            "ArtDock.Backdrop",
            NativeMethods.WS_POPUP,
            -32000, -32000, 1, 1,
            0, 0, NativeMethods.GetModuleHandle(null), 0);

        // As WPF's own window would, rather than carry a null handle into everything after.
        if (_hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        // A layered window shows nothing until it has been told how; fully opaque, since the
        // visuals carry their own alpha.
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 0xFF, NativeMethods.LWA_ALPHA);

        // Preferred over the accent policy for one reason: shape. A composition visual takes
        // a rounded-rectangle clip of any radius; the accent policy blurs the whole window
        // rect and cannot be told otherwise — see DesktopComposition.
        _composition = CompositionBackdrop.TryCreate(_hwnd, Tint);
        if (_composition is not null)
        {
            _acrylic = true;
            return;
        }

        ApplyAccentFallback();
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
            ClassName = ClassName
        };

        NativeMethods.RegisterClassEx(ref windowClass);
        _registered = true;
    }

    /// <summary>True when the system gave us a backdrop of some kind.</summary>
    public bool IsAcrylic => _acrylic;

    /// <summary>
    /// True when the sheet draws the bar — fill, rim and shadow — as well as the blur under it,
    /// so the dock must not. The composition path: there the sheet is exactly the bar's shape,
    /// and the bar and its blur are drawn in one batch, which is the only way found to keep them
    /// on the same frame. The fallback can do neither, and only blurs, a rectangle kept inside
    /// the bar that the dock goes on drawing.
    /// </summary>
    public bool DrawsBar => _composition is not null;

    /// <summary>
    /// True when DWM is drawing this window's shadow. Only ever on the fallback path: DWM
    /// shadows a window's rect, so a rectangular window holding a pill-shaped visual would
    /// cast a rectangular shadow.
    /// </summary>
    public bool HasNativeShadow => _acrylic && _composition is null;

    public bool IsVisible => _visible;

    public nint Hwnd => _hwnd;

    public void Show()
    {
        if (_disposed || _visible)
        {
            return;
        }

        _visible = true;

        // SW_SHOWNA rather than SW_SHOW: this window must never take the foreground.
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNA);
        ApplyBand();
    }

    /// <summary>
    /// Keeps the sheet in the same z-order band as the dock. They are re-stacked against
    /// each other afterwards, and that only works between windows of the same band.
    /// </summary>
    public void SetTopmost(bool topmost)
    {
        _topmost = topmost;

        if (_visible)
        {
            ApplyBand();
        }
    }

    private void ApplyBand() =>
        NativeMethods.SetWindowPos(
            _hwnd,
            _topmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

    public void Hide()
    {
        if (_disposed || !_visible)
        {
            return;
        }

        _visible = false;
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
    }

    /// <summary>
    /// Puts the sheet under the bar of <paramref name="dock"/>, which is drawn in the window
    /// <paramref name="window"/> — and, where the sheet draws the bar, draws it there.
    /// </summary>
    /// <returns>
    /// False when there was nothing to measure: a dock not laid out and drawn yet.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The bar is taken as the dock has drawn it (<see cref="DockBar.RenderedBarRect"/>), not as
    /// it is being computed, so it arrives on the screen with the icons the same frame drew —
    /// the dock's window gets there by a longer road. And it is measured in the dock window's
    /// device pixels, exactly as WPF draws it, not through <see cref="Visual.PointToScreen"/>,
    /// which rounds to whole pixels. That did not matter while the sheet was only the blur under
    /// a bar drawn in the dock; it would now, since whole pixels are what made an edge that
    /// follows the wave step.
    /// </para>
    /// <para>
    /// On the composition path the sheet's window is the dock window's twin — the same place,
    /// the same size, directly under it — and the bar is drawn in it where the dock would draw
    /// it. So the sheet moves exactly when the dock's window moves, and while it slides the
    /// shape inside it is left alone. When the dock's window changes size, the old shape is
    /// drawn in the new window until the dock's next frame, which is what the dock's own window
    /// shows too: a layered window keeps its old bitmap in its new bounds until it repaints, and
    /// the bar and its icons are best out of place together. The sheet used to be parked at the
    /// widest the bar gets, which moved it at moments the dock's window stayed where it was.
    /// </para>
    /// </remarks>
    public bool Follow(DockBar dock, nint window)
    {
        if (_disposed
            || PresentationSource.FromVisual(dock) is not { CompositionTarget: { } target, RootVisual: { } root }
            || dock.ActualWidth <= 0 || dock.ActualHeight <= 0
            || !NativeMethods.GetWindowRect(window, out var frame))
        {
            return false;
        }

        var bar = dock.RenderedBarRect;
        if (bar.IsEmpty || bar.Width <= 0 || bar.Height <= 0)
        {
            return false;
        }

        var toDevice = target.TransformToDevice;
        var toWindow = dock.TransformToAncestor(root);
        var scale = toDevice.M11;
        var radius = dock.RenderedBarRadius;

        Point ToWindow(Point point) => toDevice.Transform(toWindow.Transform(point));

        if (_composition is not null)
        {
            // The dock's window has no frame, so its rect is its client area — which is what
            // WPF draws the bar in, and so what the bar's device pixels are measured from.
            var width = frame.Right - frame.Left;
            var height = frame.Bottom - frame.Top;

            var topLeft = ToWindow(bar.TopLeft);
            var bottomRight = ToWindow(bar.BottomRight);

            Place(frame.Left, frame.Top, width, height);
            Paint(dock.BarFill, DockBar.BarBorder);
            Shape(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y, radius * scale, scale);
            return true;
        }

        // The fallback cannot be shaped, so a rectangle of blur has to be kept inside the bar
        // instead — which costs a sliver of blur under each rounded end.
        var (insetX, insetY) = Inset(bar, radius);
        bar.Inflate(-insetX, -insetY);
        if (bar.Width <= 0 || bar.Height <= 0)
        {
            return false;
        }

        var near = ToWindow(bar.TopLeft);
        var far = ToWindow(bar.BottomRight);
        var left = (int)Math.Round(frame.Left + near.X);
        var top = (int)Math.Round(frame.Top + near.Y);

        Place(left, top, (int)Math.Round(frame.Left + far.X) - left, (int)Math.Round(frame.Top + far.Y) - top);
        return true;
    }

    /// <summary>
    /// Moves the sheet's window, in device pixels. Only when it has actually moved: re-placing
    /// a composition window where it already is still costs a recomposition, and this runs on
    /// every tick of every slider.
    /// </summary>
    private void Place(int left, int top, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // Checked against where the window actually is, not only against what was asked
        // for last time. Something else can move it — the DPI handling below is swallowed
        // now, but a cached request that no longer matches reality would otherwise never be
        // re-applied, and the sheet would stay wrong for the life of the process.
        if (left == _left && top == _top && width == _width && height == _height
            && NativeMethods.GetWindowRect(_hwnd, out var actual)
            && actual.Left == left && actual.Top == top
            && actual.Right - actual.Left == width
            && actual.Bottom - actual.Top == height)
        {
            return;
        }

        (_left, _top, _width, _height) = (left, top, width, height);

        NativeMethods.SetWindowPos(
            _hwnd, 0, left, top, width, height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);

        // The composition visual is rebuilt with the window, so a shape set before this
        // move cannot be assumed to have survived it.
        _shaped = false;
    }

    /// <summary>The bar last drawn, so an unchanged one is not drawn again.</summary>
    private (double X, double Y, double Width, double Height, double Radius, double Scale) _shape;

    private bool _shaped;

    /// <summary>
    /// The bar as the sheet last drew it, in device pixels from the window's top-left — empty
    /// until it has drawn one, and always on the fallback path, which draws no bar.
    /// </summary>
    public Rect DrawnBar => _shaped ? new Rect(_shape.X, _shape.Y, _shape.Width, _shape.Height) : Rect.Empty;

    /// <summary>
    /// Draws the bar at a rounded rectangle inside the window, in device pixels relative to
    /// the window's top-left: its blur, fill, rim and shadow, all at once.
    /// </summary>
    /// <remarks>
    /// Once per sync, with the bar's real shape and nothing before it. Anything that drew an
    /// interim shape first — the window's own rectangle, say, which is how this once began —
    /// was composed by DWM whenever a frame fell between the two, and showed as a flash of the
    /// sheet at the full width of its window, far past the ends of the bar.
    /// </remarks>
    private void Shape(double x, double y, double width, double height, double radius, double scale)
    {
        // Re-applying the shape it already has re-composes the sheet for nothing, and this
        // runs on every frame of the settings dialog's demonstration wave as well as on
        // every tick of every slider.
        if (_shaped && Same(x, _shape.X) && Same(y, _shape.Y) && Same(width, _shape.Width)
            && Same(height, _shape.Height) && Same(radius, _shape.Radius) && Same(scale, _shape.Scale))
        {
            return;
        }

        _shape = (x, y, width, height, radius, scale);
        _shaped = true;
        _composition?.SetShape(x, y, width, height, radius, scale);
    }

    /// <summary>The colours last painted, so unchanged ones are not painted again.</summary>
    private (Color Fill, Color Rim)? _paint;

    private void Paint(Color fill, Color rim)
    {
        if (_paint == (fill, rim))
        {
            return;
        }

        _paint = (fill, rim);
        _composition?.SetColors(
            Windows.UI.Color.FromArgb(fill.A, fill.R, fill.G, fill.B),
            Windows.UI.Color.FromArgb(rim.A, rim.R, rim.G, rim.B));
    }

    /// <summary>Equal to well under a device pixel, which is all this geometry is measured in.</summary>
    private static bool Same(double a, double b) => Math.Abs(a - b) < 0.01;

    /// <summary>
    /// How far inside the bar the fallback's sheet has to sit for its own corners to clear the
    /// bar's, in device-independent pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sheet's radius is DWM's, the bar's is the user's, and a rounded rectangle of
    /// radius <c>a</c> sits inside one of radius <c>b</c> exactly when the corner arcs'
    /// centres are no more than <c>b - a</c> apart. That is one equation in two unknowns —
    /// the horizontal and vertical inset — so there is a whole family of insets that work
    /// and the cheapest one can be picked.
    /// </para>
    /// <para>
    /// Cheapest meaning least blur given up, which is the area of bar left uncovered:
    /// <c>2·insetX·height + 2·insetY·width</c>. Minimising that against the constraint puts
    /// almost all of the inset on the horizontal, because the bar is far wider than it is
    /// tall — an unblurred sliver under each rounded end costs a third of what a full-width
    /// band along the top and bottom would, and reads as very much less.
    /// </para>
    /// </remarks>
    private static (double X, double Y) Inset(Rect bar, double barRadius)
    {
        var slack = Math.Max(0, barRadius - DwmCornerRadius);
        if (slack <= 0)
        {
            return (0, 0);
        }

        var diagonal = Math.Sqrt((bar.Width * bar.Width) + (bar.Height * bar.Height));
        if (diagonal <= 0)
        {
            return (slack, slack);
        }

        return (
            Math.Min(slack * (1 - (bar.Height / diagonal)), (bar.Width / 2) - 1),
            Math.Min(slack * (1 - (bar.Width / diagonal)), (bar.Height / 2) - 1));
    }

    /// <summary>The radius DWM rounds windows to, in device-independent pixels.</summary>
    private const double DwmCornerRadius = 8;

    /// <summary>
    /// The route for machines without composition: DWM's accent blur, which cannot be
    /// shaped, plus the sizing frame DWM wants before it will draw a shadow.
    /// </summary>
    /// <remarks>
    /// Not layered: DWM blurs behind no layered window. The fallback's sheet is kept inside the
    /// bar, which the dock paints over, so it has nothing to be click-through for.
    /// </remarks>
    private void ApplyAccentFallback()
    {
        var exStyle = (uint)NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(
            _hwnd, NativeMethods.GWL_EXSTYLE, (nint)(exStyle & ~NativeMethods.WS_EX_LAYERED));

        var style = (uint)NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE);
        NativeMethods.SetWindowLongPtr(
            _hwnd, NativeMethods.GWL_STYLE, (nint)(style | NativeMethods.WS_THICKFRAME));

        NativeMethods.SetWindowPos(
            _hwnd, 0, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER
                | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);

        // A light tint only. The bar's own colour and opacity are drawn over this, so a
        // heavy one here would just be a second, uncontrollable darkening of it.
        _acrylic = DesktopComposition.EnableAcrylic(_hwnd, tint: 0x33000000);
        DesktopComposition.SetRoundedCorners(_hwnd, rounded: true);
    }

    /// <summary>
    /// Collapses the non-client frame, so the client area fills the window rect exactly.
    /// </summary>
    /// <remarks>
    /// Returning zero for a <c>WM_NCCALCSIZE</c> that proposed a rect keeps that rect as the
    /// client area — the frame is claimed to take up no room. The window therefore measures
    /// exactly the bar while still counting as framed for the purposes of the shadow.
    /// </remarks>
    private static nint OnWindowMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        // Swallowed. This window is created off screen, which on a machine whose monitors
        // differ in scale means it is born on one and then moved to another — and WPF, which
        // used to host it, rescaled it for that, shrinking the sheet by the ratio between the
        // two. The dock sizes this window in device pixels and nothing else may.
        if (msg == NativeMethods.WM_DPICHANGED)
        {
            return 0;
        }

        if (msg == NativeMethods.WM_NCCALCSIZE && wParam != 0)
        {
            return 0;
        }

        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _composition?.Dispose();
        _composition = null;
        NativeMethods.DestroyWindow(_hwnd);
    }
}
