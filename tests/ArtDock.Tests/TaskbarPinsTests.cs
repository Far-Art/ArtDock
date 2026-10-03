using System.IO;
using System.Runtime.InteropServices;
using ArtDock.Interop;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers <em>Add from taskbar</em> and the first run's question: reading the taskbar's
/// <c>Favorites</c> value, choosing the Start menu's shortcut in place of the taskbar's own,
/// telling an app already on the dock, and where a new dock puts what it is given.
/// </summary>
/// <remarks>
/// The value is built here as the taskbar writes it, around ID lists the shell makes for real
/// paths, so the reading is tested through to the shell's names without touching the taskbar —
/// which is read, never written, by the dock.
/// </remarks>
public class TaskbarPinsTests
{
    [Fact]
    public void Split_ReadsEveryIdList_InOrder()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var notepad = Path.Combine(windows, "notepad.exe");

        var blob = Favorites(IdListOf(windows), IdListOf(notepad));
        var items = TaskbarFavorites.Split(blob).Select(ShellIdList.ReadOne).ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal(windows, items[0]?.Path, ignoreCase: true);
        Assert.Equal(notepad, items[1]?.Path, ignoreCase: true);
    }

    [Fact]
    public void Split_StopsAtWhatDoesNotRead_KeepingWhatDid()
    {
        var good = IdListOf(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        var blob = Favorites(good);

        // The closing FF replaced by another entry whose size runs past the end.
        var truncated = blob[..^1].Concat(new byte[] { 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x01 }).ToArray();

        Assert.Single(TaskbarFavorites.Split(truncated));
        Assert.Empty(TaskbarFavorites.Split([]));
        Assert.Empty(TaskbarFavorites.Split([0xFF]));
        Assert.Empty(TaskbarFavorites.Split([0x00, 0x01, 0x00]));
    }

    [Fact]
    public void Split_RefusesAnIdListThatRunsPastItsSize()
    {
        // An item of 0x10 bytes inside an entry that says it is 4.
        byte[] blob = [0x00, 0x04, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0xFF];
        Assert.Empty(TaskbarFavorites.Split(blob));
    }

    [Fact]
    public void Equivalent_PrefersTheShortcutWithTheSameAppId()
    {
        const string chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        TaskbarPins.StartShortcut[] start =
        [
            new(@"C:\Start\Profile 2.lnk", chrome, "Chrome.Profile2"),
            new(@"C:\Start\Google Chrome.lnk", chrome, "Chrome"),
            new(@"C:\Start\Notepad.lnk", @"C:\Windows\notepad.exe", null)
        ];

        Assert.Equal(@"C:\Start\Google Chrome.lnk", TaskbarPins.Equivalent(chrome, "Chrome", start));
        Assert.Equal(@"C:\Start\Notepad.lnk", TaskbarPins.Equivalent(@"c:\windows\NOTEPAD.EXE", null, start));
    }

    [Fact]
    public void Equivalent_TakesOneWithoutAnAppId_OnlyWhenNothingMatchesExactly()
    {
        const string program = @"C:\Apps\tool.exe";
        TaskbarPins.StartShortcut[] start =
        [
            new(@"C:\Start\Other app.lnk", program, "Other"),
            new(@"C:\Start\Tool.lnk", program, null)
        ];

        // A different app ID is a different app; none at all is near enough.
        Assert.Equal(@"C:\Start\Tool.lnk", TaskbarPins.Equivalent(program, "Tool", start));
        Assert.Null(TaskbarPins.Equivalent(program, "Tool", [start[0]]));
        Assert.Null(TaskbarPins.Equivalent(@"C:\Apps\else.exe", null, start));
    }

    [Theory]
    [InlineData("Microsoft.Copilot_8wekyb3d8bbwe!App", true)]
    [InlineData("Microsoft.Windows.Explorer", true)]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", false)]
    [InlineData(@"C:\Windows\notepad.exe", false)]
    [InlineData(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Notepad++\notepad++.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAppId(string? parsingName, bool expected) =>
        Assert.Equal(expected, TaskbarPins.IsAppId(parsingName));

    [Fact]
    public void KeyOf_IsTheProgram_WhateverPathItIsPinnedBy()
    {
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");

        Assert.Equal(
            TaskbarPins.KeyOf(new PinnedAppSetting { Id = "a", TargetPath = notepad }),
            TaskbarPins.KeyOf(new PinnedAppSetting { Id = "b", TargetPath = @"%WINDIR%\notepad.exe" }),
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal("aumid:Some.App!App", TaskbarPins.KeyOf(new PinnedAppSetting { Id = "c", Aumid = " Some.App!App " }));
        Assert.Null(TaskbarPins.KeyOf(DockPresets.CreateSeparator()));
    }

    [Fact]
    public void IntoNewDock_PutsTheAppsBeforeTheRecycleBin_BehindASeparatorOfTheirOwn()
    {
        var defaults = DockPresets.CreateDefaults();
        var found = new List<PinnedAppSetting>
        {
            new() { Id = "x", Label = "One", Aumid = "One!App" },
            new() { Id = "y", Label = "Two", Aumid = "Two!App" }
        };

        var merged = TaskbarPins.IntoNewDock(defaults, found);

        Assert.Equal(defaults.Count + found.Count + 1, merged.Count);
        Assert.Equal(DockPresets.RecycleBinTarget, merged[^1].TargetPath);
        Assert.True(merged[^2].IsSeparator);
        Assert.Equal(["x", "y"], merged.Skip(merged.Count - 4).Take(2).Select(pin => pin.Id));
        Assert.True(merged[^5].IsSeparator);

        // The list it was given is left as it was.
        Assert.Equal(DockPresets.CreateDefaults().Count, defaults.Count);
    }

    [Fact]
    public void IntoNewDock_AddsAtTheEnd_WithoutASeparator_AndChangesNothingForNone()
    {
        var pins = new List<PinnedAppSetting> { new() { Id = "a", Label = "A", TargetPath = @"C:\a.exe" } };
        var found = new List<PinnedAppSetting> { new() { Id = "b", Label = "B", TargetPath = @"C:\b.exe" } };

        Assert.Equal(["a", "b"], TaskbarPins.IntoNewDock(pins, found).Select(pin => pin.Id));
        Assert.Equal(["a"], TaskbarPins.IntoNewDock(pins, []).Select(pin => pin.Id));
    }

    /// <summary>A <c>Favorites</c> value as the taskbar writes it, around these ID lists.</summary>
    private static byte[] Favorites(params byte[][] lists)
    {
        var blob = new List<byte>();
        foreach (var list in lists)
        {
            blob.Add(0x00);
            blob.AddRange(BitConverter.GetBytes((uint)list.Length));
            blob.AddRange(list);
        }

        blob.Add(0xFF);
        return [.. blob];
    }

    /// <summary>The shell's absolute ID list for a path, its terminator included.</summary>
    private static byte[] IdListOf(string path)
    {
        var pidl = ILCreateFromPathW(path);
        Assert.NotEqual(0, pidl);
        try
        {
            var bytes = new byte[ILGetSize(pidl)];
            Marshal.Copy(pidl, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            ILFree(pidl);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint ILCreateFromPathW(string path);

    [DllImport("shell32.dll")]
    private static extern uint ILGetSize(nint pidl);

    [DllImport("shell32.dll")]
    private static extern void ILFree(nint pidl);
}
