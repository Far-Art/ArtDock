using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArtDock.Localization;
using ArtDock.Services;

namespace ArtDock.Controls;

/// <summary>
/// Makes a text box record a hotkey: while it has the keyboard, the next combination pressed is
/// its value.
/// </summary>
/// <remarks>
/// <para>
/// A read-only text box, as Windows' own <em>Shortcut key</em> box in a shortcut's properties
/// is: a screen reader reads it as an edit field holding the combination, and it records while
/// it has the keyboard, so there is nothing to press first. Tab and Shift+Tab are let through, so
/// the keyboard can leave it, and Backspace or Delete empties it, as Windows' does.
/// </para>
/// <para>
/// <b>Neither Enter nor Esc reaches the dialog while it records.</b> The dialog takes Enter for
/// Save — its Save button is the default, and Enter pressed into it once wrote a test's settings
/// to disk — and Esc for Cancel, and here both are keys someone may press while trying a
/// combination. Esc puts back what the box held when it was given the keyboard instead.
/// </para>
/// <para>
/// A combination Windows keeps for itself — Win+L, Win+D, Ctrl+Alt+Del — never arrives at all,
/// Windows having acted on it, which is its own way of saying the keys are taken. One that cannot
/// be a hotkey (<see cref="Hotkey.Problem"/>) is refused and said why, and one that Windows does
/// in every window, Alt+F4 among them, is refused and let through to do it.
/// </para>
/// </remarks>
public sealed class HotkeyRecorder
{
    private readonly TextBox _box;

    /// <summary>The value when the box was last given the keyboard, for Esc to put back.</summary>
    private Hotkey? _given;

    public HotkeyRecorder(TextBox box)
    {
        _box = box;
        _box.IsReadOnly = true;
        _box.IsReadOnlyCaretVisible = false;
        _box.IsUndoEnabled = false;
        InputMethod.SetIsInputMethodEnabled(_box, false);

        _box.PreviewKeyDown += OnKeyDown;
        _box.PreviewKeyUp += OnKeyUp;
        _box.GotKeyboardFocus += (_, _) =>
        {
            _given = Value;
            RecordingChanged?.Invoke(this, EventArgs.Empty);
        };
        _box.LostKeyboardFocus += (_, _) =>
        {
            Show();
            RecordingChanged?.Invoke(this, EventArgs.Empty);
        };

        // A text box's own menu offers Copy and nothing else here.
        _box.ContextMenuOpening += (_, e) => e.Handled = true;

        Show();
    }

    /// <summary>The combination recorded, or null for none.</summary>
    public Hotkey? Value
    {
        get;
        set
        {
            field = value;
            Show();
        }
    }

    /// <summary>True while the box has the keyboard, and so records.</summary>
    public bool IsRecording => _box.IsKeyboardFocused;

    /// <summary>Raised when the person has recorded a combination, emptied the box, or put it back.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Raised as the box is given the keyboard and loses it.</summary>
    public event EventHandler? RecordingChanged;

    /// <summary>Raised with what is wrong with a combination pressed that cannot be a hotkey.</summary>
    public event EventHandler<HotkeyProblem>? Refused;

    /// <summary>Writes the value out again, in the language in force.</summary>
    public void Show() =>
        _box.Text = Value is { } hotkey ? KeyNames.Describe(hotkey) : Localizer.Get("Settings.Hotkeys.None");

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key
        };

        var modifiers = Modifiers(Keyboard.Modifiers);

        // Out of the box, as from any other.
        if (key == Key.Tab && (modifiers & ~HotkeyModifiers.Shift) == HotkeyModifiers.None)
        {
            return;
        }

        if (modifiers == HotkeyModifiers.None)
        {
            switch (key)
            {
                case Key.Escape:
                    e.Handled = true;
                    Record(_given);
                    return;

                case Key.Back or Key.Delete:
                    e.Handled = true;
                    Record(null);
                    return;
            }
        }

        var code = KeyInterop.VirtualKeyFromKey(key);
        var hotkey = new Hotkey(modifiers, code);

        // What Windows does in every window is left to it — Alt+F4 closes the dialog — and said
        // to be refused all the same.
        if (hotkey.Problem == HotkeyProblem.Windows)
        {
            Refused?.Invoke(this, HotkeyProblem.Windows);
            return;
        }

        e.Handled = true;

        // A modifier on its own is the start of a combination: shown, so the box is plainly
        // listening.
        if (!Hotkey.IsKeyToTake(code))
        {
            _box.Text = modifiers == HotkeyModifiers.None ? _box.Text : KeyNames.Held(modifiers);
            return;
        }

        if (hotkey.Problem != HotkeyProblem.None)
        {
            Refused?.Invoke(this, hotkey.Problem);
            return;
        }

        Record(hotkey);
    }

    /// <summary>Puts the value back once the modifiers shown as held so far have all been let go.</summary>
    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (Modifiers(Keyboard.Modifiers) == HotkeyModifiers.None)
        {
            Show();
        }
    }

    private void Record(Hotkey? hotkey)
    {
        Value = hotkey;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private static HotkeyModifiers Modifiers(ModifierKeys keys)
    {
        var modifiers = HotkeyModifiers.None;
        if ((keys & ModifierKeys.Windows) != 0)
        {
            modifiers |= HotkeyModifiers.Win;
        }

        if ((keys & ModifierKeys.Control) != 0)
        {
            modifiers |= HotkeyModifiers.Ctrl;
        }

        if ((keys & ModifierKeys.Alt) != 0)
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if ((keys & ModifierKeys.Shift) != 0)
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        return modifiers;
    }
}
