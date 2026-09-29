namespace ArtDock.Dock;

/// <summary>Cosine-falloff magnification, the heart of the Dock effect.</summary>
public static class DockMagnify
{
    /// <summary>
    /// Computes the magnified size for one icon.
    ///
    /// Uses a cosine falloff: the size peaks at <paramref name="maxSize"/> when the pointer
    /// sits on the icon's centre (<paramref name="distance"/> of 0) and eases back to
    /// <paramref name="baseSize"/> once the pointer is <paramref name="influenceRange"/>
    /// pixels away or further. That is what produces the smooth wave lifting the hovered
    /// icon together with its neighbours, rather than one icon popping alone.
    /// </summary>
    /// <param name="distance">Absolute distance between the pointer and the icon's resting centre.</param>
    /// <param name="influenceRange">Radius over which neighbours are affected.</param>
    /// <param name="baseSize">Resting icon size.</param>
    /// <param name="maxSize">Fully magnified icon size.</param>
    public static double MagnifiedSize(
        double distance, double influenceRange, double baseSize, double maxSize)
    {
        if (influenceRange <= 0 || distance >= influenceRange)
        {
            return baseSize;
        }

        // Raised cosine, not a plain one. Two properties matter, and a plain cosine has
        // neither.
        //
        // It reaches the edge of its influence with zero slope, so an icon eases out of
        // the wave instead of stopping dead — a plain cosine still has real slope at the
        // boundary, and that kink is visible as the wave passes an icon.
        //
        // More importantly, cos^2 sums to a constant over evenly spaced neighbours
        // (cos^2 + sin^2 = 1), provided the range spans a whole number of icon pitches.
        // That makes the *total* magnification independent of where the pointer sits, so
        // the dock stops breathing in and out as the pointer moves between icons, while
        // the icons themselves still spread apart the way they should.
        var phase = Math.Cos(distance / influenceRange * (Math.PI / 2));
        return baseSize + ((maxSize - baseSize) * phase * phase);
    }
}
