using System.Drawing;
using System.Windows.Forms;
using ArtDock.Interop;
using ArtDock.Localization;
using ArtDock.Views;

// WinForms has its own MenuItem and ContextMenu, kept for compatibility and long superseded —
// and a MenuGlyph, which draws a menu's arrows and check marks. Aliased rather than imported
// wholesale, because only the NotifyIcon is wanted from there.
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuGlyph = ArtDock.Views.MenuGlyph;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;

namespace ArtDock.Services;

/// <summary>
/// The dock's notification-area icon and menu.
/// </summary>
/// <remarks>
/// <para>
/// Uses WinForms' <see cref="NotifyIcon"/>, which is in-box on Windows and needs no
/// third-party package. WPF has no tray API of its own, and the alternative is hand-rolling
/// <c>Shell_NotifyIcon</c> against a message-only window for no gain.
/// </para>
/// <para>
/// The icon is all that is borrowed. Its menu is a WPF one shown through
/// <see cref="MenuHost"/>, because <c>ContextMenuStrip</c> draws WinForms' own grey menu and
/// no theme reaches it — next to the rest of the app it looked a decade older than it is.
/// </para>
/// </remarks>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly MenuHost _menus;
    private readonly MenuItem _toggleItem;
    private readonly MenuItem _settingsItem;
    private readonly MenuItem _exitItem;
    private readonly ContextMenu _menu;

    private bool _disposed;
    private bool _dockShown = true;

    public TrayIcon(MenuHost menus)
    {
        _menus = menus;

        // Settings… is the dock menu's Dock settings…, and has the same glyph.
        _toggleItem = MenuHost.Item(
            string.Empty, ToggleGlyph(_dockShown), () => ToggleRequested?.Invoke(this, EventArgs.Empty));
        _settingsItem = MenuHost.Item(
            string.Empty, MenuGlyph.Settings, () => SettingsRequested?.Invoke(this, EventArgs.Empty));
        _exitItem = MenuHost.Item(
            string.Empty, MenuGlyph.Exit, () => ExitRequested?.Invoke(this, EventArgs.Empty));

        _menu = new ContextMenu();
        _menu.Items.Add(_toggleItem);
        _menu.Items.Add(_settingsItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(_exitItem);

        // Built once and kept, unlike the dock's menu, so it has to be told when the language
        // changes rather than picking the new one up the next time it is built.
        Label();
        Localizer.LanguageChanged += OnLanguageChanged;

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "ArtDock",
            Visible = true
        };

        // No ContextMenuStrip, so the right-click arrives here instead and the WPF menu is
        // shown at the cursor by hand.
        _icon.MouseUp += (_, mouse) =>
        {
            if (mouse.Button == MouseButtons.Right)
            {
                ShowMenu();
            }
        };

        _icon.DoubleClick += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Opens the tray menu at the cursor.</summary>
    private void ShowMenu()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        _menus.Dispatcher.Invoke(() => _menus.ShowMenu(_menu, cursor.X, cursor.Y));
    }

    public event EventHandler? SettingsRequested;

    public event EventHandler? ToggleRequested;

    public event EventHandler? ExitRequested;

    /// <summary>Keeps the menu's wording honest about what clicking it will do.</summary>
    public void SetDockShown(bool shown)
    {
        _dockShown = shown;
        Label();
        _toggleItem.Icon = MenuIcons.Glyph(ToggleGlyph(shown));
    }

    /// <summary>The toggle's glyph, which pictures what it will do, as its words say.</summary>
    private static MenuGlyph ToggleGlyph(bool dockShown) =>
        dockShown ? MenuGlyph.Hide : MenuGlyph.Show;

    private void OnLanguageChanged(object? sender, EventArgs e) => Label();

    /// <summary>Words the entries in the current language.</summary>
    private void Label()
    {
        _toggleItem.Header = Localizer.Get(_dockShown ? "Tray.HideDock" : "Tray.ShowDock");
        _settingsItem.Header = Localizer.Get("Tray.Settings");
        _exitItem.Header = Localizer.Get("Common.Exit");
    }

    /// <summary>
    /// Loads the tray icon at the shell's small-icon size.
    /// </summary>
    /// <remarks>
    /// Reads the multi-resolution .ico and lets <see cref="Icon"/> pick the frame that
    /// matches, rather than taking the 32px frame via <c>ExtractAssociatedIcon</c> and
    /// letting the tray shrink it — a rescaled 32px icon looks soft at 16px, which is
    /// precisely the size it will spend its life at.
    /// </remarks>
    private static Icon LoadIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/ArtDock.ico"));

            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new Icon(stream, SystemInformation.SmallIconSize);
            }
        }
        catch (Exception e) when (e is ArgumentException or System.IO.IOException or UriFormatException)
        {
            // Fall through to the system icon.
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Localizer.LanguageChanged -= OnLanguageChanged;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
