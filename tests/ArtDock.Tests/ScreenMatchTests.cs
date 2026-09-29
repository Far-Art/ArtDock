using System.Windows;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers finding the display the dock was put on, when Windows has renamed the displays.
/// </summary>
/// <remarks>
/// <para>
/// The dock used to remember its display by name alone, and on the machine it was written on
/// the two displays swapped <c>\\.\DISPLAY1</c> and <c>\\.\DISPLAY2</c> across a wake from
/// sleep. A stored name that still resolves resolves to the other monitor, so nothing fell
/// back and the dock simply changed screens. It now stores the monitor's device path too.
/// </para>
/// <para>
/// The properties worth holding: the path wins over a name that has moved; a file written
/// before paths were stored goes by the name exactly as it did; a path that finds nothing
/// falls through to the name; nothing found is null, which is the caller's cue for the main
/// display; and two identical monitors, whose paths differ only by connector, stay apart.
/// </para>
/// </remarks>
public class ScreenMatchTests
{
    private const string Interface = "#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    // The two paths read from this machine's monitors: the same adapter, different panels.
    private const string MainPath = @"\\?\DISPLAY#SAM7058#5&2ba4285c&0&UID41219" + Interface;
    private const string FourKPath = @"\\?\DISPLAY#SAM0F35#5&2ba4285c&0&UID41217" + Interface;

    private static ScreenInfo Screen(string name, string? path, bool primary = false) =>
        new(name, name, new Rect(0, 0, 100, 100), new Rect(0, 0, 100, 100), primary, path);

    /// <summary>The displays as named on a morning after the names have swapped.</summary>
    private static readonly IReadOnlyList<ScreenInfo> Swapped =
    [
        Screen(@"\\.\DISPLAY1", MainPath, primary: true),
        Screen(@"\\.\DISPLAY2", FourKPath)
    ];

    [Fact]
    public void AfterTheNamesSwap_ThePathStillFindsTheSameMonitor()
    {
        // Written while the 4K display was DISPLAY1; that name is now the main display.
        var found = Screens.Match(Swapped, @"\\.\DISPLAY1", FourKPath);

        Assert.Same(Swapped[1], found);
    }

    [Fact]
    public void AFileWithoutAPath_GoesByTheName()
    {
        var found = Screens.Match(Swapped, @"\\.\DISPLAY2", devicePath: null);

        Assert.Same(Swapped[1], found);
    }

    [Fact]
    public void TheNameIsMatchedWithoutRegardToCase()
    {
        var found = Screens.Match(Swapped, @"\\.\display2", devicePath: null);

        Assert.Same(Swapped[1], found);
    }

    [Fact]
    public void APathThatFindsNothing_FallsThroughToTheName()
    {
        // The monitor moved to another connector, which changes its path, or the file came
        // from another machine: the name is the route the dock always took before.
        var found = Screens.Match(Swapped, @"\\.\DISPLAY2", @"\\?\DISPLAY#DEL4321#1&1&0&UID1" + Interface);

        Assert.Same(Swapped[1], found);
    }

    [Fact]
    public void AScreenWindowsGivesNoPathFor_IsStillFoundByName()
    {
        IReadOnlyList<ScreenInfo> screens =
        [
            Screen(@"\\.\DISPLAY1", MainPath, primary: true),
            Screen(@"\\.\DISPLAY2", path: null)
        ];

        var found = Screens.Match(screens, @"\\.\DISPLAY2", FourKPath);

        Assert.Same(screens[1], found);
    }

    [Fact]
    public void NothingStoredOrNothingFound_IsNull()
    {
        Assert.Null(Screens.Match(Swapped, deviceName: null, devicePath: null));
        Assert.Null(Screens.Match(Swapped, @"\\.\DISPLAY9", @"\\?\DISPLAY#GONE#0" + Interface));
    }

    [Fact]
    public void TwoIdenticalMonitors_AreToldApartByTheWholePath()
    {
        // Same make and model, so the same EDID: only the connector differs.
        const string left = @"\\?\DISPLAY#SAM0F35#5&2ba4285c&0&UID41217" + Interface;
        const string right = @"\\?\DISPLAY#SAM0F35#5&2ba4285c&0&UID41218" + Interface;

        IReadOnlyList<ScreenInfo> screens =
        [
            Screen(@"\\.\DISPLAY1", left, primary: true),
            Screen(@"\\.\DISPLAY2", right)
        ];

        Assert.Same(screens[1], Screens.Match(screens, @"\\.\DISPLAY1", right));
        Assert.Same(screens[0], Screens.Match(screens, @"\\.\DISPLAY2", left));
    }
}
