using System.Windows;

namespace ArtDock.Dock;

/// <summary>
/// The sizes the window previews are drawn at, in DIPs: one card per window, a row of title
/// above each picture, and the panel they sit in.
/// </summary>
/// <remarks>
/// Windows 11's own previews are the model, and these are their proportions as remembered rather
/// than measured: the pointer could not be put on the taskbar when the rest was measured
/// (TODO.md item 25, and *Resolved*). Everything here is a number <see cref="PreviewLayout"/>
/// takes, so reading the real ones off later changes nothing else.
/// </remarks>
public sealed record PreviewMetrics
{
    /// <summary>The widest a picture is drawn.</summary>
    public double PictureWidth { get; init; } = 240;

    /// <summary>The tallest a picture is drawn; every card's picture slot is this tall.</summary>
    public double PictureHeight { get; init; } = 140;

    /// <summary>
    /// The narrowest a picture may be shrunk to before a crowd of windows is shown as a list of
    /// titles instead.
    /// </summary>
    public double MinPictureWidth { get; init; } = 110;

    /// <summary>The narrowest a card's content is, so a tall, thin window still has room for a title.</summary>
    public double MinContentWidth { get; init; } = 120;

    /// <summary>The row above the picture: the app's icon, the title and the close button.</summary>
    public double TitleHeight { get; init; } = 32;

    /// <summary>Space inside a card, around its title row and picture.</summary>
    public double CardPadding { get; init; } = 8;

    /// <summary>Space between two cards.</summary>
    public double CardGap { get; init; } = 4;

    /// <summary>Space between the panel's edge and the cards.</summary>
    public double PanelPadding { get; init; } = 6;

    /// <summary>The close button's square, at the right of the title row.</summary>
    public double CloseSize { get; init; } = 24;

    /// <summary>One window's row when they are listed rather than pictured.</summary>
    public double RowHeight { get; init; } = 36;

    /// <summary>The list's width.</summary>
    public double ListWidth { get; init; } = 320;

    /// <summary>
    /// Space between the panel and the line it must stay above — and how far it rises into place
    /// as it opens, since it may not start below that line.
    /// </summary>
    public double Lift { get; init; } = 12;

    /// <summary>The closest the panel comes to the edges of the work area.</summary>
    public double ScreenMargin { get; init; } = 8;
}

/// <summary>One window's place in the panel, in physical pixels from the panel's top left.</summary>
/// <param name="Card">The whole card: what the pointer highlights and a click goes to.</param>
/// <param name="Title">Where the title is written: the icon's square at its start, then the text.</param>
/// <param name="Close">The close button's square.</param>
/// <param name="Picture">
/// Where the window's picture is drawn, in the window's own shape. Empty in a list, which has
/// no pictures.
/// </param>
public readonly record struct PreviewCard(Int32Rect Card, Int32Rect Title, Int32Rect Close, Int32Rect Picture);

/// <summary>
/// What <see cref="PreviewLayout.Arrange"/> decides: where the panel goes, and where each window
/// is drawn in it.
/// </summary>
/// <param name="Panel">The panel, in physical screen pixels.</param>
/// <param name="IsList">True when there were too many windows to picture, and they are listed.</param>
/// <param name="Cards">One per window, in the order they were given; fewer only when a list is too
/// tall for the screen, in which case the last ones are left out.</param>
public sealed record PreviewArrangement(Int32Rect Panel, bool IsList, IReadOnlyList<PreviewCard> Cards);

/// <summary>
/// Lays out the previews of an app's windows: a card for each, in a row, centred over the item
/// and kept on its display — shrunk alike when they do not fit, and listed when shrinking would
/// make them too small to read.
/// </summary>
/// <remarks>
/// <para>
/// Pure, and in physical pixels throughout: the panel is placed with <c>SetWindowPos</c> and the
/// pictures by DWM, both of which count pixels, and a layout in DIPs rounded at the end would put
/// a picture a pixel off its card at 150%. The metrics are DIPs, scaled once on the way in.
/// </para>
/// <para>
/// A picture keeps its window's shape, fitted inside the metrics' box. The cards are as tall as
/// one another and as wide as their pictures, as Windows' are, so a row of a wide window and a
/// narrow one lines up along its titles.
/// </para>
/// </remarks>
public static class PreviewLayout
{
    /// <summary>The shape taken for a window whose size could not be read.</summary>
    private const double FallbackAspect = 16.0 / 9.0;

    /// <summary>Lays the previews out.</summary>
    /// <param name="sources">Each window's size in pixels, as DWM reports it; zero where unknown.</param>
    /// <param name="metrics">The sizes to draw at, in DIPs.</param>
    /// <param name="scale">The scale of the display the panel goes on.</param>
    /// <param name="workArea">That display's work area, in physical pixels.</param>
    /// <param name="anchorX">The x the panel is centred on — the held icon's middle — in physical pixels.</param>
    /// <param name="floor">
    /// The line the panel must stay above, in physical pixels: the top of the dock's hover zone.
    /// A panel reaching below it would cover the dock's own space, and the dock counts its own
    /// windows as covering it.
    /// </param>
    public static PreviewArrangement Arrange(
        IReadOnlyList<Size> sources,
        PreviewMetrics metrics,
        double scale,
        Int32Rect workArea,
        int anchorX,
        int floor)
    {
        var px = (double dips) => (int)Math.Round(dips * scale);

        var margin = px(metrics.ScreenMargin);
        var available = Math.Max(0, workArea.Width - (2 * margin));
        var bottom = floor - px(metrics.Lift);
        var tallest = Math.Max(0, bottom - (workArea.Y + margin));

        if (sources.Count == 0)
        {
            return new PreviewArrangement(Int32Rect.Empty, IsList: false, []);
        }

        // The factor the pictures are drawn at: whole size if the row fits, otherwise what
        // makes it fit. The fixed parts — padding, gaps — do not shrink, so they come off first.
        var fixedWidth = (2 * px(metrics.PanelPadding))
            + (sources.Count * 2 * px(metrics.CardPadding))
            + ((sources.Count - 1) * px(metrics.CardGap));
        var fullContent = 0;
        foreach (var source in sources)
        {
            fullContent += ContentWidth(source, metrics, scale, factor: 1);
        }

        var factor = 1.0;
        if (fixedWidth + fullContent > available)
        {
            factor = fullContent <= 0 ? 0 : Math.Max(0, available - fixedWidth) / (double)fullContent;

            // Rounding each card can overshoot the width by a pixel a card; step down until it fits.
            while (factor > 0 && fixedWidth + TotalContent(sources, metrics, scale, factor) > available)
            {
                factor -= 0.005;
            }
        }

        if (metrics.PictureWidth * factor < metrics.MinPictureWidth)
        {
            return List(sources.Count, metrics, scale, workArea, margin, anchorX, bottom, tallest);
        }

        var padding = px(metrics.PanelPadding);
        var cardPadding = px(metrics.CardPadding);
        var titleHeight = px(metrics.TitleHeight);
        var slotHeight = (int)Math.Round(metrics.PictureHeight * factor * scale);
        var cardHeight = (2 * cardPadding) + titleHeight + slotHeight;
        var close = px(metrics.CloseSize);

        var cards = new List<PreviewCard>(sources.Count);
        var x = padding;
        foreach (var source in sources)
        {
            var content = ContentWidth(source, metrics, scale, factor);
            var cardWidth = content + (2 * cardPadding);
            var picture = Fit(source, metrics, scale, factor);

            var card = new Int32Rect(x, padding, cardWidth, cardHeight);
            var closeRect = new Int32Rect(
                x + cardWidth - cardPadding - close,
                padding + cardPadding + ((titleHeight - close) / 2),
                close,
                close);
            var title = new Int32Rect(
                x + cardPadding,
                padding + cardPadding,
                Math.Max(0, content - close),
                titleHeight);
            var pictureRect = new Int32Rect(
                x + cardPadding + ((content - picture.Width) / 2),
                padding + cardPadding + titleHeight + ((slotHeight - picture.Height) / 2),
                picture.Width,
                picture.Height);

            cards.Add(new PreviewCard(card, title, closeRect, pictureRect));
            x += cardWidth + px(metrics.CardGap);
        }

        var width = x - px(metrics.CardGap) + padding;
        var height = cardHeight + (2 * padding);
        return new PreviewArrangement(Place(width, height, workArea, margin, anchorX, bottom), IsList: false, cards);
    }

    private static PreviewArrangement List(
        int count, PreviewMetrics metrics, double scale, Int32Rect workArea, int margin, int anchorX, int bottom, int tallest)
    {
        var px = (double dips) => (int)Math.Round(dips * scale);

        var padding = px(metrics.PanelPadding);
        var row = px(metrics.RowHeight);
        var close = px(metrics.CloseSize);
        var width = Math.Min(px(metrics.ListWidth), Math.Max(0, workArea.Width - (2 * margin)));

        // As many rows as the screen has room for. A single app with more windows than that is
        // rare enough that leaving the rest out beats a scrolling list in a panel that never
        // has the keyboard.
        var fits = row <= 0 ? count : Math.Max(1, (tallest - (2 * padding)) / row);
        var shown = Math.Min(count, fits);

        var cards = new List<PreviewCard>(shown);
        for (var i = 0; i < shown; i++)
        {
            var y = padding + (i * row);
            var card = new Int32Rect(padding, y, width - (2 * padding), row);
            var closeRect = new Int32Rect(padding + card.Width - close - ((row - close) / 2), y + ((row - close) / 2), close, close);
            var title = new Int32Rect(padding + px(metrics.CardPadding), y, Math.Max(0, closeRect.X - padding - px(metrics.CardPadding)), row);
            cards.Add(new PreviewCard(card, title, closeRect, Int32Rect.Empty));
        }

        var height = (shown * row) + (2 * padding);
        return new PreviewArrangement(Place(width, height, workArea, margin, anchorX, bottom), IsList: true, cards);
    }

    /// <summary>Centres the panel on the anchor, inside the work area, with its bottom on the line.</summary>
    private static Int32Rect Place(int width, int height, Int32Rect workArea, int margin, int anchorX, int bottom)
    {
        var minLeft = workArea.X + margin;
        var maxLeft = workArea.X + workArea.Width - margin - width;
        var left = anchorX - (width / 2);
        left = maxLeft >= minLeft ? Math.Clamp(left, minLeft, maxLeft) : minLeft;

        // Above the line always; on a screen too short for the panel it is the top that gives.
        var top = Math.Max(workArea.Y + margin, bottom - height);
        height = Math.Max(0, Math.Min(height, bottom - top));
        return new Int32Rect(left, top, width, height);
    }

    private static int TotalContent(IReadOnlyList<Size> sources, PreviewMetrics metrics, double scale, double factor)
    {
        var total = 0;
        foreach (var source in sources)
        {
            total += ContentWidth(source, metrics, scale, factor);
        }

        return total;
    }

    /// <summary>A card's width inside its padding: its picture's, or the narrowest a title needs.</summary>
    private static int ContentWidth(Size source, PreviewMetrics metrics, double scale, double factor) =>
        Math.Max(Fit(source, metrics, scale, factor).Width, (int)Math.Round(metrics.MinContentWidth * factor * scale));

    /// <summary>The window's shape fitted inside the picture box, at the factor.</summary>
    private static (int Width, int Height) Fit(Size source, PreviewMetrics metrics, double scale, double factor)
    {
        var boxWidth = metrics.PictureWidth * factor * scale;
        var boxHeight = metrics.PictureHeight * factor * scale;
        var aspect = source.Width > 0 && source.Height > 0 ? source.Width / source.Height : FallbackAspect;

        var width = boxWidth;
        var height = width / aspect;
        if (height > boxHeight)
        {
            height = boxHeight;
            width = height * aspect;
        }

        return ((int)Math.Round(width), (int)Math.Round(height));
    }
}
