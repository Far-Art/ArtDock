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

    /// <summary>Dimmed and non-activating; kept so a pin can outlive an uninstall.</summary>
    public bool IsDisabled { get; set; }

    /// <summary>True while the app has at least one open window — drives the indicator dot.</summary>
    public bool IsRunning { get; set; }

    /// <summary>What the shell should resolve for the icon and for activation.</summary>
    public string ShellTarget =>
        Aumid is { Length: > 0 } aumid ? $@"shell:AppsFolder\{aumid}" : TargetPath ?? string.Empty;

    /// <summary>
    /// The image path a process must be running under for this item to count as open — for a
    /// folder, the folder a File Explorer window must be showing — or null for a pin that
    /// cannot be running at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not the same thing as what the item launches. A shortcut launches as itself — that is
    /// what carries its arguments and its icon — but the process it starts runs under the
    /// path the shortcut points at, so matching on the <c>.lnk</c> never lit the indicator
    /// and clicking a running app pinned that way started a second copy.
    /// </para>
    /// <para>
    /// Only an executable gets one, and a folder. A document or a web address has no process
    /// of its own: what opens a document is its editor, whose window belongs to whatever pin
    /// launches *that*. Asking the question at all would answer it eventually — the running
    /// census falls back to matching on file name — so the dot is ruled out here rather than
    /// left to the unlikeliness of a collision.
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
    /// </remarks>
    public string? RunningTarget =>
        ExplorerFolder
        ?? ((LinkTarget ?? TargetPath) is { Length: > 0 } target
            && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? target
                : null);

    /// <summary>True when clicking this item should do something.</summary>
    public bool IsLaunchable => !IsSeparator && !IsDisabled && ShellTarget.Length > 0;
}
