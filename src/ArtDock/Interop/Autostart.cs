using System.IO;
using ArtDock.Services;
using Microsoft.Win32;

namespace ArtDock.Interop;

/// <summary>
/// Registers the dock to start with Windows, via the per-user Run key.
/// </summary>
/// <remarks>
/// The packaged <c>StartupTask</c> API would be the modern choice, but it is only available
/// to MSIX-packaged apps and the dock deliberately ships unpackaged. The Run key needs no
/// elevation, is visible to the user in Task Manager's Startup tab, and can be turned off
/// there — which is the behaviour someone would expect from a dock.
/// </remarks>
public static class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ArtDock";

    /// <summary>The value name used before the app was renamed from ImsDock.</summary>
    private const string LegacyValueName = "ImsDock";

    /// <summary>True when the dock is currently registered to run at login.</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    /// <summary>
    /// Removes the Run entry left behind by the old name, so a renamed install does not start
    /// twice at login — once as ImsDock pointing at a path that no longer exists, once as
    /// ArtDock.
    /// </summary>
    public static void RemoveLegacyEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Nothing to do; a stale entry is harmless beyond a failed launch.
        }
    }

    /// <summary>
    /// Removes the Run entry if it starts a program inside <paramref name="folder"/> — an
    /// installation on its way out — and leaves one that starts any other copy alone.
    /// </summary>
    public static void RemoveIfUnder(string folder)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not string value)
            {
                return;
            }

            var target = Path.GetFullPath(value.Trim().Trim('"'));
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;

            if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException
            or ArgumentException or NotSupportedException or IOException)
        {
            // Left as it is: a stale entry costs a failed launch at sign-in, and nothing else.
        }
    }

    /// <summary>Adds or removes the Run entry. Returns false if the registry refused.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                // Quoted: the install path may well contain spaces. Velopack's launcher rather
                // than this executable, in a copy its setup installed — see LaunchPath.
                key.SetValue(ValueName, $"\"{AppUpdater.LaunchPath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
