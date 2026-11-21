using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.Localization;

namespace ArtDock.Services;

/// <summary>
/// The picture on a group of the dock's own (<see cref="PinnedAppSetting.Items"/>): the first
/// four things it holds, two by two, in a well pressed into the bar — a wash of the bar's own
/// colour, shaded along its top and lit along its bottom. An empty group is the well alone. A
/// group an icon is being dragged into has a look of its own (<see cref="Open"/>): dark, with a
/// light rim, around the same four.
/// </summary>
/// <remarks>
/// <para>
/// The first four in the group's order and no others — no choosing by use or by name — so the
/// picture changes only when the group's first four do. With
/// <see cref="DockSettings.GroupFitsContents"/> they are placed by how many there are instead
/// (<see cref="Configure"/>, <see cref="PlaceOf"/>).
/// </para>
/// <para>
/// The well, chosen on 2026-11-21 out of four looks tried on the dock: a pale tile with a dark
/// rim (Start's folders, the look before it), the well, an opaque raised tile, and a tile in the
/// accent colour. The pale tile stood on the bar as a sticker; the well reads as part of it, and
/// follows the bar's colour. It is larger than the pale tile was, which looked smaller than the
/// icons beside it — about as large as Windows' own icons are in their box, and the dock leaves
/// it less margin than an icon (<see cref="DockInset"/>). The icons in it were tried larger, and
/// fitted by what they draw rather than by their clear margins, and were put back as they were.
/// </para>
/// <para>
/// The open group's card grows out of the tile's rectangle and corners (<see cref="TileIn"/>,
/// <see cref="TileRadiusIn"/>, <see cref="ArtRoom"/>), so those are all made from
/// <see cref="Tile"/> and <see cref="TileRadius"/> and must stay so.
/// </para>
/// <para>
/// A bitmap, as every icon the dock draws is, in the 256-unit box Windows' icons are designed in.
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

    /// <summary>How far the tile stands in from the box's edges.</summary>
    private const double TileMargin = 6;

    /// <summary>The well, in the design box.</summary>
    private static readonly Rect Tile = new(TileMargin, TileMargin, Box - (2 * TileMargin), Box - (2 * TileMargin));

    private const double TileRadius = 56;

    /// <summary>The open look's tile: wider than the resting one, as if it had opened out round the icons.</summary>
    private static readonly Rect OpenTile = new(3.5, 3.5, 249, 249);

    private const double OpenTileRadius = 60;

    /// <summary>Between the tile's edge and the icons, and between the icons.</summary>
    private const double Inset = 32;

    private const double Gutter = 16;

    /// <summary>The well's rim, inside its edge: shaded at the top and lit at the bottom.</summary>
    private const double RimWidth = 4;

    /// <summary>One item alone, with <see cref="FitsContents"/>: this share of the room inside the tile's inset.</summary>
    private const double AloneShare = 0.7;

    /// <summary>
    /// The pictures drawn lately, by the icons they show and how many the group holds — compared
    /// by reference, since the icons are the shell's cached, frozen ones and come back the same
    /// objects while they are the same. Kept short: there are only so many groups on a dock.
    /// </summary>
    private static readonly List<(ImageSource?[] Icons, int Total, BitmapSource Picture)> Drawn = [];

    /// <summary>The same, of the open look.</summary>
    private static readonly List<(ImageSource?[] Icons, int Total, BitmapSource Picture)> DrawnOpen = [];

    /// <summary>The same, of the icons alone.</summary>
    private static readonly List<(ImageSource?[] Icons, int Total, BitmapSource Picture)> DrawnIcons = [];

    /// <summary>The picture's poses: on the dock, open for an icon dragged onto it, and the icons alone.</summary>
    private enum Pose
    {
        Closed,
        Open,
        IconsOnly,
    }

    /// <summary>Whether the icons are placed by how many the group holds, rather than always two by two.</summary>
    public static bool FitsContents { get; private set; }

    /// <summary>
    /// Sets how groups are arranged from here on, forgetting every picture arranged otherwise.
    /// Returns whether anything changed.
    /// </summary>
    public static bool Configure(bool fitsContents)
    {
        if (fitsContents == FitsContents)
        {
            return false;
        }

        FitsContents = fitsContents;
        foreach (var cache in new[] { Drawn, DrawnOpen, DrawnIcons })
        {
            lock (cache)
            {
                cache.Clear();
            }
        }

        _empty = _emptyOpen = _emptyIcons = null;
        return true;
    }

    /// <summary>
    /// The top of the well's face, which shades down to <see cref="FaceBottom"/>: a wash of the
    /// bar's own colour, darker, with what is under it showing through.
    /// </summary>
    public static readonly Color FaceTop = Color.FromArgb(0x24, 0x10, 0x18, 0x38);

    /// <summary>The bottom of the well's face.</summary>
    public static readonly Color FaceBottom = Color.FromArgb(0x14, 0x10, 0x18, 0x38);

    private const int Kept = 16;

    private static BitmapSource? _empty;

    private static BitmapSource? _emptyOpen;

    private static BitmapSource? _emptyIcons;

    /// <summary>An empty group's picture.</summary>
    public static BitmapSource Empty => _empty ??= Draw([], 0, Pose.Closed, FitsContents);

    private static BitmapSource? _add;

    /// <summary>
    /// The picture for what makes a new group, in the Add menus: the well with four squares of
    /// colour in its places, a group with something in it, where the empty well alone was a
    /// blank square at a menu's size.
    /// </summary>
    public static BitmapSource AddPicture => _add ??= Draw([.. AddColours.Select(Square)], Shown, Pose.Closed, fits: false);

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

    /// <summary>
    /// The picture for a group whose items have these icons, in order: at most <see cref="Shown"/>
    /// are drawn, and how many there are in all says how they are placed when the picture
    /// <see cref="FitsContents"/>.
    /// </summary>
    public static BitmapSource For(IReadOnlyList<ImageSource?> icons) => Picture(icons, Pose.Closed);

    /// <summary>
    /// The same group open, for while an icon dragged onto it would go in: a dark tile with a
    /// light rim, wider than the resting one, round the same four.
    /// </summary>
    public static BitmapSource Open(IReadOnlyList<ImageSource?> icons) => Picture(icons, Pose.Open);

    /// <summary>
    /// The four icons alone, where the picture has them, on nothing: for the open group's card,
    /// which is the tile itself while it is the icon's size, and draws the tile's face as its own.
    /// </summary>
    public static BitmapSource IconsOnly(IReadOnlyList<ImageSource?> icons) => Picture(icons, Pose.IconsOnly);

    private static BitmapSource Picture(IReadOnlyList<ImageSource?> icons, Pose pose)
    {
        var shown = icons.Take(Shown).ToArray();
        var total = icons.Count;
        if (shown.Length == 0)
        {
            return pose switch
            {
                Pose.Open => _emptyOpen ??= Draw([], 0, Pose.Open, FitsContents),
                Pose.IconsOnly => _emptyIcons ??= Draw([], 0, Pose.IconsOnly, FitsContents),
                _ => Empty,
            };
        }

        // How many there are in all matters only where it changes the picture: placed by the
        // count, and past the four places, where the last one says how many more.
        if (!FitsContents)
        {
            total = shown.Length;
        }

        var cache = pose switch
        {
            Pose.Open => DrawnOpen,
            Pose.IconsOnly => DrawnIcons,
            _ => Drawn,
        };
        lock (cache)
        {
            foreach (var (drawnIcons, drawnTotal, picture) in cache)
            {
                if (drawnTotal == total
                    && drawnIcons.Length == shown.Length
                    && drawnIcons.Zip(shown).All(pair => ReferenceEquals(pair.First, pair.Second)))
                {
                    return picture;
                }
            }
        }

        var drawn = Draw(shown, total, pose, FitsContents);
        lock (cache)
        {
            cache.Insert(0, (shown, total, drawn));
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
    public const double ArtRoom = TileMargin / (Box - (2 * TileMargin));

    /// <summary>
    /// The share of the item's box the dock leaves clear round a group's picture, at each side:
    /// less than round an icon (<see cref="Controls.DockItemVisual.IconInset"/>), so the group
    /// stands as large as the icons beside it. The well at the icons' margin read as smaller than
    /// they did, however large it was drawn in its picture (2026-11-21).
    /// </summary>
    public const double DockInset = 0.02;

    /// <summary>The share of the item's box a group's picture is drawn across.</summary>
    private const double DockShare = 1 - (2 * DockInset);

    /// <summary>
    /// Where the tile is in an icon of a group drawn by the dock in <paramref name="icon"/>: the
    /// dock's inset of a group's picture in its box (<see cref="DockInset"/>), then the picture's
    /// own margin round its tile.
    /// </summary>
    public static Rect TileIn(Rect icon)
    {
        var inset = icon.Width * (DockInset + (DockShare * TileMargin / Box));
        var tile = icon;
        tile.Inflate(-inset, -inset);
        return tile;
    }

    /// <summary>The tile's corners in an icon drawn <paramref name="icon"/> big.</summary>
    public static double TileRadiusIn(Rect icon) => icon.Width * DockShare * TileRadius / Box;

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

    /// <summary>
    /// Where the icon in place <paramref name="index"/> goes, of a group holding
    /// <paramref name="total"/>: two by two, or — fitted — one large in the middle, two side by
    /// side, three as two over one, all centred.
    /// </summary>
    public static Rect PlaceOf(int index, int total, bool fits)
    {
        var cell = Place(0);
        var middle = Tile.Left + (Tile.Width / 2);
        if (!fits || total >= Shown)
        {
            return Place(index);
        }

        switch (total)
        {
            case 1:
                var side = (Tile.Width - (2 * Inset)) * AloneShare;
                return new Rect(middle - (side / 2), middle - (side / 2), side, side);
            case 2:
                return new Rect(Place(index).Left, middle - (cell.Height / 2), cell.Width, cell.Height);
            default:
                return index < 2
                    ? Place(index)
                    : new Rect(middle - (cell.Width / 2), Place(2).Top, cell.Width, cell.Height);
        }
    }

    private static BitmapSource Draw(IReadOnlyList<ImageSource?> icons, int total, Pose pose, bool fits)
    {
        var large = Pixels * Supersample;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(large / Box, large / Box));

            if (pose == Pose.Open)
            {
                // Dark and lit at the edge: a well opened for the icon to drop into.
                var face = new LinearGradientBrush(
                    Color.FromArgb(0xE6, 0x2C, 0x2E, 0x33), Color.FromArgb(0xE6, 0x16, 0x17, 0x1A), 90);
                face.Freeze();
                var rim = new Pen(new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)), 7);
                rim.Freeze();
                context.DrawRoundedRectangle(face, rim, OpenTile, OpenTileRadius, OpenTileRadius);
            }
            else if (pose == Pose.Closed)
            {
                DrawWell(context);
            }

            // Past the four places, fitted, the last says how many more there are instead.
            var badge = fits && total > Shown;
            var iconPlaces = badge ? Shown - 1 : Shown;
            var placed = Math.Min(total, Shown);
            for (var i = 0; i < iconPlaces && i < icons.Count; i++)
            {
                if (icons[i] is { } icon)
                {
                    context.DrawImage(icon, Fit(icon, PlaceOf(i, placed, fits)));
                }
            }

            if (badge)
            {
                DrawMore(context, PlaceOf(Shown - 1, placed, fits), total - (Shown - 1), light: pose == Pose.Open);
            }

            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(large, large, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[large * large * 4];
        bitmap.CopyPixels(pixels, large * 4, 0);
        return FolderArt.Shrink(pixels, large, Supersample);
    }

    /// <summary>The resting well: its wash, and its rim inside its edge, shaded at the top and lit at the bottom.</summary>
    private static void DrawWell(DrawingContext context)
    {
        var face = new LinearGradientBrush(FaceTop, FaceBottom, 90);
        face.Freeze();
        var edge = new LinearGradientBrush(
            Color.FromArgb(0x55, 0x10, 0x18, 0x38), Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF), 90);
        edge.Freeze();
        var rim = new Pen(edge, RimWidth);
        rim.Freeze();

        var shape = Tile;
        shape.Inflate(-RimWidth / 2, -RimWidth / 2);
        var radius = TileRadius - (RimWidth / 2);
        context.DrawRoundedRectangle(face, rim, shape, radius, radius);
    }

    /// <summary>The last place of a group holding more than four: how many more, on a rounded square.</summary>
    private static void DrawMore(DrawingContext context, Rect place, int more, bool light)
    {
        var ground = new SolidColorBrush(light ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0x10, 0x18, 0x38));
        ground.Freeze();
        var corner = place.Width * 0.24;
        context.DrawRoundedRectangle(ground, null, place, corner, corner);

        var ink = new SolidColorBrush(light ? Colors.White : Color.FromRgb(0x33, 0x3A, 0x4A));
        ink.Freeze();
        var text = new FormattedText(
            Localizer.Format("Group.More", more.ToString(CultureInfo.CurrentCulture)),
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            place.Height * (more > 99 ? 0.34 : 0.44),
            ink,
            1.0);
        context.DrawText(text, new Point(
            place.Left + ((place.Width - text.Width) / 2),
            place.Top + ((place.Height - text.Height) / 2)));
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
