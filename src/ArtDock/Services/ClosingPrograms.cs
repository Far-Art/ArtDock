using System.IO;

namespace ArtDock.Services;

/// <summary>
/// The programs that are closing: their last window has gone and their process has not — and
/// what is waiting for them to finish.
/// </summary>
/// <remarks>
/// <para>
/// Measured 2026-10-03 on Rider, closed with its window's button: its window was hidden and
/// then destroyed within 0.6 s, and its process lived on, windowless, for 5.2 s more, holding
/// the lock that keeps Rider to one copy. Opened from the dock in that gap, the new copy met the
/// lock and stopped with an error — the census, which goes by windows, had nothing under the
/// pin, so a click launched. The taskbar waits it out. So does the dock now: a program here
/// counts as closing, its dot is hollow, and a click on it is held until it has gone.
/// </para>
/// <para>
/// It stops being closing when its processes have all exited; when it has a window again; or
/// <see cref="Limit"/> after its last window went, whichever comes first. The limit is for a
/// program that keeps running without a window on purpose — one that closes to the tray — which
/// is indistinguishable from one exiting until it does not exit: a click on one of those just
/// after closing it waits out the rest of the limit, and none after that does.
/// </para>
/// <para>
/// Pure, so that the rules can be tested without a program to close: the processes are tokens
/// whose exit is asked of <c>hasExited</c> and which are handed to <c>release</c> when no
/// longer watched — process handles, in <see cref="RunningAppsService"/>.
/// </para>
/// </remarks>
public sealed class ClosingPrograms<TProcess>(Func<TProcess, bool> hasExited, Action<TProcess> release)
{
    /// <summary>How long a program can be closing, from when its last window went.</summary>
    /// <remarks>
    /// Six seconds, asked for 2026-10-03 to shorten the wait on a program that stays in the tray;
    /// it was ten. Rider took 5.2 s with a small solution open, so this is 0.8 s over it: a
    /// program slower to close than that — Rider with a large solution, which saves more — is
    /// launched at the limit into the copy still on its way out, as before any of this.
    /// </remarks>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(6);

    private readonly Dictionary<string, (List<TProcess> Processes, DateTime Until)> _closing =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<(string Path, Action Then)> _waiting = [];

    /// <summary>Whether anything is closing, and so whether there is anything to look at.</summary>
    public bool Any => _closing.Count > 0;

    /// <summary>
    /// Takes the census's news: a program that had windows and now has none, whose processes
    /// are still running, is closing; one with windows again no longer is.
    /// </summary>
    /// <param name="lost">Each program, by image path, whose windows have all gone, with the
    /// processes still running under it.</param>
    /// <param name="withWindows">Every program that has a window now, by image path.</param>
    /// <param name="now">The time, in UTC.</param>
    /// <returns>True when what is closing changed.</returns>
    public bool Note(
        IEnumerable<(string Path, IReadOnlyList<TProcess> Processes)> lost,
        IReadOnlySet<string> withWindows,
        DateTime now)
    {
        var changed = false;
        foreach (var path in _closing.Keys.Where(withWindows.Contains).ToList())
        {
            End(path);
            changed = true;
        }

        foreach (var (path, processes) in lost)
        {
            var running = processes.Where(process => !hasExited(process)).ToList();
            foreach (var gone in processes.Except(running))
            {
                release(gone);
            }

            if (running.Count == 0)
            {
                continue;
            }

            if (_closing.Remove(path, out var before))
            {
                before.Processes.ForEach(release);
            }

            _closing[path] = (running, now + Limit);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Ends what has finished closing — exited, or out of time — and runs what was waiting for it.
    /// </summary>
    /// <returns>True when anything ended.</returns>
    public bool Check(DateTime now)
    {
        var ended = _closing
            .Where(entry => now >= entry.Value.Until || entry.Value.Processes.All(hasExited))
            .Select(entry => entry.Key)
            .ToList();

        ended.ForEach(End);
        return ended.Count > 0;
    }

    /// <summary>
    /// The image path of the closing program a pin goes by, or null when it is not closing:
    /// its target exactly, or one of the same file name — the census's own fallback, for the
    /// launcher stubs Windows ships.
    /// </summary>
    public string? Find(string? target)
    {
        if (target is not { Length: > 0 })
        {
            return null;
        }

        if (_closing.ContainsKey(target))
        {
            return target;
        }

        var fileName = Path.GetFileName(target);
        return _closing.Keys.FirstOrDefault(path =>
            string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Runs <paramref name="then"/> once the program <paramref name="target"/> goes by has
    /// finished closing, if it is closing; otherwise does nothing.
    /// </summary>
    /// <returns>True when it was held to wait.</returns>
    public bool WhenClosed(string? target, Action then)
    {
        if (Find(target) is not { } path)
        {
            return false;
        }

        _waiting.Add((path, then));
        return true;
    }

    /// <summary>Lets go of every process, and forgets what was waiting.</summary>
    public void Clear()
    {
        foreach (var path in _closing.Keys.ToList())
        {
            if (_closing.Remove(path, out var entry))
            {
                entry.Processes.ForEach(release);
            }
        }

        _waiting.Clear();
    }

    private void End(string path)
    {
        if (_closing.Remove(path, out var entry))
        {
            entry.Processes.ForEach(release);
        }

        // Taken out before any runs, since what runs may wait again.
        var ready = _waiting.Where(wait => string.Equals(wait.Path, path, StringComparison.OrdinalIgnoreCase)).ToList();
        _waiting.RemoveAll(wait => string.Equals(wait.Path, path, StringComparison.OrdinalIgnoreCase));
        foreach (var (_, then) in ready)
        {
            then();
        }
    }
}
