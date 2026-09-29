using System.Windows;
using System.Windows.Interop;
using ArtDock.Interop;
using ArtDock.Localization;

namespace ArtDock.Views;

/// <summary>
/// Says that ArtDock is already running, when it is launched a second time.
/// </summary>
/// <remarks>
/// <para>
/// Every time, whether or not the dock can be seen, so a second launch always visibly lands
/// somewhere. What changes is the message: where the dock is, and — when it is out of sight —
/// why, and how to get it back. It offers the ways out of a dock that cannot be found:
/// bringing it back, where a button can, and exiting.
/// </para>
/// <para>
/// Shown modeless. <see cref="Window.ShowDialog"/> disables every other window on the
/// thread, and the dock is one of them.
/// </para>
/// </remarks>
public sealed partial class AlreadyRunningWindow : Window
{
    public AlreadyRunningWindow(DockPresence presence)
    {
        InitializeComponent();

        SetPresence(presence);

        ShowDockButton.Click += (_, _) =>
        {
            Close();
            ShowDockRequested?.Invoke(this, EventArgs.Empty);
        };

        ExitButton.Click += (_, _) =>
        {
            Close();
            ExitRequested?.Invoke(this, EventArgs.Empty);
        };

        // IsCancel only closes a modal window; this one is not, so Escape and Close both
        // come through here.
        CloseButton.Click += (_, _) => Close();

        // Opened on behalf of another process's launch, so this one does not hold the
        // foreground; see EditPinWindow.
        Loaded += (_, _) => AppLauncher.Activate(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Show dock was chosen.</summary>
    public event EventHandler? ShowDockRequested;

    /// <summary>Exit ArtDock was chosen.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>
    /// Says where the dock is now. Called again when a later launch finds this notice still
    /// open, since the dock may have come or gone in the meantime.
    /// </summary>
    public void SetPresence(DockPresence presence)
    {
        ReasonText.Text = Explain(presence);

        // Only where a button can actually do it. With auto-hide on, a dock revealed from here
        // slides away again before the pointer can get back to it; the edge is the way in.
        var canShow = presence == DockPresence.HiddenFromTray;
        ShowDockButton.Visibility = canShow ? Visibility.Visible : Visibility.Collapsed;
        ShowDockButton.IsDefault = canShow;
        CloseButton.IsDefault = !canShow;
    }

    private static string Explain(DockPresence presence) => presence switch
    {
        DockPresence.AutoHidden => Localizer.Get("AlreadyRunning.AutoHidden"),
        DockPresence.HiddenFromTray => Localizer.Get("AlreadyRunning.HiddenFromTray"),
        DockPresence.OffScreen => Localizer.Get("AlreadyRunning.OffScreen"),
        _ => Localizer.Get("AlreadyRunning.OnScreen")
    };
}
