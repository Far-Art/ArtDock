using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using ArtDock.Dock;
using ArtDock.Interop;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers reading an app's jump list: which app an item is, the files' framing, a list written
/// through Windows' own documented writer and read back, and what <em>End task</em> spares.
/// </summary>
public class JumpListTests
{
    private static void OnStaThread(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    // ---- which app an item is ---------------------------------------------------------------

    private static readonly Dictionary<Guid, string> Folders = new()
    {
        [new Guid("1AC14E77-02E7-4E5D-B744-2EB1AE5198B7")] = @"C:\Windows\System32",
        [new Guid("F38BF404-1D43-42F2-9305-67DE0B28FC23")] = @"C:\Windows",
        [new Guid("6D809377-6AF0-444B-8957-A3773F02200E")] = @"C:\Program Files",
        [new Guid("7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E")] = @"C:\Program Files (x86)",
        [new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091")] = @"C:\Users\someone\AppData\Local",
        [new Guid("5CD7AEE2-2219-4A67-B85D-6C9CE15660CB")] = @"C:\Users\someone\AppData\Local\Programs",
    };

    private static string? Folder(Guid id) => Folders.GetValueOrDefault(id);

    [Theory]
    [InlineData(@"C:\Program Files\Notepad++\notepad++.exe", @"{6D809377-6AF0-444B-8957-A3773F02200E}\Notepad++\notepad++.exe")]
    [InlineData(@"C:\Program Files (x86)\Some\app.exe", @"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Some\app.exe")]
    [InlineData(@"C:\Windows\System32\notepad.exe", @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\notepad.exe")]
    [InlineData(@"C:\Windows\regedit.exe", @"{F38BF404-1D43-42F2-9305-67DE0B28FC23}\regedit.exe")]
    [InlineData(@"C:\Users\someone\AppData\Local\Programs\Tool\tool.exe", @"{5CD7AEE2-2219-4A67-B85D-6C9CE15660CB}\Tool\tool.exe")]
    [InlineData(@"D:\Games\game.exe", @"D:\Games\game.exe")]
    [InlineData(@"C:\Program Files Extra\app.exe", @"C:\Program Files Extra\app.exe")]
    public void DerivedId_WritesTheDeepestKnownFolderAsItsId(string program, string expected) =>
        Assert.Equal(expected, JumpListApp.Derived(program, Folder));

    [Fact]
    public void DerivedId_HashesToTheFileWindowsNamesForIt() =>
        // Read off this machine's own files on 2026-11-20: Notepad++'s record of what it opened.
        Assert.Equal(
            "baacb5294867b833",
            JumpListFiles.Id(JumpListApp.Derived(@"C:\Program Files\Notepad++\notepad++.exe", Folder)));

    [Fact]
    public void AppId_PrefersTheStoreAppThenTheShortcutThenTheWindow()
    {
        const string program = @"C:\Program Files\App\app.exe";
        Assert.Equal("Store!App", JumpListApp.For("Store!App", "Shortcut", "Window", program, Folder));
        Assert.Equal("Shortcut", JumpListApp.For(null, "Shortcut", "Window", program, Folder));
        Assert.Equal("Window", JumpListApp.For(null, null, "Window", program, Folder));
        Assert.Equal(@"{6D809377-6AF0-444B-8957-A3773F02200E}\App\app.exe", JumpListApp.For(null, null, null, program, Folder));
    }

    [Fact]
    public void AppId_IsNoneWithoutAProgram()
    {
        Assert.Null(JumpListApp.For(null, null, null, null, Folder));
        Assert.Null(JumpListApp.For(null, "", "", "https://example.com", Folder));
    }

    [Fact]
    public void AppId_OfFileExplorer_IsItsOwnName() =>
        Assert.Equal(JumpListApp.FileExplorerId, JumpListApp.For(null, null, null, @"C:\Windows\explorer.exe", Folder));

    // ---- the custom file's framing ------------------------------------------------------------

    /// <summary>A stand-in for Windows' shortcut: every entry is four bytes, its words those bytes as a number.</summary>
    private static JumpListEntry? FourBytes(Guid classId, byte[] data, int offset, out int length)
    {
        if (offset + 4 > data.Length)
        {
            length = 0;
            return null;
        }

        length = 4;
        var value = BitConverter.ToInt32(data, offset);
        return value == 0 ? new JumpListEntry { IsSeparator = true } : new JumpListEntry { Title = value.ToString() };
    }

    private static byte[] CustomFile(Action<BinaryWriter> categories, uint count)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        writer.Write(2u);
        writer.Write(count);
        writer.Write(0u);
        categories(writer);
        writer.Flush();
        return memory.ToArray();
    }

    private static void Entries(BinaryWriter writer, params int[] values)
    {
        writer.Write((uint)values.Length);
        foreach (var value in values)
        {
            writer.Write(Guid.Empty.ToByteArray());
            writer.Write(value);
        }
    }

    private static readonly byte[] Sample = CustomFile(writer =>
    {
        writer.Write(0u);
        writer.Write((ushort)5);
        writer.Write(System.Text.Encoding.Unicode.GetBytes("Group"));
        Entries(writer, 11, 12);
        writer.Write(JumpLists.CategoryFooter);

        writer.Write(1u);
        writer.Write(2u);
        writer.Write(JumpLists.CategoryFooter);

        writer.Write(2u);
        Entries(writer, 21, 0, 22);
        writer.Write(JumpLists.CategoryFooter);
    }, 3);

    [Fact]
    public void CustomFile_ReadsEachCategoryInItsOrder()
    {
        var categories = JumpLists.ParseCustom(Sample, FourBytes);

        Assert.Equal([JumpListGroup.Custom, JumpListGroup.Recent, JumpListGroup.Tasks], categories.Select(c => c.Group));
        Assert.Equal("Group", categories[0].Name);
        Assert.Equal(["11", "12"], categories[0].Entries.Select(e => e.Title));
        Assert.Empty(categories[1].Entries);
        Assert.Equal(3, categories[2].Entries.Count);
        Assert.True(categories[2].Entries[1].IsSeparator);
    }

    [Fact]
    public void CustomFile_CutShortAnywhere_KeepsWhatCameBeforeAndNeverThrows()
    {
        for (var length = 0; length < Sample.Length; length++)
        {
            var categories = JumpLists.ParseCustom(Sample[..length], FourBytes);
            Assert.True(categories.Count <= 3);
        }

        // Cut inside the tasks, the two categories before them are whole.
        Assert.Equal(2, JumpLists.ParseCustom(Sample[..^6], FourBytes).Count);
    }

    [Fact]
    public void CustomFile_WithAWrongFooter_StopsThere()
    {
        var damaged = (byte[])Sample.Clone();
        var footer = BitConverter.GetBytes(JumpLists.CategoryFooter);
        var first = damaged.AsSpan().IndexOf(footer);
        damaged[first] ^= 0xFF;

        Assert.Empty(JumpLists.ParseCustom(damaged, FourBytes));
    }

    [Fact]
    public void CustomFile_OfAnotherVersionOrUnknownKind_GivesNothingPastIt()
    {
        var version = (byte[])Sample.Clone();
        version[0] = 3;
        Assert.Empty(JumpLists.ParseCustom(version, FourBytes));

        var kind = CustomFile(writer => writer.Write(7u), 1);
        Assert.Empty(JumpLists.ParseCustom(kind, FourBytes));
    }

    [Fact]
    public void CustomFile_OfRandomBytes_NeverThrows()
    {
        var random = new Random(20261120);
        for (var i = 0; i < 2000; i++)
        {
            var bytes = new byte[random.Next(0, 200)];
            random.NextBytes(bytes);
            if (bytes.Length >= 4 && random.Next(2) == 0)
            {
                bytes[0] = 2;
                bytes[1] = bytes[2] = bytes[3] = 0;
            }

            JumpLists.ParseCustom(bytes, FourBytes);
        }
    }

    // ---- what the user pinned ---------------------------------------------------------------

    /// <summary>A <c>DestList</c> stream, laid out as Windows 11's version 6: the entries and their pins.</summary>
    private static byte[] DestList(uint version, uint pinnedCount, params (uint Entry, int Pin, string Path)[] entries)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        writer.Write(version);
        writer.Write((uint)entries.Length);
        writer.Write(pinnedCount);
        writer.Write(new byte[20]);
        foreach (var (entry, pin, path) in entries)
        {
            var fixedPart = new byte[130];
            BitConverter.GetBytes(entry).CopyTo(fixedPart, 88);
            BitConverter.GetBytes(pin).CopyTo(fixedPart, 108);
            BitConverter.GetBytes((ushort)path.Length).CopyTo(fixedPart, 128);
            writer.Write(fixedPart);
            writer.Write(System.Text.Encoding.Unicode.GetBytes(path));
            writer.Write(0u);
        }

        writer.Flush();
        return memory.ToArray();
    }

    [Theory]
    [InlineData(3u)]
    [InlineData(4u)]
    [InlineData(5u)]
    [InlineData(6u)]
    public void Pins_AreReadInTheirPinnedOrder(uint version)
    {
        var list = DestList(version, 2, (0x1, 1, @"C:\a"), (0x2, -1, @"C:\bb"), (0x1f, 0, @"C:\ccc"));
        Assert.Equal([0x1fu, 0x1u], JumpLists.PinnedIn(list));
    }

    [Fact]
    public void Pins_OfAnUnknownVersion_AreNotGuessedAt() =>
        Assert.Empty(JumpLists.PinnedIn(DestList(9, 1, (1, 0, "x"))));

    [Fact]
    public void Pins_ThatDisagreeWithTheHeader_AreNotTaken() =>
        Assert.Empty(JumpLists.PinnedIn(DestList(6, 2, (1, 0, "x"), (2, -1, "y"))));

    [Fact]
    public void Pins_OfAStreamCutShort_AreNotTaken()
    {
        var list = DestList(6, 1, (1, 0, "x"), (2, -1, "yyyy"));
        for (var length = 0; length < list.Length - 4; length++)
        {
            Assert.Empty(JumpLists.PinnedIn(list[..length]));
        }
    }

    // ---- a list written by Windows' own writer, read back --------------------------------

    /// <summary>
    /// Writes a jump list for an id only this test uses, with Windows' documented
    /// <c>ICustomDestinationList</c>, reads it back, and deletes it — so nothing of it is left in
    /// the profile, whichever way the test ends.
    /// </summary>
    [Fact]
    public void AListWindowsWrote_ReadsBackAsWritten() => OnStaThread(() =>
    {
        var appId = "ArtDock.Tests.JumpList." + Guid.NewGuid().ToString("N")[..12];
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");

        var list = (ICustomDestinationList)new DestinationListCoClass();
        try
        {
            list.SetAppID(appId);
            list.BeginList(out _, typeof(IObjectArray).GUID, out var removed);
            Marshal.ReleaseComObject(removed);

            var group = (IObjectCollection)new EnumerableObjectCollectionCoClass();
            group.AddObject(Link(notepad, "/one", "First of the group"));
            group.AddObject(Link(notepad, "/two", "Second_of the group"));
            list.AppendCategory("Test group", (IObjectArray)group);

            var tasks = (IObjectCollection)new EnumerableObjectCollectionCoClass();
            tasks.AddObject(Link(notepad, "/task", "A task"));
            tasks.AddObject(Separator());
            tasks.AddObject(Link(notepad, "/other", "Another task"));
            list.AddUserTasks((IObjectArray)tasks);
            list.CommitList();

            Assert.True(File.Exists(JumpListFiles.CustomPath(appId)), "Windows wrote no file where it was looked for");

            var read = JumpLists.Read(appId, showRecent: true, recentLimit: 10);

            Assert.Equal([JumpListGroup.Custom, JumpListGroup.Tasks], read.Select(category => category.Group));
            Assert.Equal("Test group", read[0].Name);
            Assert.Equal(["First of the group", "Second_of the group"], read[0].Entries.Select(entry => entry.Title));
            Assert.Equal(["A task", "", "Another task"], read[1].Entries.Select(entry => entry.Title));
            Assert.True(read[1].Entries[1].IsSeparator);
            Assert.All(read[1].Entries.Where(entry => !entry.IsSeparator), entry =>
            {
                Assert.Equal(notepad, entry.Path, ignoreCase: true);
                Assert.NotNull(entry.Link);
            });

            // Recent is shown only where the app asks for it, and its own groups go with it when
            // Windows is told not to show what was opened.
            Assert.Equal([JumpListGroup.Tasks], JumpLists.Read(appId, showRecent: false, recentLimit: 10).Select(c => c.Group));
        }
        finally
        {
            list.DeleteList(appId);
            Marshal.ReleaseComObject(list);
        }

        Assert.False(File.Exists(JumpListFiles.CustomPath(appId)), "the test's list was left behind");
    });

    [Fact]
    public void AnAppWithNoList_HasNothing() => OnStaThread(() =>
        Assert.Empty(JumpLists.Read("ArtDock.Tests.NoSuchApp." + Guid.NewGuid().ToString("N"), true, 10)));

    private static object Link(string path, string arguments, string title)
    {
        var link = new ShellLinkCoClass();
        var shellLink = (IShellLinkW)link;
        shellLink.SetPath(path);
        shellLink.SetArguments(arguments);
        SetString((IPropertyStore)link, TitleKey, title);
        return link;
    }

    private static object Separator()
    {
        var link = new ShellLinkCoClass();
        var store = (IPropertyStore)link;
        var key = SeparatorKey;
        var value = new PropVariant { Type = 11, Value = -1 };
        store.SetValue(ref key, ref value);
        store.Commit();
        return link;
    }

    private static void SetString(IPropertyStore store, PropertyKey key, string text)
    {
        var value = new PropVariant { Type = 31, Value = Marshal.StringToCoTaskMemUni(text) };
        try
        {
            store.SetValue(ref key, ref value);
            store.Commit();
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.Value);
        }
    }

    private static readonly PropertyKey TitleKey = new() { Format = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), Id = 2 };
    private static readonly PropertyKey SeparatorKey = new() { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 6 };

    [ComImport, Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6")]
    private class DestinationListCoClass;

    [ComImport, Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a")]
    private class EnumerableObjectCollectionCoClass;

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass;

    [ComImport, Guid("6332debf-87b5-4670-90c0-5e57b408a49e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);

        void BeginList(out uint minSlots, in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object removed);

        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, IObjectArray items);

        void AppendKnownCategory(int category);

        void AddUserTasks(IObjectArray tasks);

        void CommitList();

        void GetRemovedDestinations(in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object removed);

        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string appId);

        void AbortList();
    }

    [ComImport, Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint count);

        void GetAt(uint index, in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object item);
    }

    [ComImport, Guid("5632b1a4-e38a-400a-928a-d4cd63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        void GetCount(out uint count);

        void GetAt(uint index, in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object item);

        void AddObject([MarshalAs(UnmanagedType.IUnknown)] object item);

        void AddFromArray(IObjectArray items);

        void RemoveObjectAt(uint index);

        void Clear();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(nint file, int cch, nint findData, uint flags);

        void GetIDList(out nint idList);

        void SetIDList(nint idList);

        void GetDescription(nint name, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory(nint dir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);

        void GetArguments(nint args, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int show);

        void SetShowCmd(int show);

        void GetIconLocation(nint iconPath, int cch, out int index);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int index);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);

        void Resolve(nint window, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort Type;

        [FieldOffset(8)]
        public nint Value;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);

        void GetAt(uint index, out PropertyKey key);

        void GetValue(ref PropertyKey key, out PropVariant value);

        void SetValue(ref PropertyKey key, ref PropVariant value);

        void Commit();
    }
}

/// <summary>What <em>End task</em> never ends.</summary>
public class EndTaskTests
{
    [Theory]
    [InlineData(@"C:\Windows\explorer.exe")]
    [InlineData(@"C:\WINDOWS\system32\ApplicationFrameHost.exe")]
    [InlineData(@"C:\Windows\System32\dwm.exe")]
    [InlineData(@"C:\Windows\SystemApps\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\TextInputHost.exe")]
    [InlineData(null)]
    [InlineData("")]
    public void TheShellsOwnProcesses_AreSpared(string? program) =>
        Assert.True(EndTask.IsSpared(program, 1234, 99));

    [Fact]
    public void TheDockItself_IsSpared() =>
        Assert.True(EndTask.IsSpared(@"C:\Apps\ArtDock.exe", 99, 99));

    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe")]
    [InlineData(@"C:\Windows\System32\notepad.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\Claude_6.6395.1.0_x64__pzs8sxrjxfjjc\app\Claude.exe")]
    public void AnApp_IsNot(string program) =>
        Assert.False(EndTask.IsSpared(program, 1234, 99));
}
