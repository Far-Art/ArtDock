using System.IO;
using ArtDock.Interop;

namespace ArtDock.Services;

/// <summary>
/// What a look at the taskbar found: the pins to add, and how many the taskbar has, so that a
/// taskbar with nothing on it can be told from one whose pins are all on the dock already.
/// </summary>
/// <param name="Pins">A pin for each of the taskbar's that the dock does not have, in the taskbar's order.</param>
/// <param name="OnTaskbar">How many pins the taskbar has that the dock could open.</param>
public sealed record TaskbarImport(IReadOnlyList<PinnedAppSetting> Pins, int OnTaskbar);

/// <summary>
/// Pins for what is pinned to Windows' taskbar: the Items page's <em>Add from taskbar</em>, and
/// the question a new dock asks at its first run.
/// </summary>
/// <remarks>
/// <para>
/// The taskbar's list is read by <see cref="TaskbarFavorites"/>, and only read: nothing here
/// unpins anything from the taskbar, which keeps its pins as they were. A Store app is pinned by
/// its app ID, as the Add menu pins Settings. A shortcut is not pinned where it is: the taskbar's
/// own shortcuts live in its <em>User Pinned</em> folder and are deleted the moment the user
/// unpins the app from the taskbar, which would leave the dock's pin opening nothing. So
/// the Start menu's shortcut to the same program is pinned in its place (<see cref="Equivalent"/>),
/// and where there is none, a copy of the taskbar's is kept beside the settings
/// (<see cref="ShortcutsFolder"/>). File Explorer becomes the Add menu's File Explorer.
/// </para>
/// <para>
/// Each is named as the taskbar names it — the shortcut's name, or the Store app's — and one the
/// dock already opens is left out, by the program it opens rather than the path it is stored
/// under: Chrome pinned as <c>chrome.exe</c> and the Start menu's <em>Google Chrome</em> are one
/// app to someone looking at the dock.
/// </para>
/// </remarks>
public static class TaskbarPins
{
    /// <summary>
    /// Where a taskbar shortcut with no Start menu equivalent is copied to: beside the settings,
    /// so that <em>Delete settings</em> at an uninstall takes them with the pins that use them.
    /// </summary>
    public static string ShortcutsFolder => Path.Combine(SettingsStore.Folder, "Shortcuts");

    /// <summary>
    /// The taskbar's pins that <paramref name="existing"/> does not have.
    /// </summary>
    /// <remarks>
    /// Reads shortcuts through the shell, so call it on an STA thread. The Start menu is walked
    /// only if a shortcut needs it, and only once.
    /// </remarks>
    public static TaskbarImport Find(IEnumerable<PinnedAppSetting> existing)
    {
        var items = TaskbarFavorites.Read();
        if (items.Count == 0)
        {
            return new TaskbarImport([], 0);
        }

        var start = new Lazy<IReadOnlyList<StartShortcut>>(StartShortcuts);
        var have = new HashSet<string>(
            existing.Select(KeyOf).OfType<string>(), StringComparer.OrdinalIgnoreCase);

        var pins = new List<PinnedAppSetting>();
        var onTaskbar = 0;
        foreach (var item in items)
        {
            if (Pin(item, start) is not { } pin)
            {
                continue;
            }

            onTaskbar++;
            if (KeyOf(pin) is not { } key || have.Add(key))
            {
                pins.Add(pin);
            }
        }

        return new TaskbarImport(pins, onTaskbar);
    }

    /// <summary>
    /// A new dock's pins with the taskbar's added: after the places, set off by a separator of
    /// their own, and before the separator that keeps the Recycle Bin at the end — Finder's side,
    /// the apps, then the Trash, as a Mac has it. At the end, for a list with no separator.
    /// </summary>
    public static List<PinnedAppSetting> IntoNewDock(IReadOnlyList<PinnedAppSetting> pins, IReadOnlyList<PinnedAppSetting> found)
    {
        var merged = pins.ToList();
        if (found.Count == 0)
        {
            return merged;
        }

        var last = merged.FindLastIndex(pin => pin.IsSeparator);
        if (last < 0)
        {
            merged.AddRange(found);
        }
        else
        {
            merged.InsertRange(last, [DockPresets.CreateSeparator(), .. found]);
        }

        return merged;
    }

    /// <summary>
    /// What makes two pins the same app for an import: the app ID of a Store app, or else the
    /// program the pin opens — through a shortcut, what it points at. Null for a separator.
    /// </summary>
    public static string? KeyOf(PinnedAppSetting pin)
    {
        if (pin.IsSeparator)
        {
            return null;
        }

        if (pin.Aumid is { Length: > 0 } aumid)
        {
            return "aumid:" + aumid.Trim();
        }

        var opens = PinnedAppsService.ResolveLinkTarget(pin.TargetPath) ?? pin.TargetPath;
        return PinnedAppsService.TargetKey(opens is null ? null : Environment.ExpandEnvironmentVariables(opens));
    }

    /// <summary>A shortcut in the Start menu or on the desktop, and what it opens.</summary>
    public readonly record struct StartShortcut(string Link, string? Target, string? AppId);

    /// <summary>
    /// The Start menu shortcut that opens what the taskbar's does, or null when there is none:
    /// the same program, and the same app ID where both give one.
    /// </summary>
    /// <remarks>
    /// The app ID decides between shortcuts to one program that are different apps — a
    /// browser's profiles, its web apps — and one with no app ID is taken only when no shortcut
    /// matches exactly.
    /// </remarks>
    public static string? Equivalent(string target, string? appId, IEnumerable<StartShortcut> shortcuts)
    {
        string? near = null;
        foreach (var shortcut in shortcuts)
        {
            if (!string.Equals(shortcut.Target, target, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(shortcut.AppId, appId, StringComparison.OrdinalIgnoreCase))
            {
                return shortcut.Link;
            }

            if (appId is null || shortcut.AppId is null)
            {
                near ??= shortcut.Link;
            }
        }

        return near;
    }

    /// <summary>Whether a parsing name with no path is an app in <c>AppsFolder</c>: its app ID.</summary>
    public static bool IsAppId(string? parsingName) =>
        parsingName is { Length: > 0 }
        && !ShellIdList.IsPlace(parsingName)
        && !Path.IsPathRooted(parsingName)
        && !parsingName.StartsWith('{');

    private static PinnedAppSetting? Pin(ShellIdList.Item item, Lazy<IReadOnlyList<StartShortcut>> start)
    {
        if (item.Path is null)
        {
            if (IsAppId(item.ParsingName))
            {
                var aumid = item.ParsingName!;
                var label = DockPresets.PresetLabel(null, aumid) ?? ShellNames.DisplayName($@"shell:AppsFolder\{aumid}");

                // An app the shell no longer knows has been uninstalled since it was pinned.
                return label is null ? null : new PinnedAppSetting
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = label,
                    Aumid = aumid
                };
            }

            return item.ParsingName is { } place ? DockPresets.CreateFromShellName(place) : null;
        }

        if (!Path.GetExtension(item.Path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return PinnedAppsService.CreatePin(item.Path);
        }

        if (!File.Exists(item.Path))
        {
            return null;
        }

        var target = ShellLink.ResolveTarget(item.Path);
        var appId = ShellLink.AppId(item.Path);
        if (IsFileExplorer(target, appId) && DockPresets.Create("explorer") is { } explorer)
        {
            // As the taskbar and the Add menu call it, not as explorer.exe describes itself:
            // "Windows Explorer".
            explorer.Label = Localization.Localizer.Get("Preset.FileExplorer");
            return explorer;
        }

        // Named after the shortcut that is pinned: the Start menu's is kept up to date by the
        // app's installer, where the taskbar's keeps the name it was pinned under — measured,
        // "WebStorm 2026.1" on the taskbar for what the Start menu calls "WebStorm 2026.2.3".
        var stable = target is null ? null : Equivalent(target, appId, start.Value);
        return new PinnedAppSetting
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = Path.GetFileNameWithoutExtension(stable ?? item.Path),
            TargetPath = stable ?? Keep(item.Path)
        };
    }

    /// <summary>File Explorer's app ID, which its taskbar shortcut gives it.</summary>
    private const string FileExplorerAppId = "Microsoft.Windows.Explorer";

    /// <summary>
    /// Whether a taskbar shortcut is File Explorer's — by its app ID, since the one Windows pins
    /// for itself points at the shell's namespace rather than a file, and has no target to read.
    /// </summary>
    private static bool IsFileExplorer(string? target, string? appId) =>
        string.Equals(appId, FileExplorerAppId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            target,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A copy of a taskbar shortcut in <see cref="ShortcutsFolder"/> — the one already there, if
    /// an earlier import made it — or the taskbar's own, if it cannot be copied.
    /// </summary>
    private static string Keep(string link)
    {
        try
        {
            Directory.CreateDirectory(ShortcutsFolder);
            var bytes = File.ReadAllBytes(link);
            var stem = Path.GetFileNameWithoutExtension(link);
            for (var n = 1; n < 100; n++)
            {
                var copy = Path.Combine(ShortcutsFolder, n == 1 ? $"{stem}.lnk" : $"{stem} ({n}).lnk");
                if (!File.Exists(copy))
                {
                    File.WriteAllBytes(copy, bytes);
                    return copy;
                }

                if (File.ReadAllBytes(copy).AsSpan().SequenceEqual(bytes))
                {
                    return copy;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Pinned where it is: it works for as long as it stays on the taskbar.
        }

        return link;
    }

    private static IReadOnlyList<StartShortcut> StartShortcuts() =>
    [
        .. InstalledApps.ShortcutFiles().Select(link => new StartShortcut(link, ShellLink.ResolveTarget(link), ShellLink.AppId(link)))
    ];
}
