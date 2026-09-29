using ArtDock.Dock;

namespace ArtDock.Tests;

public class DockMagnifyTests
{
    private const double Base = 36;
    private const double Max = 46.8;
    private const double Range = 80;

    [Fact]
    public void PointerOnCentre_ReachesMaxSize()
    {
        Assert.Equal(Max, DockMagnify.MagnifiedSize(0, Range, Base, Max), 6);
    }

    [Fact]
    public void PointerAtRange_FallsBackToBase()
    {
        Assert.Equal(Base, DockMagnify.MagnifiedSize(Range, Range, Base, Max), 6);
    }

    [Fact]
    public void PointerBeyondRange_StaysAtBase()
    {
        Assert.Equal(Base, DockMagnify.MagnifiedSize(Range * 3, Range, Base, Max), 6);
    }

    [Fact]
    public void ZeroRange_DisablesMagnification()
    {
        Assert.Equal(Base, DockMagnify.MagnifiedSize(0, 0, Base, Max), 6);
        Assert.Equal(Base, DockMagnify.MagnifiedSize(10, -5, Base, Max), 6);
    }

    [Fact]
    public void Midpoint_GivesExactlyHalfTheLift()
    {
        // cos^2(pi/4) is exactly 1/2, so a raised cosine hands back half its lift at half
        // its range. The plain cosine this replaced gave ~70.7% there, and — more to the
        // point — did not sum to a constant across neighbours, which is what made the dock
        // change width as the pointer moved along it.
        Assert.Equal(Base + ((Max - Base) / 2), DockMagnify.MagnifiedSize(Range / 2, Range, Base, Max), 6);
    }

    [Fact]
    public void FalloffSumsToAConstantOverEvenlySpacedNeighbours()
    {
        // The property the whole layout rests on: with the range spanning one spacing,
        // an icon's lift and its neighbour's always add up to the same amount, so the
        // total magnification does not depend on where between them the pointer sits.
        const double spacing = 55;

        for (var offset = 0d; offset <= spacing; offset += 1)
        {
            var here = DockMagnify.MagnifiedSize(offset, spacing, Base, Max) - Base;
            var next = DockMagnify.MagnifiedSize(spacing - offset, spacing, Base, Max) - Base;

            Assert.Equal(Max - Base, here + next, 6);
        }
    }

    [Fact]
    public void SizeDecreasesMonotonicallyWithDistance()
    {
        var previous = double.MaxValue;
        for (var distance = 0d; distance <= Range; distance += 4)
        {
            var size = DockMagnify.MagnifiedSize(distance, Range, Base, Max);
            Assert.True(size <= previous, $"size grew at distance {distance}");
            previous = size;
        }
    }

    [Fact]
    public void NeverExceedsMaxOrDropsBelowBase()
    {
        for (var distance = 0d; distance <= Range * 2; distance += 3)
        {
            var size = DockMagnify.MagnifiedSize(distance, Range, Base, Max);
            Assert.InRange(size, Base, Max);
        }
    }
}
