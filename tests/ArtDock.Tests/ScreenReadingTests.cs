using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers the two ways the dock reads its displays agreeing with each other, on this machine's
/// own displays.
/// </summary>
/// <remarks>
/// <para>
/// The dock is placed by <see cref="Screens.All"/>, and asks four times a second, through the
/// cheaper <see cref="Screens.At"/> and <see cref="Screens.Count"/>, whether its display is
/// still what it was placed on (<c>DockWindow.KeepOnDisplay</c>) — re-placing it when not. If
/// the two readings ever differed for a display that had not changed, the dock would be
/// re-placed on every look, for good. So each display <see cref="Screens.All"/> lists must read
/// back the same through <see cref="Screens.At"/>, and the counts must agree.
/// </para>
/// <para>
/// In the test host's own scale, which need not be the dock's: the property is that the two
/// agree within one process, whatever its scale.
/// </para>
/// </remarks>
public class ScreenReadingTests
{
    [Fact]
    public void EveryDisplay_ReadsBackTheSameWhereItLies()
    {
        var screens = Screens.All();
        Assert.NotEmpty(screens);

        foreach (var screen in screens)
        {
            var now = Screens.At(screen.Bounds);

            Assert.NotNull(now);
            Assert.Equal(screen.Bounds, now.Value.Bounds);
            Assert.Equal(screen.WorkArea, now.Value.WorkArea);
        }
    }

    [Fact]
    public void TheCount_IsHowManyAreListed()
    {
        Assert.Equal(Screens.All().Count, Screens.Count);
    }

    [Fact]
    public void ExactlyOneIsTheMainDisplay_AndEachHasItsName()
    {
        var screens = Screens.All();

        Assert.Single(screens, screen => screen.IsPrimary);
        Assert.All(screens, screen => Assert.StartsWith(@"\\.\DISPLAY", screen.DeviceName));
    }
}
