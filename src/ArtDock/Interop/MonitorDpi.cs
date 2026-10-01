using System.Runtime.InteropServices;

namespace ArtDock.Interop;

/// <summary>
/// The scale factor a particular display is running at.
/// </summary>
/// <remarks>
/// Needed because a dock can be sent to a display other than the one it is currently on, and
/// the two can be at different scales. Converting the dock's size with the scale of the
/// display it is leaving puts it on the new one at the wrong size — and, while the settings
/// dialog is holding the window at its largest, at a size that then feeds back into the next
/// conversion and grows.
/// </remarks>
internal static class MonitorDpi
{
    /// <summary>No monitor at all when the window is off every display, rather than the nearest.</summary>
    private const uint MonitorDefaultToNull = 0;

    /// <summary>Nearest monitor to a point, rather than failing when the point is off-screen.</summary>
    private const uint MonitorDefaultToNearest = 2;

    /// <summary>MDT_EFFECTIVE_DPI: the scale Windows is actually rendering that display at.</summary>
    private const int EffectiveDpi = 0;

    /// <summary>Ninety-six dots per inch is what a scale of 1 means.</summary>
    private const double Baseline = 96.0;

    /// <summary>
    /// The scale of the display a window is actually on.
    /// </summary>
    /// <remarks>
    /// Asked of Windows rather than of WPF. WPF's own idea of a window's scale is updated
    /// from <c>WM_DPICHANGED</c>, and there is a window of time after the dock has crossed
    /// onto another display where that idea is still the old one — long enough for a layout
    /// to be computed against it, which placed the dock as if it were still on the display
    /// it had just left and knocked it back there.
    /// </remarks>
    public static double ScaleForWindow(nint hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == 0 || GetDpiForMonitor(monitor, EffectiveDpi, out var dpiX, out _) != 0)
        {
            return 1;
        }

        return dpiX <= 0 ? 1 : dpiX / Baseline;
    }

    /// <summary>
    /// The scale of the display a point is on, or the nearest one to it.
    /// </summary>
    /// <remarks>
    /// For what is placed on a display rather than moved with a window: the handle a hidden dock
    /// leaves behind goes on the dock's display, and the dock's own window is no guide to that
    /// while it is hidden. It is parked below the bottom of the screen then, and a dock moved to
    /// one end reaches past the side of it — which on this machine is the other display, at
    /// another scale.
    /// </remarks>
    public static double ScaleAt(int x, int y)
    {
        var monitor = MonitorFromPoint(new Point { X = x, Y = y }, MonitorDefaultToNearest);
        if (monitor == 0 || GetDpiForMonitor(monitor, EffectiveDpi, out var dpiX, out _) != 0)
        {
            return 1;
        }

        return dpiX <= 0 ? 1 : dpiX / Baseline;
    }

    /// <summary>True when any part of the window lies on a display that is connected.</summary>
    public static bool IsOnAnyDisplay(nint hwnd) => MonitorFromWindow(hwnd, MonitorDefaultToNull) != 0;

    /// <summary>
    /// The height of the work area of the display a window is on, or nearest to, in that
    /// display's DIPs — or null when Windows will not say.
    /// </summary>
    /// <remarks>
    /// For a window that must fit on the display it opens on. <c>SystemParameters.WorkArea</c>
    /// is the main display's, whichever one the window is on.
    /// </remarks>
    public static double? WorkAreaHeightForWindow(nint hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        var scale = GetDpiForMonitor(monitor, EffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0
            ? dpiX / Baseline
            : 1;

        return (info.WorkBottom - info.WorkTop) / scale;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public int MonitorLeft;
        public int MonitorTop;
        public int MonitorRight;
        public int MonitorBottom;
        public int WorkLeft;
        public int WorkTop;
        public int WorkRight;
        public int WorkBottom;
        public uint Flags;
    }

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
