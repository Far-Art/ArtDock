using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.Controls;
using ArtDock.Dock;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers the Recycle Bin's icon following the bin: read afresh rather than cached, and
/// drawn once it has been swapped.
/// </summary>
/// <remarks>
/// The bin is the one pin whose icon changes while it is pinned. The shell's notification
/// that it has changed is covered by hand rather than here, because the only ways to raise
/// one are to change the bin or to broadcast a fake one to every window on the machine.
/// </remarks>
public class RecycleBinIconTests
{
    private const uint Red = 0xFFFF0000;
    private const uint Blue = 0xFF0000FF;

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

    /// <summary>An opaque square of one colour, standing in for an icon.</summary>
    private static ImageSource Solid(uint bgra)
    {
        const int edge = 16;
        var bitmap = BitmapSource.Create(
            edge, edge, 96, 96, PixelFormats.Bgra32, palette: null,
            Enumerable.Repeat(bgra, edge * edge).ToArray(), edge * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>How many pixels of the bar, as drawn now, are exactly this colour.</summary>
    private static int Count(DockBar bar, uint colour)
    {
        const int w = 240;
        const int h = 160;

        bar.UpdateLayout();
        var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        target.Render(bar);

        var pixels = new uint[w * h];
        target.CopyPixels(pixels, w * 4, 0);
        return pixels.Count(pixel => pixel == colour);
    }

    private static DockBar Laid(DockItem item)
    {
        var bar = new DockBar(new DockMetrics { BaseSize = 50, MaxSize = 70, Gap = 8 })
        {
            Width = 240,
            Height = 160
        };

        bar.SetItems([item]);
        bar.Measure(new Size(240, 160));
        bar.Arrange(new Rect(0, 0, 240, 160));
        return bar;
    }

    [Fact]
    public void TheRecycleBin_IsReadAfreshWhereAnAppIsCached() => OnStaThread(() =>
    {
        var bin = PinnedAppsService.ToDockItem(DockPresets.Create("recyclebin")!);
        var app = new DockItem
        {
            Id = "explorer",
            Label = "File Explorer",
            TargetPath = Environment.ExpandEnvironmentVariables(@"%WINDIR%\explorer.exe")
        };

        var first = PinnedAppsService.LoadIcon(bin);
        Assert.NotNull(first);

        // A cached bin is wrong from the moment it changes — and one unpinned, emptied and
        // pinned again comes back wrong, because no notification reaches a pin that is not
        // there. So every read of it goes to the shell, and gets a new image.
        Assert.NotSame(first, PinnedAppsService.LoadIcon(bin));

        // Where everything else is served from the cache, which is what makes the check
        // above mean something.
        Assert.Same(PinnedAppsService.LoadIcon(app), PinnedAppsService.LoadIcon(app));
    });

    // ---- full or empty decided by the dock, since Windows' setting stopped following ----------

    [Fact]
    public void ABinEmptiedWithTheLocationUnmoved_IsAChange()
    {
        // The case a 2026-09-30 Windows update made: emptied in Explorer, the shell's icon
        // location still saying full. The notification came; comparing locations alone threw
        // it away.
        const string full = @"C:\WINDOWS\System32\imageres.dll,-54";

        Assert.True(Interop.RecycleBinWatch.HasChanged(full, full, wasEmpty: false, empty: true));
        Assert.True(Interop.RecycleBinWatch.HasChanged(full, full, wasEmpty: true, empty: false));
    }

    [Fact]
    public void SomeoneElsesImage_IsNoChange()
    {
        const string full = @"C:\WINDOWS\System32\imageres.dll,-54";

        Assert.False(Interop.RecycleBinWatch.HasChanged(full, full, wasEmpty: false, empty: false));
    }

    [Fact]
    public void AMovedLocation_OrAnUnanswered_IsAChange()
    {
        const string full = @"C:\WINDOWS\System32\imageres.dll,-54";
        const string other = @"C:\Icons\bin.ico,0";

        Assert.True(Interop.RecycleBinWatch.HasChanged(full, other, wasEmpty: false, empty: false));
        Assert.True(Interop.RecycleBinWatch.HasChanged(full, null, wasEmpty: false, empty: false));
        Assert.True(Interop.RecycleBinWatch.HasChanged(full, full, wasEmpty: false, empty: null));
    }

    [Theory]
    [InlineData(@"C:\WINDOWS\System32\imageres.dll,-54", @"C:\WINDOWS\System32\imageres.dll", -54)]
    [InlineData(@"""C:\Icons\my bin.ico"",3", @"C:\Icons\my bin.ico", 3)]
    [InlineData(@"C:\Icons\bin.ico", @"C:\Icons\bin.ico", 0)]
    public void AnIconLocation_IsSplitAsTheShellReadsIt(string location, string file, int index)
    {
        Assert.True(Interop.ShellIcons.TryParseLocation(location, out var parsedFile, out var parsedIndex));
        Assert.Equal(file, parsedFile);
        Assert.Equal(index, parsedIndex);
    }

    [Fact]
    public void AnIconLocation_HasItsVariablesExpanded()
    {
        Assert.True(Interop.ShellIcons.TryParseLocation(@"%SystemRoot%\System32\imageres.dll,-55", out var file, out _));
        Assert.Equal(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\imageres.dll"), file);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"C:\Icons\bin.ico,notanumber")]
    public void ANonsenseLocation_IsRefused(string location) =>
        Assert.False(Interop.ShellIcons.TryParseLocation(location, out _, out _));

    [Fact]
    public void TheBinFullAndEmpty_AreTwoDifferentPictures() => OnStaThread(() =>
    {
        // Read from this machine's own registry and imageres.dll: nothing written, nothing of
        // the user's bin touched.
        var full = Interop.ShellIcons.LoadFromLocation(Interop.RecycleBin.IconLocation(empty: false), 128);
        var empty = Interop.ShellIcons.LoadFromLocation(Interop.RecycleBin.IconLocation(empty: true), 128);

        Assert.NotNull(full);
        Assert.NotNull(empty);
        Assert.Equal(128, ((BitmapSource)full).PixelWidth);
        Assert.NotEqual(Pixels((BitmapSource)full), Pixels((BitmapSource)empty));
    });

    private static string Pixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels));
    }

    /// <summary>
    /// The half of "the icon does not update" that is WPF's rather than the shell's.
    /// </summary>
    /// <remarks>
    /// An element keeps what it drew. Swapping <see cref="DockItem.Icon"/> on an item the
    /// dock is showing changes nothing on screen until the dock is told — which is the whole
    /// of what <see cref="DockBar.RefreshIcon"/> is for, and why the new picture has to be
    /// asked for rather than assumed.
    /// </remarks>
    [Fact]
    public void ASwappedIcon_IsDrawnOnceTheDockIsTold() => OnStaThread(() =>
    {
        var bin = new DockItem { Id = "recyclebin", Label = "Recycle Bin", Icon = Solid(Red) };
        var bar = Laid(bin);
        Assert.True(Count(bar, Red) > 0, "the icon was never drawn, so nothing below means anything");

        bin.Icon = Solid(Blue);
        Assert.Equal(0, Count(bar, Blue));

        bar.RefreshIcon(bin);
        Assert.True(Count(bar, Blue) > 0, "the new icon was not drawn after RefreshIcon");
        Assert.Equal(0, Count(bar, Red));
    });
}
