using System.IO;
using System.Windows;
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

    public EditPinWindow(DockItem item, IconSet? iconSet = null)
    {
        InitializeComponent();

        Item = item;
        _iconPath = item.IconPath;
        _iconSet = iconSet;

        NameBox.Text = item.Label;
        TargetBox.Text = item.TargetPath ?? item.Aumid ?? string.Empty;
        UseIconBox.IsChecked = item.UseIconNotThumbnail;

        BuildFontControls();

        FontFamilyBox.SelectionChanged += (_, _) => RaisePreview();
        FontStyleBox.SelectionChanged += (_, _) => RaisePreview();
        FontSizeSlider.ValueChanged += (_, _) => RaisePreview();

        // The dock holds this item's label open while the dialog is up, so a name being
        // typed should appear there as it is typed rather than only after saving.
        NameBox.TextChanged += (_, _) => RaisePreview();

        BrowseButton.Click += (_, _) => ChooseImage();
        BrowseTargetButton.Click += (_, _) => ChooseTarget();

        // The icon follows what the item opens, so a changed target re-reads it — the
        // preview would otherwise keep showing the old app's glyph. On lost focus rather
        // than on every keystroke: resolving an icon is a COM round trip to the shell, and
        // every half-typed path would cost one.
        TargetBox.LostFocus += (_, _) => RefreshIcon();
        ResetIconButton.Click += (_, _) => SetIconPath(null);
        UseIconBox.Checked += (_, _) => RefreshIcon();
        UseIconBox.Unchecked += (_, _) => RefreshIcon();
        SaveButton.Click += (_, _) => { DialogResult = true; };

        RefreshIcon();

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

    /// <summary>Raised whenever the label styling changes, so the dock can show it live.</summary>
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

    /// <summary>Chosen typeface, or null to use the dock's default.</summary>
    public string? EditedFontFamily =>
        FontFamilyBox.SelectedIndex <= 0 ? null : FontFamilyBox.SelectedItem as string;

    /// <summary>Chosen size, or null when it matches the dock's default.</summary>
    public double? EditedFontSize =>
        Math.Abs(FontSizeSlider.Value - DefaultFontSize) < 0.01 ? null : FontSizeSlider.Value;

    /// <summary>
    /// Chosen emphasis. Always explicit — there is no "leave it" entry.
    /// </summary>
    /// <remarks>
    /// The family dropdown does have one, because a typeface the user never chose should
    /// keep following the dock if the dock's default ever changes. Emphasis is different:
    /// the dock's default is <c>Bold</c>, so an entry meaning "no override" would have to be
    /// labelled Bold to be truthful, and would then sit next to the Bold entry doing the
    /// same thing.
    /// </remarks>
    public string? EditedFontStyle => StyleKeys[Math.Max(0, FontStyleBox.SelectedIndex)];

    /// <summary>Matches the dock's own default label size.</summary>
    private const double DefaultFontSize = DockBar.DefaultTooltipSize;

    /// <summary>Stored values behind the style dropdown, in the order it lists them.</summary>
    private static readonly string[] StyleKeys = ["Regular", "Bold", "Italic", "BoldItalic"];

    /// <summary>
    /// Fills the font controls. The first entry in each dropdown means "leave it to the
    /// dock", so an item that was never customised keeps following the default if that
    /// default later changes.
    /// </summary>
    private void BuildFontControls()
    {
        FontFamilyBox.Items.Add(Localizer.Get("EditItem.DockDefault"));
        foreach (var family in Fonts.SystemFontFamilies
                     .Select(f => f.Source)
                     .Distinct()
                     .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
        {
            FontFamilyBox.Items.Add(family);
        }

        FontFamilyBox.SelectedIndex = 0;
        if (Item.FontFamily is { Length: > 0 } chosen)
        {
            var index = FontFamilyBox.Items.IndexOf(chosen);
            FontFamilyBox.SelectedIndex = index >= 0 ? index : 0;
        }

        foreach (var label in new[]
                 {
                     Localizer.Get("EditItem.Style.Regular"),
                     Localizer.Get("EditItem.Style.Bold"),
                     Localizer.Get("EditItem.Style.Italic"),
                     Localizer.Get("EditItem.Style.BoldItalic")
                 })
        {
            FontStyleBox.Items.Add(label);
        }

        // An item that has never been styled is drawn in the dock's default, so that is what
        // the dropdown has to show — otherwise the dialog would name a weight the label in
        // front of it is not using.
        var stored = Array.IndexOf(StyleKeys, Item.FontStyle ?? string.Empty);
        FontStyleBox.SelectedIndex = stored >= 0
            ? stored
            : Array.IndexOf(StyleKeys, DockBar.DefaultTooltipStyle);

        FontSizeSlider.Value = Math.Clamp(Item.FontSize ?? DefaultFontSize, 8, 28);
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
        }
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
            SetIconPath(dialog.FileName);
        }
    }

    private void SetIconPath(string? path)
    {
        _iconPath = path;
        RefreshIcon();
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

            // An icon set matches a shortcut by what it points at, as the dock does.
            LinkTarget = PinnedAppsService.ResolveLinkTarget(EditedTargetPath)
        };

        IconPreview.Source = PinnedAppsService.LoadIcon(preview, _iconSet);

        // Offered for a picture only, and greyed while a chosen image is shown, which wins
        // over the thumbnail and the icon alike.
        UseIconBox.Visibility = !IsStoreTarget && PinnedAppsService.OpensPicture(EditedTargetPath)
            ? Visibility.Visible
            : Visibility.Collapsed;
        UseIconBox.IsEnabled = _iconPath is not { Length: > 0 };

        // Says where the picture comes from, which with a set in use is not always the app:
        // the set is asked the same question the dock will ask it.
        IconPathText.Text = _iconPath is { Length: > 0 }
            ? Path.GetFileName(_iconPath)
            : PinnedAppsService.ThumbnailFor(preview) is not null
                ? Localizer.Get("EditItem.UsingThumbnail")
            : _iconSet is not null && _iconSet.FileFor(PinnedAppsService.SubjectOf(preview, _iconSet)) is not null
                ? Localizer.Format("EditItem.UsingSetIcon", _iconSet.Name)
                : Localizer.Get("EditItem.UsingOwnIcon");
    }
}
