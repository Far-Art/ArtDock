using System.IO;
using System.Runtime.InteropServices;

namespace ArtDock.Interop;

/// <summary>
/// Tells the shell that the installed program has changed, so it draws the program's icon afresh
/// rather than from its cache.
/// </summary>
/// <remarks>
/// <para>
/// Found 2026-11-21, after 0.9.14 brought a new icon: the desktop and Start menu shortcuts still
/// showed the old one. Both had been rewritten by the update and pointed at the new
/// <c>current\ArtDock.exe</c>, whose icon was the new one, pixel for pixel — but the shell keeps an
/// icon by its file's path and index, the path had not changed, and nothing told it the file had.
/// Velopack replaces the files and says nothing to the shell.
/// </para>
/// <para>
/// So after an update each of the installation's programs is announced as changed, and then the
/// association change that has the shell drop the icons it holds — what <c>ie4uinit -show</c>
/// does by hand. Once per update, so its cost — every icon on the desktop drawn again — is
/// nothing. The last is flushed: Velopack ends the process as the hook returns, and a notice
/// still queued would go with it.
/// </para>
/// </remarks>
public static class IconRefresh
{
    private const int SHCNE_UPDATEITEM = 0x00002000;
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;
    private const uint SHCNF_PATHW = 0x0005;
    private const uint SHCNF_FLUSH = 0x1000;

    /// <summary>Announces the programs of the installation <paramref name="processPath"/> belongs to as changed, and their icons with them.</summary>
    public static void Announce(string? processPath)
    {
        try
        {
            foreach (var program in ProgramsOf(processPath))
            {
                SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, program, 0);
            }

            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST | SHCNF_FLUSH, null, 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // A courtesy to the shell: an update that cannot make it is still an update.
        }
    }

    /// <summary>
    /// The installation's programs, from the path of the one running: itself, and — when it is a
    /// Velopack installation's <c>current</c> folder — the launcher of the same name beside that
    /// folder, which the shortcuts Velopack makes may name. Only files that are there.
    /// </summary>
    public static IReadOnlyList<string> ProgramsOf(string? processPath)
    {
        if (processPath is not { Length: > 0 } || !File.Exists(processPath))
        {
            return [];
        }

        var programs = new List<string> { processPath };
        var folder = Path.GetDirectoryName(processPath);
        if (folder is not null
            && string.Equals(Path.GetFileName(folder), "current", StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(folder) is { } root)
        {
            var launcher = Path.Combine(root, Path.GetFileName(processPath));
            if (File.Exists(launcher))
            {
                programs.Add(launcher);
            }
        }

        return programs;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string? item1, nint item2);
}
