using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers what makes two pins point at the same thing, which is what stops a drop of
/// something already on the dock adding a second copy of it.
/// </summary>
public class PinTargetTests
{
    [Fact]
    public void AFolderWithAndWithoutItsTrailingSlash_IsOneTarget()
    {
        // Explorer hands over a dropped folder either way depending on where it came from.
        Assert.Equal(
            PinnedAppsService.TargetKey(@"C:\Work\Shots"),
            PinnedAppsService.TargetKey(@"C:\Work\Shots\"));
    }

    [Fact]
    public void TwoRoutesToTheSameFile_AreOneTarget()
    {
        Assert.Equal(
            PinnedAppsService.TargetKey(@"C:\Work\App.exe"),
            PinnedAppsService.TargetKey(@"C:\Work\Tools\..\App.exe"));
    }

    [Fact]
    public void DifferentFiles_AreDifferentTargets()
    {
        Assert.NotEqual(
            PinnedAppsService.TargetKey(@"C:\Work\App.exe"),
            PinnedAppsService.TargetKey(@"C:\Work\Other.exe"));
    }

    [Fact]
    public void SomethingThatIsNotAPath_IsComparedAsWritten()
    {
        // The item editor takes free text, and a web address is a legitimate thing to pin.
        Assert.Equal("https://example.com", PinnedAppsService.TargetKey(" https://example.com "));
    }

    [Fact]
    public void NothingToCompare_HasNoTarget()
    {
        // A Store app is pinned by AUMID and a separator launches nothing; neither can
        // collide with anything, so neither may be treated as a duplicate of the other.
        Assert.Null(PinnedAppsService.TargetKey(null));
        Assert.Null(PinnedAppsService.TargetKey("   "));
    }

    [Fact]
    public void AnythingButAShortcut_IsNotAskedOfTheShell()
    {
        // The extension gate is what keeps a dock of executables from making a COM call per
        // item every time the settings dialog previews a change.
        Assert.Null(PinnedAppsService.ResolveLinkTarget(@"C:\Windows\System32\notepad.exe"));
        Assert.Null(PinnedAppsService.ResolveLinkTarget(null));
    }
}
