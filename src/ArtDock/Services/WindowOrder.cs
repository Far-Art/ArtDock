namespace ArtDock.Services;

/// <summary>
/// Keeps an app's windows in the order they were first seen, which does not change as the user
/// moves between them.
/// </summary>
/// <remarks>
/// <para>
/// The census lists windows as <c>EnumWindows</c> finds them, which is front to back: every
/// switch between two of an app's windows swaps them. That is the order the dock's click cycles
/// in (<see cref="RunningAppsService.NextWindow"/>), and it stays so. The previews are another
/// matter — a row of cards that reshuffled itself whenever one was clicked could not be learned,
/// and the taskbar's does not.
/// </para>
/// <para>
/// Windows first seen in the same census are numbered back to front, so of a program's windows
/// already open when the dock starts, the one furthest back — likeliest the oldest — comes first.
/// </para>
/// </remarks>
public static class WindowOrder
{
    /// <summary>
    /// Numbers the windows not seen before and forgets the ones gone.
    /// </summary>
    /// <param name="seen">Each window's number, by handle; brought up to date in place.</param>
    /// <param name="census">Every window open now, front to back.</param>
    /// <param name="next">The number the next new window takes.</param>
    /// <returns>The number the next new window after these takes.</returns>
    public static long Note(Dictionary<nint, long> seen, IReadOnlyList<nint> census, long next)
    {
        var present = new HashSet<nint>(census);
        foreach (var window in seen.Keys.Where(window => !present.Contains(window)).ToList())
        {
            seen.Remove(window);
        }

        for (var i = census.Count - 1; i >= 0; i--)
        {
            if (seen.TryAdd(census[i], next))
            {
                next++;
            }
        }

        return next;
    }

    /// <summary>The windows in the order they were first seen; any never numbered last, as given.</summary>
    public static IReadOnlyList<nint> Arrange(IReadOnlyList<nint> windows, IReadOnlyDictionary<nint, long> seen) =>
        [.. windows
            .Select((window, index) => (window, index))
            .OrderBy(entry => seen.TryGetValue(entry.window, out var number) ? number : long.MaxValue)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.window)];
}
