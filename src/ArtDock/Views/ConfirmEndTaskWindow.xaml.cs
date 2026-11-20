using System.Windows;
using System.Windows.Interop;
using ArtDock.Dock;
using ArtDock.Interop;
using ArtDock.Localization;

namespace ArtDock.Views;

/// <summary>
/// Asks before <em>End task</em> ends an app.
/// </summary>
/// <remarks>
/// The taskbar's own <em>End task</em> does not ask. The dock's does, because ending a program
/// loses whatever it has not saved and cannot be taken back, and the entry sits in a menu among
/// entries that only open things. Modal, like the dialog <em>Remove from dock</em> asks with:
/// the dock holds the item's label open while it is up, so which app is about to end is shown on
/// the dock itself as well as here.
/// </remarks>
public sealed partial class ConfirmEndTaskWindow : Window
{
    public ConfirmEndTaskWindow(DockItem item)
    {
        InitializeComponent();

        HeadingText.Text = Localizer.Format("EndTask.Heading", item.Label);
        IconImage.Source = item.Icon;
        IconImage.Visibility = IconImage.Source is null ? Visibility.Collapsed : Visibility.Visible;

        EndButton.Click += (_, _) => { DialogResult = true; };

        Loaded += (_, _) =>
        {
            // Opened from the dock, which never holds the foreground; see EditPinWindow.
            AppLauncher.Activate(new WindowInteropHelper(this).Handle);
            CancelButton.Focus();
        };
    }
}
