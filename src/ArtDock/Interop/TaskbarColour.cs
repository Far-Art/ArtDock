using System.Runtime.InteropServices;
using System.Windows.Media;
using ArtDock.Dock;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace ArtDock.Interop;

/// <summary>
/// What Windows is currently painting the taskbar with.
/// </summary>
/// <remarks>
/// <para>
/// There is no API that returns it. What can be read is everything Windows chooses it by: the
/// light or the dark theme, whether the accent is shown on the taskbar, and a contrast theme —
/// and from those, which of its materials the taskbar has on
/// (<see cref="TaskbarMaterial"/>). Whether transparency effects are on is not read here: off,
/// they make the dock's bar solid whatever it is painted with (<c>DockSettings.SolidBar</c>),
/// and the material carries the colour for that.
/// </para>
/// <para>
/// Three cases. Under a contrast theme the taskbar is the theme's window colour. With <i>Show
/// accent color on Start and taskbar</i> on, it is the accent's material, in the shade of the
/// accent the theme uses. Otherwise it is the theme's own neutral.
/// </para>
/// </remarks>
internal static class TaskbarColour
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const uint GetHighContrast = 0x0042;
    private const uint HighContrastOn = 0x0001;
    private const int ColorWindow = 5;

    /// <summary>
    /// Last resolved value.
    /// </summary>
    /// <remarks>
    /// Cached because the appearance path runs on every tick of every slider in the settings
    /// dialog, and this is a registry read and more. Windows says when it goes stale — see
    /// <see cref="Invalidate"/>.
    /// </remarks>
    private static TaskbarMaterial? _cached;

    /// <summary>Forgets the cached value, for when Windows says the colours have changed.</summary>
    public static void Invalidate() => _cached = null;

    /// <summary>The taskbar's current material.</summary>
    public static TaskbarMaterial Material() => _cached ??= Resolve();

    private static TaskbarMaterial Resolve()
    {
        if (TryGetContrastColour(out var window))
        {
            return TaskbarMaterial.Flat(window);
        }

        try
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

            // Absent means dark here: the value is written when the theme is light.
            var light = personalize?.GetValue("SystemUsesLightTheme") is int theme && theme != 0;

            // Absent means off, which is the Windows 11 default.
            var tinted = personalize?.GetValue("ColorPrevalence") is int prevalence && prevalence != 0;
            if (tinted && TryGetAccent(light, out var accent))
            {
                return TaskbarMaterial.Accent(accent);
            }

            return TaskbarMaterial.Neutral(light);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException)
        {
            // Locked-down profile. A dock that cannot read the theme is better off with the
            // look the taskbar has by default than with none at all.
            return TaskbarMaterial.Neutral(light: false);
        }
    }

    /// <summary>
    /// The shade of the accent the taskbar's material is tinted with: the third darkest in the
    /// dark theme, the third lightest in the light.
    /// </summary>
    /// <remarks>
    /// Windows only offers the accent on a dark taskbar, so the light half is for a registry set
    /// by hand. The shades are Windows' own, asked for rather than worked out; DWM's colour
    /// stands in on a machine that will not give them.
    /// </remarks>
    private static bool TryGetAccent(bool light, out Color accent)
    {
        try
        {
            var shade = new UISettings().GetColorValue(light ? UIColorType.AccentLight3 : UIColorType.AccentDark3);
            accent = Color.FromRgb(shade.R, shade.G, shade.B);
            return true;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or TypeLoadException
            or UnauthorizedAccessException or ArgumentException)
        {
            accent = default;
        }

        if (DwmGetColorizationColor(out var argb, out _) != 0)
        {
            return false;
        }

        accent = Color.FromRgb(
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));

        return true;
    }

    /// <summary>
    /// The window colour of a contrast theme, when one is on. Asked of Windows rather than of
    /// WPF, whose own copies are brought up to date by the same broadcast this is re-read on.
    /// </summary>
    private static bool TryGetContrastColour(out Color window)
    {
        window = default;

        var contrast = new HighContrast { Size = (uint)Marshal.SizeOf<HighContrast>() };
        if (!SystemParametersInfo(GetHighContrast, contrast.Size, ref contrast, 0)
            || (contrast.Flags & HighContrastOn) == 0)
        {
            return false;
        }

        // 0x00BBGGRR.
        var colour = GetSysColor(ColorWindow);
        window = Color.FromRgb((byte)(colour & 0xFF), (byte)((colour >> 8) & 0xFF), (byte)((colour >> 16) & 0xFF));
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrast
    {
        public uint Size;
        public uint Flags;
        public nint DefaultScheme;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(
        out uint pcrColorization,
        [MarshalAs(UnmanagedType.Bool)] out bool pfOpaqueBlend);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref HighContrast value, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetSysColor(int index);
}
