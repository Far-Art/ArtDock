namespace ArtDock.Dock;

/// <summary>
/// How often the wave is drawn where every frame of it is the processor's to draw: about thirty
/// times a second under <c>DockSettings.NoGpu</c>, and every frame WPF offers otherwise.
/// </summary>
/// <remarks>
/// <para>
/// What a frame of the wave costs is mostly in sending it: the dock's window is transparent, so
/// the whole of it goes to the compositor again each time anything in it moves. That is paid
/// per frame, whatever the frame changed, so the one thing that halves it is drawing half as
/// many. Everything the wave does is worked out from the time that has passed and not from a
/// count of frames, so it moves as far and as fast at either pace; it only does so in fewer
/// steps.
/// </para>
/// <para>
/// WPF offers frames at the display's own pace and no other, so a cap cannot ask for one every
/// thirtieth of a second — it can only take or pass over the ones that come. Hence
/// <see cref="CappedFrameMs"/> is thirty milliseconds and not thirty-three and a third: the frame
/// wanted is the second at 60 Hz and the fourth at 120, each of which arrives 33.3 ms after the
/// last one drawn, give or take the timer — and a threshold of 33.3 would lose every one that
/// came a hair early, and take the next instead, for twenty frames a second.
/// </para>
/// <para>
/// Pure, and fed the time, so it can be tested without a screen.
/// </para>
/// </remarks>
public static class WavePace
{
    /// <summary>
    /// The least time from one drawn frame to the next under the cap, in milliseconds.
    /// </summary>
    public const double CappedFrameMs = 30;

    /// <summary>
    /// Whether a frame offered <paramref name="sinceLastMs"/> after the last one drawn is drawn,
    /// when no two are to be closer than <paramref name="leastMs"/> — zero for no cap at all.
    /// </summary>
    public static bool Draws(double sinceLastMs, double leastMs) =>
        leastMs <= 0 || sinceLastMs >= leastMs;
}
