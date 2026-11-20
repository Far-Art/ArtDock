using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.Controls;

namespace ArtDock.Tests;

/// <summary>
/// Covers the puff of cloud an icon dragged off the dock goes up in: there only while it plays,
/// fading as it ends, and inside the room the window keeps for it.
/// </summary>
public class PoofCloudTests
{
    private const int Size = 120;

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

    /// <summary>The cloud at <paramref name="progress"/>, drawn in a box twice the size, centred.</summary>
    private static byte[] Draw(double progress)
    {
        var cloud = new PoofCloud { Progress = progress, Width = Size, Height = Size };
        var host = new System.Windows.Controls.Grid { Width = Size * 2, Height = Size * 2, Children = { cloud } };
        host.Measure(new Size(Size * 2, Size * 2));
        host.Arrange(new Rect(0, 0, Size * 2, Size * 2));

        var target = new RenderTargetBitmap(Size * 2, Size * 2, 96, 96, PixelFormats.Pbgra32);
        target.Render(host);

        var pixels = new byte[Size * 2 * Size * 2 * 4];
        target.CopyPixels(pixels, Size * 2 * 4, 0);
        return pixels;
    }

    private static double TotalAlpha(byte[] pixels)
    {
        var total = 0d;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            total += pixels[i];
        }

        return total;
    }

    [Fact]
    public void NothingIsDrawnBeforeOrAfter() => OnStaThread(() =>
    {
        Assert.Equal(0, TotalAlpha(Draw(0)));
        Assert.Equal(0, TotalAlpha(Draw(1)));
    });

    [Fact]
    public void ACloudIsDrawnWhileItPlays_AndFadesAsItEnds() => OnStaThread(() =>
    {
        var early = TotalAlpha(Draw(0.25));
        var late = TotalAlpha(Draw(0.9));

        Assert.True(early > 0);
        Assert.True(late < early / 2, $"late {late}, early {early}");
    });

    [Fact]
    public void TheCloudStaysAboutItsBox() => OnStaThread(() =>
    {
        // The box is the window's room round the icon, centred in a host twice its size: what
        // falls outside it by more than a few pixels would be cut off by the window.
        const int slack = 6;
        var inner = new Int32Rect((Size / 2) - slack, (Size / 2) - slack, Size + (slack * 2), Size + (slack * 2));

        foreach (var progress in new[] { 0.1, 0.3, 0.5, 0.7, 0.9 })
        {
            var pixels = Draw(progress);
            var outside = 0d;
            for (var y = 0; y < Size * 2; y++)
            {
                for (var x = 0; x < Size * 2; x++)
                {
                    if (x >= inner.X && x < inner.X + inner.Width && y >= inner.Y && y < inner.Y + inner.Height)
                    {
                        continue;
                    }

                    outside += pixels[(((y * Size * 2) + x) * 4) + 3];
                }
            }

            Assert.True(outside < 255 * 4, $"{outside / 255:0.0} pixels' worth outside at {progress}");
        }
    });
}
