using ArtDock.Dock;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers which items can be started as administrator — the ones the item's menu offers
/// <em>Run as administrator</em> for, and the item editor its checkbox — and that the choice
/// reaches the item the dock launches.
/// </summary>
/// <remarks>
/// Asked of this machine's file associations, as the dock asks them; the types here are
/// Windows' own, registered the same on every install.
/// </remarks>
public class RunAsAdministratorTests
{
    [Theory]
    [InlineData(@"C:\Tools\App.exe")]
    [InlineData(@"C:\Tools\APP.EXE")]
    [InlineData(@"C:\Tools\build.cmd")]
    [InlineData(@"C:\Tools\build.bat")]
    [InlineData(@"C:\Windows\System32\compmgmt.msc")]
    [InlineData("notepad.exe")]
    public void AProgram_CanRunAsAdministrator(string target) =>
        Assert.True(PinnedAppsService.CanRunAsAdministrator(target, null));

    [Theory]
    [InlineData(@"C:\Work")]
    [InlineData(@"C:\Work\notes.txt")]
    [InlineData(@"C:\Work\photo.png")]
    [InlineData("shell:Downloads")]
    [InlineData("https://example.com/setup.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElse_CannotRunAsAdministrator(string? target) =>
        Assert.False(PinnedAppsService.CanRunAsAdministrator(target, null));

    [Fact]
    public void AShortcut_CountsByWhatItPointsAt()
    {
        Assert.True(PinnedAppsService.CanRunAsAdministrator(@"C:\Links\App.lnk", @"C:\Tools\App.exe"));
        Assert.False(PinnedAppsService.CanRunAsAdministrator(@"C:\Links\Work.lnk", @"C:\Work"));

        // One the shell could not resolve has nothing to say it is a program.
        Assert.False(PinnedAppsService.CanRunAsAdministrator(@"C:\Links\Gone.lnk", null));
    }

    /// <summary>
    /// Store apps are asked of Start's own menu for them. Notepad, a desktop program packaged
    /// for the Store, has the entry; Calculator, an app of the Store's own kind, does not. Both
    /// ship with Windows 11.
    /// </summary>
    [Theory]
    [InlineData("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", true)]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", false)]
    [InlineData("windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel", false)]
    [InlineData("NoSuchPublisher.NoSuchApp_0000000000000!App", false)]
    public void AStoreApp_CanRunAsAdministrator_WhenStartOffersIt(string aumid, bool elevates)
    {
        var answer = false;
        var thread = new Thread(() => answer = PinnedAppsService.CanRunAppAsAdministrator(aumid));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Equal(elevates, answer);
    }

    [Fact]
    public void AStoreApp_IsLearnedOffTheDocksThread_AndKnownOnceLearned()
    {
        const string notepad = "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App";
        DockItem Item() => PinnedAppsService.ToDockItem(new PinnedAppSetting
        {
            Id = "a",
            Label = "Notepad",
            Aumid = notepad
        });

        var learned = new TaskCompletionSource<(string, bool, bool)>();
        var asked = PinnedAppsService.LearnRunAsAdministrator(
            [Item()],
            (aumid, elevates) => learned.TrySetResult(
                (aumid, elevates, Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)));

        // Another test may have asked already, in which case there is nothing left to learn.
        if (asked)
        {
            Assert.True(learned.Task.Wait(TimeSpan.FromSeconds(10)));
            Assert.Equal((notepad, true, true), learned.Task.Result);
        }

        Assert.True(Item().CanRunAsAdministrator);
        Assert.False(PinnedAppsService.LearnRunAsAdministrator([Item()], (_, _) => { }));
    }

    [Fact]
    public void APin_IsNotRunAsAdministrator_UnlessAskedTo()
    {
        Assert.False(new PinnedAppSetting().RunAsAdministrator);

        var item = PinnedAppsService.ToDockItem(new PinnedAppSetting
        {
            Id = "a",
            Label = "App",
            TargetPath = @"C:\Tools\App.exe",
            RunAsAdministrator = true
        });

        Assert.True(item.RunAsAdministrator);
        Assert.True(item.CanRunAsAdministrator);
    }
}
