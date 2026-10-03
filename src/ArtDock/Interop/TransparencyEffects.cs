using System.Security;
using Microsoft.Win32;

namespace ArtDock.Interop;

/// <summary>
/// Windows' own <i>Transparency effects</i> switch, in Settings › Personalization › Colors.
/// </summary>
/// <remarks>
/// <para>
/// Off, Windows makes its taskbar, Start and flyouts solid, and DWM's materials — the dialogs'
/// Mica, the previews' acrylic — go solid by themselves. The dock's bar does not: its blur is a
/// sheet of the dock's own making and its translucency is WPF's, so the dock has to ask, and
/// does what it does under <c>DockSettings.NoGpu</c> for the bar (<c>DockSettings.SolidBar</c>).
/// </para>
/// <para>
/// Cached, because the appearance path runs on every tick of every slider in the settings
/// dialog. The switch is announced by a broadcast <c>WM_SETTINGCHANGE</c>
/// (<c>ImmersiveColorSet</c>), heard through <see cref="SystemEvents"/> as the dock's other
/// broadcasts are (<c>DockWindow.OnUserPreferenceChanged</c>); <see cref="Refresh"/> reads it
/// again, and <see cref="Changed"/> tells the settings dialog.
/// </para>
/// </remarks>
internal static class TransparencyEffects
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static bool? _cached;

    /// <summary>Whether transparency effects are on — as they are by default, and when unreadable.</summary>
    public static bool Enabled => _cached ??= Read();

    /// <summary>Raised by <see cref="Refresh"/>, on its caller's thread, when the switch has moved.</summary>
    public static event EventHandler? Changed;

    /// <summary>Reads the switch again, and says so through <see cref="Changed"/> if it has moved.</summary>
    public static void Refresh()
    {
        var before = Enabled;
        _cached = Read();

        if (_cached != before)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    private static bool Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

            // Absent means on, which is Windows' default.
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
