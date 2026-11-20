using System.IO;

namespace ArtDock.Dock;

/// <summary>
/// Which app an item's jump list is filed under: Windows' name for the app, its AppUserModelID.
/// </summary>
/// <remarks>
/// <para>
/// The taskbar files an app's jump list by that name, not by its program's path. A Store pin and
/// a Start menu shortcut give it outright; a running window carries one if its app set it; and a
/// program that set none has one Windows derives from its path — the path, with the known folder
/// it lies in written as that folder's id. Checked 2026-11-20 against the files Windows had made
/// here: <c>{6D809377-…}\Notepad++\notepad++.exe</c>, <c>{1AC14E77-…}\notepad.exe</c> and
/// <c>{F38BF404-…}\explorer.exe</c> all hash to a jump list on disk, and their plain paths to none.
/// </para>
/// <para>
/// The deepest folder wins, so a program in System32 is under System, not under Windows. Pure:
/// the folders are asked of the caller, so it can be tested without the machine's.
/// </para>
/// </remarks>
public static class JumpListApp
{
    /// <summary>
    /// The known folders a derived id is written against: where programs are kept.
    /// </summary>
    public static readonly IReadOnlyList<Guid> KnownFolders =
    [
        new("1AC14E77-02E7-4E5D-B744-2EB1AE5198B7"), // System
        new("D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27"), // SystemX86
        new("F38BF404-1D43-42F2-9305-67DE0B28FC23"), // Windows
        new("6D809377-6AF0-444B-8957-A3773F02200E"), // ProgramFilesX64
        new("7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E"), // ProgramFilesX86
        new("6365D5A7-0F0D-45E5-87F6-0DA56B6A4F7D"), // ProgramFilesCommonX64
        new("DE974D24-D9C6-4D3E-BF91-F4455120B917"), // ProgramFilesCommonX86
        new("5CD7AEE2-2219-4A67-B85D-6C9CE15660CB"), // UserProgramFiles
        new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091"), // LocalAppData
        new("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D"), // RoamingAppData
    ];

    /// <summary>
    /// The app id to read an item's jump list for, or null when there is nothing to go by.
    /// </summary>
    /// <param name="aumid">The Store app the item is pinned as, if it is one.</param>
    /// <param name="shortcutId">The id the item's shortcut gives what it starts.</param>
    /// <param name="windowId">The id one of the item's open windows carries.</param>
    /// <param name="program">The program the item starts, for a derived id.</param>
    /// <param name="knownFolder">Where a known folder is, or null where there is none.</param>
    public static string? For(
        string? aumid, string? shortcutId, string? windowId, string? program, Func<Guid, string?> knownFolder)
    {
        if (aumid is { Length: > 0 })
        {
            return aumid;
        }

        if (shortcutId is { Length: > 0 })
        {
            return shortcutId;
        }

        if (windowId is { Length: > 0 })
        {
            return windowId;
        }

        if (program is not { Length: > 0 } || !Path.IsPathFullyQualified(program))
        {
            return null;
        }

        // File Explorer's windows and pins are filed under a name of its own, not its program's.
        return Path.GetFileName(program).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
            ? FileExplorerId
            : Derived(program, knownFolder);
    }

    /// <summary>File Explorer's app id: what its jump list, the one with its pinned folders, is filed under.</summary>
    public const string FileExplorerId = "Microsoft.Windows.Explorer";

    /// <summary>
    /// The id Windows gives a program that names no app of its own: its path, with the deepest
    /// known folder it lies in written as that folder's id, and as it is when it lies in none.
    /// </summary>
    public static string Derived(string program, Func<Guid, string?> knownFolder)
    {
        var best = "";
        var bestId = Guid.Empty;
        foreach (var id in KnownFolders)
        {
            if (knownFolder(id) is not { Length: > 0 } folder)
            {
                continue;
            }

            folder = Path.TrimEndingDirectorySeparator(folder);
            if (folder.Length > best.Length
                && program.Length > folder.Length
                && program[folder.Length] == Path.DirectorySeparatorChar
                && program.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            {
                best = folder;
                bestId = id;
            }
        }

        return best.Length == 0 ? program : bestId.ToString("B").ToUpperInvariant() + program[best.Length..];
    }
}
