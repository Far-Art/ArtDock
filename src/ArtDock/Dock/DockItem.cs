using System.Windows.Media;

namespace ArtDock.Dock;

/// <summary>
/// One entry in the dock — a pinned application.
/// </summary>
/// <remarks>
/// Mirrors the web component's item model, with the browser's <c>routerLink</c> replaced by
/// what a desktop dock actually launches: a filesystem target, or an AUMID for Store apps,
/// which take a different activation path entirely.
/// </remarks>
public sealed class DockItem
{
    /// <summary>Stable identity, used for tracking and as the settings key.</summary>
    public required string Id { get; init; }

    /// <summary>Name shown in the tooltip and read out by screen readers.</summary>
    public required string Label { get; init; }

    /// <summary>
    /// What to launch: an <c>.exe</c>, a <c>.lnk</c>, or a folder. Null for Store apps, which
    /// carry an <see cref="Aumid"/> instead.
    /// </summary>
    public string? TargetPath { get; init; }

    /// <summary>Application User Model ID, for Store apps.</summary>
    public string? Aumid { get; init; }

    /// <summary>
    /// A custom icon chosen by the user, overriding the shell's. Null means the target's
    /// own icon is used.
    /// </summary>
    public string? IconPath { get; init; }

    /// <summary>
    /// For a pin that opens a picture: draw it with its file's icon rather than its thumbnail.
    /// Means nothing for anything else — see <c>PinnedAppsService.ThumbnailFor</c>.
    /// </summary>
    public bool UseIconNotThumbnail { get; init; }

    /// <summary>
    /// For a folder the dock draws rather than the shell: its colour, <c>#RRGGBB</c>. Null for
    /// the shell's folder icon. See <c>Services.FolderArt</c>.
    /// </summary>
    public string? FolderColor { get; init; }

    /// <summary>The symbol on such a folder, as a code point in hex; null for none.</summary>
    public string? FolderSymbol { get; init; }

    /// <summary>Up to six characters on such a folder in place of a symbol; null for none.</summary>
    public string? FolderText { get; init; }

    /// <summary>What the symbol or text is painted in: <c>White</c>, <c>Black</c>, or null for toned.</summary>
    public string? FolderSymbolTone { get; init; }

    /// <summary>
    /// Start the program as administrator when the item is clicked. Means nothing unless
    /// <see cref="CanRunAsAdministrator"/>.
    /// </summary>
    public bool RunAsAdministrator { get; init; }

    /// <summary>The resolved icon — custom if one is set, otherwise the shell's.</summary>
    public ImageSource? Icon { get; set; }

    /// <summary>
    /// Where a shortcut points, when <see cref="TargetPath"/> is one. Null for everything
    /// else, and for a shortcut the shell cannot resolve.
    /// </summary>
    public string? LinkTarget { get; set; }

    /// <summary>
    /// The folder this pin opens in File Explorer — a folder or a drive, This PC, the Recycle
    /// Bin, a shortcut to a folder — by the shell's own name for it, or null for a pin that
    /// opens no folder. Asked of the shell as the pin is read
    /// (<c>PinnedAppsService.ExplorerFolder</c>), since answering it is a call to the shell and
    /// <see cref="RunningTarget"/> is asked on every look at the pointer.
    /// </summary>
    public string? ExplorerFolder { get; init; }

    /// <summary>
    /// The program this pin's launcher starts — <c>Battle.net.exe</c> for
    /// <c>Battle.net Launcher.exe</c> beside it — which is what runs under the pin, or null.
    /// Found as the pin is read (<c>PinnedAppsService.LaunchedProgram</c>).
    /// </summary>
    public string? LaunchedProgram { get; init; }

    /// <summary>
    /// Windows' name for the app a shortcut starts, where the shortcut gives one
    /// (<c>Interop.ShellLink.AppId</c>): the app's windows carry it, whatever their program's path.
    /// </summary>
    public string? AppId { get; init; }

    /// <summary>
    /// For a pin that opens a document, which windows are its: those whose title names it, among
    /// the windows of the app that opens it. Read as the pin is read
    /// (<c>PinnedAppsService.DocumentTarget</c>), since it asks the shell; null for anything else.
    /// </summary>
    public RunningTarget? Document { get; init; }

    /// <summary>
    /// A spacer rather than an application: draws a divider and launches nothing.
    /// </summary>
    /// <remarks>
    /// Occupies a full slot rather than a narrow one. The layout gives every icon the same
    /// width by construction — that is what keeps an icon's position independent of its
    /// neighbours — so a narrower separator would mean threading per-item widths through
    /// <see cref="DockLayout"/> and giving up exactly the property that stops the row
    /// sliding under the pointer.
    /// </remarks>
    public bool IsSeparator { get; init; }

    /// <summary>
    /// The item at the end of a dock too full for its display, standing for the items that did
    /// not fit (<see cref="DockFit"/>): a click lists them. Never a pin, never saved, and never
    /// moved — nothing is dragged past it or onto it.
    /// </summary>
    public bool IsOverflow { get; init; }

    /// <summary>Dimmed and non-activating; kept so a pin can outlive an uninstall.</summary>
    public bool IsDisabled { get; set; }

    /// <summary>True while the app has at least one open window — drives the indicator dot.</summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// True while one of those windows is a program running as administrator — which colours
    /// the dot. Means nothing unless <see cref="IsRunning"/>.
    /// </summary>
    public bool IsElevated { get; set; }

    /// <summary>
    /// True while the app is closing: its windows have gone and its process has not, yet —
    /// which hollows the dot, and holds a click until it has. Never with <see cref="IsRunning"/>.
    /// </summary>
    public bool IsClosing { get; set; }

    /// <summary>What the shell should resolve for the icon and for activation.</summary>
    public string ShellTarget =>
        Aumid is { Length: > 0 } aumid ? $@"shell:AppsFolder\{aumid}" : TargetPath ?? string.Empty;

    /// <summary>
    /// Which windows are this item's, for it to count as open — or null for a pin that cannot
    /// be running at all: a web address, a command, a separator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not the same thing as what the item launches. A shortcut launches as itself — that is
    /// what carries its arguments and its icon — but the process it starts runs under the
    /// path the shortcut points at, so matching on the <c>.lnk</c> never lit the indicator
    /// and clicking a running app pinned that way started a second copy. Where the shortcut
    /// gives the app ID its windows carry (<see cref="AppId"/>), it is matched by that as well.
    /// </para>
    /// <para>
    /// A Store app is matched by its <see cref="Aumid"/> alone, which is what its windows carry
    /// (<see cref="Interop.AppIds"/>): its program is in a package, or is a host every such app
    /// shares, and its pin names no program at all. Until 2026-10-03 such a pin had no running
    /// target and could never light — Settings, and anything else pinned by its app ID.
    /// </para>
    /// <para>
    /// A document is lit by the windows showing it (<see cref="Document"/>), by their titles —
    /// not by every window of its app, or opening one picture would light every picture on the
    /// dock. Until 2026-10-03 a document was never lit at all, on the reasoning that it has no
    /// process of its own; but a folder has none either, and the user, opening a letter from the
    /// dock in Word, saw it as Word not being recognised as running.
    /// </para>
    /// <para>
    /// A folder — anything File Explorer opens, This PC and the Recycle Bin included — is
    /// matched not to the program that opens it but to the folder File Explorer's windows show,
    /// by the shell's own name for it (<see cref="ExplorerFolder"/>): every folder window is
    /// <c>explorer.exe</c>'s, whichever folder it shows, so a folder matched by its process
    /// would be lit by all of them, and every folder pin with it. The census asks Explorer which
    /// folders each window's tabs show — a tab behind another as much as the one in front, and a
    /// click brings that tab forward. So <c>shell:Downloads</c> and
    /// <c>C:\Users\name\Downloads</c> are lit by the same windows, and a window that goes to
    /// another folder takes its dot with it.
    /// </para>
    /// <para>
    /// A launcher named after the program it starts is matched to that program
    /// (<see cref="LaunchedProgram"/>): it has no window of its own to be matched by.
    /// </para>
    /// </remarks>
    public RunningTarget? RunningTarget => field ??= FindRunningTarget();

    private RunningTarget? FindRunningTarget()
    {
        if (ExplorerFolder is { } folder)
        {
            return new RunningTarget { Folder = folder };
        }

        if (Document is { } document)
        {
            return document;
        }

        if (Aumid is { Length: > 0 } aumid)
        {
            return new RunningTarget { AppId = aumid };
        }

        var program = LaunchedProgram
            ?? ((LinkTarget ?? TargetPath) is { Length: > 0 } target
                && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? target
                    : null);

        return program is null && AppId is null ? null : new RunningTarget { Program = program, AppId = AppId };
    }

    /// <summary>True when clicking this item should do something.</summary>
    public bool IsLaunchable => !IsSeparator && !IsDisabled && ShellTarget.Length > 0;

    /// <summary>
    /// Whether the item starts a program Windows can run as administrator — the ones Explorer
    /// offers <em>Run as administrator</em> for. Asked of the shell as the pin is read
    /// (<c>PinnedAppsService.CanRunAsAdministrator</c>), since the item's menu asks it on every
    /// right-click and a launch on every click.
    /// </summary>
    public bool CanRunAsAdministrator { get; init; }
}
