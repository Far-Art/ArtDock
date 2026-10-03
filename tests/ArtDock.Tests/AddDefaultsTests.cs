using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers the Items page's <em>Add defaults</em> (<see cref="DockPresets.WithDefaults"/>): the
/// starting items a list lacks are added where they go among the ones it has, and nothing already
/// on it is taken off or moved.
/// </summary>
public class AddDefaultsTests
{
    private static PinnedAppSetting App(string id) => new() { Id = id, Label = id, TargetPath = $@"C:\Apps\{id}.exe" };

    private static string? Key(PinnedAppSetting pin) => TaskbarPins.KeyOf(pin);

    [Fact]
    public void AnEmptyList_GetsExactlyANewDock()
    {
        var (pins, added) = DockPresets.WithDefaults([]);

        Assert.Equal(DockPresets.CreateDefaults().Select(Key), pins.Select(Key));
        Assert.Equal(DockPresets.CreateDefaults().Count(pin => !pin.IsSeparator), added.Count);
    }

    [Fact]
    public void AFullSet_AddsNothing_AndLeavesTheListAsItWas()
    {
        var defaults = DockPresets.CreateDefaults();
        defaults.Insert(2, App("mine"));

        var (pins, added) = DockPresets.WithDefaults(defaults);

        Assert.Empty(added);
        Assert.Equal(defaults, pins);
    }

    [Fact]
    public void TheUsersItems_StayWhereTheyAre_AndTheMissingGoAmongTheDefaultsKept()
    {
        var defaults = DockPresets.CreateDefaults();
        var start = defaults[0];
        var downloads = defaults[3];
        var bin = defaults[^1];

        // The user's own apps, Downloads moved behind them, and Start, This PC, the user folder,
        // Settings and the separator taken off.
        List<PinnedAppSetting> list = [App("a"), App("b"), downloads, bin];

        var (pins, added) = DockPresets.WithDefaults(list);

        Assert.Equal(4, added.Count);
        Assert.DoesNotContain(added, pin => Key(pin) == Key(downloads) || Key(pin) == Key(bin));

        // Start, This PC and the user folder at the front, in order; Settings after Downloads;
        // the Recycle Bin still last, with no separator forced in front of it.
        Assert.Equal(
            [Key(start), Key(defaults[1]), Key(defaults[2]), Key(list[0]), Key(list[1]), Key(downloads), Key(defaults[4]), Key(bin)],
            pins.Select(Key));

        Assert.Same(list[0], pins[3]);
        Assert.Same(list[1], pins[4]);
    }

    [Fact]
    public void AMissingRecycleBin_GoesLast_BehindASeparator_OnlyIfTheListDoesNotEndWithOne()
    {
        var (pins, _) = DockPresets.WithDefaults([App("a")]);
        Assert.True(pins[^2].IsSeparator);
        Assert.True(DockPresets.IsRecycleBin(pins[^1].TargetPath));
        Assert.Single(pins, pin => pin.IsSeparator);

        var (ending, _) = DockPresets.WithDefaults([App("a"), DockPresets.CreateSeparator()]);
        Assert.Single(ending, pin => pin.IsSeparator);
        Assert.True(DockPresets.IsRecycleBin(ending[^1].TargetPath));
    }

    [Fact]
    public void ARenamedDefault_IsStillThere()
    {
        var downloads = DockPresets.CreateDefaults()[3];
        downloads.Label = "Stuff";

        var (pins, added) = DockPresets.WithDefaults([downloads]);

        Assert.Single(pins, pin => Key(pin) == Key(downloads));
        Assert.DoesNotContain(added, pin => Key(pin) == Key(downloads));
    }
}
