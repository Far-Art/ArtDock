using ArtDock.Dock;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers what counts as a geometry change.
/// </summary>
/// <remarks>
/// Every control in the settings dialog goes through one appearance path, and that path
/// skips the layout when the metrics have not moved. Colour and opacity changes therefore
/// have to produce metrics equal to the ones already in force — otherwise the dock re-lays
/// itself out and repaints for a slider that has not moved a single icon, which is what made
/// the colour, opacity and roundness controls flicker.
/// </remarks>
public class DockMetricsIdentityTests
{
    [Fact]
    public void Metrics_CompareByValue()
    {
        // A record, and the early-out depends on it staying one.
        Assert.Equal(new DockMetrics { BaseSize = 50 }, new DockMetrics { BaseSize = 50 });
        Assert.NotEqual(new DockMetrics { BaseSize = 50 }, new DockMetrics { BaseSize = 51 });
    }

    [Fact]
    public void ChangingTheColour_LeavesTheGeometryAlone()
    {
        var before = new DockSettings { BarColor = "#111111" };
        var after = new DockSettings { BarColor = "#EEEEEE" };

        Assert.Equal(before.Metrics, after.Metrics);
    }

    [Fact]
    public void ChangingTheOpacity_LeavesTheGeometryAlone()
    {
        var before = new DockSettings { BarOpacity = 0.4 };
        var after = new DockSettings { BarOpacity = 0.9 };

        Assert.Equal(before.Metrics, after.Metrics);
    }

    [Fact]
    public void ChangingTheRoundness_IsAGeometryChange()
    {
        // The counter-case: roundness really does belong to the metrics, so it must not be
        // skipped along with the colours.
        var before = new DockSettings { BarRoundness = 0 };
        var after = new DockSettings { BarRoundness = 1 };

        Assert.NotEqual(before.Metrics, after.Metrics);
    }

    [Fact]
    public void ChangingTheIconSize_IsAGeometryChange()
    {
        var before = new DockSettings { BaseSize = 40 };
        var after = new DockSettings { BaseSize = 64 };

        Assert.NotEqual(before.Metrics, after.Metrics);
    }

    [Fact]
    public void SavedColours_AreCappedAtTheWidthOfTheStockRow()
    {
        Assert.Equal(DockSettings.MaxCustomColors, BarPalette.Swatches.Count);
    }
}
