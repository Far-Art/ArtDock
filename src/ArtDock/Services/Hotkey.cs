using System.Globalization;

namespace ArtDock.Services;

/// <summary>The keys held with a hotkey's key, numbered as <c>RegisterHotKey</c> numbers them.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8
}

/// <summary>Why a combination cannot be a hotkey.</summary>
public enum HotkeyProblem
{
    None,

    /// <summary>
    /// Neither the Windows key, Ctrl nor Alt is held, so the key would be taken from typing —
    /// Shift on its own included, which is typing too.
    /// </summary>
    NeedsModifier,

    /// <summary>F12, which Windows keeps for debuggers whether one is running or not.</summary>
    Reserved,

    /// <summary>
    /// What Windows does in every window — Alt+F4, Alt+Space, Alt+Tab, Alt+Esc, Ctrl+Esc. Some
    /// of these could be registered, and closing a window would then stop working everywhere.
    /// </summary>
    Windows,

    /// <summary>A modifier on its own, or a key that is not one to take: a lock key, an IME's.</summary>
    NotAKey
}

/// <summary>
/// A key, and the modifiers held with it, as <c>RegisterHotKey</c> takes them: the key by its
/// virtual-key code.
/// </summary>
/// <remarks>
/// <para>
/// By virtual key, not by what the key types, so a hotkey is the same physical key on every
/// keyboard layout: the letters have the same codes on the US, Hebrew and Russian layouts though
/// they type different things, and Windows matches a hotkey by code. Written in a settings file
/// as Windows writes them in its own documentation, modifiers first —
/// <c>Win+Ctrl+A</c> — with the keys that have no letter of their own by name (<c>Space</c>,
/// <c>PageUp</c>, <c>F5</c>, <c>NumPad7</c>) and the punctuation keys by the name of their code
/// (<c>Oem1</c>), since what those type differs from one layout to the next. Anything else is
/// written as its code, <c>0xB3</c>.
/// </para>
/// <para>
/// What a person reads is another matter, and the language's: see
/// <c>Localization.KeyNames</c>.
/// </para>
/// </remarks>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, int Key)
{
    /// <summary>The modifiers that keep a combination from being typing: all but Shift.</summary>
    private const HotkeyModifiers Chords = HotkeyModifiers.Win | HotkeyModifiers.Ctrl | HotkeyModifiers.Alt;

    private const int F4 = 0x73;
    private const int F12 = 0x7B;
    private const int Tab = 0x09;
    private const int Escape = 0x1B;
    private const int Space = 0x20;

    /// <summary>The keys with a name of their own in a settings file, by code.</summary>
    private static readonly Dictionary<int, string> Names = new()
    {
        [0x08] = "Backspace",
        [Tab] = "Tab",
        [0x0D] = "Enter",
        [0x13] = "Pause",
        [Escape] = "Esc",
        [Space] = "Space",
        [0x21] = "PageUp",
        [0x22] = "PageDown",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "Left",
        [0x26] = "Up",
        [0x27] = "Right",
        [0x28] = "Down",
        [0x2C] = "PrintScreen",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
        [0x5D] = "Apps",
        [0x6A] = "Multiply",
        [0x6B] = "Add",
        [0x6D] = "Subtract",
        [0x6E] = "Decimal",
        [0x6F] = "Divide",
        [0xBA] = "Oem1",
        [0xBB] = "OemPlus",
        [0xBC] = "OemComma",
        [0xBD] = "OemMinus",
        [0xBE] = "OemPeriod",
        [0xBF] = "Oem2",
        [0xC0] = "Oem3",
        [0xDB] = "Oem4",
        [0xDC] = "Oem5",
        [0xDD] = "Oem6",
        [0xDE] = "Oem7",
        [0xDF] = "Oem8",
        [0xE2] = "Oem102"
    };

    /// <summary>The same, by name.</summary>
    private static readonly Dictionary<string, int> Codes =
        Names.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the Windows key is one of the modifiers.</summary>
    public bool HasWin => (Modifiers & HotkeyModifiers.Win) != 0;

    /// <summary>
    /// Ctrl and Alt together, without the Windows key: which is AltGr on a layout that has one,
    /// so a hotkey there takes a character from whoever was typing it.
    /// </summary>
    public bool IsAltGr =>
        (Modifiers & HotkeyModifiers.Ctrl) != 0 && (Modifiers & HotkeyModifiers.Alt) != 0 && !HasWin;

    /// <summary>Why this cannot be a hotkey, or <see cref="HotkeyProblem.None"/>.</summary>
    public HotkeyProblem Problem =>
        !IsKeyToTake(Key) ? HotkeyProblem.NotAKey
        : Key == F12 ? HotkeyProblem.Reserved
        : (Modifiers & Chords) == 0 ? HotkeyProblem.NeedsModifier
        : IsWindowsOwn ? HotkeyProblem.Windows
        : HotkeyProblem.None;

    /// <summary>What Windows does in every window, by these keys — see <see cref="HotkeyProblem.Windows"/>.</summary>
    private bool IsWindowsOwn
    {
        get
        {
            var held = Modifiers & ~HotkeyModifiers.Shift;
            return (held, Key) switch
            {
                (HotkeyModifiers.Alt, F4 or Space or Tab or Escape) => true,
                (HotkeyModifiers.Ctrl, Escape) => true,
                _ => false
            };
        }
    }

    /// <summary>
    /// Whether a key can be the key of a hotkey: not a modifier, a lock key, an IME's key or a
    /// mouse button, nor a code that stands for no key at all.
    /// </summary>
    public static bool IsKeyToTake(int key) =>
        key is > 0x07 and < 0xFF
        and not (0x10 or 0x11 or 0x12)          // Shift, Ctrl, Alt
        and not (0x5B or 0x5C)                  // the Windows keys
        and not (>= 0xA0 and <= 0xA5)           // the left and right of each modifier
        and not (0x14 or 0x90 or 0x91)          // Caps Lock, Num Lock, Scroll Lock
        and not (>= 0x15 and <= 0x1A)           // IME keys
        and not (>= 0x1C and <= 0x1F)           // more of them
        and not (0xE5 or 0xE7);                 // a key the IME has, and a character sent as a key

    /// <summary>The settings file's form: <c>Win+Ctrl+A</c>.</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if ((Modifiers & HotkeyModifiers.Win) != 0)
        {
            parts.Add("Win");
        }

        if ((Modifiers & HotkeyModifiers.Ctrl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((Modifiers & HotkeyModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((Modifiers & HotkeyModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        parts.Add(KeyName(Key));
        return string.Join('+', parts);
    }

    /// <summary>A key's name in a settings file.</summary>
    public static string KeyName(int key) => key switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x60 and <= 0x69 => "NumPad" + (key - 0x60).ToString(CultureInfo.InvariantCulture),
        >= 0x70 and <= 0x87 => "F" + (key - 0x6F).ToString(CultureInfo.InvariantCulture),
        _ => Names.TryGetValue(key, out var name) ? name : "0x" + key.ToString("X2", CultureInfo.InvariantCulture)
    };

    /// <summary>
    /// Reads a hotkey as a settings file writes it, ignoring case and spaces. Whether it can be
    /// one is <see cref="Problem"/>'s to say: this only reads.
    /// </summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = HotkeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            HotkeyModifiers? modifier = parts[i].ToUpperInvariant() switch
            {
                "WIN" => HotkeyModifiers.Win,
                "CTRL" => HotkeyModifiers.Ctrl,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                _ => null
            };

            if (modifier is not { } held)
            {
                return false;
            }

            modifiers |= held;
        }

        if (KeyCode(parts[^1]) is not { } key)
        {
            return false;
        }

        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    /// <summary>A key's code from its name in a settings file, or null for none.</summary>
    private static int? KeyCode(string name)
    {
        if (name.Length == 1 && char.ToUpperInvariant(name[0]) is var single
            && (single is >= '0' and <= '9' || single is >= 'A' and <= 'Z'))
        {
            return single;
        }

        if (Codes.TryGetValue(name, out var named))
        {
            return named;
        }

        if (name.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var digit)
            && digit is >= 0 and <= 9)
        {
            return 0x60 + digit;
        }

        if (name.Length > 1 && name[0] is 'F' or 'f'
            && int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var function)
            && function is >= 1 and <= 24)
        {
            return 0x6F + function;
        }

        if (name.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code)
            && code is > 0 and < 0xFF)
        {
            return code;
        }

        return null;
    }
}
