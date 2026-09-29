using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;

namespace ArtDock.Interop;

/// <summary>
/// The colour Windows is currently painting the taskbar with.
/// </summary>
/// <remarks>
/// <para>
/// There is no API that returns the taskbar's literal pixels, and there could not usefully
/// be one: the taskbar is acrylic over whatever the wallpaper happens to be, so its actual
/// colour changes as the wallpaper does and differs from one end of the bar to the other.
/// What can be read is the colour Windows composes it <i>from</i>, which is what "match the
/// taskbar" means to anyone asking for it.
/// </para>
/// <para>
/// Two cases. With "Show accent colour on Start and taskbar" on, that colour is the accent —
/// read from DWM, which is the component doing the compositing. With it off, the taskbar is
/// the theme's own neutral, which is a fixed pair of greys.
/// </para>
/// </remarks>
internal static class TaskbarColour
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>The Windows 11 taskbar's untinted greys, dark and light.</summary>
    private static readonly Color NeutralDark = Color.FromRgb(0x20, 0x20, 0x20);

    private static readonly Color NeutralLight = Color.FromRgb(0xF3, 0xF3, 0xF3);

    /// <summary>
    /// Last resolved value.
    /// </summary>
    /// <remarks>
    /// Cached because the appearance path runs on every tick of every slider in the settings
    /// dialog, and this is a registry read plus a DWM call. Windows says when it goes stale —
    /// see <see cref="Invalidate"/>.
    /// </remarks>
    private static Color? _cached;

    /// <summary>Forgets the cached value, for when Windows says the colours have changed.</summary>
    public static void Invalidate() => _cached = null;

    /// <summary>The taskbar's current colour.</summary>
    public static Color Current() => _cached ??= Resolve();

    private static Color Resolve()
    {
        try
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

            // Absent means off, which is the Windows 11 default.
            var tinted = personalize?.GetValue("ColorPrevalence") is int prevalence && prevalence != 0;
            if (tinted && TryGetAccent(out var accent))
            {
                return accent;
            }

            // Absent means dark here: the value is written when the theme is light.
            var light = personalize?.GetValue("SystemUsesLightTheme") is int theme && theme != 0;
            return light ? NeutralLight : NeutralDark;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException)
        {
            // Locked-down profile. A dock that cannot read the theme is better off with the
            // colour the taskbar has by default than with no colour at all.
            return NeutralDark;
        }
    }

    /// <summary>Reads the accent DWM is compositing with.</summary>
    private static bool TryGetAccent(out Color accent)
    {
        accent = NeutralDark;

        if (DwmGetColorizationColor(out var argb, out _) != 0)
        {
            return false;
        }

        // ARGB, and the alpha is DWM's blend strength rather than transparency the bar
        // should inherit — the dock has its own opacity setting for that.
        accent = Color.FromRgb(
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));

        return true;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(
        out uint pcrColorization,
        [MarshalAs(UnmanagedType.Bool)] out bool pfOpaqueBlend);
}
