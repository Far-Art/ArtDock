using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArtDock.Interop;

/// <summary>
/// Pulls application icons out of the Windows shell.
/// </summary>
/// <remarks>
/// Uses <c>IShellItemImageFactory</c> rather than the more familiar <c>SHGetFileInfo</c>,
/// because the dock magnifies its icons: <c>SHGetFileInfo</c> hands back a 32 px icon that
/// looks soft the moment it grows, while the image factory returns the largest asset the
/// target actually ships — and it handles <c>.exe</c>, <c>.lnk</c>, folders and Store app
/// AUMIDs through the same call.
/// </remarks>
public static class ShellIcons
{
    /// <summary>SIIGBF flags: take the icon, or only a thumbnail, rather than whichever the shell prefers.</summary>
    private const uint SiigbfResizeToFit = 0x0000_0000;
    private const uint SiigbfBiggerSizeOk = 0x0000_0001;
    private const uint SiigbfIconOnly = 0x0000_0004;
    private const uint SiigbfThumbnailOnly = 0x0000_0008;

    /// <summary><c>PERCEIVED_TYPE_IMAGE</c>.</summary>
    private const int PerceivedTypeImage = 2;

    private static readonly Guid IidShellItemImageFactory =
        new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>
    /// Loads the icon for a file, shortcut, folder or Store app.
    /// </summary>
    /// <param name="target">
    /// A filesystem path, or a Store app parsing name such as
    /// <c>shell:AppsFolder\{AUMID}</c>.
    /// </param>
    /// <param name="pixelSize">Requested edge length in pixels.</param>
    /// <returns>A frozen, thread-safe image, or <see langword="null"/> if the shell has none.</returns>
    public static ImageSource? Load(string target, int pixelSize = 256) =>
        GetImage(target, pixelSize, SiigbfIconOnly);

    /// <summary>
    /// Loads the thumbnail the shell has for a file — a picture drawn as itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same call as <see cref="Load"/>, asked for the thumbnail instead, so it comes from
    /// Windows' own thumbnail cache and covers whatever Explorer can show: HEIC and a camera's
    /// raw files too, where their codecs are installed. Thumbnail-only, because left to itself
    /// the shell falls back to the icon without saying so, and the caller could not tell which
    /// it had been given.
    /// </para>
    /// <para>
    /// Fitted rather than cropped, so it keeps the picture's shape: a landscape photo comes
    /// back wider than it is tall, and whatever draws it has to fit it rather than fill with it.
    /// </para>
    /// </remarks>
    /// <returns>A frozen image, or <see langword="null"/> when the shell has no thumbnail.</returns>
    public static ImageSource? LoadThumbnail(string path, int pixelSize = 256) =>
        GetImage(path, pixelSize, SiigbfThumbnailOnly);

    /// <summary>
    /// Loads an icon named the way the registry names one: a file and an index, as
    /// <c>%SystemRoot%\System32\imageres.dll,-54</c>, where a negative index is a resource id.
    /// </summary>
    /// <remarks>
    /// For an icon chosen by the dock rather than asked of a shell item — the Recycle Bin full
    /// or empty, which the shell's own answer no longer reliably tells apart (see
    /// <see cref="RecycleBin.IconLocation"/>). <c>SHDefExtractIcon</c>, which takes the frame
    /// nearest the size and scales it, as the image factory does.
    /// </remarks>
    /// <returns>A frozen image, or <see langword="null"/> when the location names no icon.</returns>
    public static ImageSource? LoadFromLocation(string location, int pixelSize = 256)
    {
        if (!TryParseLocation(location, out var file, out var index)
            || SHDefExtractIcon(file, index, 0, out var icon, 0, (uint)pixelSize) != 0
            || icon == 0)
        {
            return null;
        }

        try
        {
            var image = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is COMException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    /// <summary>
    /// Splits <c>file,index</c> into its two halves, the file's environment variables expanded
    /// and quotes taken off. No comma is index 0, as the shell reads it.
    /// </summary>
    public static bool TryParseLocation(string? location, out string file, out int index)
    {
        file = "";
        index = 0;
        if (string.IsNullOrWhiteSpace(location))
        {
            return false;
        }

        var comma = location.LastIndexOf(',');
        if (comma >= 0 && !int.TryParse(
                location.AsSpan(comma + 1).Trim(),
                System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture,
                out index))
        {
            return false;
        }

        file = Environment.ExpandEnvironmentVariables((comma >= 0 ? location[..comma] : location).Trim().Trim('"'));
        return file.Length > 0;
    }

    /// <summary>
    /// True when Windows counts a type of file as a picture.
    /// </summary>
    /// <remarks>
    /// Asked of the system rather than of a list, so a type a codec has added counts as soon as
    /// Explorer would treat it as one. An extension with no registration is not a picture.
    /// </remarks>
    /// <param name="extension">The extension, with its dot: <c>.png</c>.</param>
    public static bool IsPictureType(string extension) =>
        extension is { Length: > 1 }
        && AssocGetPerceivedType(extension, out var type, out _, 0) == 0
        && type == PerceivedTypeImage;

    /// <summary><c>E_PENDING</c>: the shell has the image in hand on another thread.</summary>
    private const int EPending = unchecked((int)0x8000_000A);

    private static ImageSource? GetImage(string target, int pixelSize, uint which)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        for (var attempt = 1; ; attempt++)
        {
            nint hbitmap = 0;
            try
            {
                SHCreateItemFromParsingName(target, 0, IidShellItemImageFactory, out var factory);

                factory.GetImage(
                    new NativeSize { Cx = pixelSize, Cy = pixelSize },
                    SiigbfResizeToFit | SiigbfBiggerSizeOk | which,
                    out hbitmap);

                Marshal.ReleaseComObject(factory);
                return hbitmap == 0 ? null : ToImageSource(hbitmap, topDown: which == SiigbfThumbnailOnly);
            }
            catch (COMException e) when (e.HResult == EPending && attempt == 1)
            {
                // Asked again, once, and at once. Two threads reading icons together can be
                // told the data "is not yet available" — measured with two threads making a
                // process's first reads at the same moment, in about a third of fresh
                // processes, the Recycle Bin and Task Manager alike — and every one came back
                // on the second asking, with no wait between. Treated as unresolvable, it left
                // a blank that the icon cache then kept for the rest of the session.
            }
            catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException
                or ArgumentException)
            {
                // Unresolvable target: a deleted app, a broken shortcut, a denied path, or a
                // string the shell will not parse at all. The dock falls back to a generic
                // glyph rather than failing to start.
                //
                // The breadth matters, because a target is free text — the item editor takes
                // whatever is typed into it, and a drop can carry an odd path. The runtime
                // translates the shell's HRESULTs into specific exceptions rather than a
                // COMException, so a path with forward slashes in it comes back as E_INVALIDARG
                // and lands here as ArgumentException; caught only as COMException, it took the
                // whole dock down.
                return null;
            }
            finally
            {
                if (hbitmap != 0)
                {
                    DeleteObject(hbitmap);
                }
            }
        }
    }

    /// <summary>
    /// True when a program carries an icon of its own, rather than being drawn with the
    /// shell's generic one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Load"/> cannot say: for a program with no icon the shell hands back its
    /// blank application icon, which is an image like any other. So the program's resources
    /// are counted instead — <c>ExtractIconEx</c> with index -1 and nowhere to put them
    /// returns how many icons it has, without extracting any.
    /// </para>
    /// <para>
    /// Worth asking because it tells apps from their machinery. Measured across the games on
    /// the machine this was written on: every game's program has an icon, and none of the
    /// crash handlers, uploaders, web helpers and unpackers beside them does.
    /// </para>
    /// </remarks>
    public static bool HasOwnIcon(string path) =>
        !string.IsNullOrWhiteSpace(path) && ExtractIconEx(path, -1, 0, 0, 0) > 0;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
    private static extern uint ExtractIconEx(string file, int index, nint large, nint small, uint icons);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocGetPerceivedType(string extension, out int type, out int flags, nint typeName);

    /// <summary>
    /// Copies the shell's HBITMAP into a WPF image.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pixels are read straight out of the DIB section rather than going through
    /// <c>Imaging.CreateBitmapSourceFromHBitmap</c>, which reports these bitmaps as
    /// <c>Bgr32</c> and throws the alpha channel away — every icon would come out on a black
    /// square.
    /// </para>
    /// <para>
    /// An icon comes back as a <b>bottom-up</b> DIB, which is GDI's default orientation: the
    /// first row in memory is the bottom row of the image, so its rows are copied back to
    /// front. Handing that buffer to WPF as-is renders every icon upside down.
    /// </para>
    /// <para>
    /// A thumbnail comes back the other way up, and its header does not say so. Its
    /// <c>biHeight</c> is positive — bottom-up, by the book — while its first row in memory is
    /// the top of the picture. Measured against pictures with a top and a bottom, fresh and
    /// from the thumbnail cache, PNG and JPEG, and against the Windows wallpapers decoded
    /// directly: top-down every time, and the header positive every time. So the caller says
    /// which it asked for, because nothing in the bitmap will.
    /// </para>
    /// <para>
    /// Both come back with <b>straight</b> alpha, not premultiplied, and the bitmap does not say
    /// that either. Measured: the Recycle Bin, the Settings gear, This PC, Downloads, Task
    /// Manager and Explorer all have partly clear pixels brighter than their alpha, which no
    /// premultiplied pixel can be, and so do the thumbnails of PNGs with transparency; a grey
    /// at half alpha comes back as 128 in every channel, where premultiplied it would be 64.
    /// Read as premultiplied, the colour of every partly clear pixel is added at full
    /// strength. Opaque and fully clear pixels read the same either way, which is why most
    /// icons looked right — while the Recycle Bin's glass came out as a white box, and the
    /// gear's antialiased edge as a white fringe. <c>ShellImageAlphaTests</c> holds it.
    /// </para>
    /// </remarks>
    private static ImageSource? ToImageSource(nint hbitmap, bool topDown)
    {
        var info = new NativeBitmap();
        if (GetObject(hbitmap, Marshal.SizeOf<NativeBitmap>(), ref info) == 0
            || info.BmBits == 0
            || info.BmBitsPixel != 32
            || info.BmWidth <= 0
            || info.BmHeight <= 0)
        {
            return null;
        }

        var stride = info.BmWidthBytes;
        var height = info.BmHeight;
        var pixels = new byte[stride * height];

        for (var row = 0; row < height; row++)
        {
            Marshal.Copy(
                info.BmBits + ((topDown ? row : height - 1 - row) * stride),
                pixels,
                row * stride,
                stride);
        }

        var source = BitmapSource.Create(
            info.BmWidth,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride);

        source.Freeze();
        return source;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int BmType;
        public int BmWidth;
        public int BmHeight;
        public int BmWidthBytes;
        public ushort BmPlanes;
        public ushort BmBitsPixel;
        public nint BmBits;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(NativeSize size, uint flags, out nint phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        nint pbc,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(nint hgdiobj, int cbBuffer, ref NativeBitmap lpvObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint hObject);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHDefExtractIconW")]
    private static extern int SHDefExtractIcon(
        string iconFile, int index, uint flags, out nint large, nint small, uint size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
