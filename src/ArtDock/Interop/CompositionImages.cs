using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.DirectX;
using Windows.UI.Composition;
using WinRT;

namespace ArtDock.Interop;

/// <summary>
/// Hands pictures WPF has drawn to the compositor: a bitmap in, a surface a composition visual
/// can paint with out.
/// </summary>
/// <remarks>
/// <para>
/// For what the compositor cannot draw itself — text, and the item's icon — on something the
/// compositor moves. The window previews' cards glide a few dozen pixels a frame, and anything
/// on them drawn by WPF arrives a frame or two apart from the card (see
/// <see cref="CompositionBackdrop"/>'s remarks), so their titles are drawn by WPF once, into a
/// bitmap, and put on the card here.
/// </para>
/// <para>
/// A composition drawing surface is written through a Direct3D device the compositor is given:
/// <c>BeginDraw</c> lends a texture, the bitmap's pixels are copied into it at the offset it
/// says, and <c>EndDraw</c> gives it back. The hardware device first, then WARP, Direct3D's
/// software one, so a machine with *No GPU* or no driver gets its titles too. WPF's
/// <see cref="PixelFormats.Pbgra32"/> is the surface's own premultiplied BGRA, byte for byte.
/// </para>
/// </remarks>
internal sealed unsafe class CompositionImages : IDisposable
{
    private const int DriverTypeHardware = 1;
    private const int DriverTypeWarp = 5;
    private const uint CreateDeviceBgraSupport = 0x20;
    private const uint SdkVersion = 7;

    private static readonly Guid Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid Multithread = new("9b7e4e00-342c-4106-a19f-4f2704f689f0");

    private nint _device;
    private nint _context;
    private CompositionGraphicsDevice? _graphics;

    private CompositionImages(nint device, nint context, CompositionGraphicsDevice graphics)
    {
        _device = device;
        _context = context;
        _graphics = graphics;
    }

    /// <summary>A way to put bitmaps on <paramref name="compositor"/>'s visuals; null when this machine will not give one.</summary>
    public static CompositionImages? TryCreate(Compositor compositor)
    {
        nint device = 0, context = 0;
        try
        {
            if (!CreateDevice(DriverTypeHardware, out device, out context)
                && !CreateDevice(DriverTypeWarp, out device, out context))
            {
                return null;
            }

            // The compositor uses the device from a thread of its own.
            if (Marshal.QueryInterface(context, in Multithread, out var multithread) == 0)
            {
                var setProtected = (delegate* unmanaged[Stdcall]<nint, int, int>)(*(nint**)multithread)[5];
                setProtected(multithread, 1);
                Marshal.Release(multithread);
            }

            compositor.As<ICompositorInterop>().CreateGraphicsDevice(device, out var abi);
            var graphics = MarshalInterface<CompositionGraphicsDevice>.FromAbi(abi);
            Marshal.Release(abi);
            return new CompositionImages(device, context, graphics);
        }
        catch (Exception e) when (e is COMException or InvalidCastException or DllNotFoundException
            or EntryPointNotFoundException or UnauthorizedAccessException)
        {
            Release(ref context);
            Release(ref device);
            return null;
        }
    }

    /// <summary>
    /// A surface holding <paramref name="bitmap"/>, pixel for pixel; null if it is empty or the
    /// device has gone — after which this is no use, and <see cref="IsBroken"/> says so.
    /// </summary>
    public CompositionDrawingSurface? Upload(BitmapSource bitmap)
    {
        if (_graphics is not { } graphics || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
        {
            return null;
        }

        if (bitmap.Format != PixelFormats.Pbgra32)
        {
            bitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
        }

        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        CompositionDrawingSurface? surface = null;
        try
        {
            surface = graphics.CreateDrawingSurface(
                new Windows.Foundation.Size(width, height),
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                DirectXAlphaMode.Premultiplied);

            var drawing = surface.As<ICompositionDrawingSurfaceInterop>();
            var iid = Texture2D;
            drawing.BeginDraw(0, ref iid, out var texture, out var offset);
            try
            {
                var box = new Box
                {
                    Left = (uint)offset.X,
                    Top = (uint)offset.Y,
                    Front = 0,
                    Right = (uint)(offset.X + width),
                    Bottom = (uint)(offset.Y + height),
                    Back = 1,
                };

                // ID3D11DeviceContext::UpdateSubresource, the 49th entry of its table.
                var update = (delegate* unmanaged[Stdcall]<nint, nint, uint, Box*, void*, uint, uint, void>)(*(nint**)_context)[48];
                fixed (byte* data = pixels)
                {
                    update(_context, texture, 0, &box, data, (uint)stride, 0);
                }
            }
            finally
            {
                Marshal.Release(texture);
                drawing.EndDraw();
            }

            return surface;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException)
        {
            // The device lost — a driver update, a sleep — or the surface refused. Nothing drawn
            // through this will show again; whoever holds it makes another.
            surface?.Dispose();
            IsBroken = true;
            return null;
        }
    }

    /// <summary>True once an upload has failed in a way the next will too.</summary>
    public bool IsBroken { get; private set; }

    public void Dispose()
    {
        _graphics?.Dispose();
        _graphics = null;
        Release(ref _context);
        Release(ref _device);
    }

    private static bool CreateDevice(int driverType, out nint device, out nint context)
    {
        var result = D3D11CreateDevice(0, driverType, 0, CreateDeviceBgraSupport, 0, 0, SdkVersion, out device, out _, out context);
        if (result >= 0 && device != 0 && context != 0)
        {
            return true;
        }

        Release(ref context);
        Release(ref device);
        return false;
    }

    private static void Release(ref nint pointer)
    {
        if (pointer != 0)
        {
            Marshal.Release(pointer);
            pointer = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Box
    {
        public uint Left;
        public uint Top;
        public uint Front;
        public uint Right;
        public uint Bottom;
        public uint Back;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        nint adapter, int driverType, nint software, uint flags, nint featureLevels, uint featureLevelCount,
        uint sdkVersion, out nint device, out int featureLevel, out nint context);

    [ComImport]
    [Guid("25297D5C-3AD4-4C9C-B5CF-E36A38512330")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorInterop
    {
        void CreateCompositionSurfaceForHandle(nint swapChain, out nint result);

        void CreateCompositionSurfaceForSwapChain(nint swapChain, out nint result);

        void CreateGraphicsDevice(nint renderingDevice, out nint result);
    }

    [ComImport]
    [Guid("FD04E6E3-FE0C-4C3C-AB19-A07601A576EE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositionDrawingSurfaceInterop
    {
        void BeginDraw(nint updateRect, ref Guid iid, out nint updateObject, out Point32 updateOffset);

        void EndDraw();

        void Resize(long size);

        void Scroll(nint scrollRect, nint clipRect, int offsetX, int offsetY);

        void ResumeDraw();

        void SuspendDraw();
    }
}
