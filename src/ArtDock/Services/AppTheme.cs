using System.Windows;
using Microsoft.Win32;

namespace ArtDock.Services;

/// <summary>
/// Which of Windows' two appearances the app follows.
/// </summary>
/// <remarks>
/// Resolved against the system rather than guessed at: WPF's <see cref="ThemeMode"/> does
/// the following for the controls, and the registry value below answers the one question it
/// cannot — whether "system" currently means light or dark — which the window's own Mica and
/// title bar have to be told separately.
/// </remarks>
public static class AppTheme
{
    /// <summary>Stored value meaning "whatever Windows is set to".</summary>
    public const string System = "System";

    public const string Light = "Light";
    public const string Dark = "Dark";

    /// <summary>The three choices, in the order the settings dialog offers them.</summary>
    public static readonly string[] Choices = [System, Light, Dark];

    /// <summary>How each choice reads in the dialog, in the order of <see cref="Choices"/>.</summary>
    /// <remarks>Asked for afresh each time, because the language can change while the dialog is open.</remarks>
    public static string[] Labels =>
    [
        Localization.Localizer.Get("Theme.System"),
        Localization.Localizer.Get("Theme.Light"),
        Localization.Localizer.Get("Theme.Dark")
    ];

    /// <summary>Turns a stored value into the mode WPF's theme takes.</summary>
    public static ThemeMode Resolve(string? theme) => theme switch
    {
        Light => ThemeMode.Light,
        Dark => ThemeMode.Dark,
        _ => ThemeMode.System
    };

    /// <summary>
    /// Whether the app is currently dark, following Windows when it has been left to.
    /// </summary>
    /// <remarks>
    /// Read from <c>AppsUseLightTheme</c>, which is where Windows keeps it and what every
    /// other app reads. Missing means light: that key is only written once the setting has
    /// been touched, and light is what a machine that has never touched it shows.
    /// </remarks>
    public static bool IsDark(string? theme) => theme switch
    {
        Light => false,
        Dark => true,
        _ => SystemPrefersDark()
    };

    private static bool SystemPrefersDark()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                defaultValue: 1);

            return value is int light && light == 0;
        }
        catch (Exception e) when (e is global::System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The index of a stored value in <see cref="Choices"/>, defaulting to system.</summary>
    public static int IndexOf(string? theme)
    {
        var index = Array.IndexOf(Choices, theme ?? System);
        return index < 0 ? 0 : index;
    }
}
