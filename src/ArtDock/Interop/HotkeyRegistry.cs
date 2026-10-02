using ArtDock.Services;

namespace ArtDock.Interop;

/// <summary>
/// The dock's hotkeys as Windows has them: registered against one window, let go of while they
/// must not be, and the ones Windows would not give.
/// </summary>
/// <remarks>
/// <para>
/// A registration that fails has to show, and this is where it is caught. At sign-in Explorer
/// has registered its own before the dock starts, so a default that Windows takes for itself in
/// some later update fails here, quietly, every time. <see cref="Taken"/> is what the settings
/// dialog says beside the hotkey: in use by another program. Nothing else says it — not a
/// notice at startup.
/// </para>
/// <para>
/// Let go of, rather than ignored, while they must not act. <c>RegisterHotKey</c> takes the key
/// from whatever is in front, so a hotkey that is only ignored is still a key the program in
/// front never saw: while the settings dialog records a new one, so that pressing the one being
/// changed reaches the box rather than firing; and while a program on the Exclusions page is in
/// front, filling a display or not, which the dock stays down for and whose keys are its own.
/// </para>
/// <para>
/// Cheap when nothing has changed, which is nearly always: the settings are applied on every
/// tick of every slider in the settings dialog, and what is in front is looked at four times a
/// second. Windows is asked again only when what is wanted changes, or the dock stops having to
/// let them go — which is also when a hotkey taken before is tried again.
/// </para>
/// </remarks>
public sealed class HotkeyRegistry : IDisposable
{
    /// <summary>
    /// The first id. Ids are the window's to choose, from 0 to 0xBFFF; each action's is this
    /// plus its number.
    /// </summary>
    private const int FirstId = 1;

    private readonly nint _hwnd;

    /// <summary>What the settings in force ask for.</summary>
    private Dictionary<HotkeyAction, Hotkey> _wanted = [];

    /// <summary>What Windows has registered for the dock now.</summary>
    private readonly Dictionary<HotkeyAction, Hotkey> _registered = [];

    /// <summary>What it would not register, when it was last asked.</summary>
    private readonly Dictionary<HotkeyAction, Hotkey> _taken = [];

    private bool _recording;
    private bool _standingDown;
    private bool _disposed;

    /// <param name="hwnd">The window to hear them in — or 0 for the calling thread's queue, as a test does.</param>
    public HotkeyRegistry(nint hwnd) => _hwnd = hwnd;

    /// <summary>Raised when <see cref="Taken"/> has changed.</summary>
    public event EventHandler? TakenChanged;

    /// <summary>
    /// The hotkeys Windows would not register when last asked — another program has them — by
    /// action. Kept while they are let go of, since nothing has been asked since.
    /// </summary>
    public IReadOnlyDictionary<HotkeyAction, Hotkey> Taken => _taken;

    /// <summary>The hotkeys registered now, by action.</summary>
    public IReadOnlyDictionary<HotkeyAction, Hotkey> Registered => _registered;

    /// <summary>
    /// True while every hotkey is let go of: while the settings dialog records one, while a program
    /// the dock stays down for is in front, and once disposed.
    /// </summary>
    public bool IsLettingGo => _recording || _standingDown || _disposed;

    /// <summary>Registers what the settings in force ask for, and lets go of what they no longer do.</summary>
    public void Apply(IReadOnlyDictionary<HotkeyAction, Hotkey> wanted)
    {
        if (wanted.Count == _wanted.Count && wanted.All(pair => _wanted.TryGetValue(pair.Key, out var had) && had == pair.Value))
        {
            return;
        }

        _wanted = new Dictionary<HotkeyAction, Hotkey>(wanted);
        Sync();
    }

    /// <summary>Lets every hotkey go while the settings dialog records one, and takes them back after.</summary>
    public void SetRecording(bool recording)
    {
        if (_recording == recording)
        {
            return;
        }

        _recording = recording;
        Sync();
    }

    /// <summary>Lets every hotkey go while a program the dock stays down for is in front.</summary>
    public void SetStandingDown(bool standingDown)
    {
        if (_standingDown == standingDown)
        {
            return;
        }

        _standingDown = standingDown;
        Sync();
    }

    /// <summary>Asks Windows again for the hotkeys it would not give, in case they have been let go of since.</summary>
    public void Retry()
    {
        if (_taken.Count > 0)
        {
            Sync();
        }
    }

    /// <summary>The action a <c>WM_HOTKEY</c> is for, from its <c>wParam</c>; null for one that is not the dock's.</summary>
    public HotkeyAction? ActionFor(nint id)
    {
        var number = (long)id - FirstId;
        return number >= 0 && number < HotkeyActions.All.Count && _registered.ContainsKey((HotkeyAction)number)
            ? (HotkeyAction)number
            : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Sync();
    }

    /// <summary>Brings Windows' registrations in line with what is wanted now.</summary>
    private void Sync()
    {
        IReadOnlyDictionary<HotkeyAction, Hotkey> target =
            IsLettingGo ? new Dictionary<HotkeyAction, Hotkey>() : _wanted;

        // Everything that goes, before anything comes: two actions trading keys can then each
        // have the other's.
        foreach (var (action, hotkey) in _registered.ToList())
        {
            if (!target.TryGetValue(action, out var wanted) || wanted != hotkey)
            {
                NativeMethods.UnregisterHotKey(_hwnd, FirstId + (int)action);
                _registered.Remove(action);
            }
        }

        var changed = false;

        // A combination taken from an action that has been given another, or none, is nothing
        // to say any more.
        foreach (var (action, hotkey) in _taken.ToList())
        {
            if (!_wanted.TryGetValue(action, out var wanted) || wanted != hotkey)
            {
                _taken.Remove(action);
                changed = true;
            }
        }

        foreach (var (action, hotkey) in target)
        {
            if (_registered.ContainsKey(action))
            {
                continue;
            }

            if (NativeMethods.RegisterHotKey(
                    _hwnd,
                    FirstId + (int)action,
                    (uint)hotkey.Modifiers | NativeMethods.MOD_NOREPEAT,
                    (uint)hotkey.Key))
            {
                _registered[action] = hotkey;
                changed |= _taken.Remove(action);
            }
            else if (!_taken.TryGetValue(action, out var known) || known != hotkey)
            {
                _taken[action] = hotkey;
                changed = true;
            }
        }

        if (changed)
        {
            TakenChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
