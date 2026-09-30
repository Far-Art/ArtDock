using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.Interop;

namespace ArtDock.Tests;

/// <summary>
/// Covers what the shell's images say about their own transparency being believed: an icon or
/// a thumbnail half see-through comes back half see-through, in its own colour.
/// </summary>
/// <remarks>
/// <para>
/// The shell hands its pixels over with straight alpha, and they were read as premultiplied.
/// Where an image is opaque or fully clear the two agree, so most icons looked right — and
/// where it is partly clear the colour was added at full strength, which drew the Recycle
/// Bin's glass as a white box and put a white fringe round the Settings gear.
/// </para>
/// <para>
/// Grey at half alpha, because it is the one colour that tells all three readings apart:
/// believed, it is grey; taken for premultiplied it is white; multiplied twice it is dark.
/// </para>
/// </remarks>
public sealed class ShellImageAlphaTests : IDisposable
{
    private const byte Grey = 128;
    private const byte Half = 128;

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "ArtDockTests", Guid.NewGuid().ToString("N"));

    public ShellImageAlphaTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // The shell can still be holding a file it was asked about.
        }
    }

    private static void OnStaThread(Action body)
    {
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure?.Throw();
    }

    /// <summary>A square of grey at half alpha, as a PNG.</summary>
    private static byte[] TranslucentPng()
    {
        const int edge = 256;
        const uint pixel = ((uint)Half << 24) | ((uint)Grey << 16) | ((uint)Grey << 8) | Grey;
        var bitmap = BitmapSource.Create(
            edge, edge, 96, 96, PixelFormats.Bgra32, palette: null,
            Enumerable.Repeat(pixel, edge * edge).ToArray(), edge * 4);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// An icon file of one 256 px frame, which an <c>.ico</c> may hold as a PNG — and which
    /// the shell draws a <c>.ico</c> with.
    /// </summary>
    private string TranslucentIcon()
    {
        var png = TranslucentPng();
        var path = Path.Combine(_folder, "translucent.ico");

        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0);   // reserved
        writer.Write((ushort)1);   // an icon
        writer.Write((ushort)1);   // one frame
        writer.Write((byte)0);     // 256 wide
        writer.Write((byte)0);     // 256 high
        writer.Write((byte)0);     // no palette
        writer.Write((byte)0);     // reserved
        writer.Write((ushort)1);   // planes
        writer.Write((ushort)32);  // bits per pixel
        writer.Write(png.Length);
        writer.Write(6 + 16);      // where the frame starts
        writer.Write(png);
        return path;
    }

    private string TranslucentPicture()
    {
        var path = Path.Combine(_folder, "translucent.png");
        File.WriteAllBytes(path, TranslucentPng());
        return path;
    }

    /// <summary>The middle pixel of an image, as straight BGRA.</summary>
    private static (byte B, byte G, byte R, byte A) Middle(ImageSource image)
    {
        var bitmap = new FormatConvertedBitmap((BitmapSource)image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        bitmap.CopyPixels(
            new Int32Rect(bitmap.PixelWidth / 2, bitmap.PixelHeight / 2, 1, 1), pixel, 4, 0);
        return (pixel[0], pixel[1], pixel[2], pixel[3]);
    }

    private static bool IsHalfGrey((byte B, byte G, byte R, byte A) pixel) =>
        Math.Abs(pixel.A - Half) <= 4
        && Math.Abs(pixel.R - Grey) <= 4
        && Math.Abs(pixel.G - Grey) <= 4
        && Math.Abs(pixel.B - Grey) <= 4;

    [Fact]
    public void AnIconHalfSeeThrough_ComesBackHalfSeeThroughInItsOwnColour() => OnStaThread(() =>
    {
        var icon = ShellIcons.Load(TranslucentIcon(), 128);

        Assert.NotNull(icon);
        var middle = Middle(icon);
        Assert.True(IsHalfGrey(middle), $"grey at half alpha came back as {middle}");
    });

    [Fact]
    public void APictureHalfSeeThrough_ComesBackHalfSeeThroughInItsOwnColour() => OnStaThread(() =>
    {
        var thumbnail = ShellIcons.LoadThumbnail(TranslucentPicture(), 128);

        Assert.NotNull(thumbnail);
        var middle = Middle(thumbnail);
        Assert.True(IsHalfGrey(middle), $"grey at half alpha came back as {middle}");
    });
}
