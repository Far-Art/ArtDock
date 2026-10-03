using System.IO;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers the pin of a launcher being lit by the program it starts — <c>Battle.net Launcher.exe</c>
/// by <c>Battle.net.exe</c> beside it — and nothing wider than that.
/// </summary>
/// <remarks>
/// Touches the filesystem, because the rule holds only when the program is really there.
/// </remarks>
public class LaunchedProgramTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ArtDockTests", Guid.NewGuid().ToString("n"));

    public LaunchedProgramTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Make(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, []);
        return path;
    }

    [Theory]
    [InlineData("Battle.net Launcher.exe", "Battle.net.exe")]
    [InlineData("GameLauncher.exe", "Game.exe")]
    [InlineData("Game-Launcher.exe", "Game.exe")]
    [InlineData("Game_launcher.EXE", "Game.exe")]
    public void ALauncher_IsLitByTheProgramNamedInIt(string launcher, string program)
    {
        var launcherPath = Make(launcher);
        var programPath = Make(program);

        Assert.Equal(programPath, PinnedAppsService.LaunchedProgram(launcherPath, null), ignoreCase: true);
    }

    [Fact]
    public void AShortcutToALauncher_IsLitTheSameWay()
    {
        var launcher = Make("Battle.net Launcher.exe");
        var program = Make("Battle.net.exe");

        Assert.Equal(program, PinnedAppsService.LaunchedProgram(@"C:\Links\Battle.net.lnk", launcher));
    }

    [Fact]
    public void ALauncherWithoutItsProgramBesideIt_IsLeftAlone() =>
        Assert.Null(PinnedAppsService.LaunchedProgram(Make("Battle.net Launcher.exe"), null));

    [Theory]
    [InlineData("Battle.net.exe")]
    [InlineData("Launcher.exe")]
    [InlineData("Launcher Tools.exe")]
    [InlineData("Battle.net Launcher.txt")]
    public void AnythingElse_IsLeftAlone(string name)
    {
        Make("Battle.net.exe");
        Make("Tools.exe");
        Make(".exe");

        Assert.Null(PinnedAppsService.LaunchedProgram(Make(name), null));
    }

    [Fact]
    public void TheRunningTarget_IsTheProgram_AndTheLaunchIsStillTheLauncher()
    {
        var launcher = Make("Battle.net Launcher.exe");
        var program = Make("Battle.net.exe");

        var item = PinnedAppsService.ToDockItem(new PinnedAppSetting { Id = "a", Label = "Battle.net", TargetPath = launcher });

        Assert.Equal(program, item.RunningTarget);
        Assert.Equal(launcher, item.ShellTarget);
    }
}
