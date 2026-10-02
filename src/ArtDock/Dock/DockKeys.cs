using System.Globalization;
using System.Windows.Input;

namespace ArtDock.Dock;

/// <summary>What a key does while the dock has the keyboard.</summary>
public enum DockKeyKind
{
    /// <summary>Nothing: a key the dock has no use for.</summary>
    None,

    /// <summary>To the item before the one held up.</summary>
    Previous,

    /// <summary>To the item after it.</summary>
    Next,

    /// <summary>To the first item.</summary>
    First,

    /// <summary>To the last item.</summary>
    Last,

    /// <summary>Opens the item held up, as a click on it does.</summary>
    Open,

    /// <summary>
    /// To the item in a place on the dock — <see cref="DockKeyCommand.Place"/> — held up, not
    /// opened: a move, as the arrows are.
    /// </summary>
    Place,

    /// <summary>Opens the menu of the item held up, as a right-click does.</summary>
    Menu,

    /// <summary>Hands the keyboard back to the window that had it.</summary>
    Back
}

/// <summary>A key's meaning while the dock has the keyboard, and the place it goes to, for <see cref="DockKeyKind.Place"/>.</summary>
public readonly record struct DockKeyCommand(DockKeyKind Kind, int Place = 0);

/// <summary>
/// The keyboard's way along the dock: what each key means, and which item it goes to — apart
/// from any window, so they can be tested.
/// </summary>
/// <remarks>
/// <para>
/// Only items can be held up — not separators, which have nothing to open and no name to read
/// — so every move steps over them, and the places 1 to 9 are counted without them: the third
/// item is the third icon, whatever divides the row.
/// </para>
/// <para>
/// The moves stop at the ends rather than going round: a list read by a screen reader does not
/// wrap, and Home and End are there for the far end. A letter does go round, as typing a
/// letter in a list of files does in Explorer.
/// </para>
/// </remarks>
public static class DockKeys
{
    /// <summary>What a key means, with the modifiers held as it was pressed.</summary>
    /// <remarks>
    /// <para>
    /// Esc gives the keyboard back, and so does Alt+F4, which is what closes a window. The menu
    /// key or Shift+F10 opens the item's menu, as anywhere in Windows.
    /// </para>
    /// <para>
    /// The moves take Ctrl, Alt and Shift as well as nothing, because the hotkey that brought the
    /// dock up is still held as the first arrow is pressed — Win+Ctrl+A, with Ctrl not yet let go,
    /// or one set on the page with Alt in it. Alt's combinations are a window's menu, and this
    /// window has none; they were refused here until the hotkeys were Win+Ctrl+Alt for a few hours
    /// on 2026-10-02. Not the Windows key, whose combinations are Windows' own. Opening, with Enter
    /// or Space, takes nothing held: Ctrl+Shift+Enter is what Windows starts a program as
    /// administrator with, which the dock does not do — and a launch, unlike a move, is not
    /// something to do by mistake.
    /// </para>
    /// <para>
    /// A digit, 1 to 9 on the number row or the keypad, goes to the item in that place and holds it
    /// up; Enter opens it. It opened the item until the evening of 2026-10-02, when it was asked to
    /// select only, and it has been a move since — taking what the moves take, so a Ctrl still held
    /// from the hotkey does not stop it.
    /// </para>
    /// </remarks>
    public static DockKeyCommand Command(Key key, ModifierKeys modifiers)
    {
        if ((key == Key.Escape && modifiers == ModifierKeys.None)
            || (key == Key.F4 && modifiers == ModifierKeys.Alt))
        {
            return new(DockKeyKind.Back);
        }

        if ((key == Key.Apps && modifiers == ModifierKeys.None)
            || (key == Key.F10 && modifiers == ModifierKeys.Shift))
        {
            return new(DockKeyKind.Menu);
        }

        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            return default;
        }

        switch (key)
        {
            case Key.Left:
                return new(DockKeyKind.Previous);
            case Key.Right:
                return new(DockKeyKind.Next);
            case Key.Home:
                return new(DockKeyKind.First);
            case Key.End:
                return new(DockKeyKind.Last);
            case >= Key.D1 and <= Key.D9:
                return new(DockKeyKind.Place, key - Key.D1 + 1);
            case >= Key.NumPad1 and <= Key.NumPad9:
                return new(DockKeyKind.Place, key - Key.NumPad1 + 1);
        }

        if (modifiers != ModifierKeys.None)
        {
            return default;
        }

        return key is Key.Enter or Key.Space ? new(DockKeyKind.Open) : default;
    }

    /// <summary>Whether the keyboard can stop on an item: anything but a separator.</summary>
    public static bool CanHold(DockItem item) => !item.IsSeparator;

    /// <summary>The first item the keyboard can stop on; -1 when there is none.</summary>
    public static int First(IReadOnlyList<DockItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (CanHold(items[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The last item the keyboard can stop on; -1 when there is none.</summary>
    public static int Last(IReadOnlyList<DockItem> items)
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (CanHold(items[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The next item the keyboard can stop on from <paramref name="from"/>, one way or the other;
    /// <paramref name="from"/> itself at the end of the row, and the first or last item when it
    /// is on none.
    /// </summary>
    /// <param name="direction">Positive for along the row, negative for back.</param>
    public static int Step(IReadOnlyList<DockItem> items, int from, int direction)
    {
        if (from < 0 || from >= items.Count)
        {
            return direction < 0 ? Last(items) : First(items);
        }

        var step = direction < 0 ? -1 : 1;
        for (var i = from + step; i >= 0 && i < items.Count; i += step)
        {
            if (CanHold(items[i]))
            {
                return i;
            }
        }

        return from;
    }

    /// <summary>The item in a place on the dock, counted from 1 without the separators; -1 when there is none.</summary>
    public static int Place(IReadOnlyList<DockItem> items, int place)
    {
        if (place < 1)
        {
            return -1;
        }

        var counted = 0;
        for (var i = 0; i < items.Count; i++)
        {
            if (CanHold(items[i]) && ++counted == place)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The next item after <paramref name="from"/> whose name starts with <paramref name="text"/>,
    /// going round to the start — and <paramref name="from"/> itself last of all; -1 when no
    /// item's does.
    /// </summary>
    /// <remarks>
    /// Without regard to case, in the language's own sense of it — a Turkish i is matched as
    /// Turkish has it.
    /// </remarks>
    public static int StartingWith(IReadOnlyList<DockItem> items, int from, string text, CultureInfo culture)
    {
        if (items.Count == 0 || string.IsNullOrEmpty(text))
        {
            return -1;
        }

        var start = from < 0 || from >= items.Count ? -1 : from;
        for (var n = 1; n <= items.Count; n++)
        {
            var i = (start + n) % items.Count;
            if (CanHold(items[i])
                && items[i].Label.TrimStart().StartsWith(text, ignoreCase: true, culture))
            {
                return i;
            }
        }

        return -1;
    }
}
