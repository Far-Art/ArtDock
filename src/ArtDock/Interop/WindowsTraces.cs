using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ArtDock.Interop;

/// <summary>
/// What Windows keeps, by itself, about a program it has seen run — found and removed for an
/// installation on its way out.
/// </summary>
/// <remarks>
/// <para>
/// Velopack's uninstaller removes what it made: the install folder, the shortcuts, the entry
/// in installed apps. The dock's own hook removes the autostart entry and, if asked, the
/// settings. None of that touches what Windows wrote down on the side, and every one of these
/// was found still there after an uninstall, on the development machine on 2026-10-04:
/// </para>
/// <list type="bullet">
/// <item>the tray icon, under <c>Control Panel\NotifyIconSettings</c> — what keeps ArtDock
/// listed in Settings' <i>Other system tray icons</i> long after it is gone;</item>
/// <item>the name Explorer read out of the executable (<c>MuiCache</c>), the launch counts
/// (<c>UserAssist</c>, its names in ROT13), the switch counts (<c>FeatureUsage</c>), the
/// Program Compatibility Assistant's record of each executable it saw start;</item>
/// <item>the Start menu's: the tile's properties, the jump list's timestamp, and two entries in
/// its cloud store;</item>
/// <item>the jump list itself, a file under <c>Recent\AutomaticDestinations</c> named by a hash
/// of the app's id — the file dialog adds to it whenever the dock pins from one;</item>
/// <item>Windows Backup's queue of the install, the uninstall and the Start tile
/// (<c>AppListBackup</c>), waiting to go to the user's backup — found in the install test of
/// 2026-10-04, the uninstall's added once Velopack had removed the entry in installed apps.</item>
/// </list>
/// <para>
/// An entry is this installation's when it names a path inside the install folder, its
/// AppUserModelID (<c>velopack.</c> and the install id, which Velopack gives the process and
/// its shortcuts), or the setup by its file name, wherever it was run from. A copy run from
/// anywhere else — a build from the source tree — is another program as far as Windows is
/// concerned, and is left alone.
/// </para>
/// <para>
/// All of it is per user, and none of it needs administrator rights. What Windows keeps for
/// the machine — Prefetch, the Amcache, the background activity moderator, the shim cache —
/// does, and stays.
/// </para>
/// <para>
/// Every method that touches the registry takes the key the paths are under, as
/// <see cref="Autostart"/>'s do: <see cref="Registry.CurrentUser"/> for the uninstaller, a
/// scratch key for the tests.
/// </para>
/// </remarks>
public sealed class WindowsTraces
{
    public const string NotifyIconsPath = @"Control Panel\NotifyIconSettings";
    public const string MuiCachePath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
    public const string UserAssistPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist";
    public const string FeatureUsagePath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage";
    public const string CompatibilityStorePath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store";
    public const string CompatibilityLayersPath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    public const string GpuPreferencesPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    public const string JumplistDataPath = @"Software\Microsoft\Windows\CurrentVersion\Search\JumplistData";
    public const string TilePropertiesPath = @"Software\Microsoft\Windows\CurrentVersion\Start\TileProperties";
    public const string NotificationsPath = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    public const string CloudStorePath = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount";
    public const string BackupQueuePath = @"Software\Microsoft\Windows\CurrentVersion\AppListBackup";

    /// <summary>The keys whose values are named by the program they are about.</summary>
    private static readonly string[] NamedByProgram =
    [
        MuiCachePath, CompatibilityStorePath, CompatibilityLayersPath, GpuPreferencesPath, JumplistDataPath
    ];

    /// <summary>
    /// What <c>MuiCache</c> puts after the path in a value's name: the two things it read out
    /// of the executable's version resource.
    /// </summary>
    private static readonly string[] MuiCacheSuffixes = [".FriendlyAppName", ".ApplicationCompany"];

    private readonly string _folder;
    private readonly string _installId;
    private readonly Regex _setup;
    private readonly Func<Guid, string?> _knownFolder;

    /// <param name="installFolder">The folder the setup installed into.</param>
    /// <param name="installId">The id Velopack installed under — <c>ArtDock.App</c>.</param>
    /// <param name="knownFolder">
    /// Where a known folder is, for the paths Windows writes as one's id and the rest — the tray's
    /// <c>{6D809377-…}\…</c> for Program Files, say. Null finds them as Windows does.
    /// </param>
    public WindowsTraces(string installFolder, string installId, Func<Guid, string?>? knownFolder = null)
    {
        _folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installFolder)) + Path.DirectorySeparatorChar;
        _installId = installId;
        _knownFolder = knownFolder ?? KnownFolderPath;

        // As vpk names it, and as a browser renames a second download of it.
        _setup = new Regex(
            $@"^{Regex.Escape(installId)}-win-Setup(?: \(\d+\))?\.exe$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// The AppUserModelID Velopack gives an installed copy's process and shortcuts, which the
    /// taskbar, Start and the jump list know it by.
    /// </summary>
    public string AppUserModelId => "velopack." + _installId;

    /// <summary>One thing Windows keeps: a value, a whole key, or a file.</summary>
    public abstract record Trace;

    /// <summary>A value named <paramref name="Name"/> in the key at <paramref name="Key"/>.</summary>
    public sealed record ValueTrace(string Key, string Name) : Trace;

    /// <summary>The key at <paramref name="Key"/>, and everything under it.</summary>
    public sealed record KeyTrace(string Key) : Trace;

    /// <summary>A file.</summary>
    public sealed record FileTrace(string Path) : Trace;

    /// <summary>
    /// Everything Windows keeps under <paramref name="root"/>, and among the jump lists in
    /// <paramref name="recentFolder"/>, about this installation.
    /// </summary>
    /// <param name="recentFolder">The user's <c>Recent</c> folder, which holds the jump lists.</param>
    public IReadOnlyList<Trace> Find(RegistryKey root, string recentFolder)
    {
        var found = new List<Trace>();

        Collect(found, () => FindTrayIcons(root));

        foreach (var path in NamedByProgram)
        {
            Collect(found, () => FindValues(root, path, name => name));
        }

        Collect(found, () => Subkeys(root, FeatureUsagePath)
            .SelectMany(key => FindValues(root, key, name => name)));

        Collect(found, () => Subkeys(root, UserAssistPath)
            .SelectMany(key => FindValues(root, key + @"\Count", Rot13)));

        Collect(found, () => FindKey(root, $@"{TilePropertiesPath}\W~{AppUserModelId}"));
        Collect(found, () => FindKey(root, $@"{NotificationsPath}\{AppUserModelId}"));
        Collect(found, () => FindCloudStore(root));

        Collect(found, () => FindBackupQueue(root));

        found.AddRange(JumpLists(recentFolder).Where(File.Exists).Select(path => new FileTrace(path)));

        return found;
    }

    /// <summary>Everything Windows keeps about this installation, for the current user.</summary>
    public IReadOnlyList<Trace> Find() => Find(Registry.CurrentUser, RecentFolder);

    /// <summary>
    /// Deletes <paramref name="traces"/>, each on its own: one that cannot be deleted is left,
    /// and the rest go anyway.
    /// </summary>
    public static void Remove(RegistryKey root, IEnumerable<Trace> traces)
    {
        foreach (var trace in traces)
        {
            try
            {
                switch (trace)
                {
                    case ValueTrace value:
                        using (var key = root.OpenSubKey(value.Key, writable: true))
                        {
                            key?.DeleteValue(value.Name, throwOnMissingValue: false);
                        }

                        break;

                    case KeyTrace key:
                        root.DeleteSubKeyTree(key.Key, throwOnMissingSubKey: false);
                        break;

                    case FileTrace file:
                        File.Delete(file.Path);
                        break;
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException
                or IOException or ArgumentException)
            {
                // Left as it is: an uninstall is no place to fail.
            }
        }
    }

    /// <summary>
    /// Deletes everything <see cref="Find()"/> finds, now — and again once Velopack's uninstaller
    /// has gone (<see cref="RemoveLater"/>).
    /// </summary>
    public void RemoveAll()
    {
        var traces = Find();
        Remove(Registry.CurrentUser, traces);
        RemoveLater(traces.Concat(Predicted(Registry.CurrentUser, RecentFolder)));
    }

    /// <summary>
    /// What Windows can be expected to write after the hook, named ahead: the switch counts and
    /// launch counts under the app's id and each executable in the install folder, and the jump
    /// lists, whether or not any of it is there yet.
    /// </summary>
    /// <remarks>
    /// Seen on 2026-10-04: Explorer wrote the jump list again after the first pass had deleted
    /// it, and <c>UserAssist</c>'s entry for <c>Update.exe</c> when Velopack's closing message
    /// was dismissed. Both are named by things known now; Windows Backup's queue is not, and the
    /// second pass searches it (<see cref="LaterScript"/>).
    /// </remarks>
    public IReadOnlyList<Trace> Predicted(RegistryKey root, string recentFolder)
    {
        var predicted = new List<Trace>();

        predicted.AddRange(FeatureUsageKeys
            .Select(key => new ValueTrace($@"{FeatureUsagePath}\{key}", AppUserModelId)));

        // Written as the app's last window goes — which is the question's, in the instant the
        // hook ends — so the first pass can run just ahead of it: seen in the second install
        // test of 2026-10-04, the value stamped 15:07:42.031 and the key last written then.
        predicted.Add(new ValueTrace(JumplistDataPath, AppUserModelId));
        predicted.Add(new KeyTrace($@"{TilePropertiesPath}\W~{AppUserModelId}"));
        predicted.Add(new KeyTrace($@"{NotificationsPath}\{AppUserModelId}"));

        var names = new List<string> { AppUserModelId };
        try
        {
            if (Directory.Exists(_folder))
            {
                names.AddRange(Directory.EnumerateFiles(_folder, "*.exe", SearchOption.AllDirectories));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        Collect(predicted, () => Subkeys(root, UserAssistPath)
            .SelectMany(key => names.Select(name => new ValueTrace(key + @"\Count", Rot13(name)))));

        predicted.AddRange(JumpLists(recentFolder).Select(path => new FileTrace(path)));

        return predicted;
    }

    /// <summary>
    /// What the second pass runs, as Windows PowerShell's <c>-Command</c>: wait for Velopack's
    /// uninstaller to exit, then a few seconds more, then delete <paramref name="traces"/>,
    /// Windows Backup's queued news of the install and the uninstall, and Velopack's log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A second pass because some of it is written after the hook has run, and the hook is the
    /// last of this program that runs. Velopack writes its log until it exits, in a folder of its
    /// own it never removes. Explorer writes the jump list and the launch counts as the
    /// question's window and Velopack's closing message go. Windows Backup queues an event for
    /// the uninstall once the entry in installed apps is gone, which Velopack removes after the
    /// hook.
    /// </para>
    /// <para>
    /// Waiting for the uninstaller rather than for a time: an interactive uninstall ends on a
    /// message Velopack waits on until the user dismisses it, which took two minutes in the test.
    /// PowerShell because <c>cmd</c> cannot wait for a process; Windows', so it is there after
    /// this program is not. Plain text, not encoded, so anyone who looks can read it. Every name
    /// goes in a single-quoted string, which PowerShell reads literally, and through .NET's
    /// registry and file calls, which take no wildcards. A name with a quote PowerShell would
    /// end the string at is left out — the first pass has had it already — as is whatever
    /// would take the command line past Windows' limit.
    /// </para>
    /// </remarks>
    public string LaterScript(IEnumerable<Trace> traces)
    {
        var script = new List<string> { "$ErrorActionPreference = 'SilentlyContinue'" };

        script.Add(Quote(Path.Combine(_folder, "Update.exe")) is { } update
            ? $"Get-Process -Name Update | Where-Object {{ $_.Path -eq {update} }} | Wait-Process -Timeout 3600"
            : "Start-Sleep -Seconds 30");
        script.Add("Start-Sleep -Seconds 5");
        script.Add("$u = [Microsoft.Win32.Registry]::CurrentUser");

        var length = script.Sum(line => line.Length + 2);

        foreach (var trace in traces.Distinct())
        {
            var line = trace switch
            {
                ValueTrace value when Quote(value.Key) is { } key && Quote(value.Name) is { } name =>
                    $"$k = $u.OpenSubKey({key}, $true); if ($k) {{ $k.DeleteValue({name}, $false); $k.Close() }}",
                KeyTrace key when Quote(key.Key) is { } path =>
                    $"$u.DeleteSubKeyTree({path}, $false)",
                FileTrace file when Quote(file.Path) is { } path =>
                    $"[IO.File]::Delete({path})",
                _ => null
            };

            if (line is not null && length + line.Length + 2 < MaxScript)
            {
                script.Add(line);
                length += line.Length + 2;
            }
        }

        script.Add(
            $"$q = $u.OpenSubKey({Quote(BackupQueuePath)}, $true); if ($q) {{ foreach ($n in $q.GetSubKeyNames()) {{ " +
            "$s = $q.OpenSubKey($n); $j = $s.GetValue($n); $s.Close(); " +
            $"if ($j -match {Quote(BackupPattern)}) " +
            "{ $q.DeleteSubKeyTree($n, $false) } }; $q.Close() }");

        script.Add($"[IO.File]::Delete($env:LOCALAPPDATA + {Quote($@"\velopack\velopack_{_installId}.log")})");

        // Only when nothing is left in it: other programs installed by Velopack log there too.
        script.Add("$v = $env:LOCALAPPDATA + '\\velopack'; if (-not (Get-ChildItem -LiteralPath $v -Force)) { Remove-Item -LiteralPath $v }");

        return string.Join("; ", script);
    }

    /// <summary>
    /// How long the second pass's script may be: Windows' limit on a command line is 32,767
    /// characters, and the rest of it is PowerShell's path and switches.
    /// </summary>
    private const int MaxScript = 30_000;

    /// <summary>
    /// Starts <see cref="LaterScript"/>, hidden, from a folder that is not the install folder — a
    /// process sitting in it would keep it from being removed.
    /// </summary>
    public void RemoveLater(IEnumerable<Trace> traces)
    {
        try
        {
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var powershell = Path.Combine(system, @"WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(powershell))
            {
                return;
            }

            var start = new ProcessStartInfo(powershell)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = system
            };

            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", LaterScript(traces) })
            {
                start.ArgumentList.Add(argument);
            }

            using var _ = Process.Start(start);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception
            or IOException)
        {
            // The first pass stands.
        }
    }

    /// <summary>
    /// <paramref name="text"/> as a PowerShell string read literally, or null when it holds a
    /// character that would end the string early or a line break.
    /// </summary>
    /// <remarks>
    /// In a single-quoted string PowerShell reads nothing but the quote, which doubles. It takes
    /// the typographic single quotes for one too, and those are refused rather than reasoned
    /// about.
    /// </remarks>
    public static string? Quote(string text) =>
        text.IndexOfAny(['‘', '’', '‚', '‛', '\r', '\n', '\0']) >= 0
            ? null
            : "'" + text.Replace("'", "''") + "'";

    /// <summary>
    /// The keys under <c>FeatureUsage</c> that count by app: switched to, launched, its jump
    /// list shown, its badge changed, its taskbar button clicked.
    /// </summary>
    private static readonly string[] FeatureUsageKeys =
        ["AppSwitched", "AppLaunch", "ShowJumpView", "AppBadgeUpdated", "TrayButtonClicked"];

    private static string RecentFolder => Environment.GetFolderPath(Environment.SpecialFolder.Recent);

    /// <summary>The app's two jump-list files, there or not.</summary>
    private IEnumerable<string> JumpLists(string recentFolder)
    {
        var hash = JumpListId(AppUserModelId);
        yield return Path.Combine(recentFolder, "AutomaticDestinations", $"{hash}.automaticDestinations-ms");
        yield return Path.Combine(recentFolder, "CustomDestinations", $"{hash}.customDestinations-ms");
    }

    /// <summary>
    /// Windows Backup's queue of installs and uninstalls waiting to go to the user's backup: a
    /// key per event, holding one value named as the key, whose JSON names the app by
    /// <c>appId</c> — the entry in installed apps — or its Start tile by <c>tileId</c>.
    /// </summary>
    private IEnumerable<Trace> FindBackupQueue(RegistryKey root)
    {
        using var queue = root.OpenSubKey(BackupQueuePath);
        if (queue is null)
        {
            yield break;
        }

        foreach (var name in queue.GetSubKeyNames())
        {
            using var item = queue.OpenSubKey(name);
            if (item?.GetValue(name) is string json && NamesUs(json))
            {
                yield return new KeyTrace($@"{BackupQueuePath}\{name}");
            }
        }
    }

    /// <summary>Whether a backup event — one entry, or a list of them — is this app's.</summary>
    /// <remarks>
    /// Matched as text, not parsed: Windows writes it as JSON with its backslashes unescaped
    /// (<c>"wingetId":"ARP\User\X64\ArtDock.App"</c>), which no JSON reader takes. Found on
    /// 2026-10-04, when PowerShell's <c>ConvertFrom-Json</c> refused ArtDock's own entries.
    /// </remarks>
    public bool NamesUs(string json) => BackupEntry().IsMatch(json);

    /// <summary>
    /// <c>"appId":"</c> the install id <c>"</c>, or <c>"tileId":"W~</c> the app id <c>"</c>, as
    /// a .NET regular expression — and so as PowerShell's <c>-match</c> reads it too. The quote
    /// is <c>\x22</c>, so the second pass's script has no double quote in it.
    /// </summary>
    private string BackupPattern =>
        $@"\x22(appId\x22\s*:\s*\x22{Regex.Escape(_installId)}|tileId\x22\s*:\s*\x22{Regex.Escape("W~" + AppUserModelId)})\x22";

    private Regex BackupEntry() => new(BackupPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Whether <paramref name="name"/> names this installation, as Windows writes names.</summary>
    public bool IsOurs(string name)
    {
        if (name.Equals(AppUserModelId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var suffix in MuiCacheSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        var path = ExpandKnownFolder(name);
        return path.StartsWith(_folder, StringComparison.OrdinalIgnoreCase)
            || _setup.IsMatch(Path.GetFileName(path));
    }

    /// <summary>
    /// A path Windows wrote as a known folder's id and the rest, as the path it stands for; any
    /// other text as it is.
    /// </summary>
    private string ExpandKnownFolder(string name)
    {
        if (name.Length < 39 || name[0] != '{' || name[37] != '}'
            || !Guid.TryParse(name.AsSpan(0, 38), out var id)
            || _knownFolder(id) is not { Length: > 0 } folder)
        {
            return name;
        }

        return Path.TrimEndingDirectorySeparator(folder) + name[38..];
    }

    private IEnumerable<Trace> FindTrayIcons(RegistryKey root)
    {
        using var icons = root.OpenSubKey(NotifyIconsPath);
        if (icons is null)
        {
            yield break;
        }

        foreach (var id in icons.GetSubKeyNames())
        {
            using var icon = icons.OpenSubKey(id);
            if (icon?.GetValue("ExecutablePath") is string path && IsOurs(path))
            {
                yield return new KeyTrace($@"{NotifyIconsPath}\{id}");
            }
        }
    }

    private IEnumerable<Trace> FindValues(RegistryKey root, string path, Func<string, string> decode)
    {
        using var key = root.OpenSubKey(path);
        if (key is null)
        {
            yield break;
        }

        foreach (var name in key.GetValueNames())
        {
            if (IsOurs(decode(name)))
            {
                yield return new ValueTrace(path, name);
            }
        }
    }

    private static IEnumerable<Trace> FindKey(RegistryKey root, string path)
    {
        using var key = root.OpenSubKey(path);
        if (key is not null)
        {
            yield return new KeyTrace(path);
        }
    }

    /// <summary>
    /// Start's two entries in the cloud store, in both its copies (<c>Cloud</c> and
    /// <c>Current</c>): the tile's, under the app's id, and the app's, under the install id —
    /// which is the name of its entry in installed apps.
    /// </summary>
    private IEnumerable<Trace> FindCloudStore(RegistryKey root)
    {
        var wanted = new[]
        {
            ("$windows.data.apps.appleveltileinfo$appleveltilelist",
                $"windows.data.apps.appleveltileinfo$w~{AppUserModelId}"),
            ("$windows.data.apps.appmetadata$appmetadatalist",
                $"windows.data.apps.appmetadata${_installId}")
        };

        foreach (var copy in new[] { "Cloud", "Current" })
        {
            var store = $@"{CloudStorePath}\{copy}";
            foreach (var list in Subkeys(root, store))
            {
                foreach (var (suffix, entry) in wanted)
                {
                    if (!list.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using var opened = root.OpenSubKey(list);
                    var name = opened?.GetSubKeyNames()
                        .FirstOrDefault(name => name.Equals(entry, StringComparison.OrdinalIgnoreCase));

                    if (name is not null)
                    {
                        yield return new KeyTrace($@"{list}\{name}");
                    }
                }
            }
        }
    }

    /// <summary>The full paths of the keys directly under <paramref name="path"/>.</summary>
    private static string[] Subkeys(RegistryKey root, string path)
    {
        using var key = root.OpenSubKey(path);
        return key?.GetSubKeyNames().Select(name => $@"{path}\{name}").ToArray() ?? [];
    }

    /// <summary>
    /// Adds what <paramref name="find"/> finds, or nothing if the registry refuses: one store
    /// that cannot be read does not keep the others from being searched.
    /// </summary>
    private static void Collect(List<Trace> found, Func<IEnumerable<Trace>> find)
    {
        try
        {
            found.AddRange(find());
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException
            or IOException)
        {
        }
    }

    /// <summary><c>UserAssist</c>'s names, which are ROT13 — its own inverse.</summary>
    public static string Rot13(string text) =>
        string.Create(text.Length, text, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = c switch
                {
                    >= 'a' and <= 'z' => (char)('a' + (c - 'a' + 13) % 26),
                    >= 'A' and <= 'Z' => (char)('A' + (c - 'A' + 13) % 26),
                    _ => c
                };
            }
        });

    /// <summary>
    /// The name the shell gives an app's jump-list files: a CRC-64 of its AppUserModelID in
    /// capitals, as UTF-16, in sixteen hex digits.
    /// </summary>
    /// <remarks>
    /// ECMA-182's polynomial, reflected, started from all ones and not inverted at the end —
    /// checked against File Explorer's, <c>Microsoft.Windows.Explorer</c>, whose file is
    /// <c>f01b4d95cf55d32a</c> on every machine.
    /// </remarks>
    public static string JumpListId(string appUserModelId)
    {
        const ulong Polynomial = 0x92C64265D32139A4;
        var crc = ulong.MaxValue;

        foreach (var b in Encoding.Unicode.GetBytes(appUserModelId.ToUpperInvariant()))
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Polynomial : crc >> 1;
            }
        }

        return crc.ToString("x16");
    }

    private static string? KnownFolderPath(Guid id) =>
        SHGetKnownFolderPath(id, KF_FLAG_DONT_VERIFY, 0, out var path) == 0 ? path : null;

    private const uint KF_FLAG_DONT_VERIFY = 0x4000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        in Guid rfid, uint dwFlags, nint hToken, [MarshalAs(UnmanagedType.LPWStr)] out string ppszPath);
}
