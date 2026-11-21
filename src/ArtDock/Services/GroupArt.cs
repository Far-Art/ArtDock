using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArtDock.Services;

/// <summary>
/// The picture on a group of the dock's own (<see cref="PinnedAppSetting.Items"/>): the first
/// four things it holds, two by two on a pale tile, as Windows 11's Start menu draws a folder of
/// its pins. An empty group is the tile alone. A group an icon is being dragged into has a look of
/// its own (<see cref="Open"/>): dark, with a light rim, around the same four.
/// </summary>
/// <remarks>
/// <para>
/// The first four in the group's order and no others — no choosing by use or by name — so the
/// picture changes only when the group's first four do.
/// </para>
/// <para>
/// A bitmap, as every icon the dock draws is, in the 256-unit box Windows' icons are designed in
/// with the tile inset as far as <see cref="OverflowArt"/>'s, so the two sit level with the rest.
/// Drawn at twice its size and averaged down by hand (<see cref="FolderArt.Shrink"/>), since
/// <see cref="RenderTargetBitmap"/> shrinks the small icons by sampling, whatever it is told.
/// </para>
/// </remarks>
public static class GroupArt
{
    /// <summary>How many of a group's items its picture shows.</summary>
    public const int Shown = 4;

    /// <summary>
    /// Twice the dock's usual 128: each of the four is drawn at about a quarter of the tile, and
    /// a magnified icon on a scaled display is well over 128 pixels.
    /// </summary>
    private const int Pixels = 256;

    private const int Supersample = 2;

    /// <summary>The design box every icon of Windows' is drawn in.</summary>
    private const double Box = 256;

    /// <summary>The tile, inset as the overflow item's is.</summary>
    private static readonly Rect Tile = new(24, 24, 208, 208);

    private const double TileRadius = 48;

    /// <summary>The open look's tile: wider than the resting one, as if it had opened out round the icons.</summary>
    private static readonly Rect OpenTile = new(6, 6, 244, 244);

    private const double OpenTileRadius = 58;

    /// <summary>Between the tile's edge and the icons, and between the icons.</summary>
    private const double Inset = 30;

    private const double Gutter = 16;

    /// <summary>
    /// The pictures drawn lately, by the icons they show — compared by reference, since the icons
    /// are the shell's cached, frozen ones and come back the same objects while they are the same.
    /// Kept short: there are only so many groups on a dock.
    /// </summary>
    private static readonly List<(ImageSource?[] Icons, BitmapSource Picture)> Drawn = [];

    /// <summary>The same, of the open look.</summary>
    private static readonly List<(ImageSource?[] Icons, BitmapSource Picture)> DrawnOpen = [];

    /// <summary>The same, of the icons alone.</summary>
    private static readonly List<(ImageSource?[] Icons, BitmapSource Picture)> DrawnIcons = [];

    /// <summary>The picture's looks: on the dock, open for an icon dragged onto it, and the icons alone.</summary>
    private enum Look
    {
        Closed,
        Open,
        IconsOnly,
    }

    /// <summary>The top of the resting tile's face, which shades down to <see cref="FaceBottom"/>.</summary>
    public static readonly Color FaceTop = Color.FromArgb(0xF2, 0xFA, 0xFB, 0xFC);

    /// <summary>The bottom of the resting tile's face.</summary>
    public static readonly Color FaceBottom = Color.FromArgb(0xF2, 0xE4, 0xE7, 0xEC);

    private const int Kept = 16;

    private static BitmapSource? _empty;

    private static BitmapSource? _emptyOpen;

    private static BitmapSource? _emptyIcons;

    /// <summary>An empty group's picture.</summary>
    public static BitmapSource Empty => _empty ??= Draw([], Look.Closed);

    private static BitmapSource? _add;

    /// <summary>
    /// The picture for what makes a new group, in the Add menus: the tile with four squares of
    /// colour in its places, a group with something in it, where the empty tile alone was a
    /// blank square at a menu's size.
    /// </summary>
    public static BitmapSource AddPicture => _add ??= Draw([.. AddColours.Select(Square)], Look.Closed);

    /// <summary>The four squares' colours, in the picture's order: blue, green, amber, purple.</summary>
    public static IReadOnlyList<Color> AddColours { get; } =
    [
        Color.FromRgb(0x3B, 0x82, 0xF6),
        Color.FromRgb(0x22, 0xC5, 0x5E),
        Color.FromRgb(0xF5, 0x9E, 0x0B),
        Color.FromRgb(0xA8, 0x55, 0xF7),
    ];

    /// <summary>A rounded square of one colour, as an icon is drawn in one of the tile's places.</summary>
    private static ImageSource Square(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        var image = new DrawingImage(new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(0, 0, 66, 66), 16, 16)));
        image.Freeze();
        return image;
    }

    /// <summary>The picture for a group whose first items have these icons, in order; at most <see cref="Shown"/> are used.</summary>
    public static BitmapSource For(IReadOnlyList<ImageSource?> icons) => Picture(icons, Look.Closed);

    /// <summary>
    /// The same group open, for while an icon dragged onto it would go in: a dark tile with a
    /// light rim, wider than the resting one, round the same four.
    /// </summary>
    public static BitmapSource Open(IReadOnlyList<ImageSource?> icons) => Picture(icons, Look.Open);

    /// <summary>
    /// The four icons alone, where the picture has them, on nothing: for the open group's card,
    /// which is the tile itself while it is the icon's size, and draws the tile's face as its own.
    /// </summary>
    public static BitmapSource IconsOnly(IReadOnlyList<ImageSource?> icons) => Picture(icons, Look.IconsOnly);

    private static BitmapSource Picture(IReadOnlyList<ImageSource?> icons, Look look)
    {
        var shown = icons.Take(Shown).ToArray();
        if (shown.Length == 0)
        {
            return look switch
            {
                Look.Open => _emptyOpen ??= Draw([], Look.Open),
                Look.IconsOnly => _emptyIcons ??= Draw([], Look.IconsOnly),
                _ => Empty,
            };
        }

        var cache = look switch
        {
            Look.Open => DrawnOpen,
            Look.IconsOnly => DrawnIcons,
            _ => Drawn,
        };
        lock (cache)
        {
            foreach (var (drawnIcons, picture) in cache)
            {
                if (drawnIcons.Length == shown.Length
                    && drawnIcons.Zip(shown).All(pair => ReferenceEquals(pair.First, pair.Second)))
                {
                    return picture;
                }
            }
        }

        var drawn = Draw(shown, look);
        lock (cache)
        {
            cache.Insert(0, (shown, drawn));
            if (cache.Count > Kept)
            {
                cache.RemoveAt(cache.Count - 1);
            }
        }

        return drawn;
    }

    /// <summary>
    /// The picture's own room round its tile, as a share of the tile's side: what reaches past a
    /// card the picture is drawn on so its tile is the card.
    /// </summary>
    public const double ArtRoom = 24.0 / 208.0;

    /// <summary>
    /// Where the tile is in an icon of a group drawn by the dock in <paramref name="icon"/>: the
    /// dock's 8% inset of a picture in its box, then the picture's own 24 of 256.
    /// </summary>
    public static Rect TileIn(Rect icon)
    {
        var inset = icon.Width * (0.08 + (0.84 * 24 / 256));
        var tile = icon;
        tile.Inflate(-inset, -inset);
        return tile;
    }

    /// <summary>The tile's corners in an icon drawn <paramref name="icon"/> big.</summary>
    public static double TileRadiusIn(Rect icon) => icon.Width * 0.84 * TileRadius / Box;

    /// <summary>Where the icon in each of the four places goes, in the design box: left to right, then down.</summary>
    public static Rect Place(int index)
    {
        var side = (Tile.Width - (2 * Inset) - Gutter) / 2;
        var column = index % 2;
        var row = index / 2;
        return new Rect(
            Tile.Left + Inset + (column * (side + Gutter)),
            Tile.Top + Inset + (row * (side + Gutter)),
            side,
            side);
    }

    private static BitmapSource Draw(IReadOnlyList<ImageSource?> icons, Look look)
    {
        var large = Pixels * Supersample;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(large / Box, large / Box));

            if (look == Look.Open)
            {
                // Dark and lit at the edge: a well opened for the icon to drop into.
                var face = new LinearGradientBrush(
                    Color.FromArgb(0xE6, 0x2C, 0x2E, 0x33), Color.FromArgb(0xE6, 0x16, 0x17, 0x1A), 90);
                face.Freeze();
                var rim = new Pen(new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)), 7);
                rim.Freeze();
                context.DrawRoundedRectangle(face, rim, OpenTile, OpenTileRadius, OpenTileRadius);
            }
            else if (look == Look.Closed)
            {
                // Pale and a little open, like Start's folders: a tray the icons sit in rather
                // than an icon of its own.
                var face = new LinearGradientBrush(FaceTop, FaceBottom, 90);
                face.Freeze();
                var rim = new Pen(new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0)), 4);
                rim.Freeze();
                context.DrawRoundedRectangle(face, rim, Tile, TileRadius, TileRadius);
            }

            for (var i = 0; i < Shown && i < icons.Count; i++)
            {
                if (icons[i] is { } icon)
                {
                    context.DrawImage(icon, Fit(icon, Place(i)));
                }
            }

            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(large, large, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[large * large * 4];
        bitmap.CopyPixels(pixels, large * 4, 0);
        return FolderArt.Shrink(pixels, large, Supersample);
    }

    /// <summary>An icon's rectangle in its place, its shape kept: a picture that is not square is centred.</summary>
    private static Rect Fit(ImageSource icon, Rect place)
    {
        var width = icon.Width;
        var height = icon.Height;
        if (!(width > 0) || !(height > 0) || Math.Abs(width - height) < 0.5)
        {
            return place;
        }

        var scale = Math.Min(place.Width / width, place.Height / height);
        var fitted = new Size(width * scale, height * scale);
        return new Rect(
            place.Left + ((place.Width - fitted.Width) / 2),
            place.Top + ((place.Height - fitted.Height) / 2),
            fitted.Width,
            fitted.Height);
    }
}
