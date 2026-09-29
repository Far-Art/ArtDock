using System.IO;

namespace ArtDock.Services;

/// <summary>One entry in the "add to dock" menu.</summary>
/// <param name="Key">Stable identifier, used as the menu command's payload.</param>
/// <param name="Label">What the menu shows.</param>
/// <param name="Target">
/// What the entry's icon is read from: the target the pin it makes will have. Null for the
/// entries that make nothing known in advance — <em>Browse…</em>, and the separator.
/// </param>
public readonly record struct DockPreset(string Key, string Label, string? Target = null);

/// <summary>
/// The things a dock can be given without going to a file dialog: a handful of apps that
/// ship with Windows, four places in the shell's namespace, the Start menu, and a
/// separator — and which of them a new dock starts with.
/// </summary>
/// <remarks>
/// Shared by the dock's right-click menu and the settings dialog's Add button, so the two
/// offer the same list rather than drifting apart.
/// </remarks>
public static class DockPresets
{
    /// <summary>Key for the entry that opens a file dialog rather than pinning something known.</summary>
    public const string BrowseKey = "browse";

    /// <summary>Key for the divider entry.</summary>
    public const string SeparatorKey = "separator";

    /// <summary>Label a separator carries in the settings list.</summary>
    public static string SeparatorLabel => Localization.Localizer.Get("Preset.Separator");

    /// <summary>
    /// Where each preset lives, and what to call it if the file has no description. An
    /// ordered list rather than a map, because this is also the order the menu shows.
    /// </summary>
    /// <remarks>
    /// Read afresh on each use, like the lists below, because the labels are in the dock's
    /// language and that can change while a menu is being built from them.
    /// </remarks>
    private static (string Key, string Path, string Label)[] Targets =>
    [
        ("taskmgr", @"%WINDIR%\System32\Taskmgr.exe", Localization.Localizer.Get("Preset.TaskManager")),
        ("control", @"%WINDIR%\System32\control.exe", Localization.Localizer.Get("Preset.ControlPanel")),
        ("explorer", @"%WINDIR%\explorer.exe", Localization.Localizer.Get("Preset.FileExplorer"))
    ];

    /// <summary>The Settings app's AUMID.</summary>
    public const string SettingsAumid =
        "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";

    /// <summary>
    /// Apps that ship with Windows as Store apps, in menu order: pinned by AUMID, since there
    /// is no file to pin.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Settings is pinned as the app rather than as the <c>ms-settings:</c> address, though
    /// both open it, because only the app has an icon. Measured through
    /// <see cref="Interop.ShellIcons.Load"/>: the address comes back as the shell's blank
    /// document, the app as its gear.
    /// </para>
    /// <para>
    /// Not existence-checked, like the places: Settings is part of Windows and cannot be
    /// uninstalled, and there is no file to ask about.
    /// </para>
    /// </remarks>
    private static (string Key, string Aumid, string Label)[] StoreApps =>
    [
        ("settings", SettingsAumid, Localization.Localizer.Get("Preset.Settings"))
    ];

    /// <summary>
    /// The Recycle Bin's parsing name, named because the dock's menu offers it an entry the
    /// other places have no equivalent of — see <see cref="Interop.RecycleBin"/>.
    /// </summary>
    public const string RecycleBinTarget = "shell:RecycleBinFolder";

    /// <summary>
    /// Places in the shell's namespace rather than files on disk, in menu order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the entries a preset list most needs, because they are the ones the file
    /// dialog cannot pick: the desktop's Recycle Bin and This PC are namespace extensions,
    /// not shortcuts. They can be dropped — see <see cref="CreateFromShellName"/> — but only
    /// from somewhere they are shown, and Windows 11 shows only the Recycle Bin on a new
    /// desktop.
    /// </para>
    /// <para>
    /// The user folder is the exception — it is a real folder, and dropping it works — and is
    /// here by name for a different reason. Dropped, it is pinned as <c>C:\Users\name</c>,
    /// which is wrong the moment the settings are imported under another account or on
    /// another machine; <c>shell:Profile</c> is whoever's folder it is at the time. The shell
    /// gives it the same icon either way, the folder with a person on it. Downloads is here
    /// for the same reason, and <c>shell:Downloads</c> also follows the folder when it has
    /// been moved elsewhere, which a stored path would not.
    /// </para>
    /// <para>
    /// They need no new machinery. <c>SHCreateItemFromParsingName</c>, which is what
    /// <see cref="Interop.ShellIcons"/> already uses, parses a <c>shell:</c> name as readily
    /// as a path, and <c>ShellExecute</c> launches one — the same two calls that make Store
    /// apps work through <c>shell:AppsFolder</c>. What they do not survive is
    /// <see cref="PinnedAppsService.CreatePin"/>, which asks the filesystem whether the
    /// target is there; <see cref="Create"/> builds these itself instead.
    /// </para>
    /// <para>
    /// Not existence-checked the way <see cref="Targets"/> is. All four exist on every copy
    /// of Windows and cannot be uninstalled, and for two of them there is no file to ask about
    /// in any case — hiding the Recycle Bin from the desktop does not remove the folder.
    /// </para>
    /// </remarks>
    private static (string Key, string Path, string Label)[] Places =>
    [
        ("thispc", "shell:MyComputerFolder", Localization.Localizer.Get("Preset.ThisPC")),
        ("userfolder", UserFolderTarget, Localization.Localizer.Get("Preset.UserFolder")),
        ("downloads", DownloadsTarget, Localization.Localizer.Get("Preset.Downloads")),
        ("recyclebin", RecycleBinTarget, Localization.Localizer.Get("Preset.RecycleBin"))
    ];

    /// <summary>The signed-in user's own folder, by the name that follows the user.</summary>
    public const string UserFolderTarget = "shell:Profile";

    /// <summary>
    /// The signed-in user's Downloads folder, by the name that follows the user — and the
    /// folder, if it has been moved out of the user folder in its properties.
    /// </summary>
    public const string DownloadsTarget = "shell:Downloads";

    /// <summary>
    /// What the user folder's pin is called: the name Explorer shows for it, which is the
    /// account's full name where there is one — or <paramref name="fallback"/>, the menu's
    /// "User folder", if the shell will not say.
    /// </summary>
    /// <remarks>
    /// Read when the pin is made and stored with it, like every label, so it does not follow
    /// the target the way <see cref="UserFolderTarget"/> does: settings imported under another
    /// account open that account's folder under the exporter's name, until renamed. The menu
    /// entry keeps saying "User folder", because it describes what it adds.
    /// </remarks>
    private static string UserFolderLabel(string fallback) =>
        Interop.ShellNames.DisplayName(UserFolderTarget) ?? fallback;

    /// <summary>
    /// What a new dock starts with, left to right.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All presets rather than the machine's own apps, which is what a new dock used to be
    /// given: File Explorer, Notepad, Paint, Snipping Tool, Command Prompt, PowerShell, Edge,
    /// Control Panel and Task Manager, less whichever were not installed. On Windows 11 Paint
    /// and Snipping Tool are Store apps and were never there to find, most of the rest are
    /// tools few people open, and the browser was Edge whatever the user's own was. These
    /// are on every machine, are the entries only a preset can supply — Settings is a Store
    /// app, so there is no file for it either — and leave the user's own apps to the user.
    /// </para>
    /// <para>
    /// The Recycle Bin is set off by a separator at the far end, where a Mac keeps the Trash.
    /// </para>
    /// </remarks>
    private static readonly string[] DefaultKeys =
        ["start", "thispc", "userfolder", "downloads", "settings", SeparatorKey, "recyclebin"];

    /// <summary>Builds the pins a new dock starts with.</summary>
    public static List<PinnedAppSetting> CreateDefaults() =>
        [.. DefaultKeys.Select(Create).OfType<PinnedAppSetting>()];

    /// <summary>Whether a pin points at the Recycle Bin.</summary>
    /// <remarks>
    /// By target rather than by preset key, because the key is not stored: a pin remembers
    /// where it points, and the item editor takes free text, so this has to answer for one
    /// that was typed as readily as one that came from the menu.
    /// </remarks>
    public static bool IsRecycleBin(string? target) =>
        string.Equals(target, RecycleBinTarget, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Entries that do something rather than open something — see <see cref="DockCommands"/>.
    /// </summary>
    /// <remarks>
    /// Built exactly as the places are, because by the time a command reaches here it is
    /// only a target string; what makes it different is what
    /// <see cref="Interop.AppLauncher"/> does with it, not how it is stored.
    /// </remarks>
    private static (string Key, string Path, string Label)[] Commands =>
    [
        ("start", DockCommands.StartTarget, Localization.Localizer.Get("Preset.Start"))
    ];

    /// <summary>
    /// The menu, in groups: the caller draws a rule between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Grouped rather than flat because the three kinds of entry are not alternatives to one
    /// another — going and finding something, taking one of the machine's own apps, and
    /// dropping in a divider are different acts, and a menu that runs them together reads
    /// as one flat list of equivalent things.
    /// </para>
    /// <para>
    /// Presets whose target is not on this machine are left out rather than offered and then
    /// silently doing nothing, and a group left empty by that is dropped — otherwise its
    /// rule would still be drawn, against nothing.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<DockPreset>> Menu()
    {
        var apps = new List<DockPreset>();
        foreach (var (key, path, label) in Targets)
        {
            var expanded = Environment.ExpandEnvironmentVariables(path);
            if (File.Exists(expanded))
            {
                apps.Add(new DockPreset(key, label, expanded));
            }
        }

        // Under the name Dock.DockItem.ShellTarget gives a Store app, which is what the shell
        // is asked for its icon once it is pinned.
        apps.AddRange(StoreApps.Select(
            app => new DockPreset(app.Key, app.Label, $@"shell:AppsFolder\{app.Aumid}")));

        IReadOnlyList<DockPreset>[] groups =
        [
            [new DockPreset(BrowseKey, Localization.Localizer.Get("Preset.Browse"))],
            apps,
            [
                .. Places.Select(place => new DockPreset(place.Key, place.Label, place.Path)),
                .. Commands.Select(command => new DockPreset(command.Key, command.Label, command.Path))
            ],
            [new DockPreset(SeparatorKey, SeparatorLabel)]
        ];

        return [.. groups.Where(group => group.Count > 0)];
    }

    /// <summary>
    /// Builds the pin for a menu key.
    /// </summary>
    /// <returns>
    /// A pin, or <see langword="null"/> for <see cref="BrowseKey"/> and for anything whose
    /// target has since gone missing — the caller decides what to do instead.
    /// </returns>
    public static PinnedAppSetting? Create(string key)
    {
        if (key == SeparatorKey)
        {
            return CreateSeparator();
        }

        var place = Array.Find(
            [.. Places, .. Commands],
            entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (place.Key is not null)
        {
            // Built here rather than through CreatePin, which would turn both kinds down:
            // there is no file at a shell name, and less than none at a command. The label
            // is given rather than read off the target for the same reason — nothing to
            // read it from — except for the user folder, whose name the shell does have.
            return new PinnedAppSetting
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = place.Path == UserFolderTarget ? UserFolderLabel(place.Label) : place.Label,
                TargetPath = place.Path
            };
        }

        var app = Array.Find(
            StoreApps, entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (app.Key is not null)
        {
            // An AUMID rather than a target path, which is what makes the item editor keep
            // it a Store app and the dock activate it through shell:AppsFolder.
            return new PinnedAppSetting
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = app.Label,
                Aumid = app.Aumid
            };
        }

        var target = Array.Find(
            Targets, entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (target.Key is null)
        {
            return null;
        }

        return PinnedAppsService.CreatePin(
            Environment.ExpandEnvironmentVariables(target.Path), target.Label);
    }

    /// <summary>
    /// The presets a place dragged out of Explorer stands for, by the parsing name the drag
    /// carries — see <see cref="Interop.ShellIdList"/>.
    /// </summary>
    /// <remarks>
    /// Control Panel answers to three: the desktop shows the category view,
    /// <c>{26EE0668-…}</c>, under the desktop icon's own <c>{5399E694-…}</c>, and the all-items
    /// view is <c>{21EC2020-…}</c>. All three are the preset's <c>control.exe</c>, which opens
    /// whichever view was last used.
    /// </remarks>
    private static readonly (string ParsingName, string Key)[] DroppedPlaces =
    [
        ("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "thispc"),
        ("::{645FF040-5081-101B-9F08-00AA002F954E}", "recyclebin"),
        ("::{26EE0668-A00A-44D7-9371-BEB064C98683}", "control"),
        ("::{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", "control"),
        ("::{21EC2020-3AEA-1069-A2DD-08002B30309D}", "control")
    ];

    /// <summary>
    /// Builds the pin for a place dropped from Explorer, named as the shell parses it:
    /// <c>::{CLSID}</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A place the Add menu also offers becomes exactly the pin the menu would have made, not a
    /// second spelling of it. The target is what everything else keys on — a Recycle Bin
    /// dropped as <c>::{645FF040-…}</c> would get no Empty entry and no full-or-empty icon,
    /// would be matched by no icon set, and would pin a second time beside the preset.
    /// </para>
    /// <para>
    /// Any other place — Network, Home, Gallery — is pinned as <c>shell:::{CLSID}</c>, which
    /// <c>ShellExecute</c> opens and the icon and name lookups resolve, under the name Explorer
    /// gives it. One the shell cannot resolve by that name is turned away rather than pinned
    /// as a blank that does nothing.
    /// </para>
    /// </remarks>
    /// <returns>A pin, or <see langword="null"/> for a name that is not a place in the namespace.</returns>
    public static PinnedAppSetting? CreateFromShellName(string parsingName)
    {
        if (!Interop.ShellIdList.IsPlace(parsingName))
        {
            return null;
        }

        var known = Array.Find(
            DroppedPlaces,
            entry => entry.ParsingName.Equals(parsingName, StringComparison.OrdinalIgnoreCase));

        if (known.Key is not null)
        {
            return Create(known.Key);
        }

        // Asked of the bare name, not the shell: one that is stored. Under shell: a CLSID that
        // is registered nowhere still makes an item, named after its own CLSID; bare, it is
        // refused — measured with {00000000-…-00000000A7D0}.
        if (Interop.ShellNames.DisplayName(parsingName) is not { } label)
        {
            return null;
        }

        return new PinnedAppSetting
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = label,
            TargetPath = "shell:" + parsingName
        };
    }

    /// <summary>Builds a divider pin.</summary>
    public static PinnedAppSetting CreateSeparator() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Label = SeparatorLabel,
        IsSeparator = true
    };
}
