using System.Runtime.InteropServices;
using System.Windows;
using ArtDock.Interop;
using ArtDock.Localization;

namespace ArtDock.Services;

/// <summary>One display the dock could live on.</summary>
/// <param name="DeviceName">
/// Windows' own name for it, such as <c>\\.\DISPLAY1</c>. Stored rather than an index,
/// because indices shuffle when a monitor is unplugged and the dock would silently move —
/// but it is only the name of the moment. Windows hands the names out again, sometimes on
/// nothing more than a wake from sleep, so it is matched after <paramref name="DevicePath"/>.
/// </param>
/// <param name="Label">What to call it in the settings dialog.</param>
/// <param name="WorkArea">Its usable area in physical pixels, taskbar excluded.</param>
/// <param name="Bounds">
/// Its whole area in physical pixels. The reveal edge is taken from this rather than from
/// the work area, whose bottom is the top of the taskbar.
/// </param>
/// <param name="IsPrimary">Whether Windows calls this the main display.</param>
/// <param name="DevicePath">
/// The monitor's own identity: its device interface path, built from the monitor's EDID and
/// the connector it is plugged into. Survives the renumbering that
/// <paramref name="DeviceName"/> does not. Null where Windows does not give one.
/// </param>
public sealed record ScreenInfo(
    string DeviceName,
    string Label,
    Rect WorkArea,
    Rect Bounds,
    bool IsPrimary,
    string? DevicePath = null);

/// <summary>
/// The displays attached to this machine.
/// </summary>
/// <remarks>
/// Asked of Windows every time, with <c>EnumDisplayMonitors</c>, and never through WinForms'
/// <c>Screen.AllScreens</c>. That list is cached, bounds and all, and forgotten only when
/// WinForms' own handler hears a display change — so a change it did not hear, or heard before
/// the display had settled, left it describing a display that was no longer there, and every
/// placement after went by it. Across a wake that comes back at a small resolution first, that
/// was a dock centred on the small display, for good (see <c>DockWindow.KeepOnDisplay</c>).
/// </remarks>
public static class Screens
{
    /// <summary>
    /// Every display, ordered left to right as they are arranged on the desktop.
    /// </summary>
    /// <remarks>
    /// Ordered by position rather than by the order Windows hands them over, so the numbering
    /// in the dialog matches how the displays are laid out in front of the user — which is
    /// how anyone reads "the second screen".
    /// </remarks>
    public static IReadOnlyList<ScreenInfo> All()
    {
        var found = new List<(string Name, Rect Bounds, Rect WorkArea, bool Primary)>();
        NativeMethods.EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new NativeMethods.MonitorInfoEx { cbSize = Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                found.Add((
                    info.szDevice,
                    RectOf(info.rcMonitor),
                    RectOf(info.rcWork),
                    (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));
            }

            return true;
        }, 0);

        var screens = found
            .OrderBy(screen => screen.Bounds.X)
            .ThenBy(screen => screen.Bounds.Y)
            .ToList();

        return [.. screens.Select((screen, index) => new ScreenInfo(
            screen.Name,
            Label(screen.Primary, index + 1, (int)screen.Bounds.Width, (int)screen.Bounds.Height),
            screen.WorkArea,
            screen.Bounds,
            screen.Primary,
            DevicePathOf(screen.Name)))];
    }

    /// <summary>How many displays the desktop spans now.</summary>
    public static int Count => NativeMethods.GetSystemMetrics(NativeMethods.SM_CMONITORS);

    /// <summary>
    /// The display lying at <paramref name="bounds"/>, as Windows has it now — its whole area
    /// and the part the taskbar leaves — or null when there is none.
    /// </summary>
    /// <remarks>
    /// Two calls, where <see cref="All"/> asks after every display's device path as well: cheap
    /// enough to ask a few times a second whether the display the dock was placed on is still
    /// the size it was.
    /// </remarks>
    public static (Rect Bounds, Rect WorkArea)? At(Rect bounds)
    {
        if (bounds.IsEmpty)
        {
            return null;
        }

        var rect = new NativeMethods.NativeRect
        {
            Left = (int)Math.Round(bounds.Left),
            Top = (int)Math.Round(bounds.Top),
            Right = (int)Math.Round(bounds.Right),
            Bottom = (int)Math.Round(bounds.Bottom),
        };
        var info = new NativeMethods.MonitorInfo { cbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        var monitor = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONULL);

        return monitor != 0 && NativeMethods.GetMonitorInfo(monitor, ref info)
            ? (RectOf(info.rcMonitor), RectOf(info.rcWork))
            : null;
    }

    private static Rect RectOf(NativeMethods.NativeRect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    /// <summary>
    /// The device interface path of the monitor on the display output called
    /// <paramref name="deviceName"/>, or null if Windows does not report one.
    /// </summary>
    /// <remarks>
    /// The first monitor on the output that is in use. An output can list more than one — a
    /// monitor that was attached once and is not now — and only the active one is the screen
    /// the dock would be drawn on.
    /// </remarks>
    private static string? DevicePathOf(string deviceName)
    {
        var device = new NativeMethods.DisplayDevice { cb = Marshal.SizeOf<NativeMethods.DisplayDevice>() };

        for (uint i = 0;
             NativeMethods.EnumDisplayDevices(
                 deviceName, i, ref device, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME);
             i++)
        {
            if ((device.StateFlags & NativeMethods.DISPLAY_DEVICE_ACTIVE) != 0
                && !string.IsNullOrEmpty(device.DeviceID))
            {
                return device.DeviceID;
            }
        }

        return null;
    }

    /// <summary>
    /// The display the dock should use, falling back to the primary one.
    /// </summary>
    /// <remarks>
    /// A stored display that no longer matches anything — the monitor was unplugged, or the
    /// settings came from another machine — falls back rather than leaving the dock on a
    /// screen that is not there.
    /// </remarks>
    public static ScreenInfo Resolve(string? deviceName, string? devicePath)
    {
        var all = All();

        if (Match(all, deviceName, devicePath) is { } matched)
        {
            return matched;
        }

        foreach (var screen in all)
        {
            if (screen.IsPrimary)
            {
                return screen;
            }
        }

        // AllScreens is never empty on a machine with a desktop, but a fallback beats a throw.
        return all.Count > 0
            ? all[0]
            : new ScreenInfo(
                string.Empty,
                Localizer.Get("Screens.Fallback"),
                new Rect(
                    SystemParameters.WorkArea.X,
                    SystemParameters.WorkArea.Y,
                    SystemParameters.WorkArea.Width,
                    SystemParameters.WorkArea.Height),
                new Rect(
                    0,
                    0,
                    SystemParameters.PrimaryScreenWidth,
                    SystemParameters.PrimaryScreenHeight),
                IsPrimary: true);
    }

    /// <summary>
    /// The stored display among <paramref name="screens"/>: by the monitor's path first, then
    /// by the name, or null when neither is there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The path first because the name is not the monitor's. Windows reassigns the display
    /// names — on the machine this was written on, the two displays swapped names across a
    /// wake from sleep with nothing unplugged — and a name that still resolves resolves to
    /// the other monitor, so nothing falls back and the dock simply changes screens.
    /// </para>
    /// <para>
    /// The name second, so a file written before the path was stored keeps working exactly
    /// as it did, and so does one for a monitor Windows gives no path for. It is also what a
    /// stored path that finds nothing falls through to: the monitor moved to another
    /// connector, which is part of the path, or the settings came from another machine.
    /// </para>
    /// <para>
    /// The whole path is compared, not the EDID inside it: two identical monitors differ only
    /// by the connector.
    /// </para>
    /// </remarks>
    public static ScreenInfo? Match(IReadOnlyList<ScreenInfo> screens, string? deviceName, string? devicePath)
    {
        if (!string.IsNullOrEmpty(devicePath))
        {
            foreach (var screen in screens)
            {
                if (string.Equals(screen.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase))
                {
                    return screen;
                }
            }
        }

        if (!string.IsNullOrEmpty(deviceName))
        {
            foreach (var screen in screens)
            {
                if (string.Equals(screen.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return screen;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// What to call a display in the settings dialog, in the dock's language — again, for a
    /// dialog whose language has just changed under it.
    /// </summary>
    /// <param name="screen">The display.</param>
    /// <param name="number">Its place counting from the left, from 1.</param>
    public static string Label(ScreenInfo screen, int number) =>
        Label(screen.IsPrimary, number, (int)screen.Bounds.Width, (int)screen.Bounds.Height);

    private static string Label(bool primary, int number, int width, int height) =>
        Localizer.Format(primary ? "Screens.DisplayMain" : "Screens.Display", number, width, height);
}
