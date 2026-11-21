using System.IO;
using ArtDock.Interop;

namespace ArtDock.Tests;

/// <summary>Which programs an update announces to the shell as changed (<see cref="IconRefresh"/>).</summary>
public sealed class IconRefreshTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ArtDock.Tests.IconRefresh." + Guid.NewGuid().ToString("N"));

    public IconRefreshTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void AnInstallation_AnnouncesItsProgram_AndTheLauncherBesideCurrent()
    {
        var current = Directory.CreateDirectory(Path.Combine(_root, "current")).FullName;
        var program = Touch(Path.Combine(current, "ArtDock.exe"));
        var launcher = Touch(Path.Combine(_root, "ArtDock.exe"));

        Assert.Equal([program, launcher], IconRefresh.ProgramsOf(program));
    }

    [Fact]
    public void ALauncherThatIsNotThere_IsLeftOut()
    {
        var current = Directory.CreateDirectory(Path.Combine(_root, "current")).FullName;
        var program = Touch(Path.Combine(current, "ArtDock.exe"));

        Assert.Equal([program], IconRefresh.ProgramsOf(program));
    }

    [Fact]
    public void AProgramOutsideAnInstallation_IsAnnouncedAlone()
    {
        var build = Directory.CreateDirectory(Path.Combine(_root, "Release")).FullName;
        var program = Touch(Path.Combine(build, "ArtDock.exe"));
        Touch(Path.Combine(_root, "ArtDock.exe"));

        Assert.Equal([program], IconRefresh.ProgramsOf(program));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoPath_AnnouncesNothing(string? path) => Assert.Empty(IconRefresh.ProgramsOf(path));

    [Fact]
    public void AProgramThatIsNotThere_AnnouncesNothing() =>
        Assert.Empty(IconRefresh.ProgramsOf(Path.Combine(_root, "current", "ArtDock.exe")));

    [Fact]
    public void TheUpdateHook_IsHandedToVelopack()
    {
        var program = File.ReadAllText(Path.Combine(SourceRoot(), "src", "ArtDock", "Program.cs"));
        Assert.Contains(".OnAfterUpdateFastCallback(AppUpdater.OnUpdated)", program);
    }

    private static string Touch(string path)
    {
        File.WriteAllBytes(path, []);
        return path;
    }

    private static string SourceRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "src", "ArtDock")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new DirectoryNotFoundException("The source tree was not found above the tests.");
    }
}
