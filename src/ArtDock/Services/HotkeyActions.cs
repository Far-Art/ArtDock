namespace ArtDock.Services;

/// <summary>
/// What a hotkey does. The names are the file format: they are what
/// <see cref="DockSettings.Hotkeys"/> is keyed by.
/// </summary>
public enum HotkeyAction
{
    /// <summary>The dock takes the keyboard: it comes up with an item held up, and the keys move along it.</summary>
    Keyboard,

    /// <summary>Hides the dock, or shows it, as the tray menu's entry does.</summary>
    ShowHide,

    /// <summary>Opens the settings dialog, or brings it to the front, as the tray menu's entry does.</summary>
    Settings,

    /// <summary>Opens the item in that place on the dock, counted without the separators.</summary>
    Place1,
    Place2,
    Place3,
    Place4,
    Place5,
    Place6,
    Place7,
    Place8,
    Place9,

    /// <summary>A second hotkey for each place, which opens the same item.</summary>
    Place1Secondary,
    Place2Secondary,
    Place3Secondary,
    Place4Secondary,
    Place5Secondary,
    Place6Secondary,
    Place7Secondary,
    Place8Secondary,
    Place9Secondary
}

/// <summary>
/// The hotkeys each action starts with, and how what is stored becomes the hotkeys in force.
/// </summary>
/// <remarks>
/// <para>
/// Every default is the Windows key and Ctrl with a key — one family, so the keys are
/// consistent, and two modifiers side by side, which one finger can hold. On 2026-10-01 none of
/// the letters was on Microsoft's list of Windows' own shortcuts, among PowerToys' defaults, or
/// registered on the machine this was written on — found by registering each candidate and
/// letting it go at once, across nine families of modifiers and three keyboard layouts.
/// <b>A</b> for ArtDock, since D for dock is taken in every family: Win+D is the desktop,
/// Win+Ctrl+D a new virtual desktop, Win+Alt+D the clock, and PowerToys has Win+Shift+D and
/// Win+Ctrl+Shift+D. <b>H</b> for hide; PowerToys has Win+Shift+H. <b>I</b> for the settings,
/// added on 2026-10-02: Win+I is Windows' own Settings, and Win+Ctrl+S was taken on that machine.
/// </para>
/// <para>
/// The places have their digits on the numeric keypad, which are themselves only while Num Lock
/// is on — off, they are its arrows, Home and End. Not the number row: Windows has its digits with
/// the Windows key alone and with Shift, Ctrl, Alt, and Ctrl and Shift, all for the taskbar's
/// buttons, and the one family it leaves free, the Windows key with Ctrl and Alt, was a key too
/// many to press comfortably — every default was moved there, and back, on 2026-10-02. Without a
/// keypad, the Keyboard hotkey and then the digit holds the same item up, and Enter opens it — the
/// digit opened it outright until that evening (<see cref="Dock.DockKeys"/>). Shift could
/// be part of no hotkey for a keypad digit: with Num Lock on, it turns them into the arrows. The
/// places' second keys have no default.
/// </para>
/// <para>
/// Keys set on the page outrank the ones an action starts with: given another action's default,
/// the action they were set for has them, and the row whose default it was says so — rather than
/// the keys going to whichever row is higher on the page, and the ones just set doing nothing.
/// Between two set on the page, the page's order decides; two defaults never share.
/// </para>
/// </remarks>
public static class HotkeyActions
{
    /// <summary>
    /// Every action, in the order the Hotkeys page lists them — the places' second keys after all
    /// of their first — which decides which of two actions given the same keys has them, once
    /// being set on the page has not (<see cref="InForce"/>).
    /// </summary>
    public static IReadOnlyList<HotkeyAction> All { get; } = Enum.GetValues<HotkeyAction>();

    /// <summary>The names in a settings file, for telling this build's actions from a later one's.</summary>
    private static readonly HashSet<string> Names = [.. All.Select(action => action.ToString())];

    /// <summary>The modifiers every default has: the Windows key and Ctrl.</summary>
    private const HotkeyModifiers Family = HotkeyModifiers.Win | HotkeyModifiers.Ctrl;

    /// <summary>The virtual key of the numeric keypad's 0, which its 1 to 9 follow.</summary>
    private const int NumPad0 = 0x60;

    /// <summary>The hotkey an action has until it is given another, or none.</summary>
    public static Hotkey? Default(HotkeyAction action) => action switch
    {
        HotkeyAction.Keyboard => new Hotkey(Family, 'A'),
        HotkeyAction.ShowHide => new Hotkey(Family, 'H'),
        HotkeyAction.Settings => new Hotkey(Family, 'I'),
        >= HotkeyAction.Place1 and <= HotkeyAction.Place9 => new Hotkey(Family, NumPad0 + Place(action)),
        _ => null
    };

    /// <summary>
    /// The place an action opens, 1 to 9, whether it is the place's first key or its second — or
    /// 0 for an action that opens none.
    /// </summary>
    public static int Place(HotkeyAction action) => action switch
    {
        >= HotkeyAction.Place1 and <= HotkeyAction.Place9 => action - HotkeyAction.Place1 + 1,
        >= HotkeyAction.Place1Secondary and <= HotkeyAction.Place9Secondary => action - HotkeyAction.Place1Secondary + 1,
        _ => 0
    };

    /// <summary>Whether an action is a place's second key.</summary>
    public static bool IsSecondary(HotkeyAction action) =>
        action is >= HotkeyAction.Place1Secondary and <= HotkeyAction.Place9Secondary;

    /// <summary>Whether a name in the stored hotkeys is one of this build's actions.</summary>
    public static bool IsAction(string name) => Names.Contains(name);

    /// <summary>
    /// The hotkey an action has, from what is stored: an action that is not there has its
    /// default, and one stored empty has none — as has one stored as something that cannot be
    /// read, or cannot be a hotkey, which only a file written by hand can hold.
    /// </summary>
    public static Hotkey? Resolve(IReadOnlyDictionary<string, string>? stored, HotkeyAction action)
    {
        if (stored is null || !stored.TryGetValue(action.ToString(), out var text))
        {
            return Default(action);
        }

        return Hotkey.TryParse(text, out var hotkey) && hotkey.Problem == HotkeyProblem.None
            ? hotkey
            : null;
    }

    /// <summary>
    /// The hotkeys that count, with quick launch on or off (<see cref="DockSettings.QuickLaunch"/>)
    /// and turned off for some places alone (<see cref="DockSettings.QuickLaunchOff"/>): all of
    /// them, but for the places' keys that are off — every place's, with quick launch off — which
    /// are then nobody's to register and no other action's to share.
    /// </summary>
    /// <param name="placesOff">The places whose keys are off, 1 to 9, first and second alike.</param>
    public static Dictionary<HotkeyAction, Hotkey?> InUse(
        IReadOnlyDictionary<HotkeyAction, Hotkey?> hotkeys,
        bool quickLaunch,
        IReadOnlyCollection<int>? placesOff = null) =>
        hotkeys
            .Where(pair => Place(pair.Key) is var place
                && (place == 0 || (quickLaunch && placesOff?.Contains(place) != true)))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

    /// <summary>The action that has this one's keys instead of it, if any.</summary>
    /// <remarks>
    /// That one has them, and this one does nothing: Windows gives a combination to one
    /// registration, and the dock asks in the order of <see cref="Ranked"/>.
    /// </remarks>
    public static HotkeyAction? SharedWith(IReadOnlyDictionary<HotkeyAction, Hotkey?> hotkeys, HotkeyAction action)
    {
        if (!hotkeys.TryGetValue(action, out var value) || value is not { } hotkey)
        {
            return null;
        }

        foreach (var other in Ranked(hotkeys))
        {
            if (other == action)
            {
                break;
            }

            if (hotkeys.TryGetValue(other, out var theirs) && theirs == hotkey)
            {
                return other;
            }
        }

        return null;
    }

    /// <summary>
    /// The hotkeys to register: every action's, but for one that cannot be a hotkey and one that
    /// an action ranked before it already has.
    /// </summary>
    public static Dictionary<HotkeyAction, Hotkey> InForce(IReadOnlyDictionary<HotkeyAction, Hotkey?> hotkeys)
    {
        var inForce = new Dictionary<HotkeyAction, Hotkey>();
        var given = new HashSet<Hotkey>();

        foreach (var action in Ranked(hotkeys))
        {
            if (hotkeys.TryGetValue(action, out var value)
                && value is { Problem: HotkeyProblem.None } hotkey
                && given.Add(hotkey))
            {
                inForce[action] = hotkey;
            }
        }

        return inForce;
    }

    /// <summary>
    /// The order in which actions are given keys that two of them share: those set to other keys
    /// than their defaults first, then those at their defaults, each in the page's order.
    /// </summary>
    private static IEnumerable<HotkeyAction> Ranked(IReadOnlyDictionary<HotkeyAction, Hotkey?> hotkeys)
    {
        bool AtDefault(HotkeyAction action) =>
            hotkeys.TryGetValue(action, out var value) && value == Default(action);

        return All.Where(action => !AtDefault(action)).Concat(All.Where(AtDefault));
    }

    /// <summary>
    /// What a settings file holds for a set of hotkeys: nothing for an action that has its
    /// default, so it goes on following the default; an empty string for one that has been
    /// given none; and the keys for the rest. Whatever <paramref name="others"/> holds for names
    /// this build does not know — a later version's actions — is carried through as it was.
    /// </summary>
    public static Dictionary<string, string> Store(
        IReadOnlyDictionary<HotkeyAction, Hotkey?> hotkeys,
        IEnumerable<KeyValuePair<string, string>>? others = null)
    {
        var stored = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, value) in others ?? [])
        {
            if (!IsAction(name))
            {
                stored[name] = value;
            }
        }

        foreach (var action in All)
        {
            var value = hotkeys.TryGetValue(action, out var chosen) ? chosen : Default(action);
            if (value != Default(action))
            {
                stored[action.ToString()] = value?.ToString() ?? string.Empty;
            }
        }

        return stored;
    }
}
