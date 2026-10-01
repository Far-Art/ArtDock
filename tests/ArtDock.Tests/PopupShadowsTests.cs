using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ArtDock.Views;

namespace ArtDock.Tests;

/// <summary>
/// Covers <see cref="PopupShadows"/>, which makes room in the popups of combo boxes and tooltips
/// for the shadows the Fluent theme casts under them.
/// </summary>
/// <remarks>
/// It works on the theme's own templates, so a test for each holds the template to what it
/// relies on: a theme that stops matching is left alone rather than broken, which would bring
/// the clipped shadow back without a sound. The rest hold a list to where the theme put it
/// before — the room is all outside the list, and the popup moves to make up for it, whichever
/// way it opens — and a tooltip to the place WPF gave it, with the room round it held to the
/// display. A tooltip's window cannot be opened without putting it on a screen, so what is
/// tested of it is the arithmetic done on the place WPF read back.
/// </remarks>
public class PopupShadowsTests
{
    private const double BoxWidth = 200;
    private const double BoxHeight = 32;

    private static void OnStaThread(Action body)
    {
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure?.Throw();
    }

    /// <summary>The Fluent theme's resources, as <c>ThemeMode</c> merges them.</summary>
    private static ResourceDictionary Fluent()
    {
        // Touching Application registers pack://application, which the theme is read from.
        _ = Application.Current;

        return new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml")
        };
    }

    /// <summary>A combo box in the Fluent theme's style, laid out but in no window.</summary>
    private static ComboBox Box()
    {
        var fluent = Fluent();
        var box = new ComboBox
        {
            Style = (Style)fluent[typeof(ComboBox)],
            Width = BoxWidth,
            Height = BoxHeight,
            ItemsSource = new[] { "One", "Two", "Three" },
        };
        box.Resources.MergedDictionaries.Add(fluent);

        box.ApplyTemplate();
        box.Measure(new Size(BoxWidth, BoxHeight));
        box.Arrange(new Rect(0, 0, BoxWidth, BoxHeight));
        box.UpdateLayout();
        return box;
    }

    private static readonly Lazy<bool> Registered = new(() =>
    {
        PopupShadows.Register();
        return true;
    });

    private static Popup PopupOf(ComboBox box) => (Popup)box.Template.FindName("PART_Popup", box);

    [Fact]
    public void AComboBoxShownInAWindow_IsFitted_WithNothingAskingForIt() => OnStaThread(() =>
    {
        _ = Registered.Value;

        // The dialogs' combo boxes are fitted by the class handler alone, which the tests below
        // go round by calling the fitting directly. A window off every display, never activated,
        // so that nothing is shown and nothing loses the foreground.
        var fluent = Fluent();
        var box = new ComboBox
        {
            Style = (Style)fluent[typeof(ComboBox)],
            Width = BoxWidth,
            Height = BoxHeight,
            ItemsSource = new[] { "One", "Two", "Three" },
        };
        box.Resources.MergedDictionaries.Add(fluent);

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -20000,
            Top = -20000,
            Width = 300,
            Height = 100,
            Content = box,
        };

        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

            Assert.True(box.IsLoaded);
            Assert.NotEqual(Rect.Empty, PopupOf(box).PlacementRectangle);
            Assert.NotEqual(default, ListOf(box).Margin);
        }
        finally
        {
            window.Close();
            window.Dispatcher.InvokeShutdown();
        }
    });

    private static FrameworkElement ListOf(ComboBox box) => (FrameworkElement)PopupOf(box).Child;

    [Fact]
    public void TheThemesComboBox_StillHasTheShadowThisFits() => OnStaThread(() =>
    {
        var box = Box();
        var popup = PopupOf(box);

        Assert.True(popup.AllowsTransparency);
        Assert.Equal(PlacementMode.Bottom, popup.Placement);
        Assert.IsType<DropShadowEffect>(ListOf(box).Effect);

        // The popup is placed against its parent, which has to be the box's own size for the
        // rectangle to be the box's.
        var target = Assert.IsAssignableFrom<FrameworkElement>(VisualTreeHelper.GetParent(popup));
        Assert.Equal(BoxWidth, target.ActualWidth);
        Assert.Equal(BoxHeight, target.ActualHeight);
    });

    [Fact]
    public void TheRoomAroundTheList_TakesInTheWholeShadow_BelowAndToBothSides() => OnStaThread(() =>
    {
        var box = Box();
        PopupShadows.FitDropDown(box);

        var list = ListOf(box);
        var shadow = (DropShadowEffect)list.Effect;

        // The theme's shadow is cast straight down, which this takes for granted: were it
        // cast sideways as well, each side would need its blur and more.
        Assert.Equal(270, shadow.Direction);

        Assert.Equal(shadow.BlurRadius, list.Margin.Left, 6);
        Assert.Equal(shadow.BlurRadius, list.Margin.Right, 6);
        Assert.Equal(shadow.BlurRadius + shadow.ShadowDepth, list.Margin.Bottom, 6);
        Assert.Equal(0, list.Margin.Top);
    });

    [Fact]
    public void OpeningBelow_TheListStillStartsUnderTheBox_AtTheThemesGap() => OnStaThread(() =>
    {
        var box = Box();
        var gap = PopupOf(box).VerticalOffset;
        PopupShadows.FitDropDown(box);

        var popup = PopupOf(box);
        var list = ListOf(box);

        // Below, the popup's top is put at the rectangle's bottom, moved by the offsets.
        var popupTop = popup.PlacementRectangle.Bottom + popup.VerticalOffset;

        Assert.Equal(BoxHeight + gap, popupTop + list.Margin.Top, 6);
    });

    [Fact]
    public void OpeningAbove_TheListEndsOverTheBox_AtTheSameGap_WithItsShadowOverTheBox() => OnStaThread(() =>
    {
        var box = Box();
        var gap = PopupOf(box).VerticalOffset;
        PopupShadows.FitDropDown(box);

        var popup = PopupOf(box);
        var list = ListOf(box);

        // Above, the popup's bottom is put at the rectangle's top, moved by the same offsets —
        // which is why they could not do this on their own.
        var popupBottom = popup.PlacementRectangle.Top + popup.VerticalOffset;

        Assert.Equal(-gap, popupBottom - list.Margin.Bottom, 6);
    });

    [Fact]
    public void TheList_LinesUpWithTheBox_AtWhicheverEndThePopupIsAligned() => OnStaThread(() =>
    {
        var box = Box();
        PopupShadows.FitDropDown(box);

        var popup = PopupOf(box);
        var list = ListOf(box);
        var rect = popup.PlacementRectangle;

        // Aligned at the start, as usual; at the end when menus drop to the left, or in a
        // right-to-left language seen from the screen.
        Assert.Equal(0, rect.Left + popup.HorizontalOffset + list.Margin.Left, 6);
        Assert.Equal(BoxWidth, rect.Right + popup.HorizontalOffset - list.Margin.Right, 6);

        // And at least as wide as the box, as the theme made the popup before the room was
        // inside it.
        Assert.Equal(BoxWidth, list.MinWidth);
    });

    [Fact]
    public void AShadowDeeperThanTheBoxIsTall_StillOpensRight_ByGivingUpSomeOfTheRoom() => OnStaThread(() =>
    {
        var box = Box();
        var gap = PopupOf(box).VerticalOffset;

        // Room for this one would turn the placement rectangle inside out.
        ListOf(box).Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 6, Direction = 270 };
        PopupShadows.FitDropDown(box);

        var popup = PopupOf(box);
        var list = ListOf(box);

        Assert.True(popup.PlacementRectangle.Height >= 0);
        Assert.Equal(BoxHeight + (2 * gap), list.Margin.Bottom, 6);

        // Still under the box when below, and over it when above.
        Assert.Equal(BoxHeight + gap, popup.PlacementRectangle.Bottom + popup.VerticalOffset, 6);
        Assert.Equal(-gap, popup.PlacementRectangle.Top + popup.VerticalOffset - list.Margin.Bottom, 6);
    });

    [Fact]
    public void FittingAgain_ChangesNothing() => OnStaThread(() =>
    {
        var box = Box();
        PopupShadows.FitDropDown(box);

        var popup = PopupOf(box);
        var list = ListOf(box);
        var rect = popup.PlacementRectangle;
        var margin = list.Margin;

        PopupShadows.FitDropDown(box);

        Assert.Equal(rect, popup.PlacementRectangle);
        Assert.Equal(margin, list.Margin);
    });

    // ---------------------------------------------------------------- tooltips

    /// <summary>A tooltip in the Fluent theme's style, laid out but in no window.</summary>
    private static ToolTip Tip()
    {
        var fluent = Fluent();
        var tip = new ToolTip { Style = (Style)fluent[typeof(ToolTip)], Content = "A tip" };
        tip.Resources.MergedDictionaries.Add(fluent);

        tip.ApplyTemplate();
        tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        tip.Arrange(new Rect(tip.DesiredSize));
        return tip;
    }

    private static DropShadowEffect ShadowOf(ToolTip tip) =>
        (DropShadowEffect)((FrameworkElement)tip.Template.FindName("Border", tip)).Effect;

    private static readonly Rect AnyScreen = new(-3840, 0, 6400, 2088);

    [Fact]
    public void TheThemesToolTip_StillHasTheShadowThisFits() => OnStaThread(() =>
    {
        var tip = Tip();

        var border = Assert.IsAssignableFrom<FrameworkElement>(tip.Template.FindName("Border", tip));
        Assert.IsType<DropShadowEffect>(border.Effect);

        // The tooltip is given its room as a margin of its own, which it has none of to lose.
        Assert.Equal(default, tip.Margin);
    });

    [Fact]
    public void ATooltipsRoom_TakesInItsWholeShadow_AllRound() => OnStaThread(() =>
    {
        var shadow = ShadowOf(Tip());
        var reach = PopupShadows.Reach(shadow);

        // Cast nowhere, so as far on every side.
        Assert.Equal(0, shadow.ShadowDepth);
        Assert.Equal(shadow.BlurRadius, reach.Left, 6);
        Assert.Equal(shadow.BlurRadius, reach.Top, 6);
        Assert.Equal(shadow.BlurRadius, reach.Right, 6);
        Assert.Equal(shadow.BlurRadius, reach.Bottom, 6);
    });

    [Fact]
    public void ADropShadowsReach_IsItsBlur_MovedByItsDepth()
    {
        // Cast down and to the right, as a shadow from a light above and to the left is.
        var reach = PopupShadows.Reach(new DropShadowEffect { BlurRadius = 10, ShadowDepth = 4, Direction = 315 });
        var d = 4 * Math.Sqrt(0.5);

        Assert.Equal(10 - d, reach.Left, 6);
        Assert.Equal(10 - d, reach.Top, 6);
        Assert.Equal(10 + d, reach.Right, 6);
        Assert.Equal(10 + d, reach.Bottom, 6);

        // And never less than nothing, however far it is cast.
        var far = PopupShadows.Reach(new DropShadowEffect { BlurRadius = 5, ShadowDepth = 20, Direction = 270 });
        Assert.Equal(0, far.Top);
    }

    [Fact]
    public void ATooltip_StaysWhereItWasPlaced_WithTheRoomRoundIt()
    {
        var placed = new Rect(1000, 500, 200, 40);
        var reach = new Thickness(30);

        var (margin, corner) = PopupShadows.ToolTipRoom(placed, AnyScreen, reach, new Vector(1.5, 1.5), FlowDirection.LeftToRight);

        // 30 of its units at 150% is 45 pixels: the window starts that far up and to the left,
        // so the tooltip inside it, 30 units in, is where WPF put it.
        Assert.Equal(new Point(955, 455), corner);
        Assert.Equal(reach, margin);
        Assert.Equal(placed.Left, corner.X + (margin.Left * 1.5), 6);
        Assert.Equal(placed.Top, corner.Y + (margin.Top * 1.5), 6);
    }

    [Fact]
    public void ATooltipsRoom_StopsAtTheEdgeOfItsDisplay()
    {
        // Ten pixels in from the left of the display, and four up from the bottom of its work
        // area: past either, the window's corner would be on another display, or its foot over
        // the taskbar, and the tooltip would be pushed back as a whole.
        var screen = new Rect(0, 0, 2560, 1392);
        var placed = new Rect(10, 1300, 200, 88);

        var (margin, corner) = PopupShadows.ToolTipRoom(placed, screen, new Thickness(30), new Vector(1, 1), FlowDirection.LeftToRight);

        Assert.Equal(new Point(0, 1270), corner);
        Assert.Equal(new Thickness(10, 30, 30, 4), margin);
    }

    [Fact]
    public void InARightToLeftTooltip_TheRoomsSidesAreMirrored()
    {
        // A shadow cast to its own right is cast to the screen's left in a mirrored tooltip.
        var reach = new Thickness(10, 0, 20, 5);
        var placed = new Rect(1000, 500, 200, 40);

        var (margin, corner) = PopupShadows.ToolTipRoom(placed, AnyScreen, reach, new Vector(1, 1), FlowDirection.RightToLeft);

        Assert.Equal(reach, margin);
        Assert.Equal(new Point(1000 - 20, 500), corner);

        // And held at the screen's right edge by its own left margin.
        var atEdge = new Rect(AnyScreen.Right - 204, 500, 200, 40);
        var (held, _) = PopupShadows.ToolTipRoom(atEdge, AnyScreen, reach, new Vector(1, 1), FlowDirection.RightToLeft);

        Assert.Equal(4, held.Left);
        Assert.Equal(20, held.Right);
    }

    [Fact]
    public void ATooltipsRoom_IsWholePixels_AtAnyScale()
    {
        var placed = new Rect(1000, 500, 200, 40);

        var (margin, corner) = PopupShadows.ToolTipRoom(placed, AnyScreen, new Thickness(30), new Vector(1.25, 1.25), FlowDirection.LeftToRight);

        // 37.5 pixels would put the tooltip between two; 38 does not.
        Assert.Equal(new Point(962, 462), corner);
        Assert.Equal(38, margin.Left * 1.25, 6);
        Assert.Equal(38, margin.Top * 1.25, 6);
    }
}
