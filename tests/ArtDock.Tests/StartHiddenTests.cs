using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Interop;
using ArtDock.Dock;
using ArtDock.Interop;

namespace ArtDock.Tests;

/// <summary>
/// Covers a dock that hides as it starts — auto-hide on, or a window in front filling its
/// display: hidden before its window first goes up, rather than shown and then slid away.
/// </summary>
/// <remarks>
/// <para>
/// Reported 2026-10-14. The dock started where it shows and was sent away from there, by
/// auto-hide once the hide delay had run out, so a dock set to keep out of the way came up at
/// every sign-in, stood for a moment, and slid off.
/// </para>
/// <para>
/// The controller is the dock's own, on a window given a handle and never shown, as in
/// <see cref="PutAwayTests"/>: no slide ever lands, so a dock that slides is caught on its way.
/// </para>
/// </remarks>
public class StartHiddenTests
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

    /// <summary>
    /// Runs a test on the dock's auto-hide, with nothing over the dock, the pointer nowhere near
    /// it, and no program in front that the edge stands down for — before its window has gone up,
    /// as the first settings are applied, or after.
    /// </summary>
    private static void WithDock(bool appeared, Action<AutoHideController> body) => OnStaThread(() =>
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -20000,
            Top = -20000,
            Width = 40,
            Height = 40,
        };

        new WindowInteropHelper(window).EnsureHandle();
        try
        {
            body(new AutoHideController(
                window,
                new WindowChrome(window),
                isPointerOverDock: () => false,
                isCovered: () => false,
                raise: () => 0,
                holdAbove: () => { },
                lower: _ => { },
                standDown: () => false,
                restingBar: () => Rect.Empty)
            {
                HasAppeared = appeared
            });
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void WithAutoHideOn_TheDockStartsHidden() => WithDock(appeared: false, dock =>
    {
        dock.SetEnabled(true);

        Assert.Equal(DockVisibility.Hidden, dock.Visibility);
        Assert.False(dock.IsPutAway);
    });

    [Fact]
    public void BehindAWindowFillingTheDisplay_TheDockStartsHidden() => WithDock(appeared: false, dock =>
    {
        dock.Yield(true);

        Assert.Equal(DockVisibility.Hidden, dock.Visibility);
        Assert.False(dock.IsPutAway);
    });

    [Fact]
    public void WithNothingHidingIt_TheDockStartsShown() => WithDock(appeared: false, dock =>
    {
        dock.SetEnabled(false);
        dock.Yield(false);

        Assert.Equal(DockVisibility.Shown, dock.Visibility);
    });

    /// <summary>
    /// Auto-hide turned on in the settings dialog: the dock is up, and hides in its own time once
    /// the pointer has been away for the hide delay — not at once, as it does when starting.
    /// </summary>
    [Fact]
    public void AutoHideTurnedOnOnceUp_LeavesTheDockUpForNow() => WithDock(appeared: true, dock =>
    {
        dock.SetEnabled(true);

        Assert.Equal(DockVisibility.Shown, dock.Visibility);
    });

    [Fact]
    public void AWindowFillingTheDisplayOnceUp_SlidesTheDockAway() => WithDock(appeared: true, dock =>
    {
        dock.Yield(true);

        Assert.Equal(DockVisibility.Hiding, dock.Visibility);
    });

    /// <summary>Started hidden, the dock comes back as it always has: sliding up.</summary>
    [Fact]
    public void StartedHidden_TheDockSlidesUpWhenBroughtBack() => WithDock(appeared: false, dock =>
    {
        dock.SetEnabled(true);
        dock.HasAppeared = true;

        dock.HoldRevealed(true);

        Assert.Equal(DockVisibility.Revealing, dock.Visibility);
    });

    /// <summary>
    /// The dock says its window has gone up only once the first settings are applied, which is
    /// where auto-hide and the look at what is in front decide whether it starts hidden.
    /// </summary>
    /// <remarks>
    /// Read from the source, since the other order works on every machine with auto-hide off and
    /// nothing in front: said first, the dock would start where it shows and slide away as before.
    /// </remarks>
    [Fact]
    public void TheWindowIsSaidToHaveAppeared_AfterTheFirstSettingsAreApplied()
    {
        var source = File.ReadAllText(Path.Combine(FindSourceRoot(), "Views", "DockWindow.xaml.cs"));

        var start = source.IndexOf("protected override void OnSourceInitialized", StringComparison.Ordinal);
        Assert.True(start >= 0, "DockWindow's OnSourceInitialized was not found.");

        var applied = source.IndexOf("ApplySettings(_settings.Current);", start, StringComparison.Ordinal);
        var appeared = source.IndexOf("_autoHide.HasAppeared = true;", start, StringComparison.Ordinal);

        Assert.True(applied >= 0, "OnSourceInitialized no longer applies the first settings.");
        Assert.True(appeared >= 0, "OnSourceInitialized no longer says the dock's window has appeared.");
        Assert.True(applied < appeared, "The dock's window is said to have appeared before the first settings are applied.");
    }

    private static string FindSourceRoot()
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
}
