using System.Runtime.InteropServices;

namespace ArtDock.Interop;

/// <summary>
/// Reads what is on the screen under a window, and keeps that window out of what is read.
/// </summary>
/// <remarks>
/// <para>
/// For the handle a hidden dock leaves behind, which can show the colours behind it inverted.
/// That takes knowing what is behind it — without seeing itself, or it would invert its own
/// inversion on the next read and flicker between the two. <see cref="ExcludeFromCapture"/> is
/// what stops that: Windows leaves the window out of every capture of the screen, GDI's
/// included, and shows what is under it instead. Measured here on 2026-09-28 with a layered
/// WPF window of the handle's own kind: 2000 of its pixels read back before, none after.
/// </para>
/// <para>
/// A read is slow to arrive and cheap to make. Measured the same day, a 600×8 strip took 8.3 ms
/// every time — one frame of the main display at 120 Hz, which is DWM handing over its next
/// composed frame — and almost none of that was work: fifteen reads a second cost about half a
/// percent of one core. So reads belong on a thread of their own, never on the dock's, whose
/// timers would otherwise stall for a frame on every one.
/// </para>
/// <para>
/// The work is DWM's instead, which copies its composed frame back for every read: about
/// 0.09 ms of it a read, measured on 2026-09-30 — a tenth of one core at every frame of a
/// 120 Hz display. Which is why the handle reads that often only while what is behind it
/// moves; see <c>HandlePace</c>.
/// </para>
/// <para>
/// The price of the exclusion is that the window is missing from screenshots and recordings as
/// well. The handle always inverts, so it always pays it; README lists it under *Known gaps*.
/// </para>
/// </remarks>
internal static class ScreenCapture
{
    private const uint WDA_NONE = 0x00;

    /// <summary>Shown on the monitor, and nowhere else: absent from every capture of the screen.</summary>
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    private const uint SRCCOPY = 0x00CC0020;
    private const uint DIB_RGB_COLORS = 0;

    /// <summary>
    /// Keeps <paramref name="hwnd"/> out of captures of the screen, or lets it back in.
    /// </summary>
    /// <returns>False when Windows would not — before Windows 10 2004, for one.</returns>
    public static bool ExcludeFromCapture(nint hwnd, bool exclude) =>
        SetWindowDisplayAffinity(hwnd, exclude ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);

    /// <summary>
    /// Copies a rectangle of the screen, in physical pixels, into <paramref name="pixels"/> as
    /// 32-bit BGRA rows from the top down.
    /// </summary>
    /// <param name="pixels">At least <c>width × height × 4</c> bytes.</param>
    /// <returns>
    /// False when nothing could be read — the secure desktop is up, the screen is locked — and
    /// then <paramref name="pixels"/> says nothing about the screen.
    /// </returns>
    /// <remarks>
    /// Blocks for about a frame of the display; see the remarks on the class. Physical pixels on
    /// the whole virtual screen, which is what a <c>PerMonitorV2</c> process sees: negative
    /// coordinates reach the display to the left of the main one. The alpha bytes are whatever
    /// GDI leaves in them, which is not an alpha, and a caller has to set its own.
    /// </remarks>
    public static bool TryCopy(int x, int y, int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0 || pixels.Length < width * height * 4)
        {
            return false;
        }

        var screen = GetDC(0);
        if (screen == 0)
        {
            return false;
        }

        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var previous = SelectObject(memory, bitmap);

        try
        {
            if (memory == 0 || bitmap == 0 || !BitBlt(memory, 0, 0, width, height, screen, x, y, SRCCOPY))
            {
                return false;
            }

            // A bitmap cannot be read while it is selected into a device context.
            SelectObject(memory, previous);

            var header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,

                // Negative for rows from the top down, the order WPF writes them in.
                Height = -height,
                Planes = 1,
                BitCount = 32
            };

            return GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref header, DIB_RGB_COLORS) == height;
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        nint dc, nint bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);
}
