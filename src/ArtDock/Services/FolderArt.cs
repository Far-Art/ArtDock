using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using ArtDock.Dock;

namespace ArtDock.Services;

/// <summary>A symbol the item editor offers for a folder: its character in the symbol font, and the key of its name.</summary>
public sealed record FolderSymbol(string Glyph, string NameKey)
{
    /// <summary>What the settings file stores for it.</summary>
    public string Code => FolderArt.FormatSymbol(Glyph);
}

/// <summary>What a folder's symbol, or its text, is painted in.</summary>
public enum FolderSymbolTone
{
    /// <summary>A deeper shade of the folder's own colour, as Windows' own folders carry theirs.</summary>
    Toned,

    White,

    Black
}

/// <summary>
/// What a drawn folder looks like: everything its pin stores about it, read. Text, when there
/// is any, takes the place of the glyph.
/// </summary>
public readonly record struct FolderLook(Color Color, string? Glyph, string? Text, FolderSymbolTone Tone);

/// <summary>
/// The folder the dock draws for a folder pin given a colour of its own: the shapes in
/// <c>Assets/folder.svg</c> painted in that colour, with a symbol from the system's symbol font,
/// or a few letters, on its front.
/// </summary>
/// <remarks>
/// <para>
/// Drawn once into a bitmap and kept, never shown as vectors. The wave rescales an icon on
/// every frame, and WPF tessellates a vector afresh whenever its scale changes: measured over
/// a row of twelve, a folder this simple drawn live held the render thread at about 1% of a
/// core, and a detailed icon at 36%, where a bitmap cost nothing that could be measured.
/// </para>
/// <para>
/// Nothing is written to disk. A folder is drawn from what its pin stores — a colour, a symbol
/// or some text, and the symbol's tone — whenever the dock reads its pins, in a few
/// milliseconds a folder, so there is no picture to go missing when the dock is reinstalled,
/// or to be left behind when settings are imported on another machine: the settings are the
/// whole of it.
/// </para>
/// <para>
/// The design is read from the SVG rather than written out here, so that the file is the one
/// place the folder's look is decided. Only the part of SVG the file uses is understood, and
/// anything else is refused rather than drawn wrong — the file is built into the program, so a
/// refusal is met by the tests, never by a user.
/// </para>
/// </remarks>
public static class FolderArt
{
    /// <summary>The size a folder is drawn at: the size the shell's icons are asked for.</summary>
    public const int PixelSize = PinnedAppsService.IconPixelSize;

    /// <summary>The most characters a folder's text may have; the item editor holds its box to it.</summary>
    public const int MaxTextLength = 6;

    /// <summary>
    /// How many times larger a folder is drawn before it is shrunk to <see cref="PixelSize"/>:
    /// every pixel the dock keeps is the average of 16 drawn, which is what smooths its edges.
    /// </summary>
    private const int Supersample = 4;

    private const string ResourceName = "ArtDock.Assets.folder.svg";

    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    /// <summary>The design, read from the SVG the first time anything asks for it.</summary>
    private static readonly Lazy<Design> Shapes = new(LoadDesign);

    /// <summary>
    /// Folders already drawn, by how they look. Shared by every pin that looks the same, and only
    /// ever holding looks a pin has: the item editor's preview draws without keeping, or dragging
    /// its colour wheel would leave a folder here for every colour it passed over.
    /// </summary>
    private static readonly Dictionary<FolderLook, BitmapSource> Drawn = new();

    /// <summary>What the theme's <c>SymbolThemeFontFamily</c> is, for a thread with no theme to ask.</summary>
    private static readonly FontFamily FallbackSymbolFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>The colour a folder starts with: the one the design is drawn in.</summary>
    public static Color DefaultColor => Shapes.Value.DefaultColor;

    /// <summary>
    /// Ready-made colours for the item editor, the design's own first: Windows' folder yellow.
    /// Each is the colour of a folder's back; its front is the same colour lightened. No two are
    /// so close that they read as one, which a test holds them to.
    /// </summary>
    public static IReadOnlyList<string> Swatches { get; } =
    [
        "#FFBE16", "#3A8DF0", "#1FB0A3", "#3DAF4E", "#E5484D", "#800020",
        "#EC5FA6", "#9B6BF2", "#7D8796", "#E9ECF1", "#12131E"
    ];

    /// <summary>
    /// The symbols the item editor offers, in the order it lists them. Each is in both fonts the
    /// theme's symbol font names, which a test holds them to.
    /// </summary>
    public static IReadOnlyList<FolderSymbol> Symbols { get; } =
    [
        new("", "EditItem.Symbol.Downloads"),
        new("", "EditItem.Symbol.Documents"),
        new("", "EditItem.Symbol.Pictures"),
        new("", "EditItem.Symbol.Music"),
        new("", "EditItem.Symbol.Videos"),
        new("", "EditItem.Symbol.Code"),
        new("", "EditItem.Symbol.Games"),
        new("", "EditItem.Symbol.Favourites"),
        new("", "EditItem.Symbol.Loved"),
        new("", "EditItem.Symbol.Cloud"),
        new("", "EditItem.Symbol.Home"),
        new("", "EditItem.Symbol.Settings"),
        new("", "EditItem.Symbol.Work"),
        new("", "EditItem.Symbol.People"),
        new("", "EditItem.Symbol.Mail"),
        new("", "EditItem.Symbol.Private"),
        new("", "EditItem.Symbol.Web"),
        new("", "EditItem.Symbol.Calendar"),
        new("", "EditItem.Symbol.Shopping"),
        new("", "EditItem.Symbol.School"),
        new("", "EditItem.Symbol.Camera"),
        new("", "EditItem.Symbol.Travel"),
        new("", "EditItem.Symbol.Ideas"),
        new("", "EditItem.Symbol.Books"),
        new("", "EditItem.Symbol.Terminal"),
        new("", "EditItem.Symbol.Headphones"),
        new("", "EditItem.Symbol.Art"),
        new("", "EditItem.Symbol.Archive"),
        new("", "EditItem.Symbol.Keys"),
        new("", "EditItem.Symbol.Puzzle"),
        new("", "EditItem.Symbol.Flag"),
        new("", "EditItem.Symbol.Bugs")
    ];

    /// <summary>
    /// The glyph the item editor shows for no symbol: a circle struck through, F140, which is in
    /// both symbol fonts — E733, which looks the same, is missing from Segoe MDL2 Assets.
    /// </summary>
    public const string NoSymbolGlyph = "";

    /// <summary>
    /// The folder for what a pin stores, or null for a pin with no colour — which keeps the
    /// shell's folder icon.
    /// </summary>
    public static ImageSource? For(string? color, string? symbol, string? text = null, string? tone = null)
    {
        if (ParseColor(color) is not { } folder)
        {
            return null;
        }

        var look = Look(folder, ParseSymbol(symbol), ParseText(text), ParseTone(tone));
        lock (Drawn)
        {
            if (!Drawn.TryGetValue(look, out var image))
            {
                image = Draw(look);
                Drawn[look] = image;
            }

            return image;
        }
    }

    /// <summary>
    /// A look as it is drawn: text in place of a glyph, and no tone where there is nothing for it
    /// to paint — so that two pins that look the same share one picture.
    /// </summary>
    public static FolderLook Look(Color color, string? glyph, string? text, FolderSymbolTone tone)
    {
        var shown = text is not null ? null : glyph;
        return new FolderLook(color, shown, text, shown is null && text is null ? FolderSymbolTone.Toned : tone);
    }

    /// <summary>
    /// Reads a stored colour: null for none, and for one that is there but unreadable, the
    /// design's own — it was still a folder somebody chose to draw.
    /// </summary>
    public static Color? ParseColor(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        return TryParseColor(stored) ?? DefaultColor;
    }

    /// <summary>
    /// Reads a colour as the item editor's hex box is typed into: <c>#RRGGBB</c>, or anything
    /// else WPF can read, without its alpha — or null for what is not a colour yet.
    /// </summary>
    public static Color? TryParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // As written, and then — for hex typed without it — with the hash in front.
        var value = text.Trim();
        return Convert(value) ?? (value.StartsWith('#') ? null : Convert("#" + value));

        static Color? Convert(string value)
        {
            try
            {
                return ColorConverter.ConvertFromString(value) is Color color
                    ? Color.FromRgb(color.R, color.G, color.B)
                    : null;
            }
            catch (FormatException)
            {
                // Half-typed, which is normal while the user is still typing.
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
    }

    /// <summary>Formats a colour the way the settings file stores it, as the bar's is.</summary>
    public static string FormatColor(Color color) => BarPalette.ToHex(color);

    /// <summary>
    /// Reads a stored symbol — the glyph's code point in hex, <c>E896</c> — or null for none,
    /// or for anything outside the private-use area the symbol fonts draw their glyphs in.
    /// </summary>
    /// <remarks>
    /// Any glyph of the font is drawn, not only those <see cref="Symbols"/> offers, so one set by
    /// hand in the file is honoured, and an entry dropped from the list later does not take a
    /// folder's symbol with it.
    /// </remarks>
    public static string? ParseSymbol(string? stored) =>
        int.TryParse(stored?.Trim(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code)
        && code is >= 0xE000 and <= 0xF8FF
            ? char.ConvertFromUtf32(code)
            : null;

    /// <summary>Formats a glyph the way the settings file stores it.</summary>
    public static string FormatSymbol(string glyph) =>
        char.ConvertToUtf32(glyph, 0).ToString("X4", CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads a folder's text: trimmed, null for none, and cut to <see cref="MaxTextLength"/> —
    /// never through the middle of a character that takes two.
    /// </summary>
    public static string? ParseText(string? stored)
    {
        var text = stored?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > MaxTextLength)
        {
            text = text[..(char.IsHighSurrogate(text[MaxTextLength - 1]) ? MaxTextLength - 1 : MaxTextLength)];
        }

        return text;
    }

    /// <summary>Reads a stored tone: <c>White</c> or <c>Black</c>, and anything else toned.</summary>
    public static FolderSymbolTone ParseTone(string? stored) =>
        Enum.TryParse<FolderSymbolTone>(stored?.Trim(), ignoreCase: true, out var tone)
        && Enum.IsDefined(tone)
            ? tone
            : FolderSymbolTone.Toned;

    /// <summary>Formats a tone the way the settings file stores it: null for the default.</summary>
    public static string? FormatTone(FolderSymbolTone tone) =>
        tone == FolderSymbolTone.Toned ? null : tone.ToString();

    /// <summary>
    /// Draws a folder without keeping it: what the item editor shows while a colour is being
    /// chosen. The dock asks through <see cref="For"/>.
    /// </summary>
    public static BitmapSource Draw(FolderLook look)
    {
        var design = Shapes.Value;
        var large = PixelSize * Supersample;
        var scale = new ScaleTransform(large / design.Size.Width, large / design.Size.Height);
        scale.Freeze();

        var pixels = Render(large, scale, dc =>
        {
            foreach (var layer in design.Layers)
            {
                dc.DrawGeometry(layer.Paint.BrushFor(look.Color), null, layer.Shape);
            }
        });

        // The symbol, or the text in its place, is drawn apart and laid on after, so that the
        // shadow its paint may cast can go between it and the folder.
        if (Mark(design, look) is { } mark)
        {
            var marked = Render(large, scale, mark.Draw);
            if (mark.Paint.Shadow is { } shadow)
            {
                Cast(pixels, marked, large, shadow, scale.ScaleX, scale.ScaleY);
            }

            Over(pixels, marked);
        }

        return Shrink(pixels, large, Supersample);
    }

    /// <summary>A folder's symbol, or its text: what it is painted in, and how it is drawn — or null for neither.</summary>
    private static (Paint Paint, Action<DrawingContext> Draw)? Mark(Design design, FolderLook look)
    {
        if (look.Text is { Length: > 0 } text)
        {
            var paint = design.Paint(look.Tone, design.Text.Fill);
            return (paint, dc => DrawText(dc, design.Text, paint, look.Color, text));
        }

        if (look.Glyph is { Length: > 0 } glyph)
        {
            var paint = design.Paint(look.Tone, design.Symbol.Fill);
            var font = SymbolFont();
            return (paint, dc => DrawSymbol(dc, design.Symbol, paint, look.Color, glyph, font));
        }

        return null;
    }

    /// <summary>Draws at the size the folder is drawn at, and reads the pixels back, premultiplied.</summary>
    private static byte[] Render(int size, Transform scale, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(scale);
            draw(dc);
            dc.Pop();
        }

        var drawn = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        drawn.Render(visual);
        var pixels = new byte[size * size * 4];
        drawn.CopyPixels(pixels, size * 4, 0);
        return pixels;
    }

    /// <summary>Lays one premultiplied picture over another, as drawing it there would have.</summary>
    private static void Over(byte[] under, byte[] over)
    {
        for (var at = 0; at < under.Length; at += 4)
        {
            var keep = 255 - over[at + 3];
            if (keep == 255)
            {
                continue;
            }

            for (var channel = at; channel < at + 4; channel++)
            {
                under[channel] = (byte)Math.Min(255, over[channel] + (((under[channel] * keep) + 127) / 255));
            }
        }
    }

    /// <summary>
    /// Lays the shadow a symbol casts on the folder under it, as SVG's <c>feDropShadow</c> casts
    /// one: the symbol's opacity, moved by the shadow's offset, blurred, and laid on in the
    /// shadow's colour at the shadow's own opacity.
    /// </summary>
    /// <remarks>
    /// Worked out only over the box the symbol inks, moved by the offset and grown by as far as
    /// the blur can carry it — the shadow falls nowhere else. The offset moves by whole pixels
    /// of the drawing, a quarter of one of the folder's: finer than anything the shrink keeps.
    /// </remarks>
    private static void Cast(byte[] under, byte[] mark, int size, Shadow shadow, double scaleX, double scaleY)
    {
        if (Inked(mark, size) is not { } inked)
        {
            return;
        }

        var dx = (int)Math.Round(shadow.Offset.X * scaleX);
        var dy = (int)Math.Round(shadow.Offset.Y * scaleY);

        // Three boxes of d carry a value at most 1.5 d, and d is at most 1.88 s + 0.5.
        var reachX = (int)Math.Ceiling(3 * shadow.Blur * scaleX) + 2;
        var reachY = (int)Math.Ceiling(3 * shadow.Blur * scaleY) + 2;
        var left = Math.Max(0, inked.Left + dx - reachX);
        var top = Math.Max(0, inked.Top + dy - reachY);
        var right = Math.Min(size - 1, inked.Right + dx + reachX);
        var bottom = Math.Min(size - 1, inked.Bottom + dy + reachY);
        var width = right - left + 1;
        var height = bottom - top + 1;

        var alpha = new float[width * height];
        for (var y = top; y <= bottom; y++)
        {
            var from = y - dy;
            for (var x = left; x <= right; x++)
            {
                var source = x - dx;
                if (from >= 0 && from < size && source >= 0 && source < size)
                {
                    alpha[((y - top) * width) + (x - left)] = mark[(((from * size) + source) * 4) + 3] / 255f;
                }
            }
        }

        Blur(alpha, width, height, shadow.Blur * scaleX, across: true);
        Blur(alpha, width, height, shadow.Blur * scaleY, across: false);

        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var cast = alpha[((y - top) * width) + (x - left)] * shadow.Opacity;
                if (cast <= 0)
                {
                    continue;
                }

                var at = ((y * size) + x) * 4;
                var keep = 1 - cast;
                under[at] = (byte)Math.Round((shadow.Color.B * cast) + (under[at] * keep));
                under[at + 1] = (byte)Math.Round((shadow.Color.G * cast) + (under[at + 1] * keep));
                under[at + 2] = (byte)Math.Round((shadow.Color.R * cast) + (under[at + 2] * keep));
                under[at + 3] = (byte)Math.Round((255 * cast) + (under[at + 3] * keep));
            }
        }
    }

    /// <summary>The smallest box holding every pixel of a drawing that is not clear, or null if none is.</summary>
    private static (int Left, int Top, int Right, int Bottom)? Inked(byte[] pixels, int size)
    {
        int left = size, top = size, right = -1, bottom = -1;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (pixels[(((y * size) + x) * 4) + 3] != 0)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return right < 0 ? null : (left, top, right, bottom);
    }

    /// <summary>
    /// Blurs one way, as SVG defines its Gaussian blur for a deviation of two pixels or more:
    /// three box blurs of d = ⌊s × 3√(2π) / 4 + ½⌋ pixels — or, for an even d, two of d centred
    /// half a pixel either side and one of d + 1 — which a browser draws the same.
    /// </summary>
    private static void Blur(float[] values, int width, int height, double deviation, bool across)
    {
        var d = (int)Math.Floor((deviation * 3 * Math.Sqrt(2 * Math.PI) / 4) + 0.5);
        if (d < 2)
        {
            return;
        }

        var half = d / 2;
        if (d % 2 == 1)
        {
            BoxBlur(values, width, height, half, half, across);
            BoxBlur(values, width, height, half, half, across);
            BoxBlur(values, width, height, half, half, across);
        }
        else
        {
            BoxBlur(values, width, height, half, half - 1, across);
            BoxBlur(values, width, height, half - 1, half, across);
            BoxBlur(values, width, height, half, half, across);
        }
    }

    /// <summary>
    /// A box blur one way: each value becomes the mean of those from <paramref name="before"/>
    /// before it to <paramref name="after"/> after, with nothing beyond the edge.
    /// </summary>
    private static void BoxBlur(float[] values, int width, int height, int before, int after, bool across)
    {
        var (lines, length, next, step) = across ? (height, width, width, 1) : (width, height, 1, width);
        var line = new float[length];
        var box = (double)(before + after + 1);
        for (var l = 0; l < lines; l++)
        {
            var start = l * next;
            for (var i = 0; i < length; i++)
            {
                line[i] = values[start + (i * step)];
            }

            var sum = 0.0;
            for (var i = 0; i <= after && i < length; i++)
            {
                sum += line[i];
            }

            for (var i = 0; i < length; i++)
            {
                values[start + (i * step)] = (float)(sum / box);
                if (i + after + 1 < length)
                {
                    sum += line[i + after + 1];
                }

                if (i - before >= 0)
                {
                    sum -= line[i - before];
                }
            }
        }
    }

    /// <summary>
    /// Shrinks a drawing made <paramref name="factor"/> times too large by averaging each block
    /// of its pixels — premultiplied, so a pixel half covered by an edge averages to half its
    /// colour and half its opacity, which is the antialiasing.
    /// </summary>
    /// <remarks>
    /// Not by drawing it again smaller. <see cref="RenderTargetBitmap"/> ignores the bitmap
    /// scaling mode and shrinks bilinearly whatever it is told — measured: nearest neighbour,
    /// linear, high quality and Fant came out pixel for pixel the same — which at 4:1 reads 4 of
    /// every 16 pixels. The supersampling was thrown away, and the symbols' edges came out in
    /// steps.
    /// </remarks>
    private static BitmapSource Shrink(byte[] source, int big, int factor)
    {
        var small = big / factor;
        var area = factor * factor;
        var target = new byte[small * small * 4];
        for (var y = 0; y < small; y++)
        {
            for (var x = 0; x < small; x++)
            {
                int b = 0, g = 0, r = 0, a = 0;
                for (var dy = 0; dy < factor; dy++)
                {
                    var at = ((((y * factor) + dy) * big) + (x * factor)) * 4;
                    for (var dx = 0; dx < factor; dx++, at += 4)
                    {
                        b += source[at];
                        g += source[at + 1];
                        r += source[at + 2];
                        a += source[at + 3];
                    }
                }

                var to = ((y * small) + x) * 4;
                target[to] = (byte)((b + (area / 2)) / area);
                target[to + 1] = (byte)((g + (area / 2)) / area);
                target[to + 2] = (byte)((r + (area / 2)) / area);
                target[to + 3] = (byte)((a + (area / 2)) / area);
            }
        }

        var bitmap = BitmapSource.Create(small, small, 96, 96, PixelFormats.Pbgra32, null, target, small * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// The theme's own symbol font, as the menus draw their glyphs in — asked of the application
    /// where there is one on this thread to ask, which a test host has not.
    /// </summary>
    private static FontFamily SymbolFont() =>
        Application.Current is { } app
        && app.Dispatcher.CheckAccess()
        && app.TryFindResource("SymbolThemeFontFamily") is FontFamily themed
            ? themed
            : FallbackSymbolFont;

    private static void DrawSymbol(DrawingContext dc, Slot slot, Paint paint, Color folder, string glyph, FontFamily font)
    {
        var text = new FormattedText(
            glyph,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            slot.Em,
            Brushes.Black,
            pixelsPerDip: 1);

        var shape = text.BuildGeometry(default);
        var ink = shape.Bounds;
        if (ink.IsEmpty)
        {
            return;
        }

        // Sized by the em rather than by the ink, so the font's own proportions between its
        // glyphs survive — a dot stays smaller than a box — and centred by the ink, since the
        // glyphs do not all sit in the middle of their em.
        shape.Transform = new TranslateTransform(
            slot.Centre.X - (ink.X + (ink.Width / 2)),
            slot.Centre.Y - (ink.Y + (ink.Height / 2)));

        PaintShape(dc, shape, paint, folder, slot.Outline);
    }

    /// <summary>
    /// A folder's text, made as large as its box allows: as tall as the box takes the font's
    /// capitals, or as wide as it takes the whole word, whichever is the less — so that one
    /// letter and six are set in the same hand, and a lower-case word is not blown up to the
    /// height of a capital.
    /// </summary>
    private static void DrawText(DrawingContext dc, TextSlot slot, Paint paint, Color folder, string text)
    {
        const double em = 100;
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            slot.Typeface,
            em,
            Brushes.Black,
            pixelsPerDip: 1);

        var shape = formatted.BuildGeometry(default);
        var ink = shape.Bounds;
        if (ink.IsEmpty)
        {
            return;
        }

        var capitals = slot.Typeface.TryGetGlyphTypeface(out var glyphs) ? glyphs.CapsHeight * em : ink.Height;
        var scale = Math.Min(slot.Box.Width / ink.Width, slot.Box.Height / Math.Max(capitals, 1));
        shape.Transform = new TransformGroup
        {
            Children =
            {
                new TranslateTransform(-(ink.X + (ink.Width / 2)), -(ink.Y + (ink.Height / 2))),
                new ScaleTransform(scale, scale),
                new TranslateTransform(slot.Box.X + (slot.Box.Width / 2), slot.Box.Y + (slot.Box.Height / 2))
            }
        };

        PaintShape(dc, shape, paint, folder, slot.Outline);
    }

    /// <summary>
    /// Paints a symbol or its text: grown <paramref name="outline"/> wider, half of it each side,
    /// and filled once.
    /// </summary>
    /// <remarks>
    /// Not filled and then stroked. The white and the black paint are not quite opaque, and a
    /// stroke laid over the fill doubled up where the two overlapped, drawing every line of a
    /// black symbol with a darker band down the inside of each edge — measured across a stem, 19
    /// at the edges and 34 between them. A layer at the paint's opacity did not prevent it, the
    /// paint's own colours being the part not quite opaque.
    /// </remarks>
    private static void PaintShape(DrawingContext dc, Geometry shape, Paint paint, Color folder, double outline)
    {
        var grown = outline > 0
            ? Geometry.Combine(
                shape,
                shape.GetWidenedPathGeometry(new Pen(Brushes.Black, outline) { LineJoin = PenLineJoin.Round }, 0.01, ToleranceType.Absolute),
                GeometryCombineMode.Union,
                null,
                0.01,
                ToleranceType.Absolute)
            : shape;

        dc.DrawGeometry(paint.BrushFor(folder), null, grown);
    }

    // ---- the design ------------------------------------------------------------------

    private static Design LoadDesign()
    {
        using var stream = typeof(FolderArt).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is not built into the program.");

        return ReadDesign(XDocument.Load(stream).Root!);
    }

    private static Design ReadDesign(XElement svg)
    {
        if (svg.Name != Svg + "svg")
        {
            throw Refused(svg, "is not an SVG document");
        }

        Allow(svg, "width", "height", "viewBox", "color");
        var box = Numbers(Required(svg, "viewBox"));
        if (box.Length != 4 || box[0] != 0 || box[1] != 0 || box[2] <= 0 || box[3] <= 0)
        {
            throw Refused(svg, "needs a viewBox from 0,0");
        }

        var defaultColor = Hex(Required(svg, "color")) ?? throw Refused(svg, "needs a color in hex");

        var shapes = new Dictionary<string, Geometry>(StringComparer.Ordinal);
        var shadows = new Dictionary<string, Shadow>(StringComparer.Ordinal);
        var gradientElements = new List<XElement>();
        var rects = new List<XElement>();
        foreach (var element in svg.Elements(Svg + "defs").Elements())
        {
            switch (Local(element))
            {
                case "path":
                    Allow(element, "id", "d");
                    shapes.Add(Required(element, "id"), Shape(element));
                    break;

                case "filter":
                    shadows.Add(Required(element, "id"), DropShadow(element));
                    break;

                case "linearGradient":
                    gradientElements.Add(element);
                    break;

                case "rect":
                    rects.Add(element);
                    break;

                default:
                    throw Refused(element);
            }
        }

        // Read once every filter is known, since a symbol's paint may name the shadow it casts.
        var gradients = new Dictionary<string, GradientPaint>(StringComparer.Ordinal);
        foreach (var element in gradientElements)
        {
            gradients.Add(Required(element, "id"), Gradient(element, shadows));
        }

        // Read once every gradient is known, since the places may be painted with one.
        Slot? symbol = null;
        TextSlot? text = null;
        foreach (var rect in rects)
        {
            switch (Required(rect, "id"))
            {
                case "symbol":
                    symbol = SymbolSlot(rect, gradients);
                    break;

                case "text":
                    text = TextPlace(rect, gradients);
                    break;

                default:
                    throw Refused(rect, "is neither the symbol's place nor the text's");
            }
        }

        var layers = new List<Layer>();
        foreach (var element in svg.Elements())
        {
            switch (Local(element))
            {
                case "defs":
                    Allow(element);
                    break;

                case "use":
                    Allow(element, "href", "fill", "fill-opacity");
                    var href = Required(element, "href");
                    if (!href.StartsWith('#') || !shapes.TryGetValue(href[1..], out var shape))
                    {
                        throw Refused(element, $"names no path in <defs>: {href}");
                    }

                    layers.Add(new Layer(shape, ShapeFill(element, gradients)));
                    break;

                case "path":
                    Allow(element, "d", "fill", "fill-opacity");
                    layers.Add(new Layer(Shape(element), ShapeFill(element, gradients)));
                    break;

                default:
                    throw Refused(element);
            }
        }

        return new Design(
            new Size(box[2], box[3]),
            defaultColor,
            layers,
            symbol ?? throw Refused(svg, "needs a rect with the id symbol, the symbol's place"),
            text ?? throw Refused(svg, "needs a rect with the id text, the text's place"),
            gradients.GetValueOrDefault("symbol-white") ?? throw Refused(svg, "needs a gradient symbol-white"),
            gradients.GetValueOrDefault("symbol-black") ?? throw Refused(svg, "needs a gradient symbol-black"));
    }

    /// <summary>A path's outline, filled by SVG's rule rather than WPF's default even-odd.</summary>
    private static Geometry Shape(XElement path)
    {
        var shape = Geometry.Parse("F1 " + Required(path, "d"));
        shape.Freeze();
        return shape;
    }

    /// <summary>
    /// A filter of one <c>feDropShadow</c>, the only filter the dock draws: the shadow cast by a
    /// symbol painted in a paint that names it. What it leaves out is SVG's default.
    /// </summary>
    private static Shadow DropShadow(XElement filter)
    {
        Allow(filter, "id");
        if (filter.Elements().ToList() is not [var shadow] || Local(shadow) != "feDropShadow")
        {
            throw Refused(filter, "is not one feDropShadow, the only filter the dock draws");
        }

        Allow(shadow, "dx", "dy", "stdDeviation", "flood-color", "flood-opacity");
        var color = shadow.Attribute("flood-color")?.Value ?? "#000";
        return new Shadow(
            new Vector(Optional(shadow, "dx", 2), Optional(shadow, "dy", 2)),
            Optional(shadow, "stdDeviation", 2),
            Hex(color) ?? throw Refused(shadow, $"has a colour the dock does not read: {color}"),
            Optional(shadow, "flood-opacity", 1));
    }

    private static GradientPaint Gradient(XElement gradient, Dictionary<string, Shadow> shadows)
    {
        Allow(gradient, "id", "x1", "y1", "x2", "y2", "data-shadow");
        var stops = new List<GradientStopSpec>();
        foreach (var stop in gradient.Elements())
        {
            if (Local(stop) != "stop")
            {
                throw Refused(stop);
            }

            Allow(stop, "offset", "stop-color", "stop-opacity", "data-shade");
            stops.Add(new GradientStopSpec(
                Offset(Required(stop, "offset")),
                ReadTone(stop, Required(stop, "stop-color")),
                Optional(stop, "stop-opacity", 1)));
        }

        Shadow? cast = null;
        if (gradient.Attribute("data-shadow")?.Value is { } named)
        {
            cast = Reference(named) is { } id && shadows.TryGetValue(id, out var shadow)
                ? shadow
                : throw Refused(gradient, $"names no filter: {named}");
        }

        return new GradientPaint(
            new Point(Number(Required(gradient, "x1")), Number(Required(gradient, "y1"))),
            new Point(Number(Required(gradient, "x2")), Number(Required(gradient, "y2"))),
            stops,
            1)
        {
            Shadow = cast
        };
    }

    private static Paint Fill(XElement element, Dictionary<string, GradientPaint> gradients)
    {
        var fill = Required(element, "fill");
        var opacity = Optional(element, "fill-opacity", 1);
        if (Reference(fill) is { } id)
        {
            return gradients.TryGetValue(id, out var gradient)
                ? gradient with { Opacity = opacity }
                : throw Refused(element, $"names no gradient: {fill}");
        }

        return new SolidPaint(ReadTone(element, fill), opacity);
    }

    /// <summary>A shape's paint, which casts no shadow: only a symbol's may.</summary>
    private static Paint ShapeFill(XElement element, Dictionary<string, GradientPaint> gradients) =>
        Fill(element, gradients) is { Shadow: null } paint
            ? paint
            : throw Refused(element, "is painted in a paint that casts a shadow, which only a symbol's may");

    /// <summary>The id in <c>url(#id)</c>, or null for anything else.</summary>
    private static string? Reference(string value) =>
        value.StartsWith("url(#", StringComparison.Ordinal) && value.EndsWith(')') ? value[5..^1] : null;

    private static Slot SymbolSlot(XElement rect, Dictionary<string, GradientPaint> gradients)
    {
        Allow(rect, "id", "x", "y", "width", "height", "fill", "fill-opacity", "stroke-width", "data-shade");
        var box = Box(rect);

        return new Slot(
            new Point(box.X + (box.Width / 2), box.Y + (box.Height / 2)),
            box.Height,
            Fill(rect, gradients),
            Optional(rect, "stroke-width", 0));
    }

    private static TextSlot TextPlace(XElement rect, Dictionary<string, GradientPaint> gradients)
    {
        Allow(rect, "id", "x", "y", "width", "height", "fill", "fill-opacity", "stroke-width", "data-shade", "font-family", "font-weight");
        var weight = Required(rect, "font-weight");
        var typeface = new Typeface(
            new FontFamily(Required(rect, "font-family")),
            FontStyles.Normal,
            int.TryParse(weight, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? FontWeight.FromOpenTypeWeight(number)
                : throw Refused(rect, $"has a font-weight the dock does not read: {weight}"),
            FontStretches.Normal);

        return new TextSlot(Box(rect), Fill(rect, gradients), Optional(rect, "stroke-width", 0), typeface);
    }

    private static Rect Box(XElement rect) => new(
        Number(Required(rect, "x")),
        Number(Required(rect, "y")),
        Number(Required(rect, "width")),
        Number(Required(rect, "height")));

    /// <summary>A colour, with the darkening an element's <c>data-shade</c> asks of it.</summary>
    private static Tone ReadTone(XElement element, string value) =>
        new(
            value == "currentColor"
                ? null
                : Hex(value) ?? throw Refused(element, $"has a colour the dock does not read: {value}"),
            Optional(element, "data-shade", 0));

    /// <summary><c>#rgb</c> or <c>#rrggbb</c>, and nothing else.</summary>
    private static Color? Hex(string value)
    {
        var digits = value.StartsWith('#') ? value[1..] : string.Empty;
        if (digits.Length == 3)
        {
            digits = string.Concat(digits.Select(digit => $"{digit}{digit}"));
        }

        return digits.Length == 6
            && uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb)
            ? Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : null;
    }

    private static double Offset(string value) =>
        value.EndsWith('%') ? Number(value[..^1]) / 100 : Number(value);

    private static double Number(string value) =>
        double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static double[] Numbers(string value) =>
        [.. value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(Number)];

    private static double Optional(XElement element, string name, double fallback) =>
        element.Attribute(name)?.Value is { } value ? Number(value) : fallback;

    private static string Required(XElement element, string name) =>
        element.Attribute(name)?.Value ?? throw Refused(element, $"needs {name}");

    private static string Local(XElement element) =>
        element.Name.Namespace == Svg ? element.Name.LocalName : throw Refused(element);

    /// <summary>Refuses an attribute the dock would not draw as a browser does.</summary>
    private static void Allow(XElement element, params string[] names)
    {
        foreach (var attribute in element.Attributes())
        {
            if (!attribute.IsNamespaceDeclaration
                && (attribute.Name.Namespace != XNamespace.None || !names.Contains(attribute.Name.LocalName)))
            {
                throw Refused(element, $"has {attribute.Name.LocalName}, which the dock does not draw");
            }
        }
    }

    private static InvalidDataException Refused(XElement element, string? why = null) =>
        new($"folder.svg: <{element.Name.LocalName}> {why ?? "is not something the dock draws"}.");

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    /// <summary>
    /// The design: its canvas, its starting colour, its shapes in order, where the symbol and the
    /// text go, and the paints for a white or a black symbol.
    /// </summary>
    private sealed record Design(
        Size Size,
        Color DefaultColor,
        IReadOnlyList<Layer> Layers,
        Slot Symbol,
        TextSlot Text,
        Paint White,
        Paint Black)
    {
        /// <summary>What a symbol or text is painted in for a tone: its place's own paint when toned.</summary>
        public Paint Paint(FolderSymbolTone tone, Paint toned) => tone switch
        {
            FolderSymbolTone.White => White,
            FolderSymbolTone.Black => Black,
            _ => toned
        };
    }

    private sealed record Layer(Geometry Shape, Paint Paint);

    /// <summary>Where a symbol is drawn, and how: see the comment at the top of the SVG.</summary>
    private sealed record Slot(Point Centre, double Em, Paint Fill, double Outline);

    /// <summary>Where a folder's text is drawn — fitted into the box — and how.</summary>
    private sealed record TextSlot(Rect Box, Paint Fill, double Outline, Typeface Typeface);

    /// <summary>
    /// A colour of the design: a fixed one, or the folder's own — <c>currentColor</c> — darkened
    /// by mixing in <see cref="Shade"/> of black.
    /// </summary>
    private readonly record struct Tone(Color? Fixed, double Shade)
    {
        public Color For(Color folder)
        {
            var color = Fixed ?? folder;
            var keep = 1 - Shade;
            return Color.FromRgb(
                (byte)Math.Round(color.R * keep),
                (byte)Math.Round(color.G * keep),
                (byte)Math.Round(color.B * keep));
        }
    }

    private abstract record Paint(double Opacity)
    {
        /// <summary>The shadow a symbol painted in this casts, if it casts one.</summary>
        public Shadow? Shadow { get; init; }

        public abstract Brush BrushFor(Color folder);
    }

    /// <summary>
    /// A shadow as SVG's <c>feDropShadow</c> casts one: the shape's opacity moved by
    /// <see cref="Offset"/>, blurred to a standard deviation of <see cref="Blur"/> — both in the
    /// design's units — and laid in <see cref="Color"/> at <see cref="Opacity"/>.
    /// </summary>
    private sealed record Shadow(Vector Offset, double Blur, Color Color, double Opacity);

    private sealed record SolidPaint(Tone Tone, double Opacity) : Paint(Opacity)
    {
        public override Brush BrushFor(Color folder) =>
            Frozen(new SolidColorBrush(Tone.For(folder)) { Opacity = Opacity });
    }

    private sealed record GradientStopSpec(double Offset, Tone Tone, double Opacity);

    /// <summary>A linear gradient across the shape it fills, as SVG's default objectBoundingBox has it.</summary>
    private sealed record GradientPaint(Point Start, Point End, IReadOnlyList<GradientStopSpec> Stops, double Opacity)
        : Paint(Opacity)
    {
        public override Brush BrushFor(Color folder)
        {
            var brush = new LinearGradientBrush { StartPoint = Start, EndPoint = End, Opacity = Opacity };
            foreach (var stop in Stops)
            {
                var color = stop.Tone.For(folder);
                brush.GradientStops.Add(new GradientStop(
                    Color.FromArgb((byte)Math.Round(stop.Opacity * 255), color.R, color.G, color.B),
                    stop.Offset));
            }

            return Frozen(brush);
        }
    }
}
