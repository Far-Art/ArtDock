using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArtDock.Services;

/// <summary>
/// The shadow an icon on the dock casts: faint, close, and falling a little below it.
/// </summary>
/// <remarks>
/// <para>
/// Worked out once per icon, from the icon's own outline, into a small bitmap the dock draws
/// under it — never a <c>DropShadowEffect</c>. WPF keeps nothing of an effect's output, so an
/// effect would draw every icon off-screen and blur it again on every frame of the wave, where
/// a bitmap costs nothing measurable, the same as the icon over it. The blur is the one a drawn
/// folder's symbol casts (<see cref="SvgBlur"/>).
/// </para>
/// <para>
/// The icon itself is never touched: it is drawn exactly as it is without a shadow, over this.
/// Baking the two into one picture would resample the icon, and softening the icon is the one
/// thing its shadow must not do.
/// </para>
/// <para>
/// Nor is the shadow under the icon, which takes out of it whatever the icon covers. A shadow is
/// not seen through what casts it — the bar's is cut out of the bar for the same reason — and
/// left in, it would darken an icon's antialiased rim, which reads as a blurred edge, and
/// anything see-through: a drop preview, a disabled item, the launch pulse, which all fade the
/// icon and its shadow together.
/// </para>
/// <para>
/// Every measure is a share of the icon's longer side, so the shadow keeps its proportions at
/// every icon size, and the wave magnifies it with the icon.
/// </para>
/// </remarks>
public sealed class IconShadow
{
    /// <summary>A shadow's shape, every length a share of the icon's longer side.</summary>
    /// <param name="Drop">How far below the icon it falls.</param>
    /// <param name="Deviation">How soft it is: the blur's standard deviation.</param>
    /// <param name="Inset">How far it is drawn in from the icon's outline before it is blurred,
    /// which keeps it off the icon's sides and top so that it reads as below the icon rather than
    /// as a ring round it.</param>
    /// <param name="Opacity">How dark it is where it is darkest, 0 to 1.</param>
    public sealed record Look(double Drop, double Deviation, double Inset, double Opacity);

    /// <summary>The dock's shadow.</summary>
    public static Look Subtle { get; } = new(Drop: 0.03, Deviation: 0.035, Inset: 0.015, Opacity: 0.3);

    /// <summary>
    /// How many pixels across its longer side an icon's shadow is worked out at.
    /// </summary>
    /// <remarks>
    /// Half the 128 the shell's icons come in, and a quarter of the memory: a shadow is all blur,
    /// and has no detail that the extra pixels would keep. Even at the top of the wave on a 150%
    /// display it is drawn little larger than this.
    /// </remarks>
    public const int WorkSize = 64;

    /// <summary>
    /// Each icon's shadow, kept for as long as the icon is.
    /// </summary>
    /// <remarks>
    /// By the picture rather than by the item: the icons are already kept and shared — two pins
    /// of one program draw the same one — and a picture that is replaced, as the Recycle Bin's
    /// is whenever it fills or empties, takes its shadow with it.
    /// </remarks>
    private static readonly ConditionalWeakTable<ImageSource, StrongBox<IconShadow?>> Cache = new();

    private readonly int _width;
    private readonly int _height;
    private readonly int _pad;

    private IconShadow(BitmapSource image, int width, int height, int pad)
    {
        Image = image;
        _width = width;
        _height = height;
        _pad = pad;
    }

    /// <summary>The shadow, to be drawn at <see cref="Around"/> before the icon.</summary>
    public BitmapSource Image { get; }

    /// <summary>
    /// The dock's shadow for an icon, worked out the first time it is asked for; null for a
    /// picture with nothing in it, or one that cannot be read.
    /// </summary>
    public static IconShadow? For(ImageSource icon) =>
        Cache.GetValue(icon, static picture => new StrongBox<IconShadow?>(Cast(picture, Subtle))).Value;

    /// <summary>
    /// Where the shadow goes for an icon drawn in <paramref name="icon"/>: the same rectangle,
    /// grown by the room the shadow's blur and drop take.
    /// </summary>
    public Rect Around(Rect icon)
    {
        var x = icon.Width / _width;
        var y = icon.Height / _height;
        return new Rect(
            icon.X - (_pad * x),
            icon.Y - (_pad * y),
            (_width + (2 * _pad)) * x,
            (_height + (2 * _pad)) * y);
    }

    /// <summary>Works out an icon's shadow in a given look, without keeping it.</summary>
    public static IconShadow? Cast(ImageSource icon, Look look)
    {
        float[]? outline;
        int width, height;
        try
        {
            outline = OutlineOf(icon, out width, out height);
        }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException
            or ArgumentException or COMException or FileFormatException)
        {
            // A picture WPF could draw but not read back. Better an icon with no shadow than a
            // dock that cannot draw.
            return null;
        }

        if (outline is null || !outline.Any(alpha => alpha > 0))
        {
            return null;
        }

        var longer = Math.Max(width, height);
        var drop = Math.Max(0, look.Drop * longer);
        var deviation = Math.Max(0, look.Deviation * longer);
        var inset = (int)Math.Round(Math.Max(0, look.Inset * longer));

        // Room for the blur on every side — three deviations is as far as SvgBlur carries a
        // value — and below for the drop as well. Kept the same on every side, so the icon sits
        // in the middle of its shadow's picture and Around is a plain inflation.
        var pad = (int)Math.Ceiling(3 * deviation) + (int)Math.Ceiling(drop) + 1;
        var stride = width + (2 * pad);
        var rows = height + (2 * pad);

        // The outline moved down by the drop, a fraction of a pixel shared between two rows.
        var cast = new float[stride * rows];
        var whole = (int)Math.Floor(drop);
        var part = (float)(drop - whole);
        for (var y = 0; y < height; y++)
        {
            var at = ((y + pad + whole) * stride) + pad;
            for (var x = 0; x < width; x++)
            {
                var alpha = outline[(y * width) + x];
                cast[at + x] += alpha * (1 - part);
                cast[at + stride + x] += alpha * part;
            }
        }

        Shrink(cast, stride, rows, inset);
        SvgBlur.Blur(cast, stride, rows, deviation, across: true);
        SvgBlur.Blur(cast, stride, rows, deviation, across: false);

        // Black, so premultiplied it is nothing but its alpha.
        var pixels = new byte[stride * rows * 4];
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < stride; x++)
            {
                var ix = x - pad;
                var iy = y - pad;
                var covered = ix >= 0 && ix < width && iy >= 0 && iy < height ? outline[(iy * width) + ix] : 0;
                var value = cast[(y * stride) + x] * (1 - covered) * look.Opacity;
                pixels[(((y * stride) + x) * 4) + 3] = (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
            }
        }

        var image = BitmapSource.Create(stride, rows, 96, 96, PixelFormats.Pbgra32, null, pixels, stride * 4);
        image.Freeze();
        return new IconShadow(image, width, height, pad);
    }

    /// <summary>
    /// The icon's opacity, 0 to 1, at <see cref="WorkSize"/> pixels across its longer side.
    /// </summary>
    /// <remarks>
    /// Shrunk by <see cref="TransformedBitmap"/>, which averages every pixel it covers, so a thin
    /// line still casts the faint shadow it should rather than none or a broken one.
    /// </remarks>
    private static float[]? OutlineOf(ImageSource icon, out int width, out int height)
    {
        width = height = 0;
        if ((icon as BitmapSource ?? Rasterize(icon)) is not { PixelWidth: > 0, PixelHeight: > 0 } source)
        {
            return null;
        }

        var scale = (double)WorkSize / Math.Max(source.PixelWidth, source.PixelHeight);
        var sized = Math.Abs(scale - 1) < 1e-9
            ? source
            : new TransformedBitmap(source, new ScaleTransform(scale, scale));
        var pbgra = sized.Format == PixelFormats.Pbgra32
            ? sized
            : new FormatConvertedBitmap(sized, PixelFormats.Pbgra32, null, 0);

        width = pbgra.PixelWidth;
        height = pbgra.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var bgra = new byte[width * height * 4];
        pbgra.CopyPixels(bgra, width * 4, 0);

        var alpha = new float[width * height];
        for (var i = 0; i < alpha.Length; i++)
        {
            alpha[i] = bgra[(i * 4) + 3] / 255f;
        }

        return alpha;
    }

    /// <summary>
    /// A picture that is not a bitmap — a drawing — drawn into one at <see cref="WorkSize"/>.
    /// </summary>
    private static BitmapSource? Rasterize(ImageSource image)
    {
        if (!(image.Width > 0) || !(image.Height > 0))
        {
            return null;
        }

        var scale = WorkSize / Math.Max(image.Width, image.Height);
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(image, new Rect(0, 0, width, height));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return target;
    }

    /// <summary>
    /// Draws a shape in by <paramref name="radius"/> pixels: each value becomes the least of
    /// those within that distance across and down.
    /// </summary>
    private static void Shrink(float[] values, int width, int height, int radius)
    {
        if (radius <= 0)
        {
            return;
        }

        ShrinkOneWay(values, width, height, radius, across: true);
        ShrinkOneWay(values, width, height, radius, across: false);
    }

    private static void ShrinkOneWay(float[] values, int width, int height, int radius, bool across)
    {
        var (lines, length, next, step) = across ? (height, width, width, 1) : (width, height, 1, width);
        var line = new float[length];
        for (var l = 0; l < lines; l++)
        {
            var start = l * next;
            for (var i = 0; i < length; i++)
            {
                line[i] = values[start + (i * step)];
            }

            for (var i = 0; i < length; i++)
            {
                // Nothing lies past the edge, so a shape is drawn in from it too.
                var least = i - radius < 0 || i + radius >= length ? 0 : line[i];
                for (var j = Math.Max(0, i - radius); j <= Math.Min(length - 1, i + radius) && least > 0; j++)
                {
                    least = Math.Min(least, line[j]);
                }

                values[start + (i * step)] = least;
            }
        }
    }
}
