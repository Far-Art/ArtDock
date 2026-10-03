using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using ArtDock.Interop;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers dropping places that have no file behind them — This PC, the Recycle Bin, Control
/// Panel — which Explorer drags as a <c>Shell IDList Array</c> and nothing else.
/// </summary>
/// <remarks>
/// The blocks are built from ID lists the shell itself parses, laid out as Explorer lays out
/// a <c>CIDA</c>: the desktop as the parent, whose ID list is empty, and each item under it.
/// </remarks>
public class ShellDropTests : IDisposable
{
    private const string ThisPc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
    private const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";
    private const string ControlPanel = "::{26EE0668-A00A-44D7-9371-BEB064C98683}";
    private const string Network = "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ArtDock.Tests." + Guid.NewGuid().ToString("N"));

    public ShellDropTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary directory left behind is not a failed test.
        }

        GC.SuppressFinalize(this);
    }

    private string File(string name)
    {
        var path = Path.Combine(_root, name);
        System.IO.File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void ABlock_ReadsBackAsThePlacesAndFilesItHolds()
    {
        var file = File("Notes.txt");

        var items = ShellIdList.Read(Cida(ThisPc, file));

        Assert.Equal(2, items.Count);
        Assert.Null(items[0].Path);
        Assert.Equal(ThisPc, items[0].ParsingName, ignoreCase: true);
        Assert.Equal(file, items[1].Path, ignoreCase: true);
    }

    [Fact]
    public void AMalformedBlock_ReadsAsNothingRatherThanAsACrash()
    {
        // The block comes from whichever process started the drag. An ID list that runs off
        // the end would be read past it by the shell, on a mouse move over the dock.
        var good = Cida(ThisPc);

        Assert.Empty(ShellIdList.Read([]));
        Assert.Empty(ShellIdList.Read([1, 0]));

        var hugeCount = (byte[])good.Clone();
        BitConverter.GetBytes(uint.MaxValue).CopyTo(hugeCount, 0);
        Assert.Empty(ShellIdList.Read(hugeCount));

        var offsetPastTheEnd = (byte[])good.Clone();
        BitConverter.GetBytes((uint)good.Length + 10).CopyTo(offsetPastTheEnd, 8);
        Assert.Empty(ShellIdList.Read(offsetPastTheEnd));

        // The item's ID list, cut off before its terminator.
        Assert.Empty(ShellIdList.Read(good[..^2]));

        // An item claiming a size too small to move past.
        var stuck = (byte[])good.Clone();
        var itemAt = BitConverter.ToInt32(good, 8);
        BitConverter.GetBytes((ushort)1).CopyTo(stuck, itemAt);
        Assert.Empty(ShellIdList.Read(stuck));
    }

    [Theory]
    [InlineData(ThisPc, "thispc")]
    [InlineData(RecycleBin, "recyclebin")]
    [InlineData(ControlPanel, "control")]
    [InlineData("::{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", "control")]
    [InlineData("::{21EC2020-3AEA-1069-A2DD-08002B30309D}", "control")]
    [InlineData("::{645ff040-5081-101b-9f08-00aa002f954e}", "recyclebin")]
    public void APlaceTheAddMenuOffers_IsPinnedAsTheMenuPinsIt(string dropped, string preset)
    {
        // The target is what the Empty entry, the full-or-empty icon, icon sets and the
        // already-pinned check all key on. A second spelling of the same place would miss
        // every one of them.
        var pin = DockPresets.CreateFromShellName(dropped);

        Assert.NotNull(pin);
        Assert.Equal(DockPresets.Create(preset)!.TargetPath, pin.TargetPath);
        Assert.Equal(DockPresets.Create(preset)!.Aumid, pin.Aumid);
        Assert.Equal(DockPresets.Create(preset)!.Label, pin.Label);
    }

    [Fact]
    public void ADroppedRecycleBin_IsTheRecycleBin()
    {
        var pin = DroppedItems.Pin(RecycleBin);

        Assert.NotNull(pin);
        Assert.True(DockPresets.IsRecycleBin(pin.TargetPath));
    }

    [Fact]
    public void AnyOtherPlace_IsPinnedByItsShellNameUnderExplorersName()
    {
        var pin = DockPresets.CreateFromShellName(Network);

        Assert.NotNull(pin);
        Assert.Equal("shell:" + Network, pin.TargetPath);
        Assert.Equal(ShellNames.DisplayName(pin.TargetPath!), pin.Label);
        Assert.False(string.IsNullOrWhiteSpace(pin.Label));

        var item = PinnedAppsService.ToDockItem(pin);
        Assert.True(item.IsLaunchable);

        // A folder to the shell, lit while a File Explorer window shows it.
        Assert.NotNull(item.RunningTarget);
        Assert.Equal(ShellNames.FolderName(pin.TargetPath), item.RunningTarget.Folder);
    }

    [Theory]
    [InlineData("::{00000000-0000-0000-0000-00000000A7D0}")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Archive.zip\inside.txt")]
    [InlineData("")]
    public void ANameThatIsNoPlaceTheShellKnows_IsNotPinnedAsOne(string name)
    {
        // Pinned, it would be a blank icon that does nothing when clicked. The unregistered
        // CLSID is the case that matters: under shell: the shell still makes an item of it.
        Assert.Null(DockPresets.CreateFromShellName(name));
    }

    [Fact]
    public void AMixedDrag_KeepsItsOrder_AndItsFiles()
    {
        // Explorer puts no paths on a drag that includes a place — not even the files' — so
        // the files have to come out of the ID list too.
        var file = File("Notes.txt");
        var data = new DataObject();
        data.SetData(ShellIdList.Format, new MemoryStream(Cida(file, RecycleBin, ThisPc)));

        var targets = DroppedItems.Targets(data);

        Assert.NotNull(targets);
        Assert.Equal(3, targets.Count);
        Assert.Equal(file, targets[0], ignoreCase: true);
        Assert.Equal(RecycleBin, targets[1], ignoreCase: true);
        Assert.Equal(ThisPc, targets[2], ignoreCase: true);

        var pins = targets.Select(DroppedItems.Pin).ToList();
        Assert.All(pins, Assert.NotNull);
        Assert.Equal(
            new[] { file, DockPresets.RecycleBinTarget, "shell:MyComputerFolder" },
            pins.Select(pin => pin!.TargetPath),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheDesktopsOwnDragOfThisPcAndTheBin_ArrivesThroughWpf_AsTheirPresets()
    {
        // Not a block built by hand: the data object the desktop itself hands to a drag, asked
        // for as Explorer asks for it, and read through WPF's wrapper the way a real drop is.
        // What this proves that the others cannot is that WPF gives the ID list back at all.
        Assert.Equal(0, SHGetDesktopFolder(out var desktop));
        var pc = IdListPointer(ThisPc);
        var bin = IdListPointer(RecycleBin);
        try
        {
            Assert.Equal(0, desktop.GetUIObjectOf(
                0, 2, [pc, bin], typeof(System.Runtime.InteropServices.ComTypes.IDataObject).GUID, 0, out var native));

            var data = new DataObject(Marshal.GetObjectForIUnknown(native));
            Marshal.Release(native);

            // As measured: no paths at all, which is why the drop used to be refused.
            Assert.False(data.GetDataPresent(DataFormats.FileDrop));

            var pins = DroppedItems.Targets(data)?.Select(DroppedItems.Pin).ToList();

            Assert.NotNull(pins);
            Assert.Equal(
                new[] { "shell:MyComputerFolder", DockPresets.RecycleBinTarget },
                pins.Select(pin => pin?.TargetPath));
        }
        finally
        {
            Marshal.FreeCoTaskMem(pc);
            Marshal.FreeCoTaskMem(bin);
            Marshal.ReleaseComObject(desktop);
        }
    }

    [Fact]
    public void ADragOfFilesOnly_IsReadFromItsPaths_AsItAlwaysWas()
    {
        // Explorer offers both formats for files. The paths win, so that nothing about an
        // ordinary drop changes because the ID list is now being read at all.
        var file = File("Notes.txt");
        var elsewhere = Path.Combine(_root, "as the source named it.txt");
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { elsewhere });
        data.SetData(ShellIdList.Format, new MemoryStream(Cida(file)));

        Assert.Equal(new[] { elsewhere }, DroppedItems.Targets(data));
    }

    [Fact]
    public void ADragOfNeither_HoldsNothing()
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, "some text");

        Assert.Null(DroppedItems.Targets(data));
        Assert.Null(DroppedItems.Signature(data));
    }

    [Fact]
    public void TwoDragsOfDifferentPlaces_AreToldApart()
    {
        // The signature is what decides whether a hovering drag is resolved again. Two places
        // with no paths must not share one, or the second drag would preview the first.
        var pc = new DataObject();
        pc.SetData(ShellIdList.Format, new MemoryStream(Cida(ThisPc)));
        var bin = new DataObject();
        bin.SetData(ShellIdList.Format, new MemoryStream(Cida(RecycleBin)));

        Assert.NotNull(DroppedItems.Signature(pc));
        Assert.NotEqual(DroppedItems.Signature(pc), DroppedItems.Signature(bin));
    }

    [Theory]
    [InlineData(DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link, DragDropEffects.Copy)]
    [InlineData(DragDropEffects.Copy, DragDropEffects.Copy)]
    [InlineData(DragDropEffects.Link, DragDropEffects.Link)]
    [InlineData(DragDropEffects.Move | DragDropEffects.Link, DragDropEffects.Link)]
    [InlineData(DragDropEffects.Move, DragDropEffects.None)]
    [InlineData(DragDropEffects.None, DragDropEffects.None)]
    public void TheDropsEffect_IsOneTheSourceOffered_AndNeverAMove(
        DragDropEffects allowed, DragDropEffects expected)
    {
        // This PC and the Recycle Bin offer only a link, and answering Copy to one of them is
        // answering None. A move would be the source told to delete what was dragged.
        Assert.Equal(expected, DroppedItems.Effect(allowed));
    }

    /// <summary>
    /// A <c>CIDA</c> holding the named items under the desktop, as Explorer's desktop builds
    /// one: the count, the offsets, the parent's empty ID list, then each item's.
    /// </summary>
    private static byte[] Cida(params string[] names)
    {
        var lists = names.Select(IdListOf).ToList();
        byte[] desktop = [0, 0];

        var header = sizeof(uint) * (lists.Count + 2);
        using var block = new MemoryStream();
        using var writer = new BinaryWriter(block);

        writer.Write((uint)lists.Count);
        var at = header;
        writer.Write((uint)at);
        at += desktop.Length;
        foreach (var list in lists)
        {
            writer.Write((uint)at);
            at += list.Length;
        }

        writer.Write(desktop);
        foreach (var list in lists)
        {
            writer.Write(list);
        }

        return block.ToArray();
    }

    /// <summary>
    /// The shell's own ID list for a name, for the caller to free. A place directly on the
    /// desktop is one item long, so this is also its ID list relative to the desktop.
    /// </summary>
    private static nint IdListPointer(string name)
    {
        Assert.Equal(0, SHParseDisplayName(name, 0, out var pidl, 0, out _));
        return pidl;
    }

    /// <summary>The absolute ID list the shell parses a name to.</summary>
    private static byte[] IdListOf(string name)
    {
        var pidl = IdListPointer(name);
        try
        {
            var bytes = new byte[ILGetSize(pidl)];
            Marshal.Copy(pidl, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string pszName, nint pbc, out nint ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern uint ILGetSize(nint pidl);

    [DllImport("shell32.dll")]
    private static extern int SHGetDesktopFolder(out IShellFolder ppshf);

    /// <summary>Only as far as <c>GetUIObjectOf</c>; the slots before it hold the vtable's place.</summary>
    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        void ParseDisplayName();

        void EnumObjects();

        void BindToObject();

        void BindToStorage();

        void CompareIDs();

        void CreateViewObject();

        void GetAttributesOf();

        [PreserveSig]
        int GetUIObjectOf(
            nint hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] nint[] apidl,
            in Guid riid, nint rgfReserved, out nint ppv);
    }
}
