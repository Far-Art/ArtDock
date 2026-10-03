using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Interop;
using ArtDock.Controls;
using ArtDock.Dock;
using ArtDock.IconSets;
using ArtDock.Interop;
using ArtDock.Localization;
using ArtDock.Services;
using Microsoft.Win32;

namespace ArtDock.Views;

/// <summary>
/// Renames a pinned item and lets the user give it a custom icon.
/// </summary>
/// <remarks>
/// A window rather than a context menu on purpose. The dock is
/// <c>WS_EX_NOACTIVATE</c> and takes no keyboard focus, so a popup hosted by it could
/// not host a text box the user can actually type in; a separate, ordinary window can.
/// </remarks>
public sealed partial class EditPinWindow : Window
{
    private string? _iconPath;

    /// <summary>The icon set the dock is drawing from, so the preview shows what the dock will.</summary>
    private readonly IconSet? _iconSet;

    /// <summary>
    /// Whether the icon is to be the folder the dock draws: one of the three ways to have it,
    /// with an image chosen for the item and the item's own icon, each of which undoes the others.
    /// </summary>
    private bool _drawsFolder;

    /// <summary>
    /// The colour the folder is drawn in. Kept while the drawn folder is turned off, so that
    /// customizing it again before saving brings back what was chosen.
    /// </summary>
    private Color _folderColor;

    /// <summary>The symbol on the folder, as its glyph; null for none.</summary>
    private string? _folderSymbol;

    /// <summary>A few letters on the folder in place of the symbol; null for none.</summary>
    private string? _folderText;

    /// <summary>What the symbol, or the text, is painted in.</summary>
    private FolderSymbolTone _tone;

    /// <summary>
    /// Set while the dialog writes a box itself — the hex box after the wheel moves, the text box
    /// after a symbol is picked — so the box's own change handler does not take it for typing.
    /// </summary>
    private bool _writing;

    private readonly ColorWheel _folderWheel = new();

    private readonly List<(ToggleButton Tile, string? Glyph)> _symbolTiles = [];

    /// <summary>
    /// A text style that sets nothing, for text inside the symbol tiles: being explicit, it keeps
    /// the window's own implicit one — which holds every TextBlock to the ordinary text colour —
    /// off them, so they take the tile's colour, which turns to the accent's when it is pressed.
    /// </summary>
    private readonly Style _tileText = new(typeof(TextBlock));

    /// <summary>
    /// Whether what the item opens is a folder, and for which target that was asked — a trip
    /// to the shell, so asked once per target rather than on every tick of the colour wheel.
    /// </summary>
    private (string? Target, bool IsFolder)? _folderCheck;

    /// <summary>
    /// The name <em>Reset</em> puts back, and for which target it was asked — a file's version
    /// resource or a trip to the shell, so asked once per target rather than on every keystroke
    /// in the name box.
    /// </summary>
    private (string? Target, string? Name)? _defaultName;

    public EditPinWindow(DockItem item, IconSet? iconSet = null)
    {
        InitializeComponent();

        Item = item;
        _iconPath = item.IconPath;
        _iconSet = iconSet;

        NameBox.Text = item.Label;
        TargetBox.Text = item.TargetPath ?? item.Aumid ?? string.Empty;
        UseIconBox.IsChecked = item.UseIconNotThumbnail;
        RunAsAdminBox.IsChecked = item.RunAsAdministrator;

        // A folder that was never drawn starts from the design's own colour, so customizing it
        // shows the folder as it was meant to look rather than a colour nobody chose.
        _drawsFolder = item.FolderColor is { Length: > 0 };
        _folderColor = FolderArt.ParseColor(item.FolderColor) ?? FolderArt.DefaultColor;
        _folderSymbol = FolderArt.ParseSymbol(item.FolderSymbol);
        _folderText = FolderArt.ParseText(item.FolderText);
        _tone = FolderArt.ParseTone(item.FolderSymbolTone);

        BuildFolderControls();

        // A folder already customized opens with its customization out, which is what it is
        // most likely being edited for. Pressed here, before the button's handler is attached,
        // so opening is all it does: Customize would also set aside an image chosen for the item.
        if (_drawsFolder && _iconPath is not { Length: > 0 })
        {
            CustomizeButton.IsChecked = true;
            CustomizePanel.Visibility = Visibility.Visible;
        }

        // The window takes the height of what it shows, which for a folder is a good deal more
        // than for anything else — but never more than the display it is on, where it scrolls
        // instead. The main display's until the window exists; then the display WPF centres it
        // on, its owner's if it has one; then wherever it is moved, since the displays need
        // not be the same height.
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - HeightMargin);
        SourceInitialized += (_, _) => CapHeight(
            new WindowInteropHelper(Owner ?? this).Handle);
        LocationChanged += (_, _) => CapHeight(new WindowInteropHelper(this).Handle);

        // The dock holds this item's label open while the dialog is up, so a name being
        // typed should appear there as it is typed rather than only after saving.
        NameBox.TextChanged += (_, _) =>
        {
            RaisePreview();
            ShowDefaultName();
        };

        ResetNameButton.Click += (_, _) => ResetName();
        BrowseButton.Click += (_, _) => ChooseImage();
        BrowseTargetButton.Click += (_, _) => ChooseTarget();

        // The icon follows what the item opens, so a changed target re-reads it — the
        // preview would otherwise keep showing the old app's glyph. On lost focus rather
        // than on every keystroke: resolving an icon is a COM round trip to the shell, and
        // every half-typed path would cost one. The default name follows it for the same
        // reason, and at the same cost.
        TargetBox.LostFocus += (_, _) =>
        {
            RefreshIcon();
            ShowDefaultName();
        };
        ResetIconButton.Click += (_, _) => UseOwnIcon();
        CustomizeButton.Checked += (_, _) => Customize();
        CustomizeButton.Unchecked += (_, _) => CustomizePanel.Visibility = Visibility.Collapsed;
        UseIconBox.Checked += (_, _) => RefreshIcon();
        UseIconBox.Unchecked += (_, _) => RefreshIcon();
        SaveButton.Click += (_, _) => { DialogResult = true; };

        RefreshIcon();
        ShowDefaultName();

        Loaded += (_, _) =>
        {
            // The dock never takes the foreground, so its process has no foreground
            // rights and this dialog opens unfocused — you would have to click it before
            // you could type. AppLauncher.Activate does the AttachThreadInput dance that
            // Windows requires to hand focus over.
            AppLauncher.Activate(new WindowInteropHelper(this).Handle);

            NameBox.SelectAll();
            NameBox.Focus();
        };
    }

    /// <summary>The item being edited.</summary>
    public DockItem Item { get; }

    /// <summary>Raised as the name is typed, so the dock can show it live.</summary>
    public event EventHandler? PreviewChanged;

    private void RaisePreview() => PreviewChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>The edited name, trimmed. Falls back to the original if left blank.</summary>
    public string EditedLabel =>
        string.IsNullOrWhiteSpace(NameBox.Text) ? Item.Label : NameBox.Text.Trim();

    /// <summary>The chosen icon path, or null to use the target's own icon.</summary>
    public string? EditedIconPath => _iconPath;

    /// <summary>
    /// Whether a picture is drawn with its icon rather than its thumbnail.
    /// </summary>
    /// <remarks>
    /// Read against the target as it stands, not as it stood when the box was last shown: a
    /// target typed and saved with Enter never loses focus, so nothing has re-read it. Kept only
    /// for a picture, so the file never holds a choice the dialog no longer offers for it.
    /// </remarks>
    public bool EditedUseIconNotThumbnail =>
        UseIconBox.IsChecked == true && !IsStoreTarget && PinnedAppsService.OpensPicture(EditedTargetPath);

    /// <summary>Whether the item's program is started as administrator.</summary>
    /// <remarks>
    /// Kept only for a program that can be, and read against the target as it stands, as the
    /// thumbnail choice is.
    /// </remarks>
    public bool EditedRunAsAdministrator => RunAsAdminBox.IsChecked == true && TargetElevates;

    /// <summary>Whether what the item opens, as it stands, is a program Windows can run as administrator.</summary>
    private bool TargetElevates =>
        !IsStoreTarget
        && PinnedAppsService.CanRunAsAdministrator(
            EditedTargetPath, PinnedAppsService.ResolveLinkTarget(EditedTargetPath));

    /// <summary>The colour of the folder the dock is to draw, or null for the shell's icon.</summary>
    /// <remarks>
    /// Kept only for a folder, as the thumbnail choice is kept only for a picture, and read
    /// against the target as it stands.
    /// </remarks>
    public string? EditedFolderColor => DrawsFolder ? FolderArt.FormatColor(_folderColor) : null;

    /// <summary>The symbol on that folder, or null for none — and for text, which takes its place.</summary>
    public string? EditedFolderSymbol =>
        DrawsFolder && _folderText is null && _folderSymbol is { Length: > 0 } glyph ? FolderArt.FormatSymbol(glyph) : null;

    /// <summary>The text on that folder, or null for none.</summary>
    public string? EditedFolderText => DrawsFolder ? _folderText : null;

    /// <summary>What the symbol or the text is painted in, or null for toned.</summary>
    public string? EditedFolderSymbolTone => DrawsFolder ? FolderArt.FormatTone(_tone) : null;

    /// <summary>Whether the dock is to draw this item as a folder of its own.</summary>
    private bool DrawsFolder => _drawsFolder && IsFolderTarget();

    /// <summary>Whether the item opens a folder on disk, by path or by <c>shell:</c> name.</summary>
    /// <remarks>
    /// A folder already drawn that still opens the same place counts whatever the shell says
    /// now, so a share that happens to be offline while the dialog is open does not lose its
    /// colour on Save.
    /// </remarks>
    private bool IsFolderTarget()
    {
        var target = EditedTargetPath;
        if (_folderCheck is { } check && string.Equals(check.Target, target, StringComparison.Ordinal))
        {
            return check.IsFolder;
        }

        var drawnBefore = Item.FolderColor is { Length: > 0 }
            && string.Equals(target, Item.TargetPath, StringComparison.OrdinalIgnoreCase);
        var isFolder = !IsStoreTarget && (drawnBefore || ShellNames.IsFileSystemFolder(target));
        _folderCheck = (target, isFolder);
        return isFolder;
    }

    /// <summary>
    /// What the item should open. Null when the item is a Store app, whose activation goes
    /// through <see cref="EditedAumid"/> instead.
    /// </summary>
    public string? EditedTargetPath => IsStoreTarget ? null : Trimmed(TargetBox.Text);

    /// <summary>The Store app id, when that is what this item was and still is.</summary>
    public string? EditedAumid => IsStoreTarget ? Trimmed(TargetBox.Text) : null;

    /// <summary>
    /// Whether the box holds an AUMID rather than a path.
    /// </summary>
    /// <remarks>
    /// Decided by what the item already was rather than by inspecting the text: an AUMID is
    /// just a string with a bang in it, and guessing wrong would send activation down the
    /// wrong path. Editing a Store item's id keeps it a Store item; a classic app stays a
    /// classic app whatever is typed.
    /// </remarks>
    private bool IsStoreTarget => Item.Aumid is { Length: > 0 };

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Fills the folder's controls, which Customize opens: the wheel, the swatches and the hex
    /// box for its colour; what its symbol is painted in; a tile for each symbol it can carry;
    /// and the box for text in place of one.
    /// </summary>
    private void BuildFolderControls()
    {
        _folderWheel.Color = _folderColor;
        _folderWheel.ColorPicked += (_, color) => SetFolderColor(color, fromWheel: true);
        FolderWheelHost.Content = _folderWheel;

        foreach (var hex in FolderArt.Swatches)
        {
            FolderSwatchPanel.Children.Add(BuildSwatch(hex));
        }

        WriteHex();

        // Taken as it is typed, once it reads as a colour; half-typed hex changes nothing.
        FolderHexBox.TextChanged += (_, _) =>
        {
            if (!_writing && FolderArt.TryParseColor(FolderHexBox.Text) is { } typed)
            {
                SetFolderColor(typed, fromWheel: false, fromHex: true);
            }
        };

        // Put back as the colour it stands for once the box is left, so "f00" reads "#FF0000".
        FolderHexBox.LostFocus += (_, _) => WriteHex();

        foreach (var (button, tone) in new[]
                 {
                     (ToneTonedButton, FolderSymbolTone.Toned),
                     (ToneWhiteButton, FolderSymbolTone.White),
                     (ToneBlackButton, FolderSymbolTone.Black)
                 })
        {
            button.IsChecked = _tone == tone;
            button.Checked += (_, _) =>
            {
                _tone = tone;
                ShowFolder();
            };
        }

        AddSymbolTile(null, Localizer.Get("EditItem.Symbol.None"));
        foreach (var symbol in FolderArt.Symbols)
        {
            AddSymbolTile(symbol.Glyph, Localizer.Get(symbol.NameKey));
        }

        FolderTextBox.MaxLength = FolderArt.MaxTextLength;
        FolderTextBox.Text = _folderText ?? string.Empty;

        // Text takes the symbol's place as soon as there is any; emptied, the symbol is back.
        FolderTextBox.TextChanged += (_, _) =>
        {
            if (_writing)
            {
                return;
            }

            _folderText = FolderArt.ParseText(FolderTextBox.Text);
            MarkSymbol();
            ShowFolder();
        };

        MarkSymbol();
    }

    /// <summary>A colour from the wheel, a swatch or the hex box, carried to the other two and shown.</summary>
    private void SetFolderColor(Color color, bool fromWheel, bool fromHex = false)
    {
        _folderColor = color;
        if (!fromWheel)
        {
            _folderWheel.Color = color;
        }

        if (!fromHex)
        {
            WriteHex();
        }

        ShowFolder();
    }

    /// <summary>The colour into the hex box, as the dialog's own write rather than typing.</summary>
    private void WriteHex()
    {
        _writing = true;
        FolderHexBox.Text = FolderArt.FormatColor(_folderColor);
        _writing = false;
    }

    /// <summary>One colour swatch, drawn as the settings dialog draws the bar's.</summary>
    private Button BuildSwatch(string hex)
    {
        var color = FolderArt.ParseColor(hex) ?? FolderArt.DefaultColor;
        var fill = new SolidColorBrush(color);
        fill.Freeze();

        var swatch = new Button
        {
            Width = 30,
            Height = 22,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(0),
            ToolTip = hex,

            // The colour lives in a child rather than in the button's own Background, which the
            // template repaints on hover — see the settings dialog's swatches.
            Content = new Border
            {
                Width = 22,
                Height = 14,
                Background = fill,
                CornerRadius = new CornerRadius(3)
            }
        };

        swatch.Click += (_, _) => SetFolderColor(color, fromWheel: false);

        return swatch;
    }

    /// <summary>One symbol tile; <paramref name="glyph"/> null for the tile that takes the symbol away.</summary>
    private void AddSymbolTile(string? glyph, string name)
    {
        var text = new TextBlock
        {
            Style = _tileText,
            Text = glyph ?? FolderArt.NoSymbolGlyph,
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // In the theme's own symbol font, as the menus' glyphs are, and as the folder is drawn.
        text.SetResourceReference(TextBlock.FontFamilyProperty, "SymbolThemeFontFamily");

        var tile = new ToggleButton
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 3, 3),
            ToolTip = name,
            Content = text
        };

        // A glyph means nothing read aloud, so the tile is named for what it shows.
        AutomationProperties.SetName(tile, name);

        // A symbol picked is a symbol shown, so it takes the place of any text.
        tile.Click += (_, _) =>
        {
            _folderSymbol = glyph;
            _folderText = null;
            _writing = true;
            FolderTextBox.Text = string.Empty;
            _writing = false;
            MarkSymbol();
            ShowFolder();
        };

        _symbolTiles.Add((tile, glyph));
        SymbolPanel.Children.Add(tile);
    }

    /// <summary>
    /// Shows which symbol is chosen: one tile pressed, as in a group of radio buttons — a
    /// pressed tile clicked again stays pressed rather than leaving none — and none while text
    /// stands in the symbol's place.
    /// </summary>
    private void MarkSymbol()
    {
        foreach (var (tile, glyph) in _symbolTiles)
        {
            tile.IsChecked = _folderText is null && glyph == _folderSymbol;
        }
    }

    /// <summary>Shows the folder in the colour and with the symbol just picked.</summary>
    private void ShowFolder()
    {
        _drawsFolder = true;
        RefreshIcon();
    }

    /// <summary>
    /// The drawn folder, with its colour and symbol opened under the button. Pressing it is
    /// asking for the folder, so the folder is drawn at once — in the colour last chosen, or the
    /// design's own — rather than when something is picked, which would leave the panel showing
    /// a colour the icon above it is not in.
    /// </summary>
    private void Customize()
    {
        _iconPath = null;
        _drawsFolder = true;
        CustomizePanel.Visibility = Visibility.Visible;
        RefreshIcon();
    }

    /// <summary>
    /// The item's own icon, as the shell has it: no chosen image, and no drawn folder either —
    /// the button had cleared only the image, and left a drawn folder standing in front of it.
    /// </summary>
    private void UseOwnIcon()
    {
        _iconPath = null;
        _drawsFolder = false;
        CustomizeButton.IsChecked = false;
        RefreshIcon();
    }

    /// <summary>Room left between the window and the edges of the work area, in DIPs.</summary>
    private const double HeightMargin = 40;

    /// <summary>
    /// Holds the window to the work area of the display <paramref name="hwnd"/> is on, in that
    /// display's DIPs, which is what WPF keeps a window's size in across a change of scale.
    /// </summary>
    /// <remarks>
    /// Was the main display's (<c>SystemParameters.WorkArea</c>) wherever the editor opened.
    /// Both displays here are 1392 DIPs tall, so it never showed; a shorter second display
    /// would have had a folder's editor run past its bottom edge rather than scroll. Setting an
    /// unchanged height does nothing, so asking on every move costs two calls into Windows.
    /// </remarks>
    private void CapHeight(nint hwnd)
    {
        if (hwnd != 0 && MonitorDpi.WorkAreaHeightForWindow(hwnd) is { } height)
        {
            MaxHeight = Math.Max(MinHeight, height - HeightMargin);
        }
    }

    /// <summary>An image chosen for the item, in place of the drawn folder as of the item's own icon.</summary>
    private void UseImage(string path)
    {
        _iconPath = path;
        _drawsFolder = false;
        CustomizeButton.IsChecked = false;
        RefreshIcon();
    }


    /// <summary>Picks what the item opens.</summary>
    private void ChooseTarget()
    {
        var dialog = new OpenFileDialog
        {
            Title = Localizer.Get("EditItem.ChooseTarget.Title"),
            Filter = FileFilters.Pinnable,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            TargetBox.Text = dialog.FileName;
            RefreshIcon();
            ShowDefaultName();
        }
    }

    /// <summary>
    /// The name the item would be given if what it opens, as the dialog has it now, were pinned
    /// afresh — see <see cref="PinnedAppsService.DefaultLabel"/>. Null when there is none.
    /// </summary>
    private string? DefaultName()
    {
        var target = Trimmed(TargetBox.Text);
        if (_defaultName is { } known && string.Equals(known.Target, target, StringComparison.Ordinal))
        {
            return known.Name;
        }

        var name = PinnedAppsService.DefaultLabel(EditedTargetPath, EditedAumid);
        _defaultName = (target, name);
        return name;
    }

    /// <summary>
    /// Offers <em>Reset</em> only when it would change the name, and says on it what it would
    /// change it to.
    /// </summary>
    private void ShowDefaultName()
    {
        var name = DefaultName();
        ResetNameButton.IsEnabled = name is not null
            && !string.Equals(name, NameBox.Text.Trim(), StringComparison.Ordinal);
        ResetNameButton.ToolTip = name is null ? null : Localizer.Format("EditItem.ResetName.Tip", name);
    }

    /// <summary>
    /// Puts the default name in the box, where it is shown on the dock as typing would be, and
    /// saved — or not — with everything else.
    /// </summary>
    private void ResetName()
    {
        if (DefaultName() is not { } name)
        {
            return;
        }

        NameBox.Text = name;

        // The button greys as the name changes, and would leave the keyboard nowhere.
        NameBox.Focus();
        NameBox.CaretIndex = name.Length;
    }

    private void ChooseImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = Localizer.Get("EditItem.ChooseIcon.Title"),
            Filter = FileFilters.Images,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            UseImage(dialog.FileName);
        }
    }

    private void RefreshIcon()
    {
        // Preview exactly what the dock will show, by asking for it the same way — and
        // against the edited target, so changing what the item opens changes its icon here
        // rather than only after saving.
        var preview = new DockItem
        {
            Id = Item.Id,
            Label = Item.Label,
            TargetPath = EditedTargetPath,
            Aumid = EditedAumid,
            IconPath = _iconPath,
            UseIconNotThumbnail = UseIconBox.IsChecked == true,
            FolderColor = EditedFolderColor,
            FolderSymbol = EditedFolderSymbol,
            FolderText = EditedFolderText,
            FolderSymbolTone = EditedFolderSymbolTone,

            // An icon set matches a shortcut by what it points at, as the dock does.
            LinkTarget = PinnedAppsService.ResolveLinkTarget(EditedTargetPath)
        };

        // A drawn folder is drawn here without being kept. LoadIcon keeps what it is given, and
        // dragging the colour wheel would leave a folder in its caches for every colour passed.
        var drawsFolder = DrawsFolder && _iconPath is not { Length: > 0 };
        IconPreview.Source = drawsFolder
            ? FolderArt.Draw(FolderArt.Look(_folderColor, _folderSymbol, _folderText, _tone))
            : PinnedAppsService.LoadIcon(preview, _iconSet);

        // Offered for a folder only; a target changed to something else closes what it opened.
        var isFolder = IsFolderTarget();
        CustomizeButton.Visibility = isFolder ? Visibility.Visible : Visibility.Collapsed;
        if (!isFolder)
        {
            CustomizeButton.IsChecked = false;
        }

        // Offered for a picture only, and greyed while a chosen image is shown, which wins
        // over the thumbnail and the icon alike.
        UseIconBox.Visibility = !IsStoreTarget && PinnedAppsService.OpensPicture(EditedTargetPath)
            ? Visibility.Visible
            : Visibility.Collapsed;
        UseIconBox.IsEnabled = _iconPath is not { Length: > 0 };

        // Offered for a program only. Here with the icon because it follows the target the same
        // way, and is asked as rarely: a shortcut is read to see what it points at.
        RunAsAdminBox.Visibility = TargetElevates ? Visibility.Visible : Visibility.Collapsed;

        // Says where the picture comes from, which with a set in use is not always the app:
        // the set is asked the same question the dock will ask it.
        IconPathText.Text = _iconPath is { Length: > 0 }
            ? Path.GetFileName(_iconPath)
            : PinnedAppsService.ThumbnailFor(preview) is not null
                ? Localizer.Get("EditItem.UsingThumbnail")
            : drawsFolder
                ? Localizer.Get("EditItem.UsingFolder")
            : _iconSet is not null && _iconSet.FileFor(PinnedAppsService.SubjectOf(preview, _iconSet)) is not null
                ? Localizer.Format("EditItem.UsingSetIcon", _iconSet.Name)
                : Localizer.Get("EditItem.UsingOwnIcon");
    }
}
