using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ArtDock.Interop;
using ArtDock.Services;

namespace ArtDock.Views;

/// <summary>
/// The glyphs drawn beside commands: in the menus, and on the settings dialog's buttons.
/// </summary>
/// <remarks>
/// Named for what the entry does rather than for the picture, so that the same command in two
/// menus — <em>Settings…</em> is on both the dock's and the tray's — cannot be given two
/// different pictures. Nor can it on a button: the Items page's <em>Remove</em> is the dock
/// menu's <em>Remove from dock</em>, and draws its glyph.
/// </remarks>
public enum MenuGlyph
{
    Edit,
    Unpin,
    Add,
    Settings,
    Locked,
    Delete,
    Hide,
    Show,
    Exit,
    Browse,
    Apps,
    Games,
    Info,
    MoveUp,
    MoveDown,
    ClearAll,

    /// <summary>The symbol font's <em>Admin</em>, for what starts a program as administrator.</summary>
    Administrator
}

/// <summary>
/// What fills a menu's icon column: a glyph for a command, and the target's own icon for an
/// entry that pins one.
/// </summary>
/// <remarks>
/// <para>
/// Every entry gets one, because the column is there whether it is used or not. The Fluent
/// theme's menu item puts its icon in a column the whole menu shares, so a single icon indents
/// every entry, and the ones left without read as unfinished. <see cref="MenuHost"/> makes the
/// icon a parameter for that reason, rather than something to remember.
/// </para>
/// <para>
/// The glyphs are drawn in the theme's own symbol font, <c>SymbolThemeFontFamily</c> — Segoe
/// Fluent Icons, falling back to Segoe MDL2 Assets — which is what Windows 11 draws its own
/// menus with, and what the theme already draws a menu's check marks and chevrons in. So the
/// font is present wherever the menus are themed at all, and a glyph is the system's rather
/// than an imitation. <c>MenuGlyphTests</c> holds every code point below against both fonts.
/// </para>
/// <para>
/// A glyph takes its colour from the entry it is in, because a <see cref="TextBlock"/> inherits
/// <c>Foreground</c>. That covers both of the entries that are not in the menu's ordinary text
/// colour: <em>Remove from dock</em>, which <see cref="MenuHost.DangerItem"/> paints in the
/// critical colour, and a note, which the theme greys by setting the disabled colour on the
/// entry itself rather than on its header.
/// </para>
/// <para>
/// The settings dialog's buttons draw the same glyphs before their words, named through
/// <see cref="GlyphProperty"/>, and take their colour from the button the same way.
/// </para>
/// </remarks>
public static class MenuIcons
{
    /// <summary>The icon column's size in DIPs: Windows 11's own menus draw their glyphs at 16.</summary>
    private const double Size = 16;

    /// <summary>Twice <see cref="Size"/>, so an image is still sharp on a 200% display.</summary>
    private const int ImagePixels = 32;

    /// <summary>
    /// The images of the targets the Add menu offers, read once. The dock's menu is built
    /// afresh on every right-click, and reading its eight shell icons each time was measured at
    /// about 25 ms warm and 190 ms cold — a pause between the click and the menu.
    /// </summary>
    private static readonly Dictionary<string, ImageSource> TargetIcons =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A glyph for a command.</summary>
    public static TextBlock Glyph(MenuGlyph glyph)
    {
        var text = new TextBlock
        {
            Text = CodePoint(glyph),
            FontSize = Size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Referenced rather than named, so it is exactly the font the theme draws its own
        // symbols in, fallback and all.
        text.SetResourceReference(TextBlock.FontFamilyProperty, "SymbolThemeFontFamily");
        return text;
    }

    /// <summary>
    /// The glyph a button draws before its words, named in its XAML by the command it carries —
    /// <c>views:MenuIcons.Glyph="Add"</c> — and drawn by the settings dialog's
    /// <c>GlyphButton</c> style.
    /// </summary>
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
        "Glyph", typeof(MenuGlyph?), typeof(MenuIcons));

    public static MenuGlyph? GetGlyph(DependencyObject element) => (MenuGlyph?)element.GetValue(GlyphProperty);

    public static void SetGlyph(DependencyObject element, MenuGlyph? glyph) => element.SetValue(GlyphProperty, glyph);

    /// <summary>A glyph's character in the symbol font, for a binding to <see cref="GlyphProperty"/>.</summary>
    public static IValueConverter CodePointOf { get; } = new CodePointConverter();

    private sealed class CodePointConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is MenuGlyph glyph ? CodePoint(glyph) : null;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>The character each glyph is in the symbol font.</summary>
    public static string CodePoint(MenuGlyph glyph) => glyph switch
    {
        MenuGlyph.Edit => "",
        MenuGlyph.Unpin => "",
        MenuGlyph.Add => "",
        MenuGlyph.Settings => "",
        MenuGlyph.Locked => "",
        MenuGlyph.Delete => "",
        MenuGlyph.Hide => "",
        MenuGlyph.Show => "",
        MenuGlyph.Exit => "",
        MenuGlyph.Browse => "",
        MenuGlyph.Apps => "",
        MenuGlyph.Games => "",
        MenuGlyph.Info =>"",
        MenuGlyph.MoveUp => "",
        MenuGlyph.MoveDown => "",
        MenuGlyph.ClearAll => "",
        MenuGlyph.Administrator => "",
        _ => throw new ArgumentOutOfRangeException(nameof(glyph), glyph, null)
    };

    /// <summary>An image in the icon column.</summary>
    /// <returns>
    /// Null for no image, which leaves the entry's column empty — the theme collapses the
    /// icon for a null rather than laying out an image of nothing.
    /// </returns>
    public static Image? Picture(ImageSource? source)
    {
        if (source is null)
        {
            return null;
        }

        var image = new Image { Source = source, Width = Size, Height = Size };

        // Every image here is shrunk to fit, from twice the size or more, and the default
        // linear filter does that by skipping pixels: a thin line in the icon breaks up.
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    /// <summary>
    /// The icon for an entry of the Add menu: what it pins, drawn as itself.
    /// </summary>
    /// <remarks>
    /// The target's own icon rather than a generic glyph, because each of these is a real
    /// target the entry puts on the dock, and it is recognised by its icon before its name.
    /// Not the active icon set's, though the pin will be drawn with that once it is made: the
    /// menu says what is being added, not how the dock will dress it.
    /// </remarks>
    public static FrameworkElement? For(DockPreset preset) => preset.Key switch
    {
        DockPresets.BrowseKey => Glyph(MenuGlyph.Browse),
        DockPresets.SearchAppsKey => Glyph(MenuGlyph.Apps),
        DockPresets.SearchGamesKey => Glyph(MenuGlyph.Games),
        DockPresets.SeparatorKey => Rule(),
        _ => Picture(TargetIcon(preset.Target))
    };

    /// <summary>The image the shell has for a target, or the one a command ships with.</summary>
    /// <remarks>
    /// The Recycle Bin is read afresh every time, as it is for the dock itself — it is drawn
    /// full or empty and changes between the two by itself. And a target the shell gave no
    /// image for is asked again next time rather than remembered as having none, so one bad
    /// answer does not blank an entry until the dock restarts.
    /// </remarks>
    private static ImageSource? TargetIcon(string? target)
    {
        if (target is not { Length: > 0 })
        {
            return null;
        }

        if (TargetIcons.TryGetValue(target, out var cached))
        {
            return cached;
        }

        var icon = DockCommands.IsCommand(target)
            ? DockCommands.IconFor(target)
            : ShellIcons.Load(target, ImagePixels);

        if (icon is not null && !DockPresets.IsRecycleBin(target))
        {
            TargetIcons[target] = icon;
        }

        return icon;
    }

    /// <summary>
    /// The Separator entry's icon: the rule it adds, drawn the way the dock draws one — a
    /// short upright bar with round ends — since the font has no glyph that is one.
    /// </summary>
    private static Grid Rule()
    {
        const double thickness = 1.5;

        var rule = new Border
        {
            Width = thickness,
            Height = 12,
            CornerRadius = new CornerRadius(thickness / 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // In the entry's text colour, found through the entry: a Border's background inherits
        // nothing, where a glyph's colour follows the entry's by itself.
        rule.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(MenuItem), 1)
        });

        // Held at the size of a glyph, so the column is as wide as it is for every other entry
        // and the rule stands in the middle of it.
        return new Grid { Width = Size, Height = Size, Children = { rule } };
    }
}
