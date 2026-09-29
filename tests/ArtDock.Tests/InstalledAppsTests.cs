using System.IO;
using ArtDock.Interop;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers <em>Scan for apps…</em>: which Start menu shortcuts are apps worth offering, and
/// telling a program with an icon of its own from one the shell draws a blank for.
/// </summary>
/// <remarks>
/// <para>
/// The shortcuts themselves are not made here — reading one goes through the shell — so the
/// rule is tested apart from the reading: a program, not an uninstaller, not a helper, not
/// inside a game's folder, and there.
/// </para>
/// <para>
/// The icon test reads real files: a program that ships with Windows for one that has an
/// icon, and files made for the test for ones that do not.
/// </para>
/// </remarks>
public class InstalledAppsTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ArtDockTests", Guid.NewGuid().ToString("n"));

    public InstalledAppsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }

        GC.SuppressFinalize(this);
    }

    private static readonly string[] Games = [@"E:\Games\StarCraft II", @"E:\Programms\Steam\steamapps\common\Cyberpunk 2077"];

    private static bool Keep(string name, string? target) =>
        InstalledApps.Keep(name, target, Games, _ => true);

    [Fact]
    public void AnAppsShortcut_IsKept()
    {
        Assert.True(Keep("VLC media player", @"C:\Program Files\VideoLAN\VLC\vlc.exe"));
    }

    [Theory]
    [InlineData(@"C:\Program Files\App\Manual.chm")]
    [InlineData(@"C:\Program Files\App\readme.txt")]
    [InlineData(null)]
    public void AShortcutToSomethingOtherThanAProgram_IsNot(string? target)
    {
        // A help file, a document, or a Store app and a web address, which have no path.
        Assert.False(Keep("Something", target));
    }

    [Theory]
    [InlineData("Uninstall 7-Zip", @"C:\Program Files\7-Zip\7zFM.exe")]
    [InlineData("Foo Uninstaller", @"C:\Program Files\Foo\foo.exe")]
    public void AnUninstaller_IsNot_EvenWhenItRunsTheAppsOwnProgram(string name, string target)
    {
        // Some uninstall with the app's own program and an argument; that shortcut must not be
        // the one the program is offered under.
        Assert.False(Keep(name, target));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Foo\unins000.exe")]
    [InlineData(@"C:\Program Files\Foo\Updater.exe")]
    public void AHelpersProgram_IsNot(string target)
    {
        Assert.False(Keep("Foo", target));
    }

    [Theory]
    [InlineData("Administrative Tools", @"C:\Windows\System32\control.exe")]
    [InlineData("Install Additional Tools for Node.js", @"C:\Windows\System32\cmd.exe")]
    [InlineData("Some Script", @"C:\Windows\System32\wscript.exe")]
    public void AShortcutThatRunsAHost_IsNot(string name, string target)
    {
        // Named for what it runs, not for the console or control panel it runs it in —
        // listing one would stand the dock down over every console.
        Assert.False(Keep(name, target));
    }

    [Fact]
    public void AnAppNamedLikeAHelper_IsStillKept()
    {
        // The shortcut's name is checked for uninstalling only: an app is not a helper for
        // having "crash" or "update" in its name.
        Assert.True(Keep("Crash Course Player", @"C:\Program Files\Crash Course\player.exe"));
    }

    [Fact]
    public void AGamesProgram_IsLeftToTheScanForGames()
    {
        Assert.False(Keep("StarCraft II", @"E:\Games\StarCraft II\StarCraft II.exe"));
        Assert.False(Keep("Cyberpunk 2077", @"E:\Programms\Steam\steamapps\common\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe"));
    }

    [Fact]
    public void AProgramThatIsNotThere_IsNot()
    {
        Assert.False(InstalledApps.Keep("Gone", @"C:\Gone\gone.exe", Games, _ => false));
    }

    [Theory]
    [InlineData(@"E:\Games\StarCraft II\SC2.exe", @"E:\Games\StarCraft II", true)]
    [InlineData(@"E:\Games\StarCraft II\Versions\SC2.exe", @"E:\Games\StarCraft II\", true)]
    [InlineData(@"e:\games\starcraft ii\sc2.exe", @"E:\Games\StarCraft II", true)]
    [InlineData(@"E:\Games\StarCraft II Beta\SC2.exe", @"E:\Games\StarCraft II", false)]
    [InlineData(@"E:\Games\StarCraft II", @"E:\Games\StarCraft II", false)]
    [InlineData(@"E:\Games\SC2.exe", @"E:\Games\StarCraft II", false)]
    public void InsideAFolder_MeansUnderIt_NotBesideIt(string path, string folder, bool inside)
    {
        // "StarCraft II Beta" begins with "StarCraft II" and is not inside it.
        Assert.Equal(inside, InstalledApps.IsInside(path, folder));
    }

    // ---- icons ---------------------------------------------------------------------

    [Fact]
    public void AProgramWithAnIcon_HasOne()
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        Assert.True(ShellIcons.HasOwnIcon(explorer));
    }

    [Fact]
    public void AFileThatIsNoProgram_HasNone()
    {
        var fake = Path.Combine(_dir, "not-really.exe");
        File.WriteAllText(fake, "not a program");

        Assert.False(ShellIcons.HasOwnIcon(fake));
    }

    [Fact]
    public void AProgramThatIsNotThere_HasNone()
    {
        Assert.False(ShellIcons.HasOwnIcon(Path.Combine(_dir, "missing.exe")));
        Assert.False(ShellIcons.HasOwnIcon(string.Empty));
    }
}
