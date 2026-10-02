using System.Globalization;
using ArtDock.Interop;
using ArtDock.Services;

namespace ArtDock.Localization;

/// <summary>
/// A hotkey as a person reads it: <c>Win + Ctrl + A</c>, in the language in force.
/// </summary>
/// <remarks>
/// <para>
/// The letters and digits by their own characters, which are the ones printed on the key on
/// every keyboard Windows has a layout for — the Russian and Hebrew layouts give the key that
/// types ф or ש the code of A, and their keyboards have the A printed beside it. The keys with
/// names, and the modifiers, from the string table, so Ctrl can be Strg where that is what the
/// key says.
/// </para>
/// <para>
/// The punctuation keys by what they type on the keyboard layout in use, since that is what is
/// printed on them: the same code is ; on a US keyboard and ж on a Russian one, and its name in
/// a settings file, <c>Oem1</c>, means nothing to anyone.
/// </para>
/// </remarks>
public static class KeyNames
{
    /// <summary>What goes between the keys of a combination.</summary>
    private const string Joiner = " + ";

    /// <summary>A hotkey, written out.</summary>
    public static string Describe(Hotkey hotkey) =>
        string.Join(Joiner, [.. Modifiers(hotkey.Modifiers), Name(hotkey.Key)]);

    /// <summary>
    /// The modifiers held so far, while a combination is being pressed: <c>Win + Ctrl + …</c>.
    /// </summary>
    public static string Held(HotkeyModifiers modifiers) =>
        string.Join(Joiner, [.. Modifiers(modifiers), "…"]);

    private static IEnumerable<string> Modifiers(HotkeyModifiers modifiers)
    {
        if ((modifiers & HotkeyModifiers.Win) != 0)
        {
            yield return Localizer.Get("Key.Win");
        }

        if ((modifiers & HotkeyModifiers.Ctrl) != 0)
        {
            yield return Localizer.Get("Key.Ctrl");
        }

        if ((modifiers & HotkeyModifiers.Alt) != 0)
        {
            yield return Localizer.Get("Key.Alt");
        }

        if ((modifiers & HotkeyModifiers.Shift) != 0)
        {
            yield return Localizer.Get("Key.Shift");
        }
    }

    /// <summary>One key's name.</summary>
    public static string Name(int key) => key switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x60 and <= 0x69 => Localizer.Format("Key.NumPad", key - 0x60),
        >= 0x70 and <= 0x87 => Localizer.Format("Key.Function", key - 0x6F),
        0x6A => Localizer.Format("Key.NumPad", "*"),
        0x6B => Localizer.Format("Key.NumPad", "+"),
        0x6D => Localizer.Format("Key.NumPad", "-"),
        0x6E => Localizer.Format("Key.NumPad", "."),
        0x6F => Localizer.Format("Key.NumPad", "/"),
        0x08 => Localizer.Get("Key.Backspace"),
        0x09 => Localizer.Get("Key.Tab"),
        0x0D => Localizer.Get("Key.Enter"),
        0x13 => Localizer.Get("Key.Pause"),
        0x1B => Localizer.Get("Key.Esc"),
        0x20 => Localizer.Get("Key.Space"),
        0x21 => Localizer.Get("Key.PageUp"),
        0x22 => Localizer.Get("Key.PageDown"),
        0x23 => Localizer.Get("Key.End"),
        0x24 => Localizer.Get("Key.Home"),
        0x25 => Localizer.Get("Key.Left"),
        0x26 => Localizer.Get("Key.Up"),
        0x27 => Localizer.Get("Key.Right"),
        0x28 => Localizer.Get("Key.Down"),
        0x2C => Localizer.Get("Key.PrintScreen"),
        0x2D => Localizer.Get("Key.Insert"),
        0x2E => Localizer.Get("Key.Delete"),
        0x5D => Localizer.Get("Key.Apps"),
        _ => Typed(key) ?? Localizer.Format("Key.Code", key)
    };

    /// <summary>What a key types with nothing held, on the layout in use — null for a key that types nothing.</summary>
    /// <remarks>
    /// Upper case, as keys are printed. A dead key — the accent that waits for a letter — is
    /// marked by Windows in the top bit, and is named by the accent all the same.
    /// </remarks>
    private static string? Typed(int key)
    {
        var typed = (char)(NativeMethods.MapVirtualKey((uint)key, NativeMethods.MAPVK_VK_TO_CHAR) & 0xFFFF);
        return typed == '\0' || char.IsControl(typed) || char.IsWhiteSpace(typed)
            ? null
            : char.ToUpper(typed, CultureInfo.CurrentCulture).ToString();
    }
}
