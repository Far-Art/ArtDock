namespace ArtDock.Dock;

/// <summary>
/// When the Windows key and Ctrl count as held: the moment they are down together with nothing
/// else — and not again, once anything else has been pressed with them, until both have been let
/// go. What the items' numbers on the dock wait for, and the dock coming up for them.
/// </summary>
/// <remarks>
/// <para>
/// At once, asked for on 2026-10-02 after an hour of waiting half a second first. The wait was so
/// that the two pressed as the start of another shortcut — Windows' own Win+Ctrl and an arrow
/// switches virtual desktops, and the dock's hotkeys are Win+Ctrl with a key — would never count;
/// now they count from the moment they are down, and the next key ends it.
/// </para>
/// <para>
/// Spent, rather than only interrupted, by anything else: a hotkey of the dock's pressed while the
/// numbers show has done what they were for, and an arrow that switched desktops is not a reason to
/// put them up again while the two are still down. A hold ended by another key is cut short
/// (<see cref="CutShort"/>): the two were the start of that key's shortcut, and what came up for
/// them goes straight back. Pure, so it can be tested; the keys are read by
/// <see cref="Interop.HeldKeys"/>.
/// </para>
/// </remarks>
public sealed class WinCtrlHold
{
    /// <summary>True once something else has been pressed with the two, until they are let go.</summary>
    private bool _spent;

    /// <summary>True while the two count as held.</summary>
    public bool IsHeld { get; private set; }

    /// <summary>
    /// True when the last hold was ended by another key pressed with the two — the start of
    /// another shortcut — rather than by their being let go or by a hotkey of the dock's; until a
    /// hold starts again.
    /// </summary>
    public bool CutShort { get; private set; }

    /// <summary>Takes one look at the keys.</summary>
    /// <param name="down">Whether the Windows key and Ctrl are both down.</param>
    /// <param name="other">Whether anything else is down that ends the hold.</param>
    /// <returns>True when <see cref="IsHeld"/> has changed.</returns>
    public bool Look(bool down, bool other)
    {
        if (!down)
        {
            _spent = false;
            return Set(false);
        }

        if (other && !_spent)
        {
            _spent = true;
            CutShort = IsHeld;
            return Set(false);
        }

        if (_spent)
        {
            return Set(false);
        }

        CutShort = false;
        return Set(true);
    }

    /// <summary>
    /// Something else was pressed with the two, which the keys cannot see — a hotkey of the dock's:
    /// the hold ends, not cut short, and does not start again until they have been let go.
    /// </summary>
    /// <returns>True when <see cref="IsHeld"/> has changed.</returns>
    public bool Spend()
    {
        _spent = true;
        CutShort = false;
        return Set(false);
    }

    private bool Set(bool held)
    {
        if (IsHeld == held)
        {
            return false;
        }

        IsHeld = held;
        return true;
    }
}
