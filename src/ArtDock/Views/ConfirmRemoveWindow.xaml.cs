using System.Windows;
using System.Windows.Interop;
using ArtDock.Dock;
using ArtDock.Interop;
using ArtDock.Localization;

namespace ArtDock.Views;

/// <summary>
/// Asks before <em>Remove from dock</em> takes an item off the dock.
/// </summary>
/// <remarks>
/// <para>
/// Only there. The settings dialog's <em>Remove</em> does not ask, because nothing it does is
/// kept until Save and Cancel undoes it; the dock's menu writes the change the moment the
/// entry is chosen, and there is no Cancel after it.
/// </para>
/// <para>
/// Modal, like the item editor opened from the same menu: the dock holds the item's label open
/// while it is up, so which icon is about to go is shown on the dock itself as well as here.
/// </para>
/// </remarks>
public sealed partial class ConfirmRemoveWindow : Window
{
    /// <param name="item">What is to be removed.</param>
    /// <param name="group">The group of the dock's it is in, or null for one on the dock itself.</param>
    public ConfirmRemoveWindow(DockItem item, DockItem? group = null)
    {
        InitializeComponent();

        // A separator has no name to put in the question, and nothing about it for the
        // reassurance to reassure about.
        if (item.IsSeparator)
        {
            HeadingText.Text = Localizer.Get("RemoveItem.Heading.Separator");
            DetailText.Visibility = Visibility.Collapsed;
        }
        else if (group is not null)
        {
            HeadingText.Text = Localizer.Format("RemoveItem.Heading.InGroup", item.Label, group.Label);
        }
        else
        {
            HeadingText.Text = Localizer.Format("RemoveItem.Heading", item.Label);
        }

        // A group holding something asks what becomes of it: kept, on the dock where the group
        // was, or taken off with it. Neither is the default; Cancel still is.
        if (item is { IsGroup: true, Children.Count: > 0 } && group is null)
        {
            var count = item.Children.Count;
            DetailText.Text = Localizer.Format("RemoveItem.Detail.Group", count);
            KeepButton.Content = Localizer.Format("RemoveItem.KeepItems", count);
            KeepButton.Visibility = Visibility.Visible;
            RemoveButton.Content = Localizer.Get("RemoveItem.RemoveAll");
            KeepButton.Click += (_, _) =>
            {
                KeepsItems = true;
                DialogResult = true;
            };
        }

        IconImage.Source = item.IsSeparator ? null : item.Icon;
        IconImage.Visibility = IconImage.Source is null ? Visibility.Collapsed : Visibility.Visible;

        RemoveButton.Click += (_, _) => { DialogResult = true; };

        Loaded += (_, _) =>
        {
            // Opened from the dock, which never holds the foreground; see EditPinWindow.
            AppLauncher.Activate(new WindowInteropHelper(this).Handle);
            CancelButton.Focus();
        };
    }

    /// <summary>
    /// After a yes, for a group: whether what it holds is to stay on the dock in its place
    /// (<em>Keep items</em>) rather than go with it (<em>Remove all</em>).
    /// </summary>
    public bool KeepsItems { get; private set; }
}
