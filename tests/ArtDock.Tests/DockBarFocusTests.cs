using System.Runtime.ExceptionServices;
using System.Windows;
using ArtDock.Controls;
using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers who is allowed to put the held label away, and what the settings dialog's preview
/// holds up around it.
/// </summary>
/// <remarks>
/// <para>
/// The dock holds one item magnified with its label open while a menu or a dialog is about
/// that item. Two of those overlap: a context menu holds the item, its entry opens the edit
/// dialog, and the dialog takes the hold for itself — but the menu's <c>Closed</c> arrives
/// afterwards, so a release that did not check whose hold it was took the label down in
/// front of the dialog that had just asked for it.
/// </para>
/// <para>
/// Under all of that, while the settings dialog is open, is its preview: the item selected on
/// its Items page, or the middle icon. The selection is named by id and outlives the holds,
/// the contents changing, and the dock not having the item yet.
/// </para>
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

    // ---- the item selected in the settings dialog ------------------------------------

    [Fact]
    public void PreviewItem_HoldsUpTheSelectedItem_AsTheMiddleWasHeld() => OnStaThread(() =>
    {
        var (bar, _) = Dock();
        bar.PreviewMagnification(true);
        Assert.Null(bar.PreviewedItem);

        bar.PreviewItem("three");
        Assert.Equal("three", bar.PreviewedItem?.Id);

        // Labelled as a hovered item is, not held as a menu holds one: a pointer arriving on
        // the dock takes it over, label and all.
        Assert.False(bar.IsHoldingLabel);

        bar.PreviewItem(null);
        Assert.Null(bar.PreviewedItem);
    });

    [Fact]
    public void PreviewItem_FollowsTheItem_WhereverTheContentsPutIt() => OnStaThread(() =>
    {
        var (bar, items) = Dock();
        bar.PreviewMagnification(true);
        bar.PreviewItem("one");

        // Moved to the end, as the dialog's Move down moves it, and previewed as it moves.
        bar.SetItems([items[1], items[2], items[0]]);
        Assert.Equal("one", bar.PreviewedItem?.Id);

        // Gone while the list is without it, and found again when it is back.
        bar.SetItems([items[1], items[2]]);
        Assert.Null(bar.PreviewedItem);

        bar.SetItems(items);
        Assert.Equal("one", bar.PreviewedItem?.Id);
    });

    [Fact]
    public void PreviewItem_CanNameAnItemTheDockHasNotBeenGivenYet() => OnStaThread(() =>
    {
        // A row added in the dialog is selected first and previewed after, so the dock hears
        // of the selection before it has the item.
        var (bar, items) = Dock();
        bar.PreviewMagnification(true);

        bar.PreviewItem("four");
        Assert.Null(bar.PreviewedItem);

        bar.SetItems([.. items, Pin("four")]);
        Assert.Equal("four", bar.PreviewedItem?.Id);
    });

    [Fact]
    public void AHold_OutranksThePreview_AndHandsBackToTheSelection() => OnStaThread(() =>
    {
        var (bar, items) = Dock();
        bar.PreviewMagnification(true);
        bar.PreviewItem("one");

        // An icon's menu on the dock, or the item editor.
        var claim = bar.FocusItem(items[2]);
        Assert.True(bar.IsHoldingLabel);
        Assert.Null(bar.PreviewedItem);

        // The selection moving underneath does not take the item away from the hold.
        bar.PreviewItem("two");
        Assert.True(bar.IsHoldingLabel);

        // Let go, the dock goes back to the dialog's preview, as it now stands — not to nothing,
        // with the dialog still open.
        bar.ReleaseFocus(claim);
        Assert.False(bar.IsHoldingLabel);
        Assert.Equal("two", bar.PreviewedItem?.Id);
    });

    [Fact]
    public void FocusItem_GivesNoClaimForAMissingItem_ThoughThePreviewComesBack() => OnStaThread(() =>
    {
        // Letting go of the old hold brings the preview back, which must not be mistaken for
        // a hold taken for the item asked about.
        var (bar, _) = Dock();
        bar.PreviewMagnification(true);
        bar.PreviewItem("two");

        Assert.Equal(0, bar.FocusItem(Pin("missing")));
        Assert.False(bar.IsHoldingLabel);
        Assert.Equal("two", bar.PreviewedItem?.Id);
    });

    [Fact]
    public void EndingThePreview_ForgetsTheSelection() => OnStaThread(() =>
    {
        var (bar, _) = Dock();
        bar.PreviewMagnification(true);
        bar.PreviewItem("one");

        bar.PreviewMagnification(false);
        Assert.Null(bar.PreviewedItem);

        // The next dialog opens with nothing selected, so the dock starts from the middle.
        bar.PreviewMagnification(true);
        Assert.Null(bar.PreviewedItem);
    });

    // ---- what a right-click lands on --------------------------------------------------

    /// <summary>The same dock, laid out in a window of the size it asks for.</summary>
    private static (DockBar Bar, DockItem[] Items, DockMetrics Metrics) Arranged()
    {
        var metrics = new DockMetrics();
        var bar = new DockBar(metrics);
        var items = new[] { Pin("one"), Pin("two"), Pin("three") };
        bar.SetItems(items);

        var size = bar.PreferredSize();
        bar.Width = size.Width;
        bar.Height = size.Height;
        bar.Measure(size);
        bar.Arrange(new Rect(size));
        bar.UpdateLayout();

        return (bar, items, metrics);
    }

    /// <summary>The middle of an icon at rest, in the dock's coordinates.</summary>
    private static Point IconCentre(DockBar bar, DockMetrics metrics, int index) =>
        new(
            bar.RestingBarRect.Left + new DockLayout(metrics).RestingCentre(index),
            bar.RestingBarRect.Bottom - metrics.PaddingY - (metrics.BaseSize / 2));

    [Fact]
    public void ItemAt_NamesTheIconDrawnThere() => OnStaThread(() =>
    {
        var (bar, items, metrics) = Arranged();

        for (var i = 0; i < items.Length; i++)
        {
            Assert.Same(items[i], bar.ItemAt(IconCentre(bar, metrics, i)));
        }

        // Between two icons is the bar, which is the dock's own menu and no item's.
        var between = IconCentre(bar, metrics, 0);
        between.X = (between.X + IconCentre(bar, metrics, 1).X) / 2;
        Assert.Null(bar.ItemAt(between));
    });

    /// <summary>
    /// The case that opened one icon's menu over another: the hold a menu takes does not
    /// decide what a click lands on.
    /// </summary>
    /// <remarks>
    /// While an icon's menu is open the dock holds that icon and stops following the pointer,
    /// so the hover goes on naming it — and the right-click that closes the menu over another
    /// icon arrives before the hold is let go. The menu asked the hover, and came up over the
    /// icon clicked with the commands of the one before.
    /// </remarks>
    [Fact]
    public void ItemAt_IsNotTheHeldItem_WhereAnotherIsDrawn() => OnStaThread(() =>
    {
        var (bar, items, metrics) = Arranged();

        bar.FocusItem(items[0]);
        Assert.True(bar.IsHoldingLabel);

        Assert.Same(items[2], bar.ItemAt(IconCentre(bar, metrics, 2)));
    });
}
