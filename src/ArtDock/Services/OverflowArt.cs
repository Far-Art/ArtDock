using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArtDock.Services;

/// <summary>
/// The picture on the overflow item — the one at the end of a dock too full for its display
/// (<c>Dock.DockFit</c>): three dots on a pale tile, Windows' sign for "more".
/// </summary>
/// <remarks>
/// A bitmap, as every icon the dock draws is, for the reason <see cref="FolderArt"/> gives: the
/// wave rescales an icon every frame, and a vector is tessellated again at every scale. Drawn
/// once, in the 256-unit box Windows' icons are designed in and at the dock's 128 pixels, with
/// the tile inset as far as the system's own tiled icons are, so it sits level beside them.
/// </remarks>
public static class OverflowArt
{
    private const int Pixels = 128;

    private static BitmapSource? _picture;

    /// <summary>The picture, drawn the first time it is asked for.</summary>
    public static BitmapSource Picture => _picture ??= Draw();

    private static BitmapSource Draw()
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            // In the 256-unit design box, scaled to the bitmap.
            context.PushTransform(new ScaleTransform(Pixels / 256.0, Pixels / 256.0));

            var tile = new Rect(24, 24, 208, 208);
            var face = new LinearGradientBrush(
                Color.FromRgb(0xFC, 0xFC, 0xFD), Color.FromRgb(0xE6, 0xE8, 0xEE), 90);
            face.Freeze();
            var rim = new Pen(new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0)), 4);
            rim.Freeze();
            context.DrawRoundedRectangle(face, rim, tile, 48, 48);

            var dot = new SolidColorBrush(Color.FromRgb(0x4A, 0x4D, 0x57));
            dot.Freeze();
            foreach (var x in (ReadOnlySpan<double>)[80, 128, 176])
            {
                context.DrawEllipse(dot, null, new Point(x, 128), 15, 15);
            }

            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(Pixels, Pixels, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
