using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ArtDock.Interop;

/// <summary>
/// Where a point of an element is on screen, in device pixels, and back again — to the fraction
/// of a pixel.
/// </summary>
/// <remarks>
/// WPF's own <see cref="Visual.PointToScreen"/> and <see cref="Visual.PointFromScreen"/> go by
/// way of Win32's whole pixels, and round to them. That is nothing for placing a menu, and too
/// much for laying one drawing over another in another window: an open group's card, sized to its
/// icon's place from two corners each rounded so, landed most of a pixel larger than the icon,
/// and the icon was seen to settle by that much as it took the card's place — measured frame by
/// frame on 2026-11-21. Here only the window's own corner goes through Win32, and that is whole
/// pixels to begin with.
/// </remarks>
internal static class ScreenSpace
{
    /// <summary>A point of <paramref name="visual"/>'s, on screen; null when it is in no window.</summary>
    public static Point? ToScreen(Visual visual, Point point)
    {
        if (!Window(visual, out var root, out var target, out var corner))
        {
            return null;
        }

        var inWindow = ReferenceEquals(visual, root) ? point : visual.TransformToAncestor(root).Transform(point);
        var pixels = target.TransformToDevice.Transform(inWindow);
        return new Point(corner.X + pixels.X, corner.Y + pixels.Y);
    }

    /// <summary>A point on screen, in <paramref name="visual"/>'s own units; null when it is in no window.</summary>
    public static Point? FromScreen(Visual visual, Point screen)
    {
        if (!Window(visual, out var root, out var target, out var corner))
        {
            return null;
        }

        var inWindow = target.TransformFromDevice.Transform(new Point(screen.X - corner.X, screen.Y - corner.Y));
        if (ReferenceEquals(visual, root))
        {
            return inWindow;
        }

        return root.TransformToDescendant(visual) is { } down && down.TryTransform(inWindow, out var own) ? own : null;
    }

    private static bool Window(
        Visual visual, out Visual root, out HwndTarget target, out NativeMethods.NativePoint corner)
    {
        root = null!;
        target = null!;
        corner = default;
        if (PresentationSource.FromVisual(visual) is not HwndSource { RootVisual: { } top, CompositionTarget: { } drawn } source)
        {
            return false;
        }

        root = top;
        target = drawn;
        return NativeMethods.ClientToScreen(source.Handle, ref corner);
    }
}
