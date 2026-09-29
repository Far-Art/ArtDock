using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.Controls;
using ArtDock.Dock;
using ArtDock.Interop;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers a pinned picture being drawn as itself: its thumbnail by default, its icon on
/// request, and the dock fitting it rather than squashing it into a square.
/// </summary>
/// <remarks>
/// The thumbnails are real ones, from the shell, of pictures written for the test — a solid
/// colour, so what came back can be told from the shell's icon for the type by looking at
/// the middle of it.
/// </remarks>
public sealed class PictureThumbnailTests : IDisposable
{
    private const uint Red = 0xFFFF0000;
    private const uint Blue = 0xFF0000FF;

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "ArtDockTests", Guid.NewGuid().ToString("N"));

    public PictureThumbnailTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // The shell can still be holding a file it was asked to thumbnail; a stray
            // folder in %TEMP% is not a failure of anything under test.
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

    private static BitmapSource Solid(uint bgra, int width, int height)
    {
        var bitmap = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, palette: null,
            Enumerable.Repeat(bgra, width * height).ToArray(), width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>One colour over the top half and another under it, which has a right way up.</summary>
    private static BitmapSource Split(uint top, uint bottom, int width, int height)
    {
        var pixels = new uint[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = i / width < height / 2 ? top : bottom;
        }

        var bitmap = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, palette: null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Writes a picture in the format the name's extension says: one colour, or two stacked
    /// when <paramref name="bottom"/> is given.
    /// </summary>
    private string Picture(string name, uint bgra, int width = 200, int height = 100, uint? bottom = null)
    {
        BitmapEncoder encoder = Path.GetExtension(name) == ".jpg" ? new JpegBitmapEncoder() : new PngBitmapEncoder();
        var image = bottom is { } under ? Split(bgra, under, width, height) : Solid(bgra, width, height);

        // Opaque, which a JPEG has to be anyway.
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0)));

        var path = Path.Combine(_folder, name);
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }

        return path;
    }

    private static DockItem Pin(string path, bool useIcon = false, string? iconPath = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Label = Path.GetFileName(path),
        TargetPath = path,
        IconPath = iconPath,
        UseIconNotThumbnail = useIcon
    };

    /// <summary>
    /// The pixel at a point of an image, as straight BGRA, given as a share of the way across
    /// and down.
    /// </summary>
    private static (byte B, byte G, byte R, byte A) PixelAt(ImageSource image, double across = 0.5, double down = 0.5)
    {
        var bitmap = new FormatConvertedBitmap((BitmapSource)image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        bitmap.CopyPixels(
            new Int32Rect((int)(bitmap.PixelWidth * across), (int)(bitmap.PixelHeight * down), 1, 1),
            pixel,
            4,
            0);
        return (pixel[0], pixel[1], pixel[2], pixel[3]);
    }

    private static bool IsRed(ImageSource image, double down = 0.5) =>
        PixelAt(image, down: down) is { A: 255, R: > 200, G: < 60, B: < 60 };

    private static bool IsBlue(ImageSource image, double down = 0.5) =>
        PixelAt(image, down: down) is { A: 255, B: > 200, G: < 60, R: < 60 };

    [Theory]
    [InlineData(".png", true)]
    [InlineData(".jpg", true)]
    [InlineData(".JPEG", true)]
    [InlineData(".exe", false)]
    [InlineData(".txt", false)]
    [InlineData(".lnk", false)]
    [InlineData("", false)]
    public void APictureType_IsWhatWindowsSaysItIs(string extension, bool picture) =>
        Assert.Equal(picture, ShellIcons.IsPictureType(extension));

    [Theory]
    [InlineData(@"C:\Photos\beach.png", true)]
    [InlineData(@"\\server\share\beach.jpg", true)]
    [InlineData("https://example.com/beach.png", false)]
    [InlineData("beach.png", false)]
    [InlineData(@"C:\Windows\notepad.exe", false)]
    [InlineData(null, false)]
    public void OnlyAPictureOnDisk_HasAThumbnailToShow(string? target, bool picture)
    {
        // A web address can end in .png as well, and asking the shell for its thumbnail
        // would be a download on the thread that draws the dock.
        Assert.Equal(picture, PinnedAppsService.OpensPicture(target));
    }

    [Theory]
    [InlineData("landscape.png")]
    [InlineData("landscape.jpg")]
    public void APinnedPicture_IsDrawnAsItself(string name) => OnStaThread(() =>
    {
        var icon = PinnedAppsService.LoadIcon(Pin(Picture(name, Red)));

        Assert.NotNull(icon);
        Assert.True(IsRed(icon), $"the middle of the image is {PixelAt(icon)}, not the picture's red");

        // Its own shape, which is what tells a thumbnail from any icon the shell has.
        Assert.True(icon.Width > icon.Height * 1.5, $"a 2:1 picture came back {icon.Width}×{icon.Height}");
    });

    /// <summary>
    /// A thumbnail comes back from the shell with its rows the other way round from an icon.
    /// </summary>
    /// <remarks>
    /// Every other test here draws in one colour, which reads the same either way up — which
    /// is how a dock that showed every picture upside down passed them.
    /// </remarks>
    [Theory]
    [InlineData("upright.png")]
    [InlineData("upright.jpg")]
    public void APinnedPicture_IsTheRightWayUp(string name) => OnStaThread(() =>
    {
        var icon = PinnedAppsService.LoadIcon(Pin(Picture(name, Red, bottom: Blue)));

        Assert.NotNull(icon);
        Assert.True(IsRed(icon, down: 0.25), $"the top of the image is {PixelAt(icon, down: 0.25)}, not red");
        Assert.True(IsBlue(icon, down: 0.75), $"the bottom of the image is {PixelAt(icon, down: 0.75)}, not blue");
    });

    [Fact]
    public void AskedForTheIcon_APictureGetsTheIcon() => OnStaThread(() =>
    {
        var icon = PinnedAppsService.LoadIcon(Pin(Picture("asked.png", Red), useIcon: true));

        Assert.NotNull(icon);
        Assert.Equal(icon.Width, icon.Height, 3);
        Assert.False(IsRed(icon), "the icon came back as the picture's own red");
    });

    [Fact]
    public void AChosenImage_WinsOverTheThumbnail() => OnStaThread(() =>
    {
        var chosen = Picture("chosen.png", Blue, 64, 64);
        var icon = PinnedAppsService.LoadIcon(Pin(Picture("target.png", Red), iconPath: chosen));

        Assert.NotNull(icon);
        Assert.True(IsBlue(icon), $"the middle of the image is {PixelAt(icon)}, not the chosen blue");
    });

    [Fact]
    public void APictureRewritten_IsReadAgain() => OnStaThread(() =>
    {
        // The thumbnail is of the file's contents, which change under a pin in a way an
        // application's icon does not.
        var path = Picture("edited.png", Red);
        Assert.True(IsRed(PinnedAppsService.LoadIcon(Pin(path))!));

        Picture("edited.png", Blue);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        var again = PinnedAppsService.LoadIcon(Pin(path));
        Assert.NotNull(again);
        Assert.True(IsBlue(again), $"the middle of the image is {PixelAt(again)}: the old thumbnail was kept");
    });

    [Fact]
    public void AWideImage_IsFittedOnTheDockRatherThanSquashed() => OnStaThread(() =>
    {
        // At 50 the box inside the inset is 42 square, so a 2:1 image fills it 42 wide by
        // 21 high, from 14.5 down to 35.5. Stretched, it filled the whole box.
        const int size = 50;
        var visual = new DockItemVisual(
            new DockItem { Id = "wide", Label = "Wide", Icon = Solid(Red, 80, 40) },
            size);
        visual.Measure(new Size(size, size));
        visual.Arrange(new Rect(0, 0, size, size));

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var pixels = new uint[size * size];
        target.CopyPixels(pixels, size * 4, 0);
        uint At(int x, int y) => pixels[(y * size) + x];

        Assert.Equal(Red, At(25, 25));
        Assert.Equal(0u, At(25, 8) >> 24);
        Assert.Equal(0u, At(25, 42) >> 24);
    });
}
