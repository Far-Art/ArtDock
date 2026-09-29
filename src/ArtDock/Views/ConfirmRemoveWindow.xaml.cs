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
    public ConfirmRemoveWindow(DockItem item)
    {
        InitializeComponent();

        // A separator has no name to put in the question, and nothing about it for the
        // reassurance to reassure about.
        if (item.IsSeparator)
        {
            HeadingText.Text = Localizer.Get("RemoveItem.Heading.Separator");
            DetailText.Visibility = Visibility.Collapsed;
        }
        else
        {
            HeadingText.Text = Localizer.Format("RemoveItem.Heading", item.Label);
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
}
