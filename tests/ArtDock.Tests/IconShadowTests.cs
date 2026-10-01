using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.Controls;
using ArtDock.Dock;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers the shadow an icon casts onto the bar: that it falls below the icon rather than
/// round it, is faint, is never under the icon, and leaves the icon itself exactly as it is
/// drawn without one.
/// </summary>
/// <remarks>
/// Asked for as a shadow that would not blur the icons, which is what the last of these holds:
/// every pixel the icon covers whole is the same with its shadow as without it.
/// </remarks>
public class IconShadowTests
{
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

    /// <summary>A picture the size the shell's icons come in, opaque inside a margin of <paramref name="margin"/>.</summary>
    private static BitmapSource Square(int size = 128, int margin = 0)
    {
        var pixels = new byte[size * size * 4];
        for (var y = margin; y < size - margin; y++)
        {
            for (var x = margin; x < size - margin; x++)
            {
                var at = ((y * size) + x) * 4;
                pixels[at] = 0xD7;
                pixels[at + 1] = 0x78;
                pixels[at + 2] = 0x00;
                pixels[at + 3] = 0xFF;
            }
        }

        var image = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
        image.Freeze();
        return image;
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var bgra = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(bgra, image.PixelWidth * 4, 0);
        return bgra;
    }

    /// <summary>
    /// An icon on its own, drawn by the dock's own control at a resting size of 64 and placed
    /// 16 in from the corner of a picture with room round it for the shadow.
    /// </summary>
    private static byte[] Drawn(ImageSource icon, bool shadow, out int size)
    {
        size = 100;
        var visual = new DockItemVisual(new DockItem { Id = "app", Label = "App", Icon = icon }, 64)
        {
            CastsShadow = shadow
        };
        visual.Measure(new Size(64, 64));
        visual.Arrange(new Rect(16, 16, 64, 64));

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return Pixels(target);
    }

    private static double MeanAlpha(byte[] bgra, int stride, int left, int right, int top, int bottom)
    {
        var sum = 0.0;
        var count = 0;
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                sum += bgra[(((y * stride) + x) * 4) + 3];
                count++;
            }
        }

        return sum / count;
    }

    [Fact]
    public void AnIcon_CastsNoShadow_UnlessAsked() => OnStaThread(() =>
    {
        // The icon spans 21.12 to 74.88 each way: 64 at rest, inset 8%, placed at 16.
        var plain = Drawn(Square(), shadow: false, out var size);

        Assert.Equal(0, MeanAlpha(plain, size, 30, 66, 77, 82));
    });

    [Fact]
    public void TheShadow_FallsBelowTheIcon_RatherThanRoundIt() => OnStaThread(() =>
    {
        var drawn = Drawn(Square(), shadow: true, out var size);

        // Three pixels out from each edge of the icon, which spans 21.12 to 74.88 each way.
        var below = MeanAlpha(drawn, size, 30, 66, 75, 77);
        var above = MeanAlpha(drawn, size, 30, 66, 18, 20);
        var beside = MeanAlpha(drawn, size, 18, 20, 30, 66);

        Assert.True(below >= 8, $"no shadow below the icon: {below:0.0} of 255");
        Assert.True(above < below / 4, $"as much shadow above the icon ({above:0.0}) as below it ({below:0.0})");
        Assert.True(beside < below * 0.6, $"a ring round the icon: {beside:0.0} beside it against {below:0.0} below");
    });

    [Fact]
    public void TheIcon_IsDrawnExactlyAsWithoutItsShadow() => OnStaThread(() =>
    {
        // Wherever the icon covers a pixel whole, the pixel is the icon's, untouched: the shadow
        // goes under it and the icon is not drawn any differently for having one.
        var plain = Drawn(Square(), shadow: false, out _);
        var shadowed = Drawn(Square(), shadow: true, out _);

        var covered = 0;
        for (var at = 0; at < plain.Length; at += 4)
        {
            if (plain[at + 3] != 0xFF)
            {
                continue;
            }

            covered++;
            Assert.Equal(plain[at..(at + 4)], shadowed[at..(at + 4)]);
        }

        Assert.True(covered > 2000, "the icon was not drawn");
    });

    [Fact]
    public void TheShadow_IsNeverUnderTheIcon() => OnStaThread(() =>
    {
        // A shadow is not seen through what casts it. Left under the icon it darkens the icon's
        // antialiased rim, which reads as a blurred edge, and anything see-through.
        var shadow = IconShadow.Cast(Square(margin: 16), IconShadow.Subtle)!;
        var pixels = Pixels(shadow.Image);
        var width = shadow.Image.PixelWidth;

        // The icon, at the 64 the shadow is worked at, covers 8 to 56 of it, inset by the pad
        // that Around puts back.
        var pad = (width - IconShadow.WorkSize) / 2;
        for (var y = pad + 9; y < pad + 55; y++)
        {
            for (var x = pad + 9; x < pad + 55; x++)
            {
                Assert.Equal(0, pixels[(((y * width) + x) * 4) + 3]);
            }
        }
    });

    [Fact]
    public void TheShadow_IsFaint() => OnStaThread(() =>
    {
        var shadow = IconShadow.Cast(Square(margin: 16), IconShadow.Subtle)!;
        var pixels = Pixels(shadow.Image);

        var darkest = 0;
        for (var at = 3; at < pixels.Length; at += 4)
        {
            darkest = Math.Max(darkest, pixels[at]);
        }

        Assert.InRange(darkest, 20, (int)Math.Ceiling(IconShadow.Subtle.Opacity * 255));
    });

    [Fact]
    public void TheShadow_IsCentredOnTheIcon_AndGrowsWithIt() => OnStaThread(() =>
    {
        // Every measure a share of the icon, so the wave, which magnifies the icon, magnifies its
        // shadow the same — and a picture that is not square, a photo's thumbnail, is shadowed
        // where it is drawn.
        var wide = BitmapSource.Create(96, 48, 96, 96, PixelFormats.Pbgra32, null,
            Enumerable.Repeat((byte)0xFF, 96 * 48 * 4).ToArray(), 96 * 4);
        var shadow = IconShadow.Cast(wide, IconShadow.Subtle)!;

        var icon = new Rect(10, 20, 60, 30);
        var around = shadow.Around(icon);
        Assert.Equal(icon.X + (icon.Width / 2), around.X + (around.Width / 2), 6);
        Assert.Equal(icon.Y + (icon.Height / 2), around.Y + (around.Height / 2), 6);
        Assert.Equal(icon.X - around.X, icon.Y - around.Y, 6);

        var twice = shadow.Around(new Rect(icon.X * 2, icon.Y * 2, icon.Width * 2, icon.Height * 2));
        Assert.Equal(around.Width * 2, twice.Width, 6);
        Assert.Equal(around.Height * 2, twice.Height, 6);
    });

    [Fact]
    public void AnIcon_IsShadowedOnce_AndAnEmptyPictureNotAtAll() => OnStaThread(() =>
    {
        var icon = Square(margin: 16);
        Assert.Same(IconShadow.For(icon), IconShadow.For(icon));

        Assert.Null(IconShadow.For(Square(margin: 64)));
    });

    [Fact]
    public void TheDock_ShadowsEveryIcon_AndOnesAddedLater() => OnStaThread(() =>
    {
        var bar = new DockBar(new DockMetrics { BaseSize = 50, MaxSize = 70, Gap = 8 });
        DockItem Pin(int i) => new() { Id = $"p{i}", Label = $"P{i}" };

        bar.SetItems([Pin(0), Pin(1)]);
        Assert.All(bar.Children.OfType<DockItemVisual>(), visual => Assert.False(visual.CastsShadow));

        bar.IconShadows = true;
        bar.SetItems([Pin(0), Pin(1), Pin(2)]);
        Assert.Equal(3, bar.Children.OfType<DockItemVisual>().Count());
        Assert.All(bar.Children.OfType<DockItemVisual>(), visual => Assert.True(visual.CastsShadow));

        bar.IconShadows = false;
        Assert.All(bar.Children.OfType<DockItemVisual>(), visual => Assert.False(visual.CastsShadow));
    });

    [Fact]
    public void TheShadow_IsOn_ForANewDock_AndForOneSetUpBeforeItExisted()
    {
        // On unless turned off, chosen once it had been seen on the dock. A file written before
        // the setting existed has nothing to say about it, and reads as on: a dock already set up
        // gains the shadow with the update that brings it.
        Assert.True(new DockSettings().IconShadows);

        var dir = Path.Combine(Path.GetTempPath(), "ArtDockTests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "before.json");
            File.WriteAllText(path, """{ "BaseSize": 44, "AutoHide": true }""");

            Assert.True(SettingsStore.Import(path).IconShadows);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
