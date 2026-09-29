namespace ArtDock.Services;

/// <summary>
/// Hands memory back to Windows once the dock has gone quiet after doing something.
/// </summary>
/// <remarks>
/// <para>
/// A dock spends almost all of its life idle, and the .NET garbage collector is tuned for
/// programs that do not: it runs when an allocation budget fills, not when the program goes
/// quiet, and it keeps what it has committed ready for the next burst. So whatever the last
/// burst left behind stays in Task Manager's figure for as long as the dock sits still,
/// which is indefinitely. Measured on this machine, opening and closing the settings dialog
/// once took the dock from 40 MB to 60 MB, and it stayed there.
/// </para>
/// <para>
/// Only a third of that was the managed heap. The rest was native memory — window
/// surfaces, bitmaps, render resources — owned by the closed dialog's objects and released by
/// their finalizers, which run only after a collection has found them dead. That is why the
/// trim is a collection, then the finalizers, then a second collection: the first finds the
/// dialog, the finalizers free what it held outside the managed heap, and the second, in
/// <see cref="GCCollectionMode.Aggressive"/> mode, compacts and decommits everything left
/// over. The order matters; aggressive first and finalizers after recovered only the
/// managed part. Done this way the same dialog left nothing behind — 37 MB after, against
/// 35–37 MB for a dock whose dialog was never opened — and a live heap that was flat across
/// four opens, so there is no leak for this to be covering up.
/// </para>
/// <para>
/// What counts as a burst is read from the allocation counter rather than wired to the
/// things that cause one, so a dialog, a menu, a drag or a run of the wave are all caught
/// without any of them knowing about this. The measured rates make the two easy to tell
/// apart: an idle dock allocates about 30 KB a second — the cursor poll — and a moving wave
/// about 1.6 MB. A trim costs about 10 ms, spent on a pool thread rather than the dispatcher
/// so that waiting for the finalizers cannot hold up the dock, or deadlock against one that
/// needs it.
/// </para>
/// <para>
/// Nothing here pages memory out. Trimming the working set (<c>SetProcessWorkingSetSize</c>)
/// would make the figure drop further and mean nothing — the pages come back the next time
/// they are touched. This returns memory the dock has actually finished with.
/// </para>
/// </remarks>
internal sealed class MemoryTrim : IDisposable
{
    /// <summary>How often the allocation counter is read.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Allocated in one interval, the most that still counts as quiet. An idle dock
    /// allocates about 1 MB in 30 seconds; a second of the wave is 1.6 MB.
    /// </summary>
    private const long QuietBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Allocated since the last trim, the least worth trimming for. Keeps an idle dock to
    /// a trim every four or five minutes, rather than one per interval for nothing.
    /// </summary>
    private const long WorthTrimmingBytes = 8 * 1024 * 1024;

    private readonly Timer _timer;
    private long _lastRead;

    /// <summary>Where the counter stood at the last trim; null until the first.</summary>
    /// <remarks>
    /// Null rather than the count at startup, so the first quiet interval always trims:
    /// starting up is a burst of its own, and the one every run has.
    /// </remarks>
    private long? _lastTrim;

    public MemoryTrim()
    {
        _lastRead = GC.GetTotalAllocatedBytes();
        _timer = new Timer(_ => Check(), null, Interval, Interval);
    }

    private void Check()
    {
        var allocated = GC.GetTotalAllocatedBytes();
        var recent = allocated - _lastRead;
        _lastRead = allocated;

        if (recent > QuietBytes)
        {
            // Still busy; the burst is not over, so neither is its garbage.
            return;
        }

        if (_lastTrim is { } trimmed && allocated - trimmed < WorthTrimmingBytes)
        {
            return;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        _lastTrim = _lastRead = GC.GetTotalAllocatedBytes();
    }

    public void Dispose() => _timer.Dispose();
}
