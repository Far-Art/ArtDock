using System.Windows.Threading;
using ArtDock.Interop;

namespace ArtDock.Services;

/// <summary>What a launch of ArtDock asks of the dock, by the switch it was given.</summary>
internal enum LaunchRequest
{
    /// <summary>No switch: the dock, or word of where it is.</summary>
    Plain,

    /// <summary><c>--settings</c>: the settings dialog.</summary>
    Settings,

    /// <summary><c>--toggle</c>: hide the dock, or show it, as its hotkey does.</summary>
    Toggle,

    /// <summary><c>--keyboard</c>: the dock takes the keyboard, or gives it back, as its hotkey does.</summary>
    Keyboard
}

/// <summary>
/// Keeps one ArtDock to a session, and tells the copy already running that it was launched
/// again.
/// </summary>
/// <remarks>
/// <para>
/// Two docks is not a harmless duplicate: they overlap on the same screen edge, fight over
/// the same settings file — last writer wins, silently — and both claim the notification
/// area, so the tray shows two identical icons and neither says which is which. The
/// application is a singleton by nature, and this is where that is stated.
/// </para>
/// <para>
/// The claim is a named mutex rather than a process scan, because a process list has a race
/// in it: two copies started together can each look, each see nothing, and each carry on.
/// The kernel resolves the mutex for us instead.
/// </para>
/// <para>
/// What a second launch should do is the running instance's decision, because only it knows
/// whether its dock can be seen. So the copy that loses the race does not act; it reports
/// the launch and leaves. There is a report for a plain launch, and one for each switch that
/// asks something of the dock — <c>--settings</c>, which keeps the flag's meaning across the
/// boundary, since the dialog is opened by the copy that has one; and <c>--toggle</c> and
/// <c>--keyboard</c>, which do what the dock's hotkeys do, so a mouse button, a macro pad or a
/// script can do it without a key registered. Another comes from the uninstaller rather than a
/// launch, and asks the dock to exit; see <see cref="CloseRunning"/>.
/// </para>
/// <para>
/// The reports are named events rather than window messages. A registered message broadcast
/// to <c>HWND_BROADCAST</c> is the traditional way to do this, and it does not work here:
/// broadcasts reach only <i>unowned</i> top-level windows, and WPF parents every window it
/// shows to a hidden parking window of its own — so the dock is owned, and never sees them.
/// An event has no such rule, needs no window handle to be found first, and is what the
/// kernel offers for exactly this.
/// </para>
/// </remarks>
internal static class SingleInstance
{
    /// <summary>
    /// The names the claim and the reports are made under.
    /// </summary>
    /// <remarks>
    /// Deliberately session-local rather than <c>Global\</c>: two users signed in at once
    /// each get their own dock on their own desktop, which is right, and a global claim
    /// would let whoever logged in first lock the other one out.
    /// </remarks>
    private const string MutexName = @"Local\ArtDock.SingleInstance";

    private const string RelaunchedName = @"Local\ArtDock.Relaunched";

    private const string ShowSettingsName = @"Local\ArtDock.ShowSettings";

    private const string ExitName = @"Local\ArtDock.Exit";

    private const string ToggleName = @"Local\ArtDock.Toggle";

    private const string KeyboardName = @"Local\ArtDock.Keyboard";

    private static Mutex? _claim;

    private static readonly List<(EventWaitHandle Event, RegisteredWaitHandle Watch)> _listeners = [];

    /// <summary>Claims the session for this process.</summary>
    /// <returns>False when another copy already holds it.</returns>
    public static bool TryClaim()
    {
        // Initially owned, so the claim is taken in the same call that creates it and there
        // is no window between the two for a second copy to slip through.
        var claim = new Mutex(initiallyOwned: true, MutexName, out var created);

        if (!created)
        {
            claim.Dispose();
            return false;
        }

        _claim = claim;
        return true;
    }

    /// <summary>
    /// Starts listening for later launches.
    /// </summary>
    /// <remarks>
    /// Both callbacks run on the calling thread's dispatcher, so they can touch windows
    /// directly — the waits themselves are satisfied on thread-pool threads, which cannot.
    /// </remarks>
    /// <param name="onRelaunched">A plain second launch.</param>
    /// <param name="onShowSettings">A second launch with <c>--settings</c>.</param>
    /// <param name="onExitRequested">The uninstaller asking the dock to go; see <see cref="CloseRunning"/>.</param>
    /// <param name="onToggle">A second launch with <c>--toggle</c>.</param>
    /// <param name="onKeyboard">A second launch with <c>--keyboard</c>.</param>
    public static void Listen(
        Action onRelaunched, Action onShowSettings, Action onExitRequested, Action onToggle, Action onKeyboard)
    {
        if (_claim is null || _listeners.Count > 0)
        {
            return;
        }

        var dispatcher = Dispatcher.CurrentDispatcher;

        Watch(RelaunchedName, onRelaunched, dispatcher);
        Watch(ShowSettingsName, onShowSettings, dispatcher);
        Watch(ExitName, onExitRequested, dispatcher);
        Watch(ToggleName, onToggle, dispatcher);
        Watch(KeyboardName, onKeyboard, dispatcher);
    }

    /// <summary>What a launch with these arguments asks for: the first switch there is, of the ones the dock knows.</summary>
    public static LaunchRequest RequestOf(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            switch (arg.ToLowerInvariant())
            {
                case "--settings":
                    return LaunchRequest.Settings;
                case "--toggle":
                    return LaunchRequest.Toggle;
                case "--keyboard":
                    return LaunchRequest.Keyboard;
            }
        }

        return LaunchRequest.Plain;
    }

    private static void Watch(string name, Action action, Dispatcher dispatcher)
    {
        // Auto-reset: each launch is one request, and the handle is armed again the moment
        // this one has been taken.
        var wake = new EventWaitHandle(false, EventResetMode.AutoReset, name);

        var watch = ThreadPool.RegisterWaitForSingleObject(
            wake,
            (_, _) => dispatcher.BeginInvoke(action),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);

        _listeners.Add((wake, watch));
    }

    /// <summary>Tells the instance already running that it was launched again, and what for.</summary>
    public static void NotifyExisting(LaunchRequest request)
    {
        var name = request switch
        {
            LaunchRequest.Settings => ShowSettingsName,
            LaunchRequest.Toggle => ToggleName,
            LaunchRequest.Keyboard => KeyboardName,
            _ => RelaunchedName
        };

        if (!EventWaitHandle.TryOpenExisting(name, out var wake))
        {
            // The claim is held by a copy that has not finished starting, or is on its way
            // out. Either way there is nothing to talk to, and this process is leaving.
            return;
        }

        using (wake)
        {
            // Given up before the event is set, not after: the running instance acts on it
            // at once, and by then this process may already be gone. Without it that
            // instance is a background process asking to come forward, which Windows
            // answers by flashing a taskbar button — and a tray app has none to flash.
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            wake.Set();
        }
    }

    /// <summary>
    /// Asks the dock running in this session, if there is one, to exit the way its own Exit
    /// does, and waits for it to have gone.
    /// </summary>
    /// <returns>
    /// True when no dock is running any more; false when one still was as the wait ran out.
    /// </returns>
    /// <remarks>
    /// <para>
    /// For the uninstaller, which must not leave a dock on screen after the program is gone —
    /// nor one that would write the settings straight back if the user had them deleted.
    /// </para>
    /// <para>
    /// Waits on the claim rather than on a process, since the claim is the one thing a dock
    /// always lets go of on its way out and nothing else holds. A dock that was killed rather
    /// than asked — Velopack stops the copy it is uninstalling before the uninstaller gets
    /// here — leaves the claim abandoned, which the wait reports by throwing, and which is
    /// just as gone. Either way the wait takes the claim, so it is let go of again at once.
    /// </para>
    /// <para>
    /// Never call this from a test: the names are the real ones, and it would close the
    /// dock the user has running.
    /// </para>
    /// </remarks>
    public static bool CloseRunning(TimeSpan wait)
    {
        if (!Mutex.TryOpenExisting(MutexName, out var claim))
        {
            return true;
        }

        using (claim)
        {
            if (EventWaitHandle.TryOpenExisting(ExitName, out var exit))
            {
                using (exit)
                {
                    exit.Set();
                }
            }

            try
            {
                if (!claim.WaitOne(wait))
                {
                    return false;
                }
            }
            catch (AbandonedMutexException)
            {
                // Its holder died without letting go. The wait has taken it all the same.
            }

            claim.ReleaseMutex();
            return true;
        }
    }

    /// <summary>Releases the claim, so the next launch is free to take it.</summary>
    public static void Release()
    {
        foreach (var (wake, watch) in _listeners)
        {
            watch.Unregister(null);
            wake.Dispose();
        }

        _listeners.Clear();

        if (_claim is not { } claim)
        {
            return;
        }

        _claim = null;

        try
        {
            claim.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not ours to release — only reachable if the claim was somehow taken on
            // another thread. Disposing still lets go of the handle, which is what matters.
        }

        claim.Dispose();
    }
}
