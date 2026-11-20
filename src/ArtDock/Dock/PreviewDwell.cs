namespace ArtDock.Dock;

/// <summary>What <see cref="PreviewDwell.Look"/> asks of the window previews.</summary>
public enum PreviewMove
{
    /// <summary>Leave them as they are.</summary>
    None,

    /// <summary>Open them for <see cref="PreviewDwell.Showing"/>.</summary>
    Open,

    /// <summary>They are open: show another item's instead, <see cref="PreviewDwell.Showing"/>.</summary>
    Switch,

    /// <summary>Close them.</summary>
    Close,
}

/// <summary>
/// When the previews of an app's windows open, move to another app and close: the pointer
/// resting on a running item opens them, as the taskbar's do, and leaving the item, the gap above
/// it and the panel closes them.
/// </summary>
/// <remarks>
/// <para>
/// Fed a look at the pointer on every poll of the dock (<c>DockBar.Polled</c>) — no timer of its
/// own, the way <see cref="WinCtrlHold"/> is fed the keys — and pure, so every rule can be tested
/// with a clock that is only a number.
/// </para>
/// <para>
/// Moving to another app waits a moment of its own (<see cref="SwitchTime"/>) rather than
/// following the pointer at once, as the taskbar's do: the panel stands above the icons, and the
/// way up to a card over a neighbour cuts across that neighbour's icon on the way.
/// </para>
/// <para>
/// Closing is by the pointer being elsewhere for <see cref="LeaveTime"/>, the dock's *Hide delay*,
/// or by <see cref="Dismiss"/> for everything that is not the pointer — a click, a menu, a drag.
/// A dismissed item does not open again until the pointer has left it: a click on an icon raises
/// a window, and the previews coming back over it while the pointer is still there would be in
/// the way of the window just asked for.
/// </para>
/// </remarks>
public sealed class PreviewDwell
{
    /// <summary>The item the pointer is resting on, and since when; null when none.</summary>
    private string? _candidate;

    private long _since;

    /// <summary>Since when the pointer has been nowhere that keeps the previews open.</summary>
    private long? _awaySince;

    /// <summary>An item dismissed while the pointer was on it, until the pointer leaves it.</summary>
    private string? _dismissed;

    /// <summary>How long the pointer rests on an item before its previews open, in milliseconds.</summary>
    public long HoverTime { get; set; } = 400;

    /// <summary>
    /// How long it rests on another item before the open previews move to it: the taskbar's,
    /// measured at 210 to 230 ms from the pointer entering a button to the glide starting, a frame
    /// or two of which is the measuring (TODO.md item 25).
    /// </summary>
    public long SwitchTime { get; set; } = 200;

    /// <summary>How long it may be away before the previews close.</summary>
    public long LeaveTime { get; set; } = 700;

    /// <summary>
    /// How long the previews wait on their own once the pointer has gone: the taskbar's, measured
    /// at 350 to 450 ms (TODO.md item 25) — and never longer than the dock's own hide delay.
    /// </summary>
    public const long OwnLeaveTime = 400;

    /// <summary>
    /// How soon after the previews' own wait the dock must be due to hide for them to wait for it
    /// and go with it, rather than go first.
    /// </summary>
    /// <remarks>
    /// Close together they look like one hide gone wrong — measured 46 to 194 ms apart, the eye
    /// caught it (2026-11-20) — so the dock leads. Far apart they are two things: with a *Hide
    /// delay* of three seconds, previews waiting for the dock stood over the desktop all that time.
    /// </remarks>
    public const long TogetherWithin = 400;

    /// <summary>
    /// How much longer than the dock's hide delay previews waiting for the dock wait on their own:
    /// long enough for the dock always to go first and take them with it, and short enough to close
    /// them anyway when it does not go — held up by the settings dialog, say. The dock was measured
    /// starting up to 230 ms after its delay.
    /// </summary>
    public const long DockLead = 500;

    /// <summary>
    /// The <see cref="LeaveTime"/> to use: the previews' own wait, or — when the dock hides, and
    /// soon after that — the dock's hide delay and a margin, so the dock goes first and takes the
    /// previews with it.
    /// </summary>
    /// <param name="dockHideDelay">The dock's *Hide delay*, in milliseconds.</param>
    /// <param name="dockHides">Whether the dock hides when the pointer leaves it: auto-hide on, or yielding to a window in front.</param>
    public static long LeaveTimeFor(long dockHideDelay, bool dockHides)
    {
        var own = Math.Min(Math.Max(0, dockHideDelay), OwnLeaveTime);
        return dockHides && dockHideDelay - own <= TogetherWithin ? dockHideDelay + DockLead : own;
    }

    /// <summary>The item whose previews are open; null while they are closed.</summary>
    public string? Showing { get; private set; }

    /// <summary>Takes one look at the pointer.</summary>
    /// <param name="now">The time, in milliseconds, from any fixed start.</param>
    /// <param name="under">
    /// The item under the pointer on the dock, if it is one whose app is running; null otherwise.
    /// </param>
    /// <param name="onPanel">Whether the pointer is on the panel, or in the gap between it and the item.</param>
    /// <param name="pressed">Whether a mouse button is down.</param>
    /// <returns>What to do; the item is <see cref="Showing"/>.</returns>
    public PreviewMove Look(long now, string? under, bool onPanel, bool pressed)
    {
        if (_dismissed is not null && under != _dismissed)
        {
            _dismissed = null;
        }

        if (under is not null && under == _dismissed)
        {
            under = null;
        }

        if (Showing is null)
        {
            return LookClosed(now, under, pressed);
        }

        if (onPanel || under == Showing)
        {
            _awaySince = null;
            _candidate = null;
            return PreviewMove.None;
        }

        if (under is not null && !pressed)
        {
            _awaySince = null;
            if (!Rested(now, under, SwitchTime))
            {
                return PreviewMove.None;
            }

            Showing = under;
            _candidate = null;
            return PreviewMove.Switch;
        }

        _candidate = null;
        _awaySince ??= now;
        if (now - _awaySince.Value < LeaveTime)
        {
            return PreviewMove.None;
        }

        Close();
        return PreviewMove.Close;
    }

    /// <summary>
    /// Closes the previews for anything but the pointer — and, when the pointer is on an item,
    /// keeps that item's from opening again until it has left.
    /// </summary>
    /// <param name="under">The item under the pointer, running or not; null when none.</param>
    /// <returns>True when the previews were open.</returns>
    public bool Dismiss(string? under)
    {
        _dismissed = under;
        var wasOpen = Showing is not null;
        Close();
        return wasOpen;
    }

    private PreviewMove LookClosed(long now, string? under, bool pressed)
    {
        if (under is null || pressed)
        {
            _candidate = null;
            return PreviewMove.None;
        }

        if (!Rested(now, under, HoverTime))
        {
            return PreviewMove.None;
        }

        Showing = under;
        _candidate = null;
        _awaySince = null;
        return PreviewMove.Open;
    }

    /// <summary>Whether the pointer has rested on the item for the time, starting the count if it has just arrived.</summary>
    private bool Rested(long now, string item, long time)
    {
        if (_candidate != item)
        {
            _candidate = item;
            _since = now;
        }

        return now - _since >= time;
    }

    private void Close()
    {
        Showing = null;
        _candidate = null;
        _awaySince = null;
    }
}
