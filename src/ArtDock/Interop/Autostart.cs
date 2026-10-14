using System.IO;
using ArtDock.Services;
using Microsoft.Win32;

namespace ArtDock.Interop;

/// <summary>
/// Registers the dock to start with Windows, via the per-user Run key and a sign-in task beside it.
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
/// Beside the entry, a Task Scheduler task starts the dock as the user signs in, ahead of
/// Explorer's queue of startup apps (<see cref="SignInTask"/>). The entry stays the switch, and
/// the task follows it: see <see cref="Refresh()"/>.
/// </para>
/// <para>
/// Every method that touches the registry has a form taking the key the paths are under —
/// <see cref="Registry.CurrentUser"/> for the dock, a scratch key for the tests, which must
/// never write the user's real Run key — and the task kept beside it, which for the tests is
/// never the real one either.
/// </para>
/// </remarks>
public static class Autostart
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// The switch the entry and the task both start the dock with: a launch at sign-in, which
    /// says nothing if a dock is up already, and goes if the entry is not this copy's or is off
    /// (<see cref="StartsThisCopy()"/>).
    /// </summary>
    public const string SignInSwitch = "--sign-in";

    /// <summary>
    /// One at a time: the dock's start brings the task into line on a pool thread, while the
    /// settings dialog's checkbox may be writing on the dock's.
    /// </summary>
    private static readonly Lock Gate = new();

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
    public static bool IsRegistered(RegistryKey root) => EntryOf(root) is not null;

    /// <summary>
    /// True when the entry is on and starts this copy of the dock — what a launch at sign-in asks
    /// before it goes any further.
    /// </summary>
    /// <remarks>
    /// The task cannot be switched off in Task Manager, so the dock it starts asks the entry,
    /// which can. And it asks whose the entry is: a task left behind by a copy that no longer
    /// has the entry would otherwise start that copy ahead of the one that does.
    /// </remarks>
    public static bool StartsThisCopy() => StartsThisCopy(Registry.CurrentUser);

    /// <inheritdoc cref="StartsThisCopy()"/>
    public static bool StartsThisCopy(RegistryKey root) => IsEnabled(root) && IsThisCopy(EntryOf(root));

    /// <summary>The program a Run value starts: its first word, or the quoted path it opens with.</summary>
    public static string CommandPath(string value)
    {
        var text = value.Trim();
        if (!text.StartsWith('"'))
        {
            return text;
        }

        var close = text.IndexOf('"', 1);
        return close < 0 ? text[1..] : text[1..close];
    }

    /// <summary>The Run value, or null where there is none.</summary>
    private static string? EntryOf(RegistryKey root)
    {
        using var key = root.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string { Length: > 0 } value ? value : null;
    }

    /// <summary>Whether a Run value starts this copy: the program <see cref="AppUpdater.LaunchPath"/> names.</summary>
    private static bool IsThisCopy(string? entry) =>
        entry is not null && AppUpdater.LaunchPath is { } launch && SamePath(CommandPath(entry), launch);

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsUnder(string path, string folder)
    {
        try
        {
            var install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(install, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
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
    /// installation on its way out — and leaves one that starts any other copy alone. The same
    /// for the sign-in task.
    /// </summary>
    public static void RemoveIfUnder(string folder) => RemoveIfUnder(Registry.CurrentUser, SignInTask.ForThisUser, folder);

    /// <inheritdoc cref="RemoveIfUnder(string)"/>
    /// <remarks>
    /// Task Manager's flag goes with the entry, so that installing again later starts at
    /// sign-in as a first install does, rather than off by a choice made about the copy that
    /// was removed.
    /// </remarks>
    public static void RemoveIfUnder(RegistryKey root, ISignInTask task, string folder)
    {
        lock (Gate)
        {
            // First, and whatever becomes of the entry: a task left starting a program that is
            // gone fails at every sign-in, with nothing to say so.
            if (task.Read() is { } command && IsUnder(command.Path, folder))
            {
                task.Remove();
            }

            try
            {
                using var key = root.OpenSubKey(RunKeyPath, writable: true);
                if (key?.GetValue(ValueName) is string value && IsUnder(CommandPath(value), folder))
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                    ClearFlag(root);
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException
                or ArgumentException or IOException)
            {
                // Left as it is: a stale entry costs a failed launch at sign-in, and nothing else.
            }
        }
    }

    /// <summary>
    /// Brings the sign-in task into line with the entry: there, starting this copy, while the
    /// entry is this copy's and on; gone while there is no entry or it is off; and left to the
    /// copy whose entry it is otherwise. The entry is given <see cref="SignInSwitch"/> if it was
    /// written before there was one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The entry is the switch because it is the one Windows shows — Task Manager's <em>Startup
    /// apps</em> and the Settings app's <em>Apps → Startup</em> list Run entries and never tasks —
    /// and the System page's checkbox is one switch with theirs (asked 2026-10-01). So the task is
    /// only ever a faster way to the same start: the dock it starts goes again unless the entry
    /// says otherwise (<see cref="StartsThisCopy()"/>), and the entry's own launch, later, finds
    /// the dock up and goes without a word.
    /// </para>
    /// <para>
    /// Run at every start of the dock, and after anything here that writes the entry or its flag,
    /// so a switch made in Windows reaches the task by the next start, and a copy updated from
    /// before there was a task gains one. A task found starting this copy already is left as it
    /// is, so one disabled in Task Scheduler stays disabled.
    /// </para>
    /// </remarks>
    public static void Refresh() => Refresh(Registry.CurrentUser, SignInTask.ForThisUser);

    /// <inheritdoc cref="Refresh()"/>
    public static void Refresh(RegistryKey root, ISignInTask task)
    {
        lock (Gate)
        {
            KeepTaskInStep(root, task);
        }
    }

    /// <summary><see cref="Refresh(RegistryKey, ISignInTask)"/>, for a caller already holding <see cref="Gate"/>.</summary>
    private static void KeepTaskInStep(RegistryKey root, ISignInTask task)
    {
        try
        {
            if (!IsEnabled(root))
            {
                task.Remove();
                return;
            }

            var entry = EntryOf(root);
            if (!IsThisCopy(entry) || AppUpdater.LaunchPath is not { } launch)
            {
                return;
            }

            // Before the task, or the entry's launch would meet the dock the task had started and
            // say ArtDock was already running.
            if (!HasSignInSwitch(entry!))
            {
                Write(root);
            }

            var wanted = new SignInCommand(launch, SignInSwitch);
            if (task.Read() is not { } current || !current.Matches(wanted))
            {
                task.Write(wanted);
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // The entry as it was; the dock starts from it as it always has.
        }
    }

    /// <summary>Whether a Run value gives the program it starts <see cref="SignInSwitch"/>.</summary>
    public static bool HasSignInSwitch(string value)
    {
        var text = value.Trim();
        var arguments = text.StartsWith('"') && text.IndexOf('"', 1) is var close and > 0
            ? text[(close + 1)..]
            : "";

        return arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(SignInSwitch, StringComparer.OrdinalIgnoreCase);
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
    public static bool SetEnabled(bool enabled) => SetEnabled(Registry.CurrentUser, SignInTask.ForThisUser, enabled);

    /// <inheritdoc cref="SetEnabled(bool)"/>
    /// <remarks>The task follows, as <see cref="Refresh()"/> has it: made for on, taken away for off.</remarks>
    public static bool SetEnabled(RegistryKey root, ISignInTask task, bool enabled)
    {
        lock (Gate)
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
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
            {
                return false;
            }

            KeepTaskInStep(root, task);
            return true;
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
    public static bool Register() => Register(Registry.CurrentUser, SignInTask.ForThisUser);

    /// <inheritdoc cref="Register()"/>
    /// <remarks>The task follows, as <see cref="Refresh()"/> has it: made while the flag says on.</remarks>
    public static bool Register(RegistryKey root, ISignInTask task)
    {
        lock (Gate)
        {
            try
            {
                if (!Write(root))
                {
                    return false;
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
            {
                return false;
            }

            KeepTaskInStep(root, task);
            return true;
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
        // this executable, in a copy its setup installed — see LaunchPath; it hands the switch on.
        key.SetValue(ValueName, $"\"{AppUpdater.LaunchPath}\" {SignInSwitch}");
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

    /// <summary>
    /// Where Windows records that it has told the user an app now starts at sign-in: a value
    /// named <c>StartupTNoti</c> and the Run value's name. Read off the development machine on
    /// 2026-10-04, still there after an uninstall.
    /// </summary>
    internal const string NotifiedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\RunNotification";

    /// <summary>
    /// Takes off the flag, with the entry it belongs to — and Windows' note that it said the
    /// entry was there, so that a later install is announced again as a new one is.
    /// </summary>
    private static void ClearFlag(RegistryKey root)
    {
        using (var approved = root.OpenSubKey(ApprovedKeyPath, writable: true))
        {
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        using var notified = root.OpenSubKey(NotifiedKeyPath, writable: true);
        notified?.DeleteValue("StartupTNoti" + ValueName, throwOnMissingValue: false);
    }
}
