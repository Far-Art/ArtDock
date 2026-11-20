namespace ArtDock.Dock;

/// <summary>What <see cref="PreviewPeek.Look"/> asks for.</summary>
public enum PeekMove
{
    /// <summary>Leave the peek as it is.</summary>
    None,

    /// <summary>Peek at <see cref="PreviewPeek.Showing"/> — a first peek, or another window at once.</summary>
    Show,

    /// <summary>End the peek.</summary>
    End,
}

/// <summary>
/// When resting on a window's card in the previews peeks at that window, as the taskbar's do:
/// after a moment on the card, then at once from card to card, and over as the pointer leaves them.
/// </summary>
/// <remarks>
/// Pure, fed a look at the pointer with the window whose card it is on, like
/// <see cref="PreviewDwell"/>. Leaving the cards ends the peek only after <see cref="Grace"/>, so
/// crossing the gap between two cards goes from one window to the next rather than back to the
/// desktop and out again.
/// </remarks>
public sealed class PreviewPeek
{
    private long _candidate;
    private long _since;
    private long? _offSince;

    /// <summary>How long the pointer rests on a card before its window is peeked at, in milliseconds.</summary>
    public long Delay { get; set; } = 500;

    /// <summary>How long it may be off the cards before the peek ends.</summary>
    public long Grace { get; set; } = 150;

    /// <summary>The window peeked at; zero while there is no peek.</summary>
    public long Showing { get; private set; }

    /// <summary>Takes one look at the pointer.</summary>
    /// <param name="now">The time, in milliseconds, from any fixed start.</param>
    /// <param name="under">The window whose card the pointer is on; zero when it is on none.</param>
    public PeekMove Look(long now, long under)
    {
        if (Showing != 0)
        {
            if (under != 0)
            {
                _offSince = null;
                if (under == Showing)
                {
                    return PeekMove.None;
                }

                Showing = under;
                return PeekMove.Show;
            }

            _offSince ??= now;
            if (now - _offSince.Value < Grace)
            {
                return PeekMove.None;
            }

            Stop();
            return PeekMove.End;
        }

        if (under == 0)
        {
            _candidate = 0;
            return PeekMove.None;
        }

        if (under != _candidate)
        {
            _candidate = under;
            _since = now;
        }

        if (now - _since < Delay)
        {
            return PeekMove.None;
        }

        Showing = under;
        _candidate = 0;
        _offSince = null;
        return PeekMove.Show;
    }

    /// <summary>Forgets the peek and the wait for one; true when a peek was showing.</summary>
    public bool Stop()
    {
        var was = Showing != 0;
        Showing = 0;
        _candidate = 0;
        _offSince = null;
        return was;
    }
}
