using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Interop;
using ArtDock.Dock;
using ArtDock.Interop;

namespace ArtDock.Tests;

/// <summary>
/// Covers a dock put away from the tray while something holds it up — the settings dialog for
/// its preview, the keyboard, a dialog opened from the dock's menu: up for as long as it is held,
/// and away again once nothing holds it, unless the tray has said otherwise meanwhile.
/// </summary>
/// <remarks>
/// <para>
/// Asked for on 2026-10-02. The dock used to stay up after the settings dialog closed, and the
/// tray's entry, worded only when it was chosen, went on offering to show a dock that was in
/// plain sight.
/// </para>
/// <para>
/// The controller is the dock's own, on a window given a handle and never shown. Nothing pumps
/// its messages, so no slide ever lands and the timer that watches the pointer never ticks: a
/// dock is where it is going from the moment it sets off, which is how the tray's entry counts it
/// (<c>DockWindow.IsDockShown</c>).
/// </para>
/// </remarks>
public class PutAwayTests
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
    /// Runs a test on the dock's auto-hide, off as a new dock has it, with nothing over the dock,
    /// the pointer nowhere near it, and no program in front that the edge stands down for.
    /// </summary>
    private static void WithDock(Action<AutoHideController> body) => OnStaThread(() =>
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
                restingBar: () => Rect.Empty));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>On screen or on its way there, as the tray's entry counts it.</summary>
    private static bool IsShown(AutoHideController dock) =>
        dock.Visibility is DockVisibility.Shown or DockVisibility.Revealing;

    /// <summary>Hidden from the tray while nothing was hiding the dock: put away.</summary>
    private static void PutAway(AutoHideController dock)
    {
        dock.Choose(shown: false);

        Assert.False(IsShown(dock));
        Assert.True(dock.IsPutAway);
    }

    [Fact]
    public void AHold_BringsUpADockPutAway_AndPutsItAwayAgainWhenItLetsGo() => WithDock(dock =>
    {
        PutAway(dock);

        dock.HoldRevealed(true);
        Assert.True(IsShown(dock));
        Assert.False(dock.IsPutAway);

        dock.HoldRevealed(false);
        Assert.False(IsShown(dock));
        Assert.True(dock.IsPutAway);
    });

    [Fact]
    public void ADockThatWasUp_StaysUpWhenTheHoldLetsGo() => WithDock(dock =>
    {
        dock.HoldRevealed(true);
        dock.HoldRevealed(false);

        Assert.True(IsShown(dock));
        Assert.False(dock.IsPutAway);
    });

    /// <summary>Slid away by auto-hide is not put away, and auto-hide takes it back down in its own time.</summary>
    [Fact]
    public void ADockAutoHideSlidAway_IsNotPutAwayWhenTheHoldLetsGo() => WithDock(dock =>
    {
        dock.SetEnabled(true);
        dock.Hide();
        Assert.False(dock.IsPutAway);

        dock.HoldRevealed(true);
        dock.HoldRevealed(false);

        Assert.True(IsShown(dock));
        Assert.False(dock.IsPutAway);
    });

    /// <summary>The settings dialog and an item's editor opened from it: the dialog lets go last.</summary>
    [Fact]
    public void OnlyTheLastHoldToLetGo_PutsTheDockAway() => WithDock(dock =>
    {
        PutAway(dock);

        dock.HoldRevealed(true);
        dock.HoldRevealed(true);
        dock.HoldRevealed(false);
        Assert.True(IsShown(dock));

        dock.HoldRevealed(false);
        Assert.False(IsShown(dock));
        Assert.True(dock.IsPutAway);
    });

    /// <summary>
    /// The tray's <em>Hide dock</em>, or the hotkey's, while the settings dialog or the keyboard has
    /// the dock up: away it goes, and away it stays when the hold lets go — whether or not it had
    /// been put away before.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenOnRequestWhileHeld_TheDockStaysAway(bool putAwayBefore) => WithDock(dock =>
    {
        if (putAwayBefore)
        {
            PutAway(dock);
        }

        dock.HoldRevealed(true);
        dock.Choose(shown: false);
        Assert.False(IsShown(dock));

        dock.HoldRevealed(false);
        Assert.False(IsShown(dock));
        Assert.True(dock.IsPutAway);
    });

    /// <summary>A choice is kept: hidden from the tray while held, and shown again, it is not put away after.</summary>
    [Fact]
    public void ShownAgainOnRequestWhileHeld_TheDockStaysUp() => WithDock(dock =>
    {
        PutAway(dock);

        dock.HoldRevealed(true);
        dock.Choose(shown: false);
        dock.Choose(shown: true);
        dock.HoldRevealed(false);

        Assert.True(IsShown(dock));
        Assert.False(dock.IsPutAway);
    });

    /// <summary>
    /// Auto-hide turned on while the settings dialog is open, and saved: it has the dock from then
    /// on, and hides it in its own time rather than the dock being put away.
    /// </summary>
    [Fact]
    public void AutoHideTurnedOnMeanwhile_HasTheDockFromThenOn() => WithDock(dock =>
    {
        PutAway(dock);

        dock.HoldRevealed(true);
        dock.SetEnabled(true);
        dock.HoldRevealed(false);

        Assert.True(IsShown(dock));
        Assert.False(dock.IsPutAway);
    });

    /// <summary>The same, cancelled: auto-hide is off again before the dialog lets go.</summary>
    [Fact]
    public void AutoHideTurnedOnAndOffAgain_StillPutsTheDockAway() => WithDock(dock =>
    {
        PutAway(dock);

        dock.HoldRevealed(true);
        dock.SetEnabled(true);
        dock.SetEnabled(false);
        dock.HoldRevealed(false);

        Assert.False(IsShown(dock));
        Assert.True(dock.IsPutAway);
    });

    /// <summary>
    /// A window filling the display in front as the hold lets go: the dock goes away put away, not
    /// merely hidden for that window, so the window going does not bring it back.
    /// </summary>
    [Fact]
    public void PutAwayUnderAWindowInFront_StaysAwayWhenTheWindowGoes() => WithDock(dock =>
    {
        PutAway(dock);

        dock.HoldRevealed(true);
        dock.Yield(true);
        Assert.True(IsShown(dock));

        dock.HoldRevealed(false);
        Assert.False(IsShown(dock));
        Assert.True(dock.IsPutAway);

        dock.Yield(false);
        Assert.False(IsShown(dock));
        Assert.True(dock.IsPutAway);
    });

    /// <summary>
    /// The hotkeys that hide or show the dock and open the settings dialog ask for it before the
    /// keyboard lets go of the dock.
    /// </summary>
    /// <remarks>
    /// Read from the source, because the other order works in every case but one: a dock put away,
    /// taken up by the keyboard, and then hidden by the hotkey or given the dialog. The keys letting
    /// go first put it away themselves, and the toggle after them finds it hidden and brings it
    /// straight back — or the dialog, holding it up for its preview, brings it back a moment after
    /// it has set off.
    /// </remarks>
    [Theory]
    [InlineData("ShowHide", "ShowHideRequested")]
    [InlineData("Settings", "SettingsRequested")]
    public void TheHotkey_AsksBeforeTheKeysLetGo(string action, string request)
    {
        var source = File.ReadAllText(Path.Combine(FindSourceRoot(), "Views", "DockWindow.xaml.cs"));

        var start = source.IndexOf($"case HotkeyAction.{action}:", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The {action} hotkey's case was not found in DockWindow.");

        var end = source.IndexOf("return;", start, StringComparison.Ordinal);
        Assert.True(end > start, $"The {action} hotkey's case has no return.");

        var body = source[start..end];
        var asked = body.IndexOf($"{request}?.Invoke", StringComparison.Ordinal);
        var letGo = body.IndexOf("EndKeyboard(", StringComparison.Ordinal);

        Assert.True(asked >= 0, $"The {action} hotkey's case no longer raises {request}.");
        Assert.True(letGo >= 0, $"The {action} hotkey's case no longer ends the keyboard.");
        Assert.True(asked < letGo, $"The {action} hotkey's case lets go of the keys before it asks.");
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
