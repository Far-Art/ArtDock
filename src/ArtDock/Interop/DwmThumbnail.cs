using System.Runtime.InteropServices;
using System.Windows;

namespace ArtDock.Interop;

/// <summary>
/// A live picture of another program's window, drawn by DWM into a rectangle of one of ours —
/// what the window previews are made of.
/// </summary>
/// <remarks>
/// <para>
/// The compositor does the drawing, from the window's own surface, so the dock copies nothing
/// and pays nothing per frame however fast the window changes. The price is that DWM draws it
/// over everything in that rectangle: nothing of ours can lie on a picture, which is why the
/// close button is in the title row above it. Measured on this machine (TODO.md, *Resolved*,
/// "What a window holding window previews can be"): it shows in an ordinary window, a layered
/// one and one on the acrylic blur alike; a minimized window gives its last picture; and the
/// source size is already the window's visible frame, without the invisible resize borders.
/// </para>
/// <para>
/// Every update is checked against the last one first: the panel is re-laid on every poll while
/// it is open, and a property set that changes nothing is still a composition.
/// </para>
/// </remarks>
internal sealed class DwmThumbnail : IDisposable
{
    private const int DwmTnpRectDestination = 0x1;
    private const int DwmTnpOpacity = 0x4;
    private const int DwmTnpVisible = 0x8;
    private const int DwmTnpSourceClientAreaOnly = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size32
    {
        public int Width;
        public int Height;
    }

    /// <summary>
    /// <c>DWM_THUMBNAIL_PROPERTIES</c>, which <c>dwmapi.h</c> packs to one byte: 45 bytes, the
    /// flags after the opacity byte unaligned.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Properties
    {
        public int Flags;
        public Rect32 Destination;
        public Rect32 Source;
        public byte Opacity;
        public int Visible;
        public int SourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(nint thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref Properties properties);

    [DllImport("dwmapi.dll")]
    private static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out Size32 size);

    private const int DwmTnpRectSource = 0x2;

    private nint _handle;
    private (Int32Rect Where, Int32Rect? Part, byte Opacity)? _shown;

    /// <summary>Whether DWM holds a source rectangle short of the whole window.</summary>
    private bool _cut;

    private DwmThumbnail(nint handle, nint source)
    {
        _handle = handle;
        Source = source;
    }

    /// <summary>The window pictured.</summary>
    public nint Source { get; }

    /// <summary>
    /// Pictures <paramref name="source"/> in <paramref name="destination"/>, which must be a
    /// top-level window of this process; null when DWM refuses — the window gone, or not one it
    /// will picture.
    /// </summary>
    /// <remarks>Registered hidden: nothing shows until <see cref="Show"/> says where.</remarks>
    public static DwmThumbnail? Register(nint destination, nint source)
    {
        if (destination == 0 || source == 0 || DwmRegisterThumbnail(destination, source, out var handle) != 0 || handle == 0)
        {
            return null;
        }

        return new DwmThumbnail(handle, source);
    }

    /// <summary>The window's size as DWM pictures it, in pixels; empty when it cannot say.</summary>
    public Size SourceSize() =>
        _handle != 0 && DwmQueryThumbnailSourceSize(_handle, out var size) == 0 && size.Width > 0 && size.Height > 0
            ? new Size(size.Width, size.Height)
            : Size.Empty;

    /// <summary>
    /// Draws the picture in a rectangle of the destination's client area, in pixels; an empty
    /// one hides it. Does nothing when that is where, and how, it already is.
    /// </summary>
    /// <param name="where">Where it goes.</param>
    /// <param name="part">
    /// Which part of the window it shows there, in the window's own pixels; null for the whole.
    /// The only way to cut a picture: nothing in the destination window cuts it, not its region
    /// and not what the compositor draws (TODO.md item 25).
    /// </param>
    /// <param name="opacity">How opaque it is, 0 to 255.</param>
    public void Show(Int32Rect where, Int32Rect? part = null, byte opacity = 255)
    {
        var visible = !where.IsEmpty && where.Width > 0 && where.Height > 0 && opacity > 0;
        if (!visible)
        {
            where = Int32Rect.Empty;
            part = null;
            opacity = 0;
        }

        if (_handle == 0 || _shown == (where, part, opacity))
        {
            return;
        }

        // DWM keeps the last source rectangle it was given, so going back to the whole window is
        // saying so: the whole, as large as DWM reports it.
        var asked = part;
        if (part is null && _cut && SourceSize() is { IsEmpty: false } whole)
        {
            part = new Int32Rect(0, 0, (int)whole.Width, (int)whole.Height);
        }

        var properties = new Properties
        {
            Flags = DwmTnpRectDestination | DwmTnpOpacity | DwmTnpVisible | DwmTnpSourceClientAreaOnly
                | (part is null ? 0 : DwmTnpRectSource),
            Destination = new Rect32
            {
                Left = where.X,
                Top = where.Y,
                Right = where.X + where.Width,
                Bottom = where.Y + where.Height,
            },
            Opacity = visible ? opacity : (byte)255,
            Visible = visible ? 1 : 0,
            SourceClientAreaOnly = 0,
        };

        if (part is { } source)
        {
            properties.Source = new Rect32
            {
                Left = source.X,
                Top = source.Y,
                Right = source.X + source.Width,
                Bottom = source.Y + source.Height,
            };
        }

        if (DwmUpdateThumbnailProperties(_handle, ref properties) == 0)
        {
            _shown = (where, asked, opacity);
            if (part is not null)
            {
                _cut = asked is not null;
            }
        }
    }

    public void Dispose()
    {
        if (_handle == 0)
        {
            return;
        }

        DwmUnregisterThumbnail(_handle);
        _handle = 0;
    }
}
