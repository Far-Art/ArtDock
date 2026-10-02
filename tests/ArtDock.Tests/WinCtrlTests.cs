using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.Controls;
using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers the Windows key and Ctrl held: when the hold counts (<see cref="WinCtrlHold"/>), which
/// items the dock numbers for it, and the number drawn on an icon.
/// </summary>
/// <remarks>
/// The keys themselves are read from Windows (<c>HeldKeys</c>) and cannot be pressed here; what
/// is tested is what the dock makes of them.
/// </remarks>
public class WinCtrlTests
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

    // ---- the hold -------------------------------------------------------------------------------

    /// <summary>The two count the moment they are down together, and stop the moment either is let go.</summary>
    [Fact]
    public void TheHold_CountsTheMomentTheKeysAreDown()
    {
        var hold = new WinCtrlHold();

        Assert.True(hold.Look(down: true, other: false));
        Assert.True(hold.IsHeld);

        Assert.False(hold.Look(down: true, other: false));

        Assert.True(hold.Look(down: false, other: false));
        Assert.False(hold.IsHeld);
        Assert.False(hold.CutShort);
    }

    /// <summary>
    /// Anything else pressed with them — an arrow that switches desktops, Shift or Alt — cuts the
    /// hold short, and it does not come back until both keys have been let go.
    /// </summary>
    [Fact]
    public void AnythingElsePressed_CutsTheHoldShort_UntilTheKeysAreLetGo()
    {
        var hold = new WinCtrlHold();
        hold.Look(down: true, other: false);

        Assert.True(hold.Look(down: true, other: true));
        Assert.False(hold.IsHeld);
        Assert.True(hold.CutShort);

        // The arrow let go, the two still down: still nothing.
        Assert.False(hold.Look(down: true, other: false));
        Assert.True(hold.CutShort);

        hold.Look(down: false, other: false);
        Assert.True(hold.Look(down: true, other: false));
        Assert.False(hold.CutShort);
    }

    /// <summary>Something else already down as the two go down means they are the start of it: no hold at all.</summary>
    [Fact]
    public void AnythingElsePressedFirst_KeepsTheHoldFromStarting()
    {
        var hold = new WinCtrlHold();

        Assert.False(hold.Look(down: true, other: true));
        Assert.False(hold.Look(down: true, other: false));
        Assert.False(hold.IsHeld);
        Assert.False(hold.CutShort);
    }

    /// <summary>
    /// A hotkey of the dock's, which the keys cannot see, ends the hold until the keys are let go —
    /// not cut short, since the key was the dock's to act on.
    /// </summary>
    [Fact]
    public void AHotkey_SpendsTheHold_WithoutCuttingItShort()
    {
        var hold = new WinCtrlHold();
        hold.Look(down: true, other: false);

        Assert.True(hold.Spend());
        Assert.False(hold.CutShort);
        Assert.False(hold.Look(down: true, other: false));

        hold.Look(down: false, other: false);
        Assert.True(hold.Look(down: true, other: false));
    }

    /// <summary>
    /// The toggle pressed with the two — Win+Ctrl+H — goes by the dock as it was before they
    /// brought it up: they bring it up the moment they are down, and a toggle that went by the
    /// dock as it is would find it up every time and never show a hidden one. Read from the
    /// source, since the dock's window is not made in a test.
    /// </summary>
    [Fact]
    public void TheToggle_GoesByTheDockAsItWasBeforeTheKeysBroughtItUp()
    {
        var source = File.ReadAllText(Path.Combine(SourceRoot(), "Views", "DockWindow.xaml.cs"));

        Assert.Matches(@"public void ToggleVisibility\(\) =>[^;]*_winCtrlRevealing \? _shownBeforeWinCtrl : IsDockShown", source);
    }

    private static string SourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ArtDock.sln")))
            {
                return Path.Combine(dir.FullName, "src", "ArtDock");
            }
        }

        throw new DirectoryNotFoundException("The repository root, with ArtDock.sln in it, was not found above the tests.");
    }

    // ---- the numbers ----------------------------------------------------------------------------

    private static DockItem Pin(string id) => new() { Id = id, Label = id };

    private static DockItem Separator(string id) => new() { Id = id, Label = "Separator", IsSeparator = true };

    private static int Places(params int[] places) => places.Aggregate(0, (bits, place) => bits | (1 << place));

    private static Dictionary<string, int?> Badges(DockBar bar) =>
        bar.Children.OfType<DockItemVisual>().ToDictionary(visual => visual.Item.Id, visual => visual.Badge);

    /// <summary>The items are numbered as the hotkeys count them: without the separators, and only those asked for.</summary>
    [Fact]
    public void TheItems_AreNumberedWithoutTheSeparators() => OnStaThread(() =>
    {
        var bar = new DockBar(new DockMetrics());
        bar.SetItems([Pin("a"), Separator("s"), Pin("b"), Pin("c")]);

        bar.ShowBadges(Places(1, 3));

        var badges = Badges(bar);
        Assert.Equal(1, badges["a"]);
        Assert.Null(badges["s"]);
        Assert.Null(badges["b"]);
        Assert.Equal(3, badges["c"]);

        bar.ShowBadges(0);
        Assert.All(Badges(bar).Values, Assert.Null);
    });

    /// <summary>An item added while the numbers are up is numbered where it stands, and the rest move up one.</summary>
    [Fact]
    public void TheNumbers_FollowTheRow() => OnStaThread(() =>
    {
        var bar = new DockBar(new DockMetrics());
        bar.SetItems([Pin("a"), Pin("b")]);
        bar.ShowBadges(Places(1, 2, 3));

        bar.SetItems([Pin("new"), Pin("a"), Pin("b")]);

        var badges = Badges(bar);
        Assert.Equal(1, badges["new"]);
        Assert.Equal(2, badges["a"]);
        Assert.Equal(3, badges["b"]);
    });

    /// <summary>Only nine are numbered: a tenth item has no key of its own.</summary>
    [Fact]
    public void NoItemPastTheNinth_IsNumbered() => OnStaThread(() =>
    {
        var bar = new DockBar(new DockMetrics());
        bar.SetItems([.. Enumerable.Range(1, 10).Select(n => Pin($"p{n}"))]);

        bar.ShowBadges(-1);

        var badges = Badges(bar);
        Assert.Equal(9, badges["p9"]);
        Assert.Null(badges["p10"]);
    });

    /// <summary>
    /// The number is drawn on the icon's top-left corner — and only while it is up — in the
    /// accent, shading to its darker shade, over the icon rather than under it.
    /// </summary>
    [Fact]
    public void TheNumber_IsDrawnOnTheIconsCorner() => OnStaThread(() =>
    {
        static byte[] Drawn(int? badge)
        {
            var visual = new DockItemVisual(new DockItem { Id = "app", Label = "App" }, 64) { Badge = badge };
            visual.Measure(new Size(64, 64));
            visual.Arrange(new Rect(0, 0, 64, 64));

            var target = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            var pixels = new byte[64 * 64 * 4];
            target.CopyPixels(pixels, 64 * 4, 0);
            return pixels;
        }

        static Color At(byte[] pixels, int x, int y)
        {
            var i = ((y * 64) + x) * 4;
            return Color.FromArgb(pixels[i + 3], pixels[i + 2], pixels[i + 1], pixels[i]);
        }

        var plain = Drawn(null);
        var badged = Drawn(4);

        // The disc is 0.42 of the icon across, so 26.88 here, centred 10.2 in from each edge; a
        // point in it clear of the digit, between the accent at its top and the darker shade at
        // its bottom.
        var inDisc = At(badged, 2, 10);
        var (light, dark) = (SystemColors.AccentColor, SystemColors.AccentColorDark1);
        static bool Between(byte value, byte a, byte b) => value >= Math.Min(a, b) - 2 && value <= Math.Max(a, b) + 2;
        Assert.Equal(255, inDisc.A);
        Assert.True(
            Between(inDisc.R, light.R, dark.R) && Between(inDisc.G, light.G, dark.G) && Between(inDisc.B, light.B, dark.B),
            $"the disc is {inDisc}, the accent {light} to {dark}");
        Assert.NotEqual(At(plain, 2, 10), inDisc);

        // The far corner is the icon's alone.
        Assert.Equal(At(plain, 58, 58), At(badged, 58, 58));
    });
}
