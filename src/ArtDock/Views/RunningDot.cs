using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArtDock.Controls;
using ArtDock.Dock;

namespace ArtDock.Views;

/// <summary>
/// An item's dot in a menu — the overflow item's, which lists the items that did not fit on the
/// dock — inline after its name, drawn as the bar draws it under the icon on the dock.
/// </summary>
/// <remarks>
/// <para>
/// Reported 2026-10-03: the overflow item's own dot said that something behind it was running,
/// and its menu did not say what. The dock's dot is the dock's one way of saying it, so the
/// menu says it the same way, in the same white, amber or hollow (<see cref="DockBar.DrawDot"/>),
/// rather than in words.
/// </para>
/// <para>
/// Inline after the name, as asked the same day, rather than under the icon, where it was
/// first: there it hung into the entry's padding, below the line it belonged to. The state is
/// the item's as the menu opens.
/// </para>
/// </remarks>
public sealed class RunningDot : FrameworkElement
{
    private readonly DockItem _item;

    public RunningDot(DockItem item)
    {
        _item = item;
        IsHitTestVisible = false;
        Width = Height = DockBar.DotReach * 2;
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>Whether an item has a dot to draw: running, or closing.</summary>
    public static bool Has(DockItem item) => item.IsRunning || item.IsClosing;

    /// <summary>
    /// A menu entry's header for an item: its name, and its dot after it while it has one.
    /// </summary>
    /// <remarks>
    /// The name is a <see cref="TextBlock"/> either way, not the entry's own string, so an
    /// underscore in it is shown rather than taken for an access key. A header that is not a
    /// string names the entry for nothing, so the caller names it by the item's name.
    /// </remarks>
    public static object Header(DockItem item)
    {
        var name = new TextBlock { Text = item.Label, VerticalAlignment = VerticalAlignment.Center };
        if (!Has(item))
        {
            return name;
        }

        var dot = new RunningDot(item) { Margin = new Thickness(Gap, 0, 0, 0) };
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { name, dot } };
    }

    /// <summary>Between the name and the dot, in DIPs: a space's width at the menu's size.</summary>
    private const double Gap = 6;

    protected override void OnRender(DrawingContext drawingContext) =>
        DockBar.DrawDot(drawingContext, new Point(ActualWidth / 2, ActualHeight / 2), _item);
}
