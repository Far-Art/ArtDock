using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ArtDock.Interop;
using ArtDock.Services;

namespace ArtDock.Views;

/// <summary>
/// What the dialogs sit on: Mica, as the theme has it, or under <c>DockSettings.NoGpu</c> the
/// theme's plain background.
/// </summary>
/// <remarks>
/// <para>
/// The Fluent theme puts every window on DWM's backdrop by itself, and does it by giving the
/// window no background at all: its <see cref="Window"/> style sets a transparent one, and the
/// render target's is transparent too, so the material shows through from behind. Measured on
/// 2026-10-01 from a window that was never shown: <c>#00FFFFFF</c> for both, in the light theme
/// and the dark. So a window cannot be taken off the material by asking DWM alone. The first
/// try at this did that, and cleared the settings dialog's own background on the understanding
/// that the theme's would then show: the theme's is the transparent one, and with no material
/// behind it the dialog came up black. And only the settings dialog was told, so the item
/// editor and the rest stayed on Mica — both reported the first time the box was ticked.
/// </para>
/// <para>
/// So taking a window off the material is two things together: DWM is told to compose none,
/// and the window is given the theme's opaque background by its resource key, which follows a
/// change between light and dark as the rest of the theme does. Every window is covered, the
/// ones open when the box is ticked and each one opened while it is, by a class handler rather
/// than by each dialog remembering to ask.
/// </para>
/// <para>
/// Not the dock's own window, nor the menus' host: those are transparent on purpose
/// (<see cref="Window.AllowsTransparency"/>), and have no material to give up.
/// </para>
/// </remarks>
public static class WindowMaterial
{
    /// <summary>
    /// The theme's opaque window background: what a window of the theme has where the backdrop
    /// is not to be had.
    /// </summary>
    private const string SolidBackground = "ApplicationBackgroundBrush";

    /// <summary>Set on a window the class handler has made solid, so it is done once.</summary>
    private static readonly DependencyProperty IsSolidProperty = DependencyProperty.RegisterAttached(
        "IsSolid", typeof(bool), typeof(WindowMaterial), new PropertyMetadata(false));

    /// <summary>
    /// Has every window opened while software rendering is on come up on the plain background.
    /// </summary>
    public static void Register()
    {
        // Not Loaded, which a class handler never hears — see PopupShadows.Register. SizeChanged
        // is raised when a window is first laid out, which is after it has a handle and before
        // it has drawn anything.
        EventManager.RegisterClassHandler(
            typeof(Window), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(OnWindowSizeChanged));
    }

    private static void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Every resize of every window comes through here, so: only while there is something
        // to do, and only once for each window.
        if (sender is not Window window || !SoftwareRendering.IsOn || (bool)window.GetValue(IsSolidProperty))
        {
            return;
        }

        Apply(window, solid: true, IsDark());

        // And again once the window's source is ready, because what DWM has just been told does
        // not last until then. A new window is laid out before the theme reaches it — measured:
        // SizeChanged, Loaded, then the theme putting its backdrop on, then SourceInitialized —
        // so the backdrop comes back straight after this. The background set here stays, which
        // is why the first build of this had the item editor plain below a title bar still on
        // Mica. Nothing puts the backdrop back after SourceInitialized.
        window.SourceInitialized += OnWindowSourceInitialized;
    }

    private static void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.SourceInitialized -= OnWindowSourceInitialized;

        if (SoftwareRendering.IsOn)
        {
            Apply(window, solid: true, IsDark());
        }
    }

    /// <summary>
    /// Puts every open dialog on the plain background, or back on the material.
    /// </summary>
    public static void ApplyToOpen(bool solid)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var dark = IsDark();
        foreach (Window window in app.Windows)
        {
            Apply(window, solid, dark);
        }
    }

    /// <summary>
    /// Puts a window on the theme's plain background, or on Mica.
    /// </summary>
    /// <param name="dark">Whether the theme in force is the dark one, for the title bar and
    /// the material, which are DWM's and are not told by the theme.</param>
    /// <remarks>
    /// A window DWM will not give the material to is given the plain background as well: left
    /// with the theme's transparent one and nothing behind it, it would be black.
    /// </remarks>
    public static void Apply(Window window, bool solid, bool dark)
    {
        if (window.AllowsTransparency)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0)
        {
            return;
        }

        if (!solid && DesktopComposition.EnableMica(hwnd, dark))
        {
            if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
            {
                target.BackgroundColor = Colors.Transparent;
            }

            window.ClearValue(Control.BackgroundProperty);
            window.ClearValue(IsSolidProperty);
            return;
        }

        if (solid)
        {
            DesktopComposition.DisableMica(hwnd, dark);
        }

        window.SetResourceReference(Control.BackgroundProperty, SolidBackground);
        window.SetValue(IsSolidProperty, true);
    }

    /// <summary>Whether the theme in force is the dark one, following Windows when left to.</summary>
    private static bool IsDark()
    {
        var mode = Application.Current?.ThemeMode ?? ThemeMode.System;

        return AppTheme.IsDark(
            mode == ThemeMode.Light ? AppTheme.Light
            : mode == ThemeMode.Dark ? AppTheme.Dark
            : AppTheme.System);
    }
}
