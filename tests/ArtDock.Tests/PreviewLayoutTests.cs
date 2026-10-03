using System.Windows;
using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers where the previews of an app's windows go and what size they are drawn at
/// (<see cref="PreviewLayout"/>): the window's shape kept, the row shrunk alike to fit its
/// display and then listed, and the panel kept on the display and above the dock.
/// </summary>
public class PreviewLayoutTests
{
    private static readonly PreviewMetrics Metrics = new();

    /// <summary>The main display here: 2560×1392 of work area at 100%.</summary>
    private static readonly Int32Rect Main = new(0, 0, 2560, 1392);

    /// <summary>The 4K display to its left, at 150%.</summary>
    private static readonly Int32Rect FourK = new(-3840, 8, 3840, 2088);

    private const int Floor = 1250;

    private static PreviewArrangement Arrange(int count, Size? size = null, double scale = 1, Int32Rect? work = null, int anchor = 1280, int floor = Floor) =>
        PreviewLayout.Arrange(
            [.. Enumerable.Repeat(size ?? new Size(1600, 900), count)],
            Metrics,
            scale,
            work ?? Main,
            anchor,
            floor);

    private static bool Inside(Int32Rect inner, Int32Rect outer) =>
        inner.X >= outer.X
        && inner.Y >= outer.Y
        && inner.X + inner.Width <= outer.X + outer.Width
        && inner.Y + inner.Height <= outer.Y + outer.Height;

    [Fact]
    public void NoWindows_NoPanel()
    {
        var arrangement = Arrange(0);
        Assert.Empty(arrangement.Cards);
        Assert.True(arrangement.Panel.IsEmpty);
    }

    [Fact]
    public void OneWideWindow_IsDrawnAsWideAsThePictureBox_InItsOwnShape()
    {
        var card = Assert.Single(Arrange(1).Cards);
        Assert.Equal(240, card.Picture.Width);
        Assert.Equal(135, card.Picture.Height);
    }

    [Fact]
    public void ATallWindow_IsFittedToTheBoxHeight_AndKeepsItsShape()
    {
        var card = Assert.Single(Arrange(1, new Size(600, 1200)).Cards);
        Assert.Equal(140, card.Picture.Height);
        Assert.Equal(70, card.Picture.Width);
    }

    [Fact]
    public void AWindowOfUnknownSize_IsDrawnSixteenByNine()
    {
        var card = Assert.Single(Arrange(1, new Size(0, 0)).Cards);
        Assert.Equal(240, card.Picture.Width);
        Assert.Equal(135, card.Picture.Height);
    }

    [Fact]
    public void ANarrowWindow_StillGetsRoomForItsTitle()
    {
        var card = Assert.Single(Arrange(1, new Size(100, 1000)).Cards);
        Assert.True(card.Card.Width >= Metrics.MinContentWidth + (2 * Metrics.CardPadding));
        Assert.True(Inside(card.Picture, card.Card));
    }

    [Fact]
    public void EveryPart_LiesInsideItsCard_AndEveryCardInsideThePanel()
    {
        var arrangement = Arrange(3);
        var panel = new Int32Rect(0, 0, arrangement.Panel.Width, arrangement.Panel.Height);
        foreach (var card in arrangement.Cards)
        {
            Assert.True(Inside(card.Card, panel));
            Assert.True(Inside(card.Title, card.Card));
            Assert.True(Inside(card.Close, card.Card));
            Assert.True(Inside(card.Picture, card.Card));
        }
    }

    [Fact]
    public void TheTitle_StopsShortOfTheCloseButton()
    {
        var card = Assert.Single(Arrange(1).Cards);
        Assert.True(card.Title.X + card.Title.Width <= card.Close.X);
    }

    [Fact]
    public void Cards_FollowOneAnother_InTheOrderGiven_WithoutOverlapping()
    {
        var cards = Arrange(4).Cards;
        for (var i = 1; i < cards.Count; i++)
        {
            Assert.True(cards[i].Card.X >= cards[i - 1].Card.X + cards[i - 1].Card.Width);
        }
    }

    [Fact]
    public void ThePanel_IsCentredOnTheAnchor()
    {
        var panel = Arrange(2).Panel;
        Assert.InRange(panel.X + (panel.Width / 2) - 1280, -1, 1);
    }

    [Fact]
    public void ThePanel_StaysAboveTheFloor_ByTheLift()
    {
        var panel = Arrange(2).Panel;
        Assert.Equal(Floor - (int)Metrics.Lift, panel.Y + panel.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2559)]
    public void NearAnEdge_ThePanelIsKeptOnTheDisplay(int anchor)
    {
        var panel = Arrange(3, anchor: anchor).Panel;
        Assert.True(panel.X >= Main.X + Metrics.ScreenMargin);
        Assert.True(panel.X + panel.Width <= Main.X + Main.Width - Metrics.ScreenMargin);
    }

    [Fact]
    public void OnTheLeftHandDisplay_ThePanelIsPlacedInItsOwnCoordinates()
    {
        var panel = Arrange(2, scale: 1.5, work: FourK, anchor: -1920, floor: 1900).Panel;
        Assert.True(Inside(panel, FourK));
        Assert.InRange(panel.X + (panel.Width / 2) + 1920, -1, 1);
    }

    [Fact]
    public void At150Percent_EverythingIsHalfAsLargeAgain()
    {
        var at100 = Assert.Single(Arrange(1).Cards);
        var at150 = Assert.Single(Arrange(1, scale: 1.5).Cards);
        Assert.Equal(360, at150.Picture.Width);
        Assert.Equal(at100.Card.Height * 1.5, at150.Card.Height, 1.0);
    }

    [Fact]
    public void ARowTooWideForTheDisplay_IsShrunkAlike_UntilItFits()
    {
        var arrangement = Arrange(10);
        Assert.False(arrangement.IsList);
        Assert.Equal(10, arrangement.Cards.Count);
        Assert.True(arrangement.Panel.Width <= Main.Width - (2 * Metrics.ScreenMargin));
        Assert.True(arrangement.Cards[0].Picture.Width < 240);
        Assert.All(arrangement.Cards, card => Assert.Equal(arrangement.Cards[0].Picture.Width, card.Picture.Width));
    }

    [Fact]
    public void ShrunkBelowTheSmallestPicture_TheWindowsAreListed()
    {
        var arrangement = Arrange(30);
        Assert.True(arrangement.IsList);
        Assert.Equal(30, arrangement.Cards.Count);
        Assert.All(arrangement.Cards, card => Assert.True(card.Picture.IsEmpty));
        Assert.Equal(Floor - (int)Metrics.Lift, arrangement.Panel.Y + arrangement.Panel.Height);
    }

    [Fact]
    public void AListTallerThanTheDisplay_ShowsWhatFits()
    {
        var arrangement = Arrange(80, floor: 600);
        Assert.True(arrangement.IsList);
        Assert.InRange(arrangement.Cards.Count, 1, 79);
        Assert.True(arrangement.Panel.Y >= Main.Y + Metrics.ScreenMargin);
        var last = arrangement.Cards[^1].Card;
        Assert.True(last.Y + last.Height <= arrangement.Panel.Height);
    }
}
