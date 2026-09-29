using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using ArtDock.Interop;
using ArtDock.Localization;

namespace ArtDock.Views;

/// <summary>
/// Owns the app's context menus, so they can be WPF menus and pick up the Fluent theme.
/// </summary>
/// <remarks>
/// <para>
/// A menu needs an owner window that can take the foreground, and neither of the two places
/// this app shows one from can. The dock is <c>WS_EX_NOACTIVATE</c> — which is what stops it
/// stealing focus from the app you are switching to, and also what stopped it hosting a
/// clickable WPF menu, so its menu used to be a Win32 <c>TrackPopupMenu</c>. The tray icon
/// has no window at all, so its menu used to be WinForms'. Both looked like an older
/// Windows than the one they were running on.
/// </para>
/// <para>
/// This is that missing window: one pixel, off screen, and never seen. It exists only to be
/// something a popup can belong to and something <c>SetForegroundWindow</c> can be pointed
/// at — the latter being what makes a menu close when you click away from it.
/// </para>
/// </remarks>
public sealed class MenuHost : Window
{
    public MenuHost()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Width = 1;
        Height = 1;
        Left = -32000;
        Top = -32000;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Off the taskbar, out of Alt-Tab, and click-through: it parks itself under the
        // cursor while a menu is open and must not swallow anything aimed past it.
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = (uint)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(
            hwnd,
            NativeMethods.GWL_EXSTYLE,
            (nint)(exStyle | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT));
    }

    /// <summary>
    /// Shows a menu at a point on screen, in device pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window is moved to the point and the menu hung off it, rather than the point
    /// being given to the menu as an absolute offset. Offsets are in device-independent
    /// pixels, so using them means converting — and the scale to convert by is the one
    /// belonging to the monitor the cursor is on, which is not necessarily the one this
    /// window reports. On a scaled display that put the menu out to the right by exactly the
    /// scale factor. Moving the window is done in device pixels through <c>SetWindowPos</c>,
    /// and WPF works out the rest from where the window actually is.
    /// </para>
    /// <para>
    /// Placed below the point and left to WPF to flip when it will not fit — which it always
    /// has to for the dock, whose menu opens at the bottom edge of the screen and has
    /// nowhere to go but up.
    /// </para>
    /// </remarks>
    public void ShowMenu(ContextMenu menu, int screenX, int screenY)
    {
        // The window has to exist before it can be moved or given the foreground.
        if (!IsVisible)
        {
            Show();
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(
            hwnd, 0, screenX, screenY, 1, 1,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);

        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.Bottom;

        // A popup is a tree of its own and inherits nothing from the windows around it, so the
        // language's reading direction is given to it here rather than reaching it by itself.
        menu.FlowDirection = Localizer.FlowDirection;
        menu.HorizontalOffset = 0;
        menu.VerticalOffset = 0;

        // Without the foreground, a click outside the menu goes to whatever window is under
        // it and the menu is left hanging open.
        NativeMethods.SetForegroundWindow(hwnd);

        menu.IsOpen = true;
    }

    /// <summary>A menu entry that runs its action once the menu has finished closing.</summary>
    /// <remarks>
    /// <para>
    /// Deferred because most of these open a dialog, and doing that from inside the click —
    /// while the menu is still tearing itself down — leaves the popup on screen behind it.
    /// </para>
    /// <para>
    /// The glyph is not optional, because an entry without one is a gap in a column the whole
    /// menu keeps — see <see cref="MenuIcons"/>.
    /// </para>
    /// </remarks>
    public static MenuItem Item(string header, MenuGlyph glyph, Action action) =>
        Item(header, MenuIcons.Glyph(glyph), action);

    /// <summary>The same, with an icon of the caller's own: an image, for an entry that pins something.</summary>
    public static MenuItem Item(string header, object? icon, Action action)
    {
        var entry = new MenuItem { Header = header, Icon = icon };
        entry.Click += (_, _) => Application.Current.Dispatcher.BeginInvoke(action);
        return entry;
    }

    /// <summary>
    /// An entry that says something rather than doing it: greyed, and inert.
    /// </summary>
    /// <remarks>
    /// For the state a menu is in rather than a command it offers — <c>Locked</c>, where the
    /// item's own commands would otherwise be. Disabled rather than drawn grey by hand, so
    /// it takes the theme's own disabled colour — its glyph too — and cannot be clicked; a
    /// click on it leaves the menu open, which is what Windows does everywhere else and is
    /// the right answer for an entry that is not a command.
    /// </remarks>
    public static MenuItem Note(string header, MenuGlyph glyph) =>
        new() { Header = header, Icon = MenuIcons.Glyph(glyph), IsEnabled = false };

    /// <summary>
    /// The same, for an entry that takes something away.
    /// </summary>
    /// <remarks>
    /// The theme's own critical colour, referenced rather than resolved, so it follows light
    /// and dark like every other brush in the app — and the same one the settings dialog
    /// gives its <c>Remove</c> button, so the two ways of unpinning an icon look alike. Set
    /// on the entry, so the glyph inherits it with the words and the entry is red all over.
    /// </remarks>
    public static MenuItem DangerItem(string header, MenuGlyph glyph, Action action)
    {
        var entry = Item(header, glyph, action);
        entry.SetResourceReference(ForegroundProperty, "SystemFillColorCriticalBrush");
        return entry;
    }
}
