using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ArtDock.Interop;

namespace ArtDock.Views;

/// <summary>
/// Makes room for the shadows the Fluent theme casts under its drop-down lists and its
/// tooltips, which it otherwise cuts off at their edges.
/// </summary>
/// <remarks>
/// <para>
/// Both templates put a <see cref="DropShadowEffect"/> on a border that is the whole of what
/// goes into a popup, and a popup's window is the size of its child's layout — which an effect
/// is no part of. So the shadow is drawn and then clipped to the border: what shows of it is
/// what lies in the corners outside the rounding, and behind the translucent rim, cut square.
/// The same theme's submenus, with the same shadow, do not have the fault: they inset the
/// border in its popup and pull the popup back by the inset. This does that for every combo
/// box and every tooltip in the app, with the room read from the shadow itself, so a theme
/// that changes its shadow is still fitted.
/// </para>
/// <para>
/// The two are placed differently, so they are fitted differently. A list is placed against
/// its box, and is told where to go before it opens (<see cref="FitDropDown"/>). A tooltip is
/// placed against the pointer — or, shown from the keyboard, against its element — by rules
/// that take the cursor's own picture into account, and is left to WPF to place, then grown
/// around the place it was given (<see cref="FitToolTip"/>).
/// </para>
/// </remarks>
public static class PopupShadows
{
    /// <summary>
    /// Fits every combo box and tooltip the app shows from here on. Once, before any window is
    /// made.
    /// </summary>
    public static void Register()
    {
        // Not Loaded: WPF raises Loaded only on an element with a Loaded handler of its own, and
        // a class handler is not one, so a class handler for it never runs. SizeChanged is
        // raised on every element, the first time when it is first laid out.
        EventManager.RegisterClassHandler(
            typeof(ComboBox), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(OnComboBoxSizeChanged));
        EventManager.RegisterClassHandler(
            typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(OnToolTipOpened));
        EventManager.RegisterClassHandler(
            typeof(ToolTip), ToolTip.ClosedEvent, new RoutedEventHandler(OnToolTipClosed));
    }

    /// <summary>
    /// How far a shadow reaches past each side of what casts it: its blur all round, moved by
    /// its depth.
    /// </summary>
    public static Thickness Reach(DropShadowEffect shadow)
    {
        // Direction turns anticlockwise from the right, and the screen's y runs down.
        var angle = shadow.Direction * Math.PI / 180;
        var dx = shadow.ShadowDepth * Math.Cos(angle);
        var dy = -shadow.ShadowDepth * Math.Sin(angle);
        var blur = shadow.BlurRadius;

        return new Thickness(
            Math.Max(0, blur - dx), Math.Max(0, blur - dy), Math.Max(0, blur + dx), Math.Max(0, blur + dy));
    }

    // ---------------------------------------------------------------- drop-down lists

    private static void OnComboBoxSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not ComboBox box)
        {
            return;
        }

        // Fitted now, so it is right before the list is first opened, and again on every
        // opening: a change of theme puts in a new template — a new popup, without any of this
        // — and leaves the box its size. A box is resized as often as its dialog is, hence the
        // removal first.
        box.DropDownOpened -= OnDropDownOpened;
        box.DropDownOpened += OnDropDownOpened;
        FitDropDown(box);
    }

    private static void OnDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is ComboBox box)
        {
            FitDropDown(box);
        }
    }

    /// <summary>
    /// Insets a combo box's list in its popup by the reach of its shadow, and places the popup
    /// so the list stays where it was. Changes nothing a second time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// None at the top, as the submenus have none: the theme casts the shadow downwards, so
    /// what would show above the list is a few percent at most, and the list slides in from its
    /// top edge — a popup reaching above the box would show it sliding out of the box's lower
    /// half.
    /// </para>
    /// <para>
    /// The popup is placed by its <see cref="Popup.PlacementRectangle"/> rather than its
    /// offsets, because the offsets are the same whichever way it opens. A list with too little
    /// room below opens above the box instead, and with the shadow's room at the bottom of its
    /// window, an offset that puts it under the box leaves a gap the depth of that room when it
    /// opens above. The rectangle has two edges to give: its bottom is where the popup's top
    /// goes when it opens below, and its top where the popup's bottom goes when it opens above
    /// — which is lower than the box's top by the shadow's room. And it is wider than the box by
    /// that room at each end, so that the list lines up with the box whichever end the popup is
    /// aligned to, which is the right one when menus drop to the left, and the left one in a
    /// right-to-left language.
    /// </para>
    /// </remarks>
    public static void FitDropDown(ComboBox box)
    {
        // Anything other than the theme's template as it stands is left alone: a popup that
        // cannot be transparent would show the room as black, and one placed some other way
        // than under or over the box is not placed by the rectangle below.
        if (box.Template?.FindName("PART_Popup", box) is not Popup popup
            || !popup.AllowsTransparency
            || popup.Placement is not (PlacementMode.Bottom or PlacementMode.Top)
            || popup.Child is not FrameworkElement list
            || list.Effect is not DropShadowEffect shadow
            || VisualTreeHelper.GetParent(popup) is not FrameworkElement target)
        {
            return;
        }

        // The same at both ends, so the rectangle is the same seen from either.
        var reach = Reach(shadow);
        var side = Math.Max(reach.Left, reach.Right);
        var below = reach.Bottom;

        // The theme's gap between box and list, kept whichever way the list opens. The popup
        // moves the rectangle down by it, which the rectangle's top allows for.
        var gap = popup.VerticalOffset;
        var width = target.ActualWidth;
        var height = target.ActualHeight;

        // The rectangle cannot be turned inside out: on a box shorter than the room under
        // the list, the room gives way, and the shadow is cut off at the bottom as before.
        below = Math.Min(below, height + 2 * gap);

        // The popup was at least as wide as the box; the list now has to be, inside it.
        list.Margin = new Thickness(side, 0, side, below);
        list.MinWidth = width;

        popup.PlacementRectangle = new Rect(
            -side, below - 2 * gap, width + 2 * side, height + 2 * gap - below);
    }

    // ---------------------------------------------------------------- tooltips

    /// <summary>Set on a tooltip while it holds the room this gave it.</summary>
    private static readonly DependencyProperty FittedProperty = DependencyProperty.RegisterAttached(
        "Fitted", typeof(bool), typeof(PopupShadows), new PropertyMetadata(false));

    private static readonly DependencyProperty[] ToolTipPlacing =
    [
        FrameworkElement.MarginProperty,
        ToolTip.PlacementProperty,
        ToolTip.HorizontalOffsetProperty,
        ToolTip.VerticalOffsetProperty,
    ];

    private static void OnToolTipOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ToolTip tip)
        {
            FitToolTip(tip);
        }
    }

    // Raised once the window is gone, after any fade, so nothing seen moves.
    private static void OnToolTipClosed(object sender, RoutedEventArgs e)
    {
        if (sender is ToolTip tip)
        {
            Unfit(tip);
        }
    }

    /// <summary>
    /// Grows an open tooltip's window by the reach of its shadow round the place WPF put it,
    /// with the tooltip still in that place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tooltip is placed under the pointer's picture, or over it with too little room below
    /// — the same choice of two a list has, but against a rectangle WPF works out from the
    /// cursor's bitmap and keeps to itself, so there is no placement rectangle to give it. So
    /// the theme's tooltip is let open where WPF puts it, that place is read from its window,
    /// and the tooltip is then placed absolutely, at that place less the room, with the room
    /// round it. All of it happens in <c>Opened</c>, which the popup raises as it shows the
    /// window and before anything has been drawn in it: the first frame is the finished one.
    /// </para>
    /// <para>
    /// The room stops at the edges of the display the tooltip is on. Past them it would put the
    /// window's corner on another display, and a popup is pushed back onto the display its
    /// corner is on — the whole tooltip would jump to the screen beside it.
    /// </para>
    /// </remarks>
    private static void FitToolTip(ToolTip tip)
    {
        // Opened again before the last showing had closed: its room is still set, and the
        // window was placed by it. Taken off, so WPF places the tooltip afresh.
        Unfit(tip);

        // A placement the app gave the tooltip itself is left as it is.
        if (tip.Parent is not Popup { AllowsTransparency: true }
            || ToolTipPlacing.Any(property => tip.ReadLocalValue(property) != DependencyProperty.UnsetValue)
            || tip.Template?.FindName("Border", tip) is not FrameworkElement border
            || border.Effect is not DropShadowEffect shadow
            || PresentationSource.FromVisual(tip) is not HwndSource { CompositionTarget: { } target } source)
        {
            return;
        }

        tip.UpdateLayout();
        if (!NativeMethods.GetWindowRect(source.Handle, out var window)
            || !ScreenOf(window, out var screen))
        {
            return;
        }

        var placed = new Rect(window.Left, window.Top, window.Right - window.Left, window.Bottom - window.Top);
        var scale = new Vector(target.TransformToDevice.M11, target.TransformToDevice.M22);
        var (margin, corner) = ToolTipRoom(placed, screen, Reach(shadow), scale, tip.FlowDirection);

        tip.SetValue(FittedProperty, true);
        tip.Margin = margin;
        tip.Placement = PlacementMode.Absolute;
        tip.HorizontalOffset = corner.X / scale.X;
        tip.VerticalOffset = corner.Y / scale.Y;

        // The element the tooltip belongs to can set all three for it, and its value wins
        // over the tooltip's own. Then it is the element's to place.
        if (tip.Placement != PlacementMode.Absolute
            || !Near(tip.HorizontalOffset, corner.X / scale.X)
            || !Near(tip.VerticalOffset, corner.Y / scale.Y))
        {
            Unfit(tip);
            return;
        }

        tip.UpdateLayout();

        // Measured rather than assumed: absolute placement can put a right-to-left tooltip's
        // right edge at the offset rather than its left, and the difference is made up once.
        if (NativeMethods.GetWindowRect(source.Handle, out var now)
            && (!Near(now.Left, corner.X) || !Near(now.Top, corner.Y)))
        {
            tip.HorizontalOffset += (corner.X - now.Left) / scale.X;
            tip.VerticalOffset += (corner.Y - now.Top) / scale.Y;
            tip.UpdateLayout();
        }
    }

    private static void Unfit(ToolTip tip)
    {
        if (!(bool)tip.GetValue(FittedProperty))
        {
            return;
        }

        tip.ClearValue(FittedProperty);
        foreach (var property in ToolTipPlacing)
        {
            tip.ClearValue(property);
        }
    }

    /// <summary>
    /// The room round a tooltip WPF has placed at <paramref name="placed"/>, in physical pixels
    /// on a display bounded by <paramref name="screen"/>: the margin to give it, in its own
    /// units and on its own sides, and where its window's top left corner then has to be.
    /// </summary>
    /// <remarks>
    /// Whole pixels, so the window's corner and the tooltip inside it land on the pixels they
    /// are meant to at any scale. In a right-to-left tooltip the margin's left is the screen's
    /// right, as the shadow's direction is.
    /// </remarks>
    public static (Thickness Margin, Point Corner) ToolTipRoom(
        Rect placed, Rect screen, Thickness reach, Vector scale, FlowDirection flow)
    {
        var mirrored = flow == FlowDirection.RightToLeft;

        // On the screen's own sides, in its pixels, held to the display.
        var left = Math.Min(Math.Round((mirrored ? reach.Right : reach.Left) * scale.X), Math.Max(0, placed.Left - screen.Left));
        var right = Math.Min(Math.Round((mirrored ? reach.Left : reach.Right) * scale.X), Math.Max(0, screen.Right - placed.Right));
        var top = Math.Min(Math.Round(reach.Top * scale.Y), Math.Max(0, placed.Top - screen.Top));
        var bottom = Math.Min(Math.Round(reach.Bottom * scale.Y), Math.Max(0, screen.Bottom - placed.Bottom));

        var margin = mirrored
            ? new Thickness(right / scale.X, top / scale.Y, left / scale.X, bottom / scale.Y)
            : new Thickness(left / scale.X, top / scale.Y, right / scale.X, bottom / scale.Y);

        return (margin, new Point(placed.Left - left, placed.Top - top));
    }

    /// <summary>
    /// The part of the display under <paramref name="window"/> a popup there is kept to: the
    /// work area when it is in it, as WPF keeps a tooltip, and the whole display when not.
    /// </summary>
    private static bool ScreenOf(NativeMethods.NativeRect window, out Rect screen)
    {
        screen = default;

        var monitor = NativeMethods.MonitorFromRect(ref window, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MonitorInfo { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == 0 || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        var work = info.rcWork;
        var area = window.Left >= work.Left && window.Top >= work.Top && window.Left <= work.Right && window.Top <= work.Bottom
            ? work
            : info.rcMonitor;

        screen = new Rect(area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top);
        return true;
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) <= 1;
}
