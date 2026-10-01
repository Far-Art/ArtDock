using System.IO;
using ArtDock.Services;
using Microsoft.Win32;

namespace ArtDock.Interop;

/// <summary>
/// Registers the dock to start with Windows, via the per-user Run key.
/// </summary>
/// <remarks>
/// <para>
/// The packaged <c>StartupTask</c> API would be the modern choice, but it is only available
/// to MSIX-packaged apps and the dock deliberately ships unpackaged. The Run key needs no
/// elevation, is visible to the user in Task Manager's Startup tab, and can be turned off
/// there — which is the behaviour someone would expect from a dock.
/// </para>
/// <para>
/// Turning it off there does not delete the Run value. Task Manager writes a flag of the same
/// name under <see cref="ApprovedKeyPath"/> instead, so whether the dock starts is the value
/// and the flag together. The checkbox on the System page is the user's choice and works the
/// same way (<see cref="SetEnabled(bool)"/>): the entry stays, and the flag says on or off, so
/// the dock stays listed in Windows' startup apps and the two switches are one. The setup and
/// the first run turn autostart on as a default, and leave a choice made in either standing
/// (<see cref="Register()"/>).
/// </para>
/// <para>
/// Every method that touches the registry has a form taking the key the paths are under —
/// <see cref="Registry.CurrentUser"/> for the dock, a scratch key for the tests, which must
/// never write the user's real Run key.
/// </para>
/// </remarks>
public static class Autostart
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Where Task Manager's *Startup apps* page records an entry it has turned off: a binary
    /// value named as the Run value is. See <see cref="IsOffFlag"/>.
    /// </summary>
    internal const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private const string ValueName = "ArtDock";

    /// <summary>The value name used before the app was renamed from ImsDock.</summary>
    private const string LegacyValueName = "ImsDock";

    /// <summary>
    /// True when the dock will start at sign-in: registered, and not turned off in Task
    /// Manager.
    /// </summary>
    public static bool IsEnabled() => IsEnabled(Registry.CurrentUser);

    /// <inheritdoc cref="IsEnabled()"/>
    public static bool IsEnabled(RegistryKey root)
    {
        if (!IsRegistered(root))
        {
            return false;
        }

        using var approved = root.OpenSubKey(ApprovedKeyPath);
        return !IsOffFlag(approved?.GetValue(ValueName) as byte[]);
    }

    /// <summary>
    /// True when there is a Run entry for the dock at all, whether or not Task Manager has
    /// turned it off.
    /// </summary>
    public static bool IsRegistered() => IsRegistered(Registry.CurrentUser);

    /// <inheritdoc cref="IsRegistered()"/>
    public static bool IsRegistered(RegistryKey root)
    {
        using var key = root.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    /// <summary>
    /// Whether one of Task Manager's flags says the entry is off.
    /// </summary>
    /// <remarks>
    /// Twelve bytes. Read off the thirteen on the development machine on 2026-10-01: an entry
    /// on is <c>02</c> and eleven zeros, one turned off is <c>03</c> and then, from the fifth
    /// byte, the <c>FILETIME</c> it was turned off at. The Settings app's *Apps → Startup* page
    /// writes the same key and shape, but <c>01</c> for off — seen the same day. An odd first
    /// byte is off, which takes in both. No flag at all is on — how an entry stands that nobody
    /// has touched in either.
    /// </remarks>
    public static bool IsOffFlag(byte[]? flag) => flag is { Length: > 0 } && (flag[0] & 1) != 0;

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
    public static void RemoveIfUnder(string folder) => RemoveIfUnder(Registry.CurrentUser, folder);

    /// <inheritdoc cref="RemoveIfUnder(string)"/>
    /// <remarks>
    /// Task Manager's flag goes with the entry, so that installing again later starts at
    /// sign-in as a first install does, rather than off by a choice made about the copy that
    /// was removed.
    /// </remarks>
    public static void RemoveIfUnder(RegistryKey root, string folder)
    {
        try
        {
            using var key = root.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not string value)
            {
                return;
            }

            var target = Path.GetFullPath(value.Trim().Trim('"'));
            var install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;

            if (target.StartsWith(install, StringComparison.OrdinalIgnoreCase))
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                ClearFlag(root);
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException
            or ArgumentException or NotSupportedException or IOException)
        {
            // Left as it is: a stale entry costs a failed launch at sign-in, and nothing else.
        }
    }

    /// <summary>
    /// The user's choice, from the System page, made as Windows makes it: on writes the Run
    /// entry for this copy and flags it on; off keeps the entry and flags it off. Returns false
    /// if the registry refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Writing the entry alone would leave a dock turned off in Windows off, with the checkbox
    /// ticked — which is how this read before the flag was taken into account.
    /// </para>
    /// <para>
    /// Off used to delete the entry, which took the dock off Windows' list of startup apps
    /// altogether rather than switching it off there; asked on 2026-10-01 to behave as Windows'
    /// own switch does. With no entry there is nothing to switch off, and nothing is written.
    /// </para>
    /// </remarks>
    public static bool SetEnabled(bool enabled) => SetEnabled(Registry.CurrentUser, enabled);

    /// <inheritdoc cref="SetEnabled(bool)"/>
    public static bool SetEnabled(RegistryKey root, bool enabled)
    {
        try
        {
            if (enabled)
            {
                if (!Write(root))
                {
                    return false;
                }

                WriteFlag(root, OnFlag());
            }
            else if (IsRegistered(root))
            {
                WriteFlag(root, OffFlag(DateTime.UtcNow));
            }

            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the Run entry and leaves Task Manager's flag alone — for the setup and the first
    /// run, which turn autostart on as a default rather than as the user's choice. Returns
    /// false if the registry refused.
    /// </summary>
    /// <remarks>
    /// A setup run over an installed copy calls the install hook as a first install does, so
    /// clearing the flag here would turn back on, at every such setup, a dock the user had
    /// turned off — in Task Manager, the Settings app, or the dock's own checkbox.
    /// </remarks>
    public static bool Register() => Register(Registry.CurrentUser);

    /// <inheritdoc cref="Register()"/>
    public static bool Register(RegistryKey root)
    {
        try
        {
            return Write(root);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool Write(RegistryKey root)
    {
        using var key = root.OpenSubKey(RunKeyPath, writable: true) ?? root.CreateSubKey(RunKeyPath);

        if (key is null)
        {
            return false;
        }

        // Quoted: the install path may well contain spaces. Velopack's launcher rather than
        // this executable, in a copy its setup installed — see LaunchPath.
        key.SetValue(ValueName, $"\"{AppUpdater.LaunchPath}\"");
        return true;
    }

    /// <summary>The flag Task Manager writes for on: <c>02</c> and eleven zeros.</summary>
    public static byte[] OnFlag() => [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>
    /// The flag Task Manager writes for off: <c>03</c>, three zeros, and the <c>FILETIME</c> it
    /// was turned off at.
    /// </summary>
    public static byte[] OffFlag(DateTime utc)
    {
        var flag = new byte[12];
        flag[0] = 0x03;
        BitConverter.TryWriteBytes(flag.AsSpan(4), utc.ToFileTimeUtc());
        return flag;
    }

    private static void WriteFlag(RegistryKey root, byte[] flag)
    {
        using var approved = root.CreateSubKey(ApprovedKeyPath);
        approved.SetValue(ValueName, flag, RegistryValueKind.Binary);
    }

    /// <summary>Takes off the flag, with the entry it belongs to.</summary>
    private static void ClearFlag(RegistryKey root)
    {
        using var approved = root.OpenSubKey(ApprovedKeyPath, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
