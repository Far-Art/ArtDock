using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace ArtDock.Interop;

/// <summary>The groups a jump list shows, as the taskbar heads them.</summary>
public enum JumpListGroup
{
    /// <summary>What the user pinned to the app's jump list.</summary>
    Pinned,

    /// <summary>A category of the app's own, under the name it gave it.</summary>
    Custom,

    /// <summary>What the app opened lately.</summary>
    Recent,

    /// <summary>What the app opens most.</summary>
    Frequent,

    /// <summary>The app's own commands: Chrome's <em>New Incognito window</em>.</summary>
    Tasks
}

/// <summary>One group of a jump list, in the order the taskbar shows them.</summary>
/// <param name="Group">Which kind of group it is.</param>
/// <param name="Name">Its name, for a <see cref="JumpListGroup.Custom"/> one: the app's own words.</param>
/// <param name="Entries">What it holds.</param>
public sealed record JumpListCategory(JumpListGroup Group, string? Name, IReadOnlyList<JumpListEntry> Entries);

/// <summary>One entry of a jump list: what to show, and how to start it again.</summary>
/// <remarks>
/// Kept as bytes rather than as the shell's objects, so nothing of COM outlives the reading: a
/// link as it saves itself, a shell item as its ID list.
/// </remarks>
public sealed record JumpListEntry
{
    /// <summary>The words to show, the app's own.</summary>
    public string Title { get; init; } = "";

    /// <summary>The icon the entry names, as <c>file,index</c>, or null for none.</summary>
    public string? IconLocation { get; init; }

    /// <summary>What it opens, by path or by the shell's name for it — for an icon, and to tell entries apart.</summary>
    public string? Path { get; init; }

    /// <summary>A line between tasks, which is not an entry at all.</summary>
    public bool IsSeparator { get; init; }

    /// <summary>The shortcut, as <c>IPersistStream</c> saved it.</summary>
    public byte[]? Link { get; init; }

    /// <summary>The shell item, as its ID list.</summary>
    public byte[]? IdList { get; init; }
}

/// <summary>
/// Reads an app's jump list — what the taskbar shows above its own entries when the app is
/// right-clicked — and starts its entries.
/// </summary>
/// <remarks>
/// <para>
/// Windows documents half of a jump list. <em>Recent</em> and <em>Frequent</em> are asked of
/// <c>IApplicationDocumentLists</c>. The app's own tasks and categories are not readable by any
/// documented means, so they are read from the file Windows writes them to,
/// <c>CustomDestinations\&lt;hash&gt;.customDestinations-ms</c>; and what the user pinned, from the
/// shell's record, <c>AutomaticDestinations\&lt;hash&gt;.automaticDestinations-ms</c>. The other
/// known route to the first, the undocumented <c>IInternalCustomDestinationList</c>, has changed
/// its interface id between releases of Windows.
/// </para>
/// <para>
/// Only the framing of either file is read here. Every entry in them is a shortcut as it saves
/// itself, and Windows' own shortcut object reads it back (<c>IPersistStream.Load</c>), so the
/// part that holds the app's words, icons and arguments is parsed by Windows. Read on 2026-11-20,
/// from the 47 custom files on this machine: a header of version 2, a count and a reserved word;
/// then each category — 0, a custom one, its name's length and its name in UTF-16, a count and
/// its entries; 1, a known one, its id (1 frequent, 2 recent, -1 none); 2, the tasks, a count and
/// its entries — and each closed by <c>0xBABFFBAB</c>. An entry is a class id, Windows' shortcut
/// in every file seen, and the object.
/// </para>
/// <para>
/// Undocumented, so it may change with any update of Windows. Whatever does not read as
/// expected gives no entries for that part — never a crash, never an entry that is not one.
/// </para>
/// </remarks>
public static class JumpLists
{
    /// <summary>The closing mark of each category in the custom file.</summary>
    public const uint CategoryFooter = 0xBABFFBAB;

    /// <summary>The custom file's only known version.</summary>
    private const uint CustomVersion = 2;

    /// <summary>The most entries a category is believed to hold before the count is taken as nonsense.</summary>
    private const uint MostEntries = 1000;

    /// <summary>The most categories a custom file is believed to hold.</summary>
    private const uint MostCategories = 100;

    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");

    /// <summary><c>PKEY_Title</c>: the words of a task.</summary>
    private static readonly AppIds.PropertyKey TitleKey = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);

    /// <summary>
    /// The words of a Store app's task, which it keeps beside the shortcut rather than as its
    /// title: Snipping Tool's <em>Take a new screenshot</em>, read 2026-11-20, where the title
    /// was empty. Not in the SDK's list of keys, so asked only when the title gives nothing.
    /// </summary>
    private static readonly AppIds.PropertyKey StoreTitleKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 27);

    /// <summary><c>PKEY_AppUserModel_IsDestListSeparator</c>: a line between tasks.</summary>
    private static readonly AppIds.PropertyKey SeparatorKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 6);

    /// <summary>
    /// Reads an app's jump list, its groups in the taskbar's order: Pinned, the app's own
    /// categories and Recent or Frequent where it put them, then Tasks. Groups with nothing in
    /// them are left out.
    /// </summary>
    /// <param name="appId">The app's AppUserModelID.</param>
    /// <param name="showRecent">
    /// False when Windows has been told not to show recently opened items: Recent and Frequent
    /// are left out then, and so are the app's own categories, which are of the same kind.
    /// </param>
    /// <param name="recentLimit">How many entries Recent or Frequent show at most.</param>
    public static IReadOnlyList<JumpListCategory> Read(string appId, bool showRecent, int recentLimit)
    {
        var categories = new List<JumpListCategory>();

        var pinned = Guard(() => ReadPinned(JumpListFiles.AutomaticPath(appId)), []);
        if (pinned.Count > 0)
        {
            categories.Add(new JumpListCategory(JumpListGroup.Pinned, null, pinned));
        }

        var custom = Guard(() => ReadCustom(JumpListFiles.CustomPath(appId)), null);

        // An app that wrote no list of its own gets the taskbar's default, Recent; one that did
        // gets only what it asked for.
        foreach (var category in custom ?? [new JumpListCategory(JumpListGroup.Recent, null, [])])
        {
            var entries = category.Group switch
            {
                JumpListGroup.Recent or JumpListGroup.Frequent when showRecent =>
                    Guard(() => ReadDocuments(appId, category.Group, recentLimit, pinned), []),
                JumpListGroup.Recent or JumpListGroup.Frequent => [],
                JumpListGroup.Custom when !showRecent => [],
                _ => category.Entries
            };

            if (entries.Any(entry => !entry.IsSeparator))
            {
                categories.Add(category with { Entries = Trim(entries) });
            }
        }

        return categories;
    }

    /// <summary>
    /// Whether Windows shows recently opened items in jump lists — <em>Show recently opened
    /// items in Start, Jump Lists and File Explorer</em> — and how many at most.
    /// </summary>
    public static (bool Show, int Limit) RecentSettings()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            var show = key?.GetValue("Start_TrackDocs") is not int track || track != 0;
            var limit = key?.GetValue("JumpListItems_Maximum") is int most && most > 0 ? most : 10;
            return (show, Math.Min(limit, 60));
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return (true, 10);
        }
    }

    /// <summary>Runs a reading, giving <paramref name="nothing"/> for any failure at all.</summary>
    /// <remarks>
    /// Broad on purpose: the formats are undocumented, and what is read is whatever any app on
    /// the machine wrote. No failure in it is the dock's to report, and none may cost the menu.
    /// </remarks>
    private static T Guard<T>(Func<T> read, T nothing)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return nothing;
        }
    }

    /// <summary>A group's entries without a separator at either end or two together.</summary>
    private static List<JumpListEntry> Trim(IReadOnlyList<JumpListEntry> entries)
    {
        var kept = new List<JumpListEntry>();
        foreach (var entry in entries)
        {
            if (entry.IsSeparator && (kept.Count == 0 || kept[^1].IsSeparator))
            {
                continue;
            }

            kept.Add(entry);
        }

        if (kept.Count > 0 && kept[^1].IsSeparator)
        {
            kept.RemoveAt(kept.Count - 1);
        }

        return kept;
    }

    // ---- the app's own: CustomDestinations ---------------------------------------------

    /// <summary>
    /// Reads one object out of a file: given its class id, the file's bytes and where the object
    /// starts, the entry it makes (null for one that is not to be shown) and how many bytes it
    /// took. A count of zero or less says it could not be read, and nothing after it can be.
    /// </summary>
    public delegate JumpListEntry? EntryReader(Guid classId, byte[] data, int offset, out int length);

    /// <summary>Reads the app's own file, or null when it has written none.</summary>
    private static IReadOnlyList<JumpListCategory>? ReadCustom(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return ParseCustom(ReadShared(path), ReadEntry);
    }

    /// <summary>
    /// The categories of a custom file, in its order. Recent and Frequent stand in their places
    /// with no entries, for the caller to fill.
    /// </summary>
    /// <remarks>
    /// Stops at the first thing that does not read as expected, keeping the categories before
    /// it: a later one cannot be found without knowing where this one ends.
    /// </remarks>
    public static IReadOnlyList<JumpListCategory> ParseCustom(byte[] data, EntryReader readEntry)
    {
        var categories = new List<JumpListCategory>();
        var at = 0;
        if (!TryUInt(data, ref at, out var version) || version != CustomVersion
            || !TryUInt(data, ref at, out var count) || count > MostCategories
            || !TryUInt(data, ref at, out _))
        {
            return categories;
        }

        for (var i = 0; i < count; i++)
        {
            if (!TryUInt(data, ref at, out var type))
            {
                break;
            }

            JumpListCategory? category;
            switch (type)
            {
                case 0:
                    if (!TryUShort(data, ref at, out var nameLength) || at + nameLength * 2 > data.Length)
                    {
                        return categories;
                    }

                    var name = Words(Encoding.Unicode.GetString(data, at, nameLength * 2));
                    at += nameLength * 2;
                    category = TryEntries(data, ref at, readEntry, out var custom)
                        ? new JumpListCategory(JumpListGroup.Custom, name, custom)
                        : null;
                    break;

                case 1:
                    if (!TryUInt(data, ref at, out var known))
                    {
                        return categories;
                    }

                    category = known switch
                    {
                        1 => new JumpListCategory(JumpListGroup.Frequent, null, []),
                        2 => new JumpListCategory(JumpListGroup.Recent, null, []),
                        _ => new JumpListCategory(JumpListGroup.Custom, null, [])
                    };
                    break;

                case 2:
                    category = TryEntries(data, ref at, readEntry, out var tasks)
                        ? new JumpListCategory(JumpListGroup.Tasks, null, tasks)
                        : null;
                    break;

                default:
                    return categories;
            }

            if (category is null || !TryUInt(data, ref at, out var footer) || footer != CategoryFooter)
            {
                return categories;
            }

            // A known category the file names as none holds nothing, and a custom one with no
            // name is not one; neither is shown.
            if (category.Group != JumpListGroup.Custom || category.Name is { Length: > 0 })
            {
                categories.Add(category);
            }
        }

        return categories;
    }

    private static bool TryEntries(byte[] data, ref int at, EntryReader readEntry, out List<JumpListEntry> entries)
    {
        entries = [];
        if (!TryUInt(data, ref at, out var count) || count > MostEntries)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (at + 16 > data.Length)
            {
                return false;
            }

            var classId = new Guid(data.AsSpan(at, 16));
            at += 16;
            var entry = readEntry(classId, data, at, out var length);
            if (length <= 0 || at + length > data.Length)
            {
                return false;
            }

            at += length;
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return true;
    }

    private static bool TryUInt(byte[] data, ref int at, out uint value)
    {
        value = 0;
        if (at < 0 || at + 4 > data.Length)
        {
            return false;
        }

        value = BitConverter.ToUInt32(data, at);
        at += 4;
        return true;
    }

    private static bool TryUShort(byte[] data, ref int at, out ushort value)
    {
        value = 0;
        if (at < 0 || at + 2 > data.Length)
        {
            return false;
        }

        value = BitConverter.ToUInt16(data, at);
        at += 2;
        return true;
    }

    /// <summary>
    /// Reads one entry with Windows' own shortcut object. Nothing else is created: a class id
    /// read from a file is not a thing to instantiate on trust, and none other has been seen.
    /// </summary>
    private static JumpListEntry? ReadEntry(Guid classId, byte[] data, int offset, out int length)
    {
        length = 0;
        if (classId != ShellLinkClass || offset < 0 || offset >= data.Length)
        {
            return null;
        }

        var stream = SHCreateMemStream(ref data[offset], (uint)(data.Length - offset));
        if (stream is null)
        {
            return null;
        }

        object? link = null;
        try
        {
            link = new ShellLinkCoClass();
            ((IPersistStream)link).Load(stream);

            var position = Marshal.AllocCoTaskMem(8);
            try
            {
                stream.Seek(0, 1 /* STREAM_SEEK_CUR */, position);
                length = (int)Marshal.ReadInt64(position);
            }
            finally
            {
                Marshal.FreeCoTaskMem(position);
            }

            return length > 0 ? FromLink(link, data.AsSpan(offset, length).ToArray()) : null;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException or IOException)
        {
            length = 0;
            return null;
        }
        finally
        {
            if (link is not null)
            {
                Marshal.ReleaseComObject(link);
            }

            Marshal.ReleaseComObject(stream);
        }
    }

    /// <summary>An entry made from a loaded shortcut, which it keeps as <paramref name="saved"/>.</summary>
    private static JumpListEntry? FromLink(object link, byte[] saved)
    {
        var shellLink = (IShellLinkW)link;
        if (link is AppIds.IPropertyStore store && AppIds.ReadFlag(store, SeparatorKey))
        {
            return new JumpListEntry { IsSeparator = true };
        }

        var path = new StringBuilder(1024);
        var found = new byte[592];
        shellLink.GetPath(path, path.Capacity, found, 0);

        var iconFile = new StringBuilder(1024);
        shellLink.GetIconLocation(iconFile, iconFile.Capacity, out var iconIndex);

        var titled = link as AppIds.IPropertyStore;
        var title = Words(titled is null ? null : AppIds.Read(titled, TitleKey))
            ?? Words(titled is null ? null : AppIds.Read(titled, StoreTitleKey));
        if (title is null)
        {
            var description = new StringBuilder(1024);
            shellLink.GetDescription(description, description.Capacity);
            title = Words(description.ToString());
        }

        title ??= TargetName(shellLink)
            ?? (path.Length > 0 ? System.IO.Path.GetFileNameWithoutExtension(path.ToString()) : null);

        // An entry with nothing to call it by, or only a reference the shell could not look up,
        // is not shown: a menu entry reading ms-resource:… is not the app's word for anything.
        if (title is not { Length: > 0 })
        {
            return null;
        }

        return new JumpListEntry
        {
            Title = title,
            Path = path.Length > 0 ? path.ToString() : null,
            IconLocation = iconFile.Length > 0 ? $"{iconFile},{iconIndex}" : null,
            Link = saved
        };
    }

    /// <summary>
    /// What a shortcut points at, as Explorer names it: <em>Downloads</em> for the folder, a
    /// file's name as Explorer shows it.
    /// </summary>
    private static string? TargetName(IShellLinkW link)
    {
        link.GetIDList(out var idList);
        if (idList == 0)
        {
            return null;
        }

        try
        {
            if (SHGetNameFromIDList(idList, SigdnNormalDisplay, out var name) != 0 || name == 0)
            {
                return null;
            }

            try
            {
                return Words(Marshal.PtrToStringUni(name));
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(idList);
        }
    }

    /// <summary>
    /// Words to show, from text an app gave: a reference to a resource looked up, and null for
    /// nothing, or for a reference that could not be looked up.
    /// </summary>
    private static string? Words(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = IndirectString(text);
        return text.StartsWith('@') || text.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
            ? null
            : text;
    }

    /// <summary>
    /// Text an app may give as a reference to a resource — <c>@%SystemRoot%\…\shell32.dll,-21815</c>
    /// — as the words it stands for; any other text as it is.
    /// </summary>
    private static string IndirectString(string text)
    {
        if (!text.StartsWith('@'))
        {
            return text;
        }

        var resolved = new StringBuilder(1024);
        return SHLoadIndirectString(text, resolved, (uint)resolved.Capacity, 0) == 0 && resolved.Length > 0
            ? resolved.ToString()
            : text;
    }

    /// <summary>A file's bytes, read without standing in the way of the shell writing it.</summary>
    private static byte[] ReadShared(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream();
        file.CopyTo(copy);
        return copy.ToArray();
    }

    // ---- what the user pinned: AutomaticDestinations ------------------------------------

    /// <summary>
    /// The pinned entries of the shell's record, in their pinned order.
    /// </summary>
    /// <remarks>
    /// The file is an OLE compound file: a <c>DestList</c> stream lists the entries, and each
    /// entry's shortcut is a stream of its own, named by the entry's number in hex. Read from a
    /// copy in memory, so the shell's own hold on the file does not matter.
    /// </remarks>
    private static IReadOnlyList<JumpListEntry> ReadPinned(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var data = ReadShared(path);
        if (data.Length == 0)
        {
            return [];
        }

        var memory = Marshal.AllocHGlobal(data.Length);
        ILockBytes? bytes = null;
        IStorage? storage = null;
        try
        {
            Marshal.Copy(data, 0, memory, data.Length);
            if (CreateILockBytesOnHGlobal(memory, false, out bytes) != 0
                || StgOpenStorageOnILockBytes(bytes, null, StgmRead | StgmShareExclusive, 0, 0, out storage) != 0)
            {
                return [];
            }

            var destList = ReadStream(storage, "DestList");
            if (destList is null)
            {
                return [];
            }

            var pinned = new List<JumpListEntry>();
            foreach (var entry in PinnedIn(destList))
            {
                if (ReadStream(storage, entry.ToString("x")) is { Length: > 0 } link
                    && ReadEntry(ShellLinkClass, link, 0, out _) is { IsSeparator: false } read)
                {
                    pinned.Add(read);
                }
            }

            return pinned;
        }
        finally
        {
            if (storage is not null)
            {
                Marshal.ReleaseComObject(storage);
            }

            if (bytes is not null)
            {
                Marshal.ReleaseComObject(bytes);
            }

            Marshal.FreeHGlobal(memory);
        }
    }

    /// <summary>
    /// The numbers of the pinned entries in a <c>DestList</c> stream, in their pinned order —
    /// or none, if the stream is not laid out as expected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A header of 32 bytes — the version, the number of entries, the number pinned — then the
    /// entries, each a fixed part and a path. Version 1 (Windows 7 and 8) has the entry's number
    /// at 88, its pin at 104 and the path's length at 108, the path after it; versions 3 and 4
    /// (Windows 10) have the number at 88, the pin at 108, the length at 128, and four bytes after
    /// the path. Windows 11 writes versions 5 and 6, laid out as 3 and 4 are: read here on
    /// 2026-11-20, every one of the 70 such streams on this machine walked to its last byte that
    /// way, and File Explorer's six pins came out in their places. A pin of -1 is not pinned; any
    /// other is its place among the pinned. A version not seen is refused rather than guessed at.
    /// </para>
    /// <para>
    /// Held to the header: every entry must fit and be counted, and as many must be pinned as the
    /// header says, or none is taken.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<uint> PinnedIn(byte[] destList)
    {
        if (destList.Length < 32)
        {
            return [];
        }

        var version = BitConverter.ToUInt32(destList, 0);
        var count = BitConverter.ToUInt32(destList, 4);
        var pinnedCount = BitConverter.ToUInt32(destList, 8);
        var (pinAt, lengthAt, trailer) = version switch
        {
            1 => (104, 108, 0),
            >= 3 and <= 6 => (108, 128, 4),
            _ => (-1, -1, -1)
        };

        if (pinAt < 0 || count > 100_000 || pinnedCount > count)
        {
            return [];
        }

        var pinned = new List<(uint Entry, int Place)>();
        var at = 32;
        for (var i = 0; i < count; i++)
        {
            if (at + lengthAt + 2 > destList.Length)
            {
                return [];
            }

            var entry = BitConverter.ToUInt32(destList, at + 88);
            var pin = BitConverter.ToInt32(destList, at + pinAt);
            var pathLength = BitConverter.ToUInt16(destList, at + lengthAt);
            at += lengthAt + 2 + pathLength * 2 + trailer;
            if (at > destList.Length)
            {
                return [];
            }

            if (pin >= 0)
            {
                pinned.Add((entry, pin));
            }
        }

        return pinned.Count == pinnedCount
            ? [.. pinned.OrderBy(pin => pin.Place).Select(pin => pin.Entry)]
            : [];
    }

    private static byte[]? ReadStream(IStorage storage, string name)
    {
        if (storage.OpenStream(name, 0, StgmRead | StgmShareExclusive, 0, out var stream) != 0 || stream is null)
        {
            return null;
        }

        try
        {
            stream.Stat(out var stat, 1 /* STATFLAG_NONAME */);
            if (stat.cbSize is <= 0 or > 16 * 1024 * 1024)
            {
                return null;
            }

            var buffer = new byte[stat.cbSize];
            var read = Marshal.AllocCoTaskMem(4);
            try
            {
                stream.Read(buffer, buffer.Length, read);
                return Marshal.ReadInt32(read) == buffer.Length ? buffer : null;
            }
            finally
            {
                Marshal.FreeCoTaskMem(read);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(stream);
        }
    }

    // ---- what the app opened: IApplicationDocumentLists ----------------------------------

    /// <summary>Recent or Frequent, asked of the shell, without what is pinned already.</summary>
    private static IReadOnlyList<JumpListEntry> ReadDocuments(
        string appId, JumpListGroup group, int limit, IReadOnlyList<JumpListEntry> pinned)
    {
        var lists = (IApplicationDocumentLists)new ApplicationDocumentListsCoClass();
        IObjectArray? array = null;
        try
        {
            lists.SetAppID(appId);

            // Asked for more than shown, since the pinned ones are taken out of it.
            var wanted = (uint)(limit + pinned.Count);
            if (lists.GetList(group == JumpListGroup.Frequent ? 1 : 0, wanted, typeof(IObjectArray).GUID, out array) != 0
                || array is null)
            {
                return [];
            }

            array.GetCount(out var count);
            var entries = new List<JumpListEntry>();
            for (uint i = 0; i < count && entries.Count < limit; i++)
            {
                array.GetAt(i, IUnknownId, out var item);
                try
                {
                    var entry = FromDocument(item);
                    if (entry is not null && !pinned.Any(pin => SamePath(pin.Path, entry.Path)))
                    {
                        entries.Add(entry);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }

            return entries;
        }
        finally
        {
            if (array is not null)
            {
                Marshal.ReleaseComObject(array);
            }

            Marshal.ReleaseComObject(lists);
        }
    }

    private static readonly Guid IUnknownId = new("00000000-0000-0000-C000-000000000046");

    private static bool SamePath(string? a, string? b) =>
        a is { Length: > 0 } && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>An entry made from what the shell's record gives: a shell item, or a shortcut.</summary>
    private static JumpListEntry? FromDocument(object item)
    {
        if (item is IShellItem shellItem)
        {
            var title = DisplayName(shellItem, SigdnNormalDisplay);
            var path = DisplayName(shellItem, SigdnFileSysPath) ?? DisplayName(shellItem, SigdnDesktopAbsoluteParsing);
            if (title is null || SHGetIDListFromObject(item, out var idList) != 0 || idList == 0)
            {
                return null;
            }

            try
            {
                var size = (int)ILGetSize(idList);
                var bytes = new byte[size];
                Marshal.Copy(idList, bytes, 0, size);
                return new JumpListEntry { Title = title, Path = path, IdList = bytes };
            }
            finally
            {
                Marshal.FreeCoTaskMem(idList);
            }
        }

        if (item is IPersistStream persist and IShellLinkW)
        {
            var stream = SHCreateEmptyMemStream(0, 0);
            if (stream is null)
            {
                return null;
            }

            try
            {
                persist.Save(stream, false);
                stream.Stat(out var stat, 1);
                stream.Seek(0, 0, 0);
                var bytes = new byte[stat.cbSize];
                stream.Read(bytes, bytes.Length, 0);
                return FromLink(item, bytes);
            }
            finally
            {
                Marshal.ReleaseComObject(stream);
            }
        }

        return null;
    }

    private static string? DisplayName(IShellItem item, uint form)
    {
        if (item.GetDisplayName(form, out var name) != 0 || name == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(name);
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
        }
    }

    private const uint SigdnNormalDisplay = 0;
    private const uint SigdnFileSysPath = 0x80058000;
    private const uint SigdnDesktopAbsoluteParsing = 0x80028000;

    // ---- starting an entry ----------------------------------------------------------------

    /// <summary>
    /// Starts an entry as the taskbar would: a shortcut by its own default command, so it keeps
    /// its arguments, its working folder and the app it names; a file or folder as Explorer
    /// opens it.
    /// </summary>
    /// <param name="entry">What to start.</param>
    /// <param name="owner">A window for anything the shell has to ask.</param>
    /// <returns>True when it was started.</returns>
    public static bool Launch(JumpListEntry entry, nint owner)
    {
        try
        {
            if (entry.Link is { Length: > 0 } link)
            {
                return LaunchLink(link, owner);
            }

            if (entry.IdList is { Length: > 0 } idList)
            {
                var pidl = Marshal.AllocCoTaskMem(idList.Length);
                try
                {
                    Marshal.Copy(idList, 0, pidl, idList.Length);
                    return Execute(pidl, null, null, owner);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException or IOException)
        {
        }

        return false;
    }

    /// <summary>
    /// A shortcut, by its own default command — what Explorer does with a shortcut it is asked to
    /// open — and failing that, what it points at with its arguments and folder.
    /// </summary>
    private static bool LaunchLink(byte[] saved, nint owner)
    {
        var stream = SHCreateMemStream(ref saved[0], (uint)saved.Length);
        if (stream is null)
        {
            return false;
        }

        object? link = null;
        var popup = (nint)0;
        try
        {
            link = new ShellLinkCoClass();
            ((IPersistStream)link).Load(stream);

            if (link is IContextMenu menu && (popup = CreatePopupMenu()) != 0
                && menu.QueryContextMenu(popup, 0, FirstCommand, LastCommand, CmfDefaultOnly) >= 0)
            {
                var command = GetMenuDefaultItem(popup, 0, 0);
                if (command != uint.MaxValue && command >= FirstCommand)
                {
                    var invoke = new CommandInvokeInfo
                    {
                        Size = Marshal.SizeOf<CommandInvokeInfo>(),
                        Window = owner,
                        Verb = (nint)(command - FirstCommand),
                        Show = SwShowNormal
                    };

                    if (menu.InvokeCommand(ref invoke) == 0)
                    {
                        return true;
                    }
                }
            }

            var shellLink = (IShellLinkW)link;
            shellLink.GetIDList(out var target);
            if (target == 0)
            {
                return false;
            }

            try
            {
                var arguments = new StringBuilder(4096);
                shellLink.GetArguments(arguments, arguments.Capacity);
                var folder = new StringBuilder(1024);
                shellLink.GetWorkingDirectory(folder, folder.Capacity);
                return Execute(
                    target,
                    arguments.Length > 0 ? arguments.ToString() : null,
                    folder.Length > 0 ? folder.ToString() : null,
                    owner);
            }
            finally
            {
                Marshal.FreeCoTaskMem(target);
            }
        }
        finally
        {
            if (popup != 0)
            {
                DestroyMenu(popup);
            }

            if (link is not null)
            {
                Marshal.ReleaseComObject(link);
            }

            Marshal.ReleaseComObject(stream);
        }
    }

    private static bool Execute(nint idList, string? arguments, string? folder, nint owner)
    {
        var info = new ShellExecuteInfo
        {
            Size = Marshal.SizeOf<ShellExecuteInfo>(),
            Mask = SeeMaskInvokeIdList,
            Window = owner,
            Parameters = arguments,
            Directory = folder,
            Show = SwShowNormal,
            IdList = idList
        };

        return ShellExecuteEx(ref info);
    }

    private const uint FirstCommand = 1;
    private const uint LastCommand = 0x7FFF;
    private const uint CmfDefaultOnly = 0x1;
    private const int SwShowNormal = 1;
    private const uint SeeMaskInvokeIdList = 0x0000000C;
    private const int StgmRead = 0x0;
    private const int StgmShareExclusive = 0x10;

    // ---- interop ----------------------------------------------------------------------------

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass
    {
    }

    [ComImport]
    [Guid("86bec222-30f2-47e0-9f25-60d11cd75c28")]
    private class ApplicationDocumentListsCoClass
    {
    }

    /// <summary><c>IShellLinkW</c>, every method declared, in vtable order.</summary>
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        // The find data as a plain buffer: an array in a COM method is a SAFEARRAY unless told
        // otherwise, and the shell writes the whole structure through what it is given.
        void GetPath(
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder file,
            int cch,
            [MarshalAs(UnmanagedType.LPArray), Out] byte[] findData,
            uint flags);

        void GetIDList(out nint idList);

        void SetIDList(nint idList);

        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);

        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int show);

        void SetShowCmd(int show);

        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int cch, out int index);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int index);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);

        void Resolve(nint window, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    /// <summary><c>IPersistStream</c>, with <c>IPersist</c>'s one method first.</summary>
    [ComImport]
    [Guid("00000109-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistStream
    {
        void GetClassID(out Guid classId);

        [PreserveSig]
        int IsDirty();

        void Load(IStream stream);

        void Save(IStream stream, [MarshalAs(UnmanagedType.Bool)] bool clearDirty);

        void GetSizeMax(out long size);
    }

    [ComImport]
    [Guid("000214e4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(nint menu, uint indexMenu, uint firstCommand, uint lastCommand, uint flags);

        [PreserveSig]
        int InvokeCommand(ref CommandInvokeInfo info);

        [PreserveSig]
        int GetCommandString(nuint command, uint type, nint reserved, nint name, int cch);
    }

    /// <summary><c>CMINVOKECOMMANDINFO</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CommandInvokeInfo
    {
        public int Size;
        public uint Mask;
        public nint Window;
        public nint Verb;
        public nint Parameters;
        public nint Directory;
        public int Show;
        public uint HotKey;
        public nint Icon;
    }

    /// <summary><c>SHELLEXECUTEINFOW</c>.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size;
        public uint Mask;
        public nint Window;
        public string? Verb;
        public string? File;
        public string? Parameters;
        public string? Directory;
        public int Show;
        public nint InstanceApp;
        public nint IdList;
        public string? Class;
        public nint ClassKey;
        public uint HotKey;
        public nint IconOrMonitor;
        public nint Process;
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint bindContext, in Guid handler, in Guid riid, out nint result);

        void GetParent(out IShellItem parent);

        [PreserveSig]
        int GetDisplayName(uint form, out nint name);

        void GetAttributes(uint mask, out uint attributes);

        void Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport]
    [Guid("3c594f9f-9f30-47a1-979a-c9e83d3d0a06")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationDocumentLists
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);

        [PreserveSig]
        int GetList(int listType, uint itemsDesired, in Guid riid, out IObjectArray? result);
    }

    [ComImport]
    [Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint count);

        void GetAt(uint index, in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object item);
    }

    [ComImport]
    [Guid("0000000a-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ILockBytes
    {
    }

    /// <summary><c>IStorage</c>, as far as <c>OpenStream</c>.</summary>
    [ComImport]
    [Guid("0000000b-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IStorage
    {
        [PreserveSig]
        int CreateStream(
            [MarshalAs(UnmanagedType.LPWStr)] string name, int mode, int reserved1, int reserved2, out IStream? stream);

        [PreserveSig]
        int OpenStream(
            [MarshalAs(UnmanagedType.LPWStr)] string name, nint reserved1, int mode, int reserved2, out IStream? stream);
    }

    [DllImport("shlwapi.dll")]
    private static extern IStream? SHCreateMemStream(ref byte init, uint size);

    [DllImport("shlwapi.dll", EntryPoint = "SHCreateMemStream")]
    private static extern IStream? SHCreateEmptyMemStream(nint init, uint size);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string source, StringBuilder output, uint cch, nint reserved);

    [DllImport("ole32.dll")]
    private static extern int CreateILockBytesOnHGlobal(nint global, [MarshalAs(UnmanagedType.Bool)] bool deleteOnRelease, out ILockBytes bytes);

    [DllImport("ole32.dll")]
    private static extern int StgOpenStorageOnILockBytes(
        ILockBytes bytes, IStorage? priority, int mode, nint exclude, int reserved, out IStorage storage);

    [DllImport("shell32.dll")]
    private static extern int SHGetIDListFromObject([MarshalAs(UnmanagedType.IUnknown)] object item, out nint idList);

    [DllImport("shell32.dll")]
    private static extern uint ILGetSize(nint idList);

    [DllImport("shell32.dll")]
    private static extern int SHGetNameFromIDList(nint idList, uint form, out nint name);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ShellExecuteExW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    private static extern uint GetMenuDefaultItem(nint menu, uint byPosition, uint flags);
}
