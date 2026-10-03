using System.Runtime.ExceptionServices;
using ArtDock.Interop;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers which windows light a folder pin: the File Explorer windows showing its folder in any
/// of their tabs (<see cref="ExplorerWindows"/>), put under their folders by the census
/// (<see cref="RunningAppsService.WindowsByFolder"/>).
/// </summary>
public class ExplorerWindowsTests
{
    private const string Downloads = @"C:\Users\someone\Downloads";
    private const string Drive = @"D:\";

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

    private static ExplorerTab Tab(nint window, nint tab, string folder) => new(window, tab, folder, null);

    [Fact]
    public void EachFolder_HasTheWindowsShowingIt_InTheCensussOrder()
    {
        ExplorerTab[] tabs = [Tab(3, 30, Downloads), Tab(1, 10, Drive), Tab(2, 20, Downloads)];

        var byFolder = RunningAppsService.WindowsByFolder([1, 2, 3], tabs);

        Assert.Equal([2, 3], byFolder[Downloads]);
        Assert.Equal([1], byFolder[Drive]);

        // The shell names a folder in whatever case it was made in; a pin may say it otherwise.
        Assert.True(byFolder.ContainsKey(Downloads.ToLowerInvariant()));
    }

    [Fact]
    public void AWindowWithTabs_IsUnderEveryFolderItsTabsShow_OnceEach()
    {
        // A tab behind another counts as much as the one in front: the folder is open there, and
        // a click brings its tab forward. Two tabs on one folder are still one window.
        ExplorerTab[] tabs = [Tab(1, 10, Drive), Tab(1, 11, Downloads), Tab(1, 12, Downloads)];

        var byFolder = RunningAppsService.WindowsByFolder([1], tabs);

        Assert.Equal([1], byFolder[Drive]);
        Assert.Equal([1], byFolder[Downloads]);
    }

    [Fact]
    public void OnlyTheCensussWindows_AreUnderAFolder()
    {
        // Explorer's answer was read before window 9 closed, and window 4 is one of Explorer's
        // own dialogs — a copy's progress — which shows no folder.
        ExplorerTab[] tabs = [Tab(1, 10, Drive), Tab(9, 90, Drive)];

        var byFolder = RunningAppsService.WindowsByFolder([1, 4], tabs);

        Assert.Equal([1], byFolder[Drive]);
        Assert.Single(byFolder);
    }

    [Fact]
    public void ExplorersTabs_AreReadWithoutFailing() => OnStaThread(() =>
    {
        // Asks Explorer about every window it has open, whatever they show: the calls into its
        // process go through, or fail quietly where there is no Explorer.
        var tabs = ExplorerWindows.Tabs();

        Assert.All(tabs, tab =>
        {
            Assert.NotEqual(0, tab.Window);
            Assert.NotEqual(0, tab.Tab);
            Assert.False(string.IsNullOrEmpty(tab.Folder));
        });
    });
}
