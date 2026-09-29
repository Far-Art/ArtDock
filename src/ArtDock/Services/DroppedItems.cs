using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using ArtDock.Interop;

namespace ArtDock.Services;

/// <summary>
/// What a drag onto the dock holds that could be pinned, and how the dock answers it.
/// </summary>
/// <remarks>
/// Apart from the window so that it can be tested with a <see cref="DataObject"/> built by
/// hand, which is the only kind of drag a test can hold.
/// </remarks>
public static class DroppedItems
{
    /// <summary>
    /// Identifies a drag by its contents, for deciding whether to resolve it again — or null
    /// for a drag that holds neither paths nor shell items.
    /// </summary>
    /// <remarks>
    /// Asked on every mouse move, so the ID list is hashed rather than read: reading it means
    /// a round of shell calls per item.
    /// </remarks>
    public static string? Signature(IDataObject data)
    {
        var paths = FilePaths(data);
        var ids = IdList(data);
        if (paths is null && ids is null)
        {
            return null;
        }

        var hash = new HashCode();
        hash.AddBytes(ids ?? []);
        return $"{string.Join('\n', paths ?? [])}\n{ids?.Length}:{hash.ToHashCode():X8}";
    }

    /// <summary>
    /// What the drag holds, in the order it holds it: a path for each file or folder, and the
    /// shell's parsing name — <c>::{CLSID}</c> — for each place that has no path. Null when it
    /// holds neither.
    /// </summary>
    /// <remarks>
    /// Paths are taken from the ID list only when it holds a place, which is exactly when
    /// Explorer leaves them off the drag. Otherwise a drag of files is read from
    /// <c>CF_HDROP</c>, as it always was, and one from a program that offers nothing else —
    /// most that are not Explorer — is read the same way.
    /// </remarks>
    public static IReadOnlyList<string>? Targets(IDataObject data)
    {
        if (IdList(data) is { } ids)
        {
            var items = ShellIdList.Read(ids);
            if (items.Any(item => item.Path is null && ShellIdList.IsPlace(item.ParsingName)))
            {
                return
                [
                    .. items
                        .Select(item => item.Path ?? (ShellIdList.IsPlace(item.ParsingName) ? item.ParsingName : null))
                        .OfType<string>()
                ];
            }
        }

        return FilePaths(data);
    }

    /// <summary>Builds the pin for one of <see cref="Targets"/>' entries.</summary>
    /// <returns>A pin, or <see langword="null"/> when there is nothing there to pin.</returns>
    public static PinnedAppSetting? Pin(string target) =>
        ShellIdList.IsPlace(target)
            ? DockPresets.CreateFromShellName(target)
            : PinnedAppsService.CreatePin(target);

    /// <summary>
    /// The effect a drop that pins reports: a copy where the source offers one, which is what
    /// it has always been for files, and a link where it does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The places on the desktop offer nothing but a link — This PC, the Recycle Bin, Control
    /// Panel, Network, Home and Gallery have <c>SFGAO_CANLINK</c> and neither of the others —
    /// and an answer the source did not offer is taken as none: no drop, and the no-entry
    /// cursor, even over a dock that would have pinned it.
    /// </para>
    /// <para>
    /// Never a move, which is the source being told to delete what was dragged.
    /// </para>
    /// </remarks>
    public static DragDropEffects Effect(DragDropEffects allowed) =>
        allowed.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy
        : allowed.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link
        : DragDropEffects.None;

    /// <summary>The drag's <c>CF_HDROP</c> paths, or null when it has none.</summary>
    private static string[]? FilePaths(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths
            : null;

    /// <summary>The drag's <see cref="ShellIdList.Format"/> block, or null when it has none.</summary>
    private static byte[]? IdList(IDataObject data)
    {
        try
        {
            return data.GetDataPresent(ShellIdList.Format)
                && data.GetData(ShellIdList.Format) is MemoryStream stream
                    ? stream.ToArray()
                    : null;
        }
        catch (COMException)
        {
            // The source offered the format and then would not render it. Read as a drag
            // without it, which leaves a drag of files exactly as it was.
            return null;
        }
    }
}
