using System.IO;
using ArtDock.Dock;
using ArtDock.Interop;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers what may be pinned, now that the answer is "anything that is there".
/// </summary>
/// <remarks>
/// Touches the filesystem because the question <see cref="PinnedAppsService.CreatePin"/>
/// asks is whether something exists — a temporary directory is the cheapest honest way to
/// put a document and a folder in front of it.
/// </remarks>
public class PinCreationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ArtDock.Tests." + Guid.NewGuid().ToString("N"));

    public PinCreationTests() => Directory.CreateDirectory(_root);

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
    public void ADocument_IsPinnedUnderItsOwnName()
    {
        // The reversal this suite exists for: a document used to be turned away outright.
        var pin = PinnedAppsService.CreatePin(File("Cover art.psd"));

        Assert.NotNull(pin);
        Assert.Equal("Cover art", pin.Label);
        Assert.EndsWith("Cover art.psd", pin.TargetPath);
    }

    [Fact]
    public void AFolder_IsPinnedUnderItsOwnName()
    {
        var folder = Path.Combine(_root, "Screenshots");
        Directory.CreateDirectory(folder);

        var pin = PinnedAppsService.CreatePin(folder);

        Assert.NotNull(pin);
        Assert.Equal("Screenshots", pin.Label);
    }

    [Fact]
    public void SomethingThatIsNotThere_IsNotPinned()
    {
        // Which is now the only reason a drop is refused, and what the notice says.
        Assert.Null(PinnedAppsService.CreatePin(Path.Combine(_root, "gone.psd")));
        Assert.Null(PinnedAppsService.CreatePin("   "));
    }

    [Fact]
    public void APickedTargetThatIsNotThere_StillMakesAPin()
    {
        // A deliberate pick answered by nothing at all looks like a broken button.
        var pin = PinnedAppsService.CreateChosenPin(Path.Combine(_root, "gone.psd"));

        Assert.Equal("gone", pin.Label);
    }

    [Fact]
    public void ADocument_NeverShowsARunningDot()
    {
        // What opens a document is its editor, whose window belongs to whatever pin starts
        // that. The census also matches on file name, so the question is not asked at all.
        var pin = PinnedAppsService.CreatePin(File("Cover art.psd"));
        var item = PinnedAppsService.ToDockItem(pin!);

        Assert.Null(item.RunningTarget);
    }

    [Fact]
    public void AnApplication_StillMatchesTheProcessItStarts()
    {
        var item = new DockItem
        {
            Id = "app",
            Label = "App",
            TargetPath = @"C:\Program Files\Thing\Thing.exe"
        };

        Assert.Equal(@"C:\Program Files\Thing\Thing.exe", item.RunningTarget);
    }

    [Fact]
    public void AShortcutToAnApplication_MatchesWhatItPointsAt()
    {
        var item = new DockItem
        {
            Id = "app",
            Label = "App",
            TargetPath = @"C:\Users\someone\Start Menu\Thing.lnk",
            LinkTarget = @"C:\Program Files\Thing\Thing.exe"
        };

        Assert.Equal(@"C:\Program Files\Thing\Thing.exe", item.RunningTarget);
    }

    [Fact]
    public void AShortcutToAFolder_ShowsNoDotEither()
    {
        var item = new DockItem
        {
            Id = "folder",
            Label = "Work",
            TargetPath = @"C:\Users\someone\Start Menu\Work.lnk",
            LinkTarget = @"C:\Work"
        };

        Assert.Null(item.RunningTarget);
    }

    [Theory]
    [InlineData("thispc", "shell:MyComputerFolder")]
    [InlineData("userfolder", "shell:Profile")]
    [InlineData("downloads", "shell:Downloads")]
    [InlineData("recyclebin", "shell:RecycleBinFolder")]
    public void AShellPlace_IsPinnedDespiteHavingNoFile(string key, string target)
    {
        // The point of these presets is that no file exists at their names, so the existence
        // check every other pin goes through would turn them down. Routing them back through
        // CreatePin would not fail loudly — the menu entry would simply do nothing.
        var pin = DockPresets.Create(key);

        Assert.NotNull(pin);
        Assert.Equal(target, pin.TargetPath);
        Assert.False(pin.IsSeparator);
        Assert.NotEmpty(pin.Label);

        // And what was stored has to be what the shell is asked to resolve, or the dock
        // draws its fallback glyph where the icon should be.
        Assert.Equal(target, PinnedAppsService.ToDockItem(pin).ShellTarget);
    }

    [Fact]
    public void TheShellPlaces_AreOfferedByTheAddMenu()
    {
        // Create knowing a key is no use if the menu never offers it.
        var offered = DockPresets.Menu().SelectMany(group => group).Select(entry => entry.Key);

        Assert.Contains("thispc", offered);
        Assert.Contains("userfolder", offered);
        Assert.Contains("downloads", offered);
        Assert.Contains("recyclebin", offered);
        Assert.Contains("start", offered);
    }

    [Fact]
    public void ANewDock_StartsWithStartThisPcTheUserFolderDownloadsSettingsAndTheRecycleBinApart()
    {
        var settings = new DockSettings();

        Assert.True(new PinnedAppsService().SeedIfEmpty(settings));

        // In this order, left to right. The user folder and Downloads are stored by the names
        // that follow whoever is signed in, not as paths — an imported settings file has to
        // open the importer's folders, not the exporter's. Settings is a Store app, so it has
        // an AUMID where the others have a target, and the separator has neither.
        var pins = settings.PinnedApps;
        Assert.Equal(
            new string?[]
            {
                DockCommands.StartTarget, "shell:MyComputerFolder", "shell:Profile", "shell:Downloads",
                null, null, "shell:RecycleBinFolder"
            },
            pins.Select(pin => pin.TargetPath));
        Assert.Equal(DockPresets.SettingsAumid, pins[4].Aumid);
        Assert.Equal(new[] { false, false, false, false, false, true, false }, pins.Select(pin => pin.IsSeparator));
        Assert.All(pins, pin => Assert.NotEmpty(pin.Label));
        Assert.Equal(7, pins.Select(pin => pin.Id).Distinct().Count());
    }

    [Fact]
    public void TheUserFolder_IsNamedAsExplorerNamesIt()
    {
        // The account's full name where there is one — not the folder's name on disk, and
        // not the menu's "User folder", which describes the entry rather than naming the pin.
        var shown = ShellNames.DisplayName(DockPresets.UserFolderTarget);

        Assert.False(string.IsNullOrWhiteSpace(shown));
        Assert.Equal(shown, DockPresets.Create("userfolder")!.Label);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"C:\ArtDock.Tests\not\here\at\all")]
    [InlineData("shell:NoSuchFolderAnywhere")]
    public void AName_TheShellCannotResolve_IsNullRatherThanAnError(string target)
    {
        // Null is what makes the label fall back to "User folder"; a throw would take the Add
        // menu down with it.
        Assert.Null(ShellNames.DisplayName(target));
    }

    [Fact]
    public void Settings_IsPinnedAsTheStoreApp()
    {
        // As the app rather than the ms-settings: address, which opens it too but draws the
        // shell's blank document instead of its icon.
        var pin = DockPresets.Create("settings");

        Assert.NotNull(pin);
        Assert.Null(pin.TargetPath);
        Assert.Equal(DockPresets.SettingsAumid, pin.Aumid);
        Assert.NotEmpty(pin.Label);

        var item = PinnedAppsService.ToDockItem(pin);
        Assert.Equal(@"shell:AppsFolder\" + DockPresets.SettingsAumid, item.ShellTarget);
        Assert.True(item.IsLaunchable);

        Assert.Contains("settings", DockPresets.Menu().SelectMany(group => group).Select(entry => entry.Key));
    }

    [Fact]
    public void ADockThatHasPins_IsNotSeeded()
    {
        var settings = new DockSettings
        {
            PinnedApps = [new PinnedAppSetting { Id = "mine", Label = "Mine", TargetPath = @"C:\mine.exe" }]
        };

        Assert.False(new PinnedAppsService().SeedIfEmpty(settings));
        Assert.Equal("mine", Assert.Single(settings.PinnedApps).Id);
    }

    [Fact]
    public void Start_IsPinnedAsACommand()
    {
        var pin = DockPresets.Create("start");

        Assert.NotNull(pin);
        Assert.Equal(DockCommands.StartTarget, pin.TargetPath);
        Assert.True(DockCommands.IsCommand(pin.TargetPath));
    }

    [Theory]
    [InlineData(@"C:\Windows\explorer.exe")]
    [InlineData("shell:RecycleBinFolder")]
    [InlineData(@"shell:AppsFolder\Some.App_8wekyb3d8bbwe!App")]
    [InlineData("https://example.com")]
    [InlineData("")]
    [InlineData(null)]
    public void AnOrdinaryTarget_IsNotMistakenForACommand(string? target)
    {
        // The scheme check stands between every pin and ShellExecute, so a target it claimed
        // by mistake would be a pin that silently stopped launching.
        Assert.False(DockCommands.IsCommand(target));
        Assert.False(DockCommands.Run(target));
    }

    [Fact]
    public void AnUnknownCommand_IsRefusedRatherThanGuessedAt()
    {
        // A settings file from a later build, or a hand-edited one. Recognising the scheme
        // is not the same as knowing what to do, and doing something else would be worse.
        Assert.True(DockCommands.IsCommand("artdock:showdesktop"));
        Assert.False(DockCommands.Run("artdock:showdesktop"));
        Assert.Null(DockCommands.IconFor("artdock:showdesktop"));
    }

    [Theory]
    [InlineData("shell:RecycleBinFolder")]
    [InlineData("SHELL:RECYCLEBINFOLDER")]
    public void TheRecycleBin_IsRecognisedByItsTarget(string target)
    {
        // By target and not by preset key, because the key is never stored — and because the
        // item editor takes free text, so a bin typed in by hand has to get the Empty entry
        // just as one added from the menu does.
        Assert.True(DockPresets.IsRecycleBin(target));
    }

    [Theory]
    [InlineData("shell:MyComputerFolder")]
    [InlineData(@"C:\$Recycle.Bin")]
    [InlineData(@"shell:RecycleBinFolder\something")]
    [InlineData("")]
    [InlineData(null)]
    public void NothingElse_IsMistakenForTheRecycleBin(string? target)
    {
        // The consequence of a false positive is an Empty Recycle Bin entry on an item that
        // is not one — an offer to permanently delete, made about the wrong thing.
        Assert.False(DockPresets.IsRecycleBin(target));
    }

    [Fact]
    public void ACommandPin_ShowsNoRunningDot()
    {
        // Nothing runs under it, and RunningTarget is what rules that out — see the .exe
        // check there. A command with a dot under it would be claiming to be an open app.
        var item = PinnedAppsService.ToDockItem(DockPresets.Create("start")!);

        Assert.Null(item.RunningTarget);
        Assert.True(item.IsLaunchable);
    }
}
