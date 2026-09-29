using System.Windows.Interop;
using ArtDock.Interop;

namespace ArtDock.Views;

/// <summary>
/// A blurred sheet of whatever is behind the dock, sitting directly under the bar and cut
/// to the bar's own shape.
/// </summary>
/// <remarks>
/// <para>
/// A second window because the dock's own cannot do this. <c>AllowsTransparency</c> puts
/// that window on a layered surface, and nothing — not DWM's materials, not the accent
/// policy, not a composition target — composes a backdrop behind a layered window. That is
/// why the bar was a flat fill where the web component it was modelled on has a blur.
/// </para>
/// <para>
/// Deliberately not a WPF <see cref="System.Windows.Window"/>, and deliberately created by
/// hand: it needs <c>WS_EX_NOREDIRECTIONBITMAP</c>, which can only be given at creation and
/// which WPF's own window never has. Without it the window keeps a redirection surface that
/// DWM composes <i>over</i> the composition visual, and the sheet comes out solid black.
/// Nothing else about the setup gives that away, so it is worth stating plainly.
/// </para>
/// <para>
/// It never takes input. <c>WS_EX_TRANSPARENT</c> passes clicks through to whatever is
/// underneath, which matters because it is a solid window sitting under a dock whose whole
/// design is that you can click through the gaps.
/// </para>
/// </remarks>
public sealed class BackdropWindow : IDisposable
{
    /// <summary>Wash drawn over the blur, so it reads as a material rather than frosting.</summary>
    private static readonly Windows.UI.Color Tint =
        Windows.UI.Color.FromArgb(0x38, 0x14, 0x16, 0x20);

    private readonly HwndSource _source;
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
    private int _radius = -1;

    public BackdropWindow()
    {
        _source = new HwndSource(new HwndSourceParameters("ArtDock.Backdrop")
        {
            WindowStyle = unchecked((int)NativeMethods.WS_POPUP),
            ExtendedWindowStyle = unchecked((int)(
                NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_NOACTIVATE
                | NativeMethods.WS_EX_TRANSPARENT
                | NativeMethods.WS_EX_NOREDIRECTIONBITMAP)),
            PositionX = -32000,
            PositionY = -32000,
            Width = 1,
            Height = 1
        });

        _hwnd = _source.Handle;

        // Preferred over the accent policy for one reason: shape. A composition visual takes
        // a rounded-rectangle clip of any radius; the accent policy blurs the whole window
        // rect and cannot be told otherwise — see DesktopComposition.
        // Hooked on both paths: the DPI message has to be swallowed either way.
        _source.AddHook(OnWindowMessage);

        _composition = CompositionBackdrop.TryCreate(_hwnd, Tint);
        if (_composition is not null)
        {
            _acrylic = true;
            return;
        }

        ApplyAccentFallback();
    }

    /// <summary>True when the system gave us a backdrop of some kind.</summary>
    public bool IsAcrylic => _acrylic;

    /// <summary>
    /// True when the sheet is exactly the shape it was asked for, rather than a rectangle
    /// the caller has to keep inside that shape.
    /// </summary>
    public bool ShapesExactly => _composition is not null;

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
    /// Puts the sheet under the bar and cuts it to the bar's shape. Everything is in device
    /// pixels, which is what the dock's bar measures in once it has been through
    /// <see cref="System.Windows.Media.Visual.PointToScreen"/>.
    /// </summary>
    public void Place(int left, int top, int width, int height, int radius)
    {
        if (_disposed || width <= 0 || height <= 0)
        {
            return;
        }

        // Checked against where the window actually is, not only against what was asked
        // for last time. Something else can move it — the DPI handling below is swallowed
        // now, but a cached request that no longer matches reality would otherwise never be
        // re-applied, and the sheet would stay wrong for the life of the process.
        var placed = left == _left && top == _top && width == _width && height == _height
            && NativeMethods.GetWindowRect(_hwnd, out var actual)
            && actual.Right - actual.Left == width
            && actual.Bottom - actual.Top == height;

        if (placed && radius == _radius)
        {
            return;
        }

        (_left, _top, _width, _height, _radius) = (left, top, width, height, radius);

        // Only when it has actually moved. The roundness slider changes the radius without
        // moving the sheet a pixel, and re-placing a composition window on every tick of it
        // costs a recomposition each time — which is what that slider was flickering.
        if (!placed)
        {
            NativeMethods.SetWindowPos(
                _hwnd, 0, left, top, width, height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
        }

        // Deliberately no clip from here. The two callers are both in SyncBackdrop: the
        // composition path sets the real one — the bar's rectangle — on the very next line,
        // and the fallback path has no composition to clip at all. Setting the window's own
        // rectangle first therefore never survived, but it did leave the sheet drawn at the
        // full width of its window for whatever frame DWM composed in between. That window
        // is parked wider than the bar on purpose, so the flash was a whole icon's worth of
        // acrylic appearing beyond the ends of the bar and vanishing again — which is what
        // the colour and roundness sliders were flickering.
        if (!placed)
        {
            // The composition visual is rebuilt with the window, so a clip set before this
            // move cannot be assumed to have survived it.
            _clipped = false;
        }
    }

    /// <summary>The clip last applied, so an unchanged one is not re-applied.</summary>
    private (double X, double Y, double Width, double Height, double Radius) _clip;

    private bool _clipped;

    /// <summary>
    /// Cuts the sheet to a rounded rectangle inside the window, in device pixels relative to
    /// the window's top-left. Only the composition path can do this; on the fallback the
    /// window itself has to be moved instead.
    /// </summary>
    public void Clip(double x, double y, double width, double height, double radius)
    {
        if (_composition is null)
        {
            return;
        }

        // Re-applying the clip it already has re-composes the sheet for nothing, and this
        // runs on every frame of the settings dialog's demonstration wave as well as on
        // every tick of every slider.
        if (_clipped && Same(x, _clip.X) && Same(y, _clip.Y) && Same(width, _clip.Width)
            && Same(height, _clip.Height) && Same(radius, _clip.Radius))
        {
            return;
        }

        _clip = (x, y, width, height, radius);
        _clipped = true;
        _composition.SetClip(x, y, width, height, radius);
    }

    /// <summary>Equal to well under a device pixel, which is all this geometry is measured in.</summary>
    private static bool Same(double a, double b) => Math.Abs(a - b) < 0.01;

    /// <summary>
    /// The route for machines without composition: DWM's accent blur, which cannot be
    /// shaped, plus the sizing frame DWM wants before it will draw a shadow.
    /// </summary>
    private void ApplyAccentFallback()
    {
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
    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // Swallowed. This window is created off screen, which on a machine whose monitors
        // differ in scale means it is born on one and then moved to another — and WPF's own
        // handling of that rescales it, shrinking the sheet by the ratio between the two.
        // The dock sizes this window in device pixels and nothing else may.
        if (msg == NativeMethods.WM_DPICHANGED)
        {
            handled = true;
            return 0;
        }

        if (msg == NativeMethods.WM_NCCALCSIZE && wParam != 0)
        {
            handled = true;
        }

        return 0;
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
        _source.Dispose();
    }
}
