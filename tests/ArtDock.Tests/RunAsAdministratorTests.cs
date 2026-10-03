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

    [Fact]
    public void AStoreApp_CannotRunAsAdministrator() =>
        Assert.False(PinnedAppsService.ToDockItem(new PinnedAppSetting
        {
            Id = "a",
            Label = "Calculator",
            Aumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
            RunAsAdministrator = true
        }).CanRunAsAdministrator);

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
