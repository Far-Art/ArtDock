using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers what a click on an item asks for, by its button and the keys held with it — read as
/// the taskbar reads them.
/// </summary>
public class DockClickTests
{
    [Theory]
    [InlineData(false, false, false, DockClick.Open)]
    [InlineData(false, true, false, DockClick.Open)]
    [InlineData(false, false, true, DockClick.NewWindow)]
    [InlineData(true, false, false, DockClick.NewWindow)]
    [InlineData(true, true, false, DockClick.NewWindow)]
    [InlineData(true, false, true, DockClick.NewWindow)]
    [InlineData(false, true, true, DockClick.AsAdministrator)]
    [InlineData(true, true, true, DockClick.AsAdministrator)]
    public void AClick_MeansWhatItDoesOnTheTaskbar(bool middle, bool control, bool shift, DockClick click) =>
        Assert.Equal(click, DockClicks.Of(middle, control, shift));
}
