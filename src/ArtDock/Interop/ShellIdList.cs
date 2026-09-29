using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ArtDock.Interop;

/// <summary>
/// Reads the <c>Shell IDList Array</c> Explorer puts on every drag: the shell's own account of
/// what is being dragged, which covers the things a list of paths cannot.
/// </summary>
/// <remarks>
/// <para>
/// This PC, the Recycle Bin and Control Panel are namespace extensions with no file behind
/// them, so a drag of one carries no <c>CF_HDROP</c> — measured on this machine, by asking the
/// desktop folder for the data object Explorer would drag: those three, and Network, Home,
/// Gallery and Libraries, offer this format and <c>Preferred DropEffect</c>, and nothing
/// else. A selection mixing them with ordinary files carries no paths at all, not even the
/// files'.
/// </para>
/// <para>
/// The format is a <c>CIDA</c>: a count, then one offset more than the count, each from the
/// start of the block. The first offset is the parent folder's absolute ID list; the rest are
/// the items, relative to it.
/// </para>
/// </remarks>
public static class ShellIdList
{
    /// <summary>The clipboard format's registered name, <c>CFSTR_SHELLIDLIST</c>.</summary>
    public const string Format = "Shell IDList Array";

    /// <summary>SIGDN_FILESYSPATH: a path on disk, which only a filesystem item has.</summary>
    private const uint SigdnFileSysPath = 0x80058000;

    /// <summary>
    /// SIGDN_DESKTOPABSOLUTEPARSING: the name the shell parses back to the same item —
    /// <c>::{20D04FE0-3AEA-1069-A2D8-08002B30309D}</c> for This PC.
    /// </summary>
    private const uint SigdnDesktopAbsoluteParsing = 0x80028000;

    /// <summary>One dragged item: its path if it is on disk, and its name in the shell.</summary>
    /// <param name="Path">The item's path, or null for anything that is not a file or folder.</param>
    /// <param name="ParsingName">What the shell parses back to the item, or null if it will not say.</param>
    public readonly record struct Item(string? Path, string? ParsingName);

    /// <summary>
    /// Whether a parsing name is a place rooted in the shell's namespace, by a CLSID, rather
    /// than something inside a file — an entry in a zip is named after the zip's path.
    /// </summary>
    public static bool IsPlace(string? parsingName) =>
        parsingName is not null && parsingName.StartsWith("::{", StringComparison.Ordinal);

    /// <summary>
    /// The items in a <c>CIDA</c> block, in the order they were dragged.
    /// </summary>
    /// <remarks>
    /// Every offset and every ID list is walked against the block's length before the shell
    /// is shown any of it. The block comes from whatever process started the drag, and an ID
    /// list that runs off the end would take the dock down on a mouse move.
    /// </remarks>
    /// <returns>The items, or an empty list for a block that is not a well-formed <c>CIDA</c>.</returns>
    public static IReadOnlyList<Item> Read(byte[] cida)
    {
        if (cida.Length < sizeof(uint))
        {
            return [];
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(cida);

        // Compared as a long, so a count near uint.MaxValue cannot wrap the arithmetic back
        // under the length.
        if (count == 0 || sizeof(uint) * (count + 2L) > cida.Length)
        {
            return [];
        }

        var offsets = new int[count + 1];
        for (var i = 0; i < offsets.Length; i++)
        {
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(cida.AsSpan(sizeof(uint) * (i + 1)));
            if (!IsIdList(cida, offset))
            {
                return [];
            }

            offsets[i] = (int)offset;
        }

        var block = Marshal.AllocHGlobal(cida.Length);
        try
        {
            Marshal.Copy(cida, 0, block, cida.Length);

            var items = new List<Item>((int)count);
            for (var i = 1; i < offsets.Length; i++)
            {
                var absolute = ILCombine(block + offsets[0], block + offsets[i]);
                if (absolute == 0)
                {
                    continue;
                }

                try
                {
                    items.Add(new Item(
                        NameOf(absolute, SigdnFileSysPath),
                        NameOf(absolute, SigdnDesktopAbsoluteParsing)));
                }
                finally
                {
                    ILFree(absolute);
                }
            }

            return items;
        }
        finally
        {
            Marshal.FreeHGlobal(block);
        }
    }

    /// <summary>
    /// Whether an ID list starts at <paramref name="offset"/> and ends inside the block: a run
    /// of items, each led by its own size, closed by a size of zero.
    /// </summary>
    private static bool IsIdList(byte[] block, uint offset)
    {
        var at = (long)offset;
        while (at + sizeof(ushort) <= block.Length)
        {
            var size = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan((int)at));
            if (size == 0)
            {
                return true;
            }

            // An item's size counts its own two bytes, so anything smaller would never advance.
            if (size < sizeof(ushort))
            {
                return false;
            }

            at += size;
        }

        return false;
    }

    /// <summary>One of the item's names, or null when it has no name of that kind.</summary>
    private static string? NameOf(nint pidl, uint sigdn)
    {
        if (SHGetNameFromIDList(pidl, sigdn, out var name) != 0 || name == 0)
        {
            return null;
        }

        try
        {
            var text = Marshal.PtrToStringUni(name);
            return string.IsNullOrEmpty(text) ? null : text;
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
        }
    }

    [DllImport("shell32.dll")]
    private static extern nint ILCombine(nint pidl1, nint pidl2);

    [DllImport("shell32.dll")]
    private static extern void ILFree(nint pidl);

    [DllImport("shell32.dll")]
    private static extern int SHGetNameFromIDList(nint pidl, uint sigdnName, out nint ppszName);
}
