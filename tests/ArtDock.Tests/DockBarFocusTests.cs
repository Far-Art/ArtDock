using System.Runtime.ExceptionServices;
using ArtDock.Controls;
using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers who is allowed to put the held label away.
/// </summary>
/// <remarks>
/// The dock holds one item magnified with its label open while a menu or a dialog is about
/// that item. Two of those overlap: a context menu holds the item, its entry opens the edit
/// dialog, and the dialog takes the hold for itself — but the menu's <c>Closed</c> arrives
/// afterwards, so a release that did not check whose hold it was took the label down in
/// front of the dialog that had just asked for it.
/// </remarks>
public class DockBarFocusTests
{
    /// <summary>
    /// Runs a test body on an STA thread, which every WPF element requires.
    /// </summary>
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

    private static DockItem Pin(string id) => new() { Id = id, Label = id };

    private static (DockBar Bar, DockItem[] Items) Dock()
    {
        var bar = new DockBar(new DockMetrics());
        var items = new[] { Pin("one"), Pin("two"), Pin("three") };
        bar.SetItems(items);
        return (bar, items);
    }

    [Fact]
    public void FocusItem_HoldsTheLabelOpen() => OnStaThread(() =>
    {
        var (bar, items) = Dock();

        Assert.False(bar.IsHoldingLabel);
        Assert.NotEqual(0, bar.FocusItem(items[1]));
        Assert.True(bar.IsHoldingLabel);
    });

    [Fact]
    public void FocusItem_GivesNoClaimForAnItemTheDockDoesNotHave() => OnStaThread(() =>
    {
        var (bar, _) = Dock();

        Assert.Equal(0, bar.FocusItem(Pin("missing")));
        Assert.False(bar.IsHoldingLabel);
    });

    [Fact]
    public void ReleaseFocus_PutsItAwayForTheHolderThatTookIt() => OnStaThread(() =>
    {
        var (bar, items) = Dock();

        var claim = bar.FocusItem(items[0]);
        bar.ReleaseFocus(claim);

        Assert.False(bar.IsHoldingLabel);
    });

    [Fact]
    public void ReleaseFocus_IsIgnoredOnceSomethingElseHasTakenTheHold() => OnStaThread(() =>
    {
        var (bar, items) = Dock();

        // The context menu holds the item, then its entry's dialog takes over.
        var menuClaim = bar.FocusItem(items[0]);
        var dialogClaim = bar.FocusItem(items[0]);

        // The menu's Closed, arriving late — this is the case that used to blank the label.
        bar.ReleaseFocus(menuClaim);
        Assert.True(bar.IsHoldingLabel);

        bar.ReleaseFocus(dialogClaim);
        Assert.False(bar.IsHoldingLabel);
    });

    [Fact]
    public void ReleaseFocus_IsIgnoredAfterAnOutrightClear() => OnStaThread(() =>
    {
        var (bar, items) = Dock();

        var claim = bar.FocusItem(items[2]);
        bar.ClearFocus();
        bar.FocusItem(items[0]);

        bar.ReleaseFocus(claim);

        Assert.True(bar.IsHoldingLabel);
    });

    [Fact]
    public void PreviewMagnification_HoldsNoLabel() => OnStaThread(() =>
    {
        // The settings dialog's demonstration magnifies an icon without naming it, so it
        // must not read as a held label.
        var (bar, _) = Dock();

        bar.PreviewMagnification(true);

        Assert.False(bar.IsHoldingLabel);
    });
}
