using System.Windows;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers the two questions the edge asks before it does anything over a fullscreen program:
/// is the program in front one the dock stays down for, and does its window fill the display.
/// </summary>
/// <remarks>
/// <para>
/// The first is matched on the executable's file name rather than its path, because games
/// move — a versioned install folder, a Steam library on another drive — and a match that
/// stopped working would fail the way that matters: the dock rising over the game again. The
/// tests hold that, and hold that it does not overreach into names that merely contain the
/// listed one.
/// </para>
/// <para>
/// The second is geometry in physical pixels, against this machine's own two displays at
/// their real sizes: the main one at the origin, and the 4K one to its left at negative
/// coordinates. The case worth guarding is the maximized window, which must not count while
/// the taskbar is showing — it stops at the taskbar, well short of the bottom.
/// </para>
/// </remarks>
public class FullscreenAppsTests
{
    // ---- which program -------------------------------------------------------------

    [Fact]
    public void AProgramIsMatched_WhereverItNowLives()
    {
        // Listed from one install; running from another after an update moved it.
        string[] apps = [@"C:\Games\Strategy\1.2.0\strategy.exe"];

        Assert.True(FullscreenApps.Contains(apps, @"D:\SteamLibrary\common\Strategy\1.3.0\strategy.exe"));
    }

    [Fact]
    public void TheMatchIgnoresCase()
    {
        string[] apps = [@"C:\Games\Shooter.EXE"];

        Assert.True(FullscreenApps.Contains(apps, @"c:\games\shooter.exe"));
    }

    [Fact]
    public void AnotherProgram_IsNotMatched()
    {
        string[] apps = [@"C:\Games\game.exe"];

        Assert.False(FullscreenApps.Contains(apps, @"C:\Games\game2.exe"));
    }

    [Fact]
    public void ANameThatOnlyContainsTheListedOne_IsNotMatched()
    {
        // The whole file name, not a suffix of it: "mygame.exe" is not "game.exe".
        string[] apps = [@"C:\Games\game.exe"];

        Assert.False(FullscreenApps.Contains(apps, @"C:\Games\mygame.exe"));
    }

    [Fact]
    public void AFolderNamedLikeTheProgram_IsNotMatched()
    {
        string[] apps = [@"C:\Games\game.exe"];

        Assert.False(FullscreenApps.Contains(apps, @"C:\game.exe\launcher.exe"));
    }

    [Fact]
    public void AnEntryWrittenAsAFileName_MatchesAsWellAsAPath()
    {
        // The settings file is plain JSON, and a hand-written entry is likelier to be the
        // name than the full path.
        string[] apps = ["cs2.exe"];

        Assert.True(FullscreenApps.Contains(apps, @"C:\Steam\steamapps\common\cs2\game\bin\win64\cs2.exe"));
    }

    [Fact]
    public void NoProgramInFront_MatchesNothing()
    {
        string[] apps = [@"C:\Games\game.exe"];

        Assert.False(FullscreenApps.Contains(apps, null));
        Assert.False(FullscreenApps.Contains(apps, ""));
    }

    [Fact]
    public void AnEmptyList_MatchesNothing()
    {
        Assert.False(FullscreenApps.Contains([], @"C:\Games\game.exe"));
    }

    [Fact]
    public void BlankEntries_AreSkippedRatherThanMatchedOrThrown()
    {
        // A hand-edited file can hold an empty string, or a null in the array, and an entry
        // like that must not match everything — nor take the dock down on every tick.
        string?[] apps = ["", "   ", null, @"C:\Games\game.exe"];

        Assert.False(FullscreenApps.Contains(apps, @"C:\Windows\notepad.exe"));
        Assert.True(FullscreenApps.Contains(apps, @"C:\Games\game.exe"));
    }

    // ---- does it fill the display — the Exclusions page's question -----------------

    /// <summary>The main display: 2560 × 1440 at the origin.</summary>
    private static readonly Rect Main = new(0, 0, 2560, 1440);

    /// <summary>The 4K display to its left, at negative coordinates.</summary>
    private static readonly Rect FourK = new(-3840, 8, 3840, 2160);

    [Fact]
    public void AWindowExactlyTheSizeOfTheDisplay_FillsIt()
    {
        Assert.True(FullscreenApps.Fills(Main, Main));
    }

    [Fact]
    public void ABorderlessWindowReachingPastTheDisplay_FillsIt()
    {
        Assert.True(FullscreenApps.Fills(new Rect(-1, -1, 2562, 1442), Main));
    }

    [Fact]
    public void AFullscreenWindowOnADisplayAtNegativeCoordinates_FillsIt()
    {
        Assert.True(FullscreenApps.Fills(FourK, FourK));
    }

    [Fact]
    public void AWindowSpreadAcrossBothDisplays_FillsEach()
    {
        // A game run across every screen at once covers the dock's, whichever that is.
        var both = new Rect(-3840, 0, 3840 + 2560, 2168);

        Assert.True(FullscreenApps.Fills(both, Main));
        Assert.True(FullscreenApps.Fills(both, FourK));
    }

    [Fact]
    public void AMaximizedWindow_DoesNotFillTheDisplayWhileTheTaskbarShows()
    {
        // As Windows reports one: the invisible resize border hangs 8px past the work area
        // on every side, and the work area stops 48px short of the bottom, at the taskbar.
        var maximized = new Rect(-8, -8, 2560 + 16, 1392 + 16);

        Assert.False(FullscreenApps.Fills(maximized, Main));
    }

    [Fact]
    public void AMaximizedWindow_FillsTheDisplayWhereTheTaskbarHidesItself()
    {
        // The work area is then the whole display, and the window covers it — which is
        // what it looks like, so it counts.
        var maximized = new Rect(-8, -8, 2560 + 16, 1440 + 16);

        Assert.True(FullscreenApps.Fills(maximized, Main));
    }

    [Fact]
    public void AWindowOneRowShortOfTheBottom_DoesNotFillTheDisplay()
    {
        Assert.False(FullscreenApps.Fills(new Rect(0, 0, 2560, 1439), Main));
    }

    [Fact]
    public void AFullscreenWindowOnTheOtherDisplay_DoesNotFillThisOne()
    {
        Assert.False(FullscreenApps.Fills(FourK, Main));
        Assert.False(FullscreenApps.Fills(Main, FourK));
    }

    [Fact]
    public void ADisplayNotYetKnown_IsFilledByNothing()
    {
        // Before the dock has been placed there is no display to measure against, and an
        // empty rectangle is contained by everything — which would stand the edge down for
        // any listed program anywhere.
        Assert.False(FullscreenApps.Fills(Main, Rect.Empty));
        Assert.False(FullscreenApps.Fills(Main, new Rect(0, 0, 0, 0)));
    }

    // ---- what the dock goes under, whatever Always on top says ------------------
    //
    // The window in front covering the dock display's work area: maximized there, with the
    // taskbar showing or not, or fullscreen. Asked on 2026-09-30 — fullscreen first, then the
    // same day maximized — and both get the same answer, so the question is geometry alone.
    // The desktop, which covers everything, is ruled out by ForegroundApp. Telling the two
    // apart is the handle's question, below.

    /// <summary>The main display's work area: everything above its 48px taskbar.</summary>
    private static readonly Rect MainWork = new(0, 0, 2560, 1392);

    /// <summary>The 4K display's work area.</summary>
    private static readonly Rect FourKWork = new(-3840, 8, 3840, 2088);

    /// <summary>
    /// The one asked for: the maximize button, with the taskbar showing — as Windows reports
    /// the window, its invisible resize border hanging 8px past the work area on every side.
    /// </summary>
    [Fact]
    public void AMaximizedWindow_FillsTheWorkAreaWhileTheTaskbarShows()
    {
        var maximized = new Rect(-8, -8, 2560 + 16, 1392 + 16);

        Assert.True(FullscreenApps.Fills(maximized, MainWork));
    }

    [Fact]
    public void AMaximizedWindowThatDrawsItsOwnFrame_FillsTheWorkAreaExactly()
    {
        // A browser with no invisible border reports the work area itself.
        Assert.True(FullscreenApps.Fills(MainWork, MainWork));
    }

    [Fact]
    public void AFullscreenWindow_FillsTheWorkAreaToo()
    {
        Assert.True(FullscreenApps.Fills(Main, MainWork));
        Assert.True(FullscreenApps.Fills(new Rect(-4, -4, 2568, 1448), MainWork));
    }

    [Fact]
    public void ASnappedHalf_DoesNotFillTheWorkArea()
    {
        var leftHalf = new Rect(-8, -8, 1280 + 16, 1392 + 16);

        Assert.False(FullscreenApps.Fills(leftHalf, MainWork));
    }

    [Fact]
    public void AWindowOneRowShortOfTheTaskbar_DoesNotFillTheWorkArea()
    {
        Assert.False(FullscreenApps.Fills(new Rect(0, 0, 2560, 1391), MainWork));
    }

    [Fact]
    public void AWindowMaximizedOnTheOtherDisplay_DoesNotFillThisOnesWorkArea()
    {
        var maximizedThere = new Rect(-3840 - 8, 8 - 8, 3840 + 16, 2088 + 16);

        Assert.False(FullscreenApps.Fills(maximizedThere, MainWork));
        Assert.True(FullscreenApps.Fills(maximizedThere, FourKWork));
    }

    // ---- what the handle is not drawn over -----------------------------------------
    //
    // A window that has the whole display for itself, and not a maximized one — asked on
    // 2026-10-01 so that the handle does not sit over a video. Where the taskbar shows, the
    // geometry tells them apart; where it hides, the work area is the whole display and the
    // window's title bar and state have to.

    /// <summary>
    /// The case asked for: a browser showing a video, or after F11 — over the whole display,
    /// past the taskbar, whatever its style says.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AWindowOverTheTaskbar_IsFullscreen_HoweverItIsStyled(bool maximized, bool captioned)
    {
        Assert.True(FullscreenApps.IsFullscreen(Main, Main, MainWork, maximized, captioned));
        Assert.True(FullscreenApps.IsFullscreen(
            new Rect(-4, -4, 2568, 1448), Main, MainWork, maximized, captioned));
    }

    [Fact]
    public void AMaximizedWindow_IsNotFullscreen_WhileTheTaskbarShows()
    {
        var window = new Rect(-8, -8, 2560 + 16, 1392 + 16);

        Assert.False(FullscreenApps.IsFullscreen(window, Main, MainWork, maximized: true, captioned: true));
    }

    [Fact]
    public void AMaximizedWindowWithATitleBar_IsNotFullscreen_WhereTheTaskbarHidesItself()
    {
        // The work area is the whole display, and the window covers it as a fullscreen one would.
        var window = new Rect(-8, -8, 2560 + 16, 1440 + 16);

        Assert.False(FullscreenApps.IsFullscreen(window, Main, Main, maximized: true, captioned: true));
    }

    /// <summary>
    /// StarCraft II's borderless fullscreen, as measured here on 2026-09-28: a maximized popup
    /// with no caption, over the whole display. Asking only "is it maximized" took it for an
    /// ordinary window, and drew the handle over the game.
    /// </summary>
    [Fact]
    public void AMaximizedWindowWithNoTitleBar_IsFullscreen_WhereTheTaskbarHidesItself()
    {
        Assert.True(FullscreenApps.IsFullscreen(Main, Main, Main, maximized: true, captioned: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWindowNotMaximized_OverTheDisplay_IsFullscreen_WhereTheTaskbarHidesItself(bool captioned)
    {
        Assert.True(FullscreenApps.IsFullscreen(
            new Rect(-4, -4, 2568, 1448), Main, Main, maximized: false, captioned));
    }

    [Fact]
    public void AFullscreenWindowOnTheOtherDisplay_IsNotFullscreenOnThisOne()
    {
        Assert.False(FullscreenApps.IsFullscreen(FourK, Main, MainWork, maximized: false, captioned: false));
        Assert.True(FullscreenApps.IsFullscreen(FourK, FourK, FourKWork, maximized: false, captioned: false));
    }

    [Fact]
    public void AWindowOneRowShortOfTheBottom_IsNotFullscreen_HoweverItIsStyled()
    {
        var shortOfTheBottom = new Rect(0, 0, 2560, 1439);

        Assert.False(FullscreenApps.IsFullscreen(shortOfTheBottom, Main, MainWork, maximized: false, captioned: false));
        Assert.False(FullscreenApps.IsFullscreen(shortOfTheBottom, Main, Main, maximized: true, captioned: false));
    }
}
