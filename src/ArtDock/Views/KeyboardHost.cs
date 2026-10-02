using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ArtDock.Dock;
using ArtDock.Interop;

namespace ArtDock.Views;

/// <summary>
/// The window that has the keyboard while the dock is being used from it.
/// </summary>
/// <remarks>
/// <para>
/// The dock's own window never takes the foreground, and must not: <c>WS_EX_NOACTIVATE</c> is
/// what keeps a click on an icon from taking it from the app being switched to. So the keys need
/// a window of their own, as the menus do (<see cref="MenuHost"/>), and this is it: given the
/// foreground for as long as the dock has the keyboard, and only then. It turns keys into
/// commands (<see cref="DockKeys.Command"/>) and leaves what they do to the dock.
/// </para>
/// <para>
/// For a screen reader it is a list of the dock's items, whose selection moves with the item held
/// up: each is read by its name, as one of so many. The dock draws its items rather than being
/// made of controls, so without this a screen reader would have nothing to follow while the keys
/// moved along them — and a blind user is who the keyboard matters to most. The list is
/// invisible, and laid over the bar so what it says it is reading is where the dock is; the
/// window is click-through, so the pointer goes on reaching the dock beneath it.
/// </para>
/// </remarks>
public sealed class KeyboardHost : Window
{
    private readonly ListBox _list;

    /// <summary>The items listed, in the dock's order — every one that can be held up.</summary>
    private IReadOnlyList<DockItem> _items = [];

    /// <summary>False while the window is being put away, so its losing the foreground is not taken for the person leaving.</summary>
    private bool _open;

    public KeyboardHost()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Width = 1;
        Height = 1;
        Left = -32000;
        Top = -32000;
        Title = "ArtDock";

        var row = new FrameworkElementFactory(typeof(UniformGrid));
        row.SetValue(UniformGrid.RowsProperty, 1);

        _list = new ListBox
        {
            Opacity = 0,
            ItemsPanel = new ItemsPanelTemplate(row),
            BorderThickness = new Thickness(0)
        };

        _list.SetResourceReference(AutomationProperties.NameProperty, "Keyboard.Dock");
        _list.SetResourceReference(AutomationProperties.HelpTextProperty, "Keyboard.Dock.Hint");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        Content = _list;

        PreviewKeyDown += OnKeyDown;
        PreviewTextInput += OnTextInput;
        Activated += (_, _) => FocusSelected();
        Deactivated += (_, _) =>
        {
            if (_open)
            {
                Lost?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>Raised with what a key means — see <see cref="DockKeys.Command"/>.</summary>
    public event EventHandler<DockKeyCommand>? Command;

    /// <summary>Raised with what was typed, for the dock to go to the next item whose name starts with it.</summary>
    public event EventHandler<string>? Typed;

    /// <summary>Raised when something else has taken the foreground while the dock had the keyboard.</summary>
    public event EventHandler? Lost;

    private nint Hwnd => new WindowInteropHelper(this).Handle;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Off the taskbar, out of Alt+Tab, and click-through: it lies over the dock, and the
        // pointer has to go on reaching the dock beneath it.
        var exStyle = (uint)NativeMethods.GetWindowLongPtr(Hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(
            Hwnd,
            NativeMethods.GWL_EXSTYLE,
            (nint)(exStyle | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT));
    }

    /// <summary>
    /// Lists the items, lays the window over the bar and takes the foreground.
    /// </summary>
    /// <param name="bar">The bar at rest, in physical screen pixels.</param>
    /// <returns>False when Windows would not give it the foreground, so no key would reach it.</returns>
    public bool Open(IReadOnlyList<DockItem> items, Rect bar)
    {
        List(items);

        if (!IsVisible)
        {
            Show();
        }

        if (!bar.IsEmpty)
        {
            NativeMethods.SetWindowPos(
                Hwnd, 0,
                (int)Math.Round(bar.Left), (int)Math.Round(bar.Top),
                Math.Max(1, (int)Math.Round(bar.Width)), Math.Max(1, (int)Math.Round(bar.Height)),
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
        }

        _open = true;
        return TakeForeground();
    }

    /// <summary>
    /// Takes the foreground — again, after a menu opened from the keyboard has closed on nothing.
    /// </summary>
    /// <remarks>
    /// Windows lets a program take the foreground only on the heels of something the person did
    /// to it: a hotkey it was sent, a launch of it, or the foreground being its already, as it is
    /// when a menu of its own has just closed. <see cref="AppLauncher.Activate"/> attaches to the
    /// foreground's input for the moment it takes, which is what that rule asks for.
    /// </remarks>
    public bool TakeForeground()
    {
        _open = true;
        return AppLauncher.Activate(Hwnd) && WindowsApi.GetForegroundWindow() == Hwnd;
    }

    /// <summary>Puts the list in step with the dock's items, when they change while it has the keyboard.</summary>
    public void List(IReadOnlyList<DockItem> items)
    {
        _items = [.. items.Where(DockKeys.CanHold)];
        _list.Items.Clear();

        foreach (var item in _items)
        {
            var row = new ListBoxItem { Content = item.Label };
            AutomationProperties.SetName(row, item.Label);
            _list.Items.Add(row);
        }
    }

    /// <summary>Selects the item held up, and gives it the keyboard, which is what a screen reader reads.</summary>
    public void Select(DockItem? item)
    {
        var index = -1;
        for (var i = 0; i < _items.Count; i++)
        {
            if (item is not null && (ReferenceEquals(_items[i], item) || _items[i].Id == item.Id))
            {
                index = i;
                break;
            }
        }

        _list.SelectedIndex = index;
        FocusSelected();
    }

    /// <summary>Puts the window away without its going counting as the person leaving.</summary>
    public void Dismiss()
    {
        _open = false;
        if (IsVisible)
        {
            Hide();
        }
    }

    private void FocusSelected()
    {
        if (!IsActive)
        {
            return;
        }

        // The rows are made when the list is laid out, which a list filled a moment ago has not
        // been yet.
        _list.UpdateLayout();

        if (_list.SelectedIndex >= 0
            && _list.ItemContainerGenerator.ContainerFromIndex(_list.SelectedIndex) is ListBoxItem row)
        {
            row.Focus();
        }
        else
        {
            _list.Focus();
        }
    }

    /// <summary>
    /// Every key is the dock's, but the ones that type: those are let through to arrive as text,
    /// which is how a letter on any layout — a Cyrillic one, a Hebrew one — reaches
    /// <see cref="Typed"/> as the letter it is.
    /// </summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        var command = DockKeys.Command(key, modifiers);

        if (command.Kind != DockKeyKind.None)
        {
            e.Handled = true;
            Command?.Invoke(this, command);
            return;
        }

        // A key that types, with nothing held but Shift, goes on to arrive as what it types.
        // Anything else would only move the invisible list's own selection, or wake a menu
        // there is none of.
        e.Handled = (modifiers & ~ModifierKeys.Shift) != ModifierKeys.None || !Types(key);
    }

    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = true;

        if (e.Text is { Length: > 0 } text
            && !char.IsControl(text[0])
            && !char.IsWhiteSpace(text[0])
            && !char.IsDigit(text[0]))
        {
            Typed?.Invoke(this, text);
        }
    }

    /// <summary>Whether a key types something: a letter, or a punctuation key.</summary>
    private static bool Types(Key key) =>
        key is >= Key.A and <= Key.Z
            or >= Key.Oem1 and <= Key.OemBackslash
            or Key.Multiply or Key.Add or Key.Subtract or Key.Decimal or Key.Divide;
}
