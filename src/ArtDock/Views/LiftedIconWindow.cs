using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ArtDock.Controls;
using ArtDock.Dock;
using ArtDock.Interop;
using ArtDock.Localization;

namespace ArtDock.Views;

/// <summary>
/// An icon pulled off the dock, carried under the pointer, with <i>Remove</i> over it once
/// letting go will take it off (<see cref="DragOff"/>) — and, let go there, going up in a puff of
/// cloud (<see cref="PoofCloud"/>).
/// </summary>
/// <remarks>
/// <para>
/// A window of its own because the dock's clips what it draws to itself, and it is only a slot
/// wider than the bar. Made by hand, like <see cref="HandleWindow"/>, so its size is set in
/// device pixels: the icon is as big as the dock's magnified one on the dock's display, and
/// stays that size across a display of another scale, because <c>WM_DPICHANGED</c> is swallowed
/// and what is drawn is stretched to whatever the window is (a <see cref="Viewbox"/>), whatever
/// scale WPF believes it is at.
/// </para>
/// <para>
/// It never takes input: the dock has the mouse captured for the length of the drag, and the
/// window is layered and <c>WS_EX_TRANSPARENT</c>, which is what keeps it out of the way of
/// <c>WindowFromPoint</c> and of the dock's look at what covers it (<c>WindowChrome.IsClickThrough</c>).
/// It is moved from the dock window's <c>WM_MOUSEMOVE</c>, by input, never by a frame clock.
/// </para>
/// </remarks>
public sealed class LiftedIconWindow : IDisposable
{
    /// <summary>How long <i>Remove</i> takes to come up, and to go.</summary>
    private static readonly Duration BubbleFade = new(TimeSpan.FromMilliseconds(120));

    /// <summary>How long the icon takes to go once it has been let go of: a shrink and a fade, inside the cloud.</summary>
    private static readonly Duration GoneFade = new(TimeSpan.FromMilliseconds(140));

    /// <summary>How long the cloud it goes up in takes to burst and clear.</summary>
    private static readonly Duration PoofTime = new(TimeSpan.FromMilliseconds(450));

    /// <summary>How far past the icon's edge the cloud reaches, as a share of the icon.</summary>
    private const double PoofMargin = 0.45;

    private readonly HwndSource _source;
    private readonly nint _hwnd;
    private readonly FrameworkElement _icon;
    private readonly Border _bubble;
    private readonly PoofCloud _poof = new();
    private readonly ScaleTransform _shrink = new(1, 1);

    /// <summary>The window's size, in device pixels.</summary>
    private readonly int _width;

    private readonly int _height;

    /// <summary>From the window's top to the icon's middle, in device pixels: where the pointer is held.</summary>
    private readonly int _hotY;

    private (int X, int Y)? _placed;
    private bool _armed;
    private bool _going;
    private bool _disposed;

    /// <param name="item">The item being carried.</param>
    /// <param name="iconSize">The icon's size, in the dock's units.</param>
    /// <param name="lettering">The dock's labels' lettering, for <i>Remove</i>.</param>
    /// <param name="scale">Device pixels to the dock's unit, on the dock's display.</param>
    public LiftedIconWindow(DockItem item, double iconSize, (Typeface Typeface, double Size) lettering, double scale)
    {
        var text = new TextBlock
        {
            Text = Localizer.Get("Drag.Remove"),
            FontFamily = lettering.Typeface.FontFamily,
            FontStyle = lettering.Typeface.Style,
            FontWeight = lettering.Typeface.Weight,
            FontSize = lettering.Size,
            Foreground = DockBar.TooltipText,
        };

        _bubble = new Border
        {
            Background = DockBar.TooltipFill,
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(DockBar.TooltipPadX, DockBar.TooltipPadY, DockBar.TooltipPadX, DockBar.TooltipPadY),
            Margin = new Thickness(0, 0, 0, DockBar.TooltipGap),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = text,
            Opacity = 0,
        };

        var icon = new DockItemVisual(item, iconSize)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Shrinking about the icon's middle, which is where the pointer let go of it — by a
        // transform of its own: the icon's is the wave's, and scales about its bottom.
        _icon = new Border
        {
            Child = icon,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _shrink,
        };

        // Room round the icon for the cloud it goes up in, which the window clips to. The bubble
        // keeps its usual gap above the icon itself, reaching down into that room.
        var margin = iconSize * PoofMargin;
        var stage = new Grid
        {
            Width = iconSize + (margin * 2),
            Height = iconSize + (margin * 2),
            Children = { _icon, _poof },
        };
        _bubble.Margin = new Thickness(0, 0, 0, DockBar.TooltipGap - margin);

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(2),
            Children = { _bubble, stage },
        };

        stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var design = stack.DesiredSize;
        var iconMiddle = design.Height - 2 - margin - (iconSize / 2);

        _width = Math.Max(1, (int)Math.Ceiling(design.Width * scale));
        _height = Math.Max(1, (int)Math.Ceiling(design.Height * scale));
        _hotY = (int)Math.Round(iconMiddle * scale);

        _source = new HwndSource(new HwndSourceParameters("ArtDock.LiftedIcon")
        {
            WindowStyle = unchecked((int)NativeMethods.WS_POPUP),
            ExtendedWindowStyle = unchecked((int)(
                NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_NOACTIVATE
                | NativeMethods.WS_EX_TRANSPARENT
                | NativeMethods.WS_EX_TOPMOST)),
            UsesPerPixelTransparency = true,
            PositionX = -32000,
            PositionY = -32000,
            Width = _width,
            Height = _height,
        })
        {
            RootVisual = new Viewbox { Stretch = Stretch.Fill, Child = stack },
        };

        _hwnd = _source.Handle;
        _source.AddHook(OnWindowMessage);
    }

    /// <summary>Puts the icon's middle under the pointer, at a point in device pixels, showing the window the first time.</summary>
    public void Follow(int x, int y)
    {
        if (_disposed || _going)
        {
            return;
        }

        var left = x - (_width / 2);
        var top = y - _hotY;
        if (_placed == (left, top))
        {
            return;
        }

        var first = _placed is null;
        _placed = (left, top);

        if (first)
        {
            NativeMethods.SetWindowPos(
                _hwnd, NativeMethods.HWND_TOPMOST, left, top, _width, _height,
                NativeMethods.SWP_NOACTIVATE);
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNA);
        }
        else
        {
            NativeMethods.SetWindowPos(
                _hwnd, 0, left, top, 0, 0,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOSIZE);
        }
    }

    /// <summary>Shows <i>Remove</i> over the icon, or takes it down.</summary>
    public void SetArmed(bool armed)
    {
        if (_disposed || armed == _armed)
        {
            return;
        }

        _armed = armed;
        _bubble.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(armed ? 1 : 0, BubbleFade));
    }

    /// <summary>The icon has been let go of and is coming off the dock: it shrinks and fades, then the window goes.</summary>
    public void Vanish()
    {
        if (_disposed || _going)
        {
            return;
        }

        _going = true;
        var shrink = new DoubleAnimation(0.3, GoneFade) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        _shrink.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        _shrink.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        _icon.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, GoneFade));
        _bubble.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, GoneFade));

        // The window goes once the cloud has cleared.
        var poof = new DoubleAnimation(0, 1, PoofTime);
        poof.Completed += (_, _) => Dispose();
        _poof.BeginAnimation(PoofCloud.ProgressProperty, poof);
    }

    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // Swallowed, as the handle's is: its size is the dock's to set, and WPF's handling of a
        // move to a display of another scale would resize it by the ratio between the two.
        if (msg == NativeMethods.WM_DPICHANGED)
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
        _bubble.BeginAnimation(UIElement.OpacityProperty, null);
        _icon.BeginAnimation(UIElement.OpacityProperty, null);
        _poof.BeginAnimation(PoofCloud.ProgressProperty, null);
        _source.Dispose();
    }
}
