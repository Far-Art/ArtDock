using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.Services;
using ArtDock.Views;

namespace ArtDock.Tests;

/// <summary>
/// Groups of the dock's own: a pin that holds pins (<see cref="PinnedAppSetting.Items"/>), drawn
/// as the first four of them, two by two, and opened as a grid above itself.
/// </summary>
public sealed class GroupTests : IDisposable
{
    private readonly string _group = Path.Combine(Path.GetTempPath(), "ArtDockGroupTests_" + Guid.NewGuid().ToString("N"));

    public GroupTests() => Directory.CreateDirectory(_group);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_group, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static PinnedAppSetting Pin(string id) => new() { Id = id, Label = id, TargetPath = $@"C:\Apps\{id}.exe" };

    private static PinnedAppSetting Group(string id, params PinnedAppSetting[] items) => new()
    {
        Id = id,
        Label = id,
        TargetPath = DockCommands.GroupTarget,
        Items = [.. items]
    };

    // ---- the model -----------------------------------------------------------------------

    [Fact]
    public void AGroup_IsKnownByItsTarget()
    {
        Assert.True(Group("f").IsGroup);
        Assert.True(DockCommands.IsCommand(DockCommands.GroupTarget));
        Assert.False(Pin("a").IsGroup);

        // A separator is never a group, whatever its target says.
        Assert.False(new PinnedAppSetting { TargetPath = DockCommands.GroupTarget, IsSeparator = true }.IsGroup);
    }

    [Fact]
    public void AGroup_IsNeverRunAsACommand() =>
        Assert.False(DockCommands.Run(DockCommands.GroupTarget));

    [Fact]
    public void CloningTheSettings_CopiesWhatAGroupHolds_RatherThanSharingIt()
    {
        // The reflection test over the file format skips Items, which is left out of the file
        // when null; this is its share of that test. A copy sharing the original's list passes
        // any comparison of values, and is the one that breaks Cancel.
        var original = new DockSettings { PinnedApps = [Group("f", Pin("a"), Pin("b"))] };

        var copy = original.Clone();
        copy.PinnedApps[0].Items!.RemoveAt(0);
        copy.PinnedApps[0].Items![0].Label = "renamed";

        Assert.Equal(["a", "b"], original.PinnedApps[0].Items!.Select(pin => pin.Label));
    }

    [Fact]
    public void AGroup_ComesBackFromTheFile_WithWhatItHolds()
    {
        var path = Path.Combine(_group, "group.json");
        SettingsStore.Export(new DockSettings { PinnedApps = [Pin("a"), Group("f", Pin("b"), Pin("c"))] }, path);

        var pins = SettingsStore.Import(path).PinnedApps;

        Assert.Equal(["a", "f"], pins.Select(pin => pin.Id));
        Assert.True(pins[1].IsGroup);
        Assert.Equal(["b", "c"], pins[1].Items!.Select(pin => pin.Id));
        Assert.Null(pins[0].Items);
    }

    [Fact]
    public void APinThatIsNoGroup_WritesNoItems()
    {
        // So a file without groups is the file it was before groups were thought of.
        var path = Path.Combine(_group, "plain.json");
        SettingsStore.Export(new DockSettings { PinnedApps = [Pin("a")] }, path);

        Assert.DoesNotContain("\"Items\"", File.ReadAllText(path));
    }

    [Fact]
    public void IdsRepeatedInsideAGroup_AreMadeUnique()
    {
        var settings = new DockSettings { PinnedApps = [Pin("a"), Group("f", Pin("a"), Pin(""))] };

        Assert.True(settings.RepairPinIds());

        var ids = settings.EveryPin().Select(pin => pin.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("a", settings.PinnedApps[0].Id);
    }

    [Fact]
    public void APinInAGroup_IsFoundAndRemovedByItsId()
    {
        var settings = new DockSettings { PinnedApps = [Pin("a"), Group("f", Pin("b"), Pin("c"))] };

        Assert.Equal("b", settings.FindPin("b")?.Label);
        Assert.Equal("f", settings.GroupOf("b")?.Id);
        Assert.Null(settings.GroupOf("a"));

        Assert.True(settings.RemovePin("b"));
        Assert.Equal(["c"], settings.PinnedApps[1].Items!.Select(pin => pin.Id));
        Assert.False(settings.RemovePin("b"));
    }

    [Fact]
    public void Ungrouping_PutsWhatAGroupHeld_WhereItWas()
    {
        var settings = new DockSettings
        {
            PinnedApps = [Pin("a"), Group("g", Pin("b"), new PinnedAppSetting { Id = "s", IsSeparator = true }, Pin("c")), Pin("d")]
        };

        Assert.True(settings.Ungroup("g"));
        Assert.Equal(["a", "b", "c", "d"], settings.PinnedApps.Select(pin => pin.Id));

        // Only a group on the dock is ungrouped.
        Assert.False(settings.Ungroup("a"));
        Assert.False(settings.Ungroup("g"));
    }

    [Fact]
    public void AGroup_HoldsNoGroupAndNoSeparator()
    {
        // One level deep, whatever a hand-edited file says.
        var group = Group(
            "f",
            Pin("a"),
            Group("inner", Pin("b")),
            new PinnedAppSetting { Id = "s", IsSeparator = true },
            Pin("c"));

        var item = PinnedAppsService.ToDockItem(group);

        Assert.True(item.IsGroup);
        Assert.Equal(["a", "c"], item.Children.Select(child => child.Id));
        Assert.Null(item.RunningTarget);
        Assert.False(item.CanRunAsAdministrator);
    }

    [Fact]
    public void TheAddMenu_OffersAnEmptyGroup()
    {
        Assert.Contains(DockPresets.Menu().SelectMany(group => group), preset => preset.Key == DockPresets.GroupKey);

        var group = DockPresets.Create(DockPresets.GroupKey);

        Assert.NotNull(group);
        Assert.True(group.IsGroup);
        Assert.Empty(group.Items!);
        Assert.False(string.IsNullOrWhiteSpace(group.Label));
        Assert.Equal(DockPresets.GroupLabel, PinnedAppsService.DefaultLabel(group.TargetPath, null));
    }

    [Fact]
    public void ANewGroup_IsNamedApartFromTheOthers()
    {
        var label = DockPresets.GroupLabel;

        Assert.Equal(label, DockPresets.NextGroupLabel([Pin("a")]));

        // A pin that is no group does not take the name.
        Assert.Equal(label, DockPresets.NextGroupLabel([Named(Pin("a"), label)]));

        var second = DockPresets.NextGroupLabel([Named(Group("g"), label)]);
        Assert.NotEqual(label, second);
        Assert.StartsWith(label, second);

        // The first free one, a gap included.
        var third = DockPresets.NextGroupLabel([Named(Group("g1"), label), Named(Group("g2"), second)]);
        Assert.NotEqual(second, third);
        Assert.Equal(second, DockPresets.NextGroupLabel([Named(Group("g1"), label), Named(Group("g3"), third)]));
    }

    private static PinnedAppSetting Named(PinnedAppSetting pin, string label)
    {
        pin.Label = label;
        return pin;
    }

    // ---- the picture ---------------------------------------------------------------------

    [Fact]
    public void ThePicture_ShowsTheFirstFour_InOrder() => OnStaThread(() =>
    {
        Color[] colours = [Colors.Red, Colors.Lime, Colors.Blue, Colors.Yellow, Colors.Magenta];
        var picture = GroupArt.For([.. colours.Select(Solid)]);

        for (var i = 0; i < GroupArt.Shown; i++)
        {
            var place = GroupArt.Place(i);
            var pixel = PixelAt(picture, place.Left + (place.Width / 2), place.Top + (place.Height / 2));
            Assert.Equal(colours[i], pixel);
        }
    });

    [Fact]
    public void ThePicture_LeavesAnEmptyPlaceForEachItemNotThere() => OnStaThread(() =>
    {
        var picture = GroupArt.For([Solid(Colors.Red)]);

        var second = GroupArt.Place(1);
        Assert.NotEqual(Colors.Red, PixelAt(picture, second.Left + (second.Width / 2), second.Top + (second.Height / 2)));
        Assert.NotSame(GroupArt.Empty, picture);
    });

    [Fact]
    public void AnEmptyGroup_IsTheTileAlone_WithNoPlaces() => OnStaThread(() =>
    {
        // Each place's middle is the tile's own face, as it is between the places at the same
        // height — the face is shaded from top to bottom.
        for (var i = 0; i < GroupArt.Shown; i++)
        {
            var place = GroupArt.Place(i);
            var y = place.Top + (place.Height / 2);
            Assert.Equal(PixelAt(GroupArt.Empty, 128, y), PixelAt(GroupArt.Empty, place.Left + (place.Width / 2), y));
        }
    });

    /// <summary>The Add menus' picture is the tile with a square of each colour in its four places, in order.</summary>
    [Fact]
    public void TheAddPicture_HasAColouredSquareInEachPlace() => OnStaThread(() =>
    {
        var picture = GroupArt.AddPicture;
        Assert.Same(picture, GroupArt.AddPicture);
        for (var i = 0; i < GroupArt.Shown; i++)
        {
            var place = GroupArt.Place(i);
            var pixel = PixelAt(picture, place.Left + (place.Width / 2), place.Top + (place.Height / 2));
            var wanted = GroupArt.AddColours[i];
            Assert.InRange(Math.Abs(pixel.R - wanted.R) + Math.Abs(pixel.G - wanted.G) + Math.Abs(pixel.B - wanted.B), 0, 12);
        }
    });

    [Fact]
    public void TheOpenLook_IsDark_AroundTheSameIcons() => OnStaThread(() =>
    {
        ImageSource[] icons = [Solid(Colors.Red), Solid(Colors.Blue)];
        var open = GroupArt.Open(icons);

        Assert.NotSame(GroupArt.For(icons), open);
        Assert.Same(open, GroupArt.Open([.. icons]));

        var first = GroupArt.Place(0);
        Assert.Equal(Colors.Red, PixelAt(open, first.Left + (first.Width / 2), first.Top + (first.Height / 2)));

        // Between the places, where the tile shows: dark and solid open, a faint wash at rest —
        // the well, which lets the bar show through.
        var dark = PixelAt(open, 128, 128);
        Assert.True(dark.R + dark.G + dark.B < 200);
        Assert.True(AlphaAt(open, 128, 128) > 0xC0);
        Assert.InRange(AlphaAt(GroupArt.For(icons), 128, 128), 1, 0x40);
    });

    [Fact]
    public void TheIconsAlone_AreWhereThePictureHasThem_OnNothing() => OnStaThread(() =>
    {
        ImageSource[] icons = [Solid(Colors.Red), Solid(Colors.Blue)];
        var alone = GroupArt.IconsOnly(icons);

        Assert.Same(alone, GroupArt.IconsOnly([.. icons]));

        var first = GroupArt.Place(0);
        var second = GroupArt.Place(1);
        Assert.Equal(Colors.Red, PixelAt(alone, first.Left + (first.Width / 2), first.Top + (first.Height / 2)));
        Assert.Equal(Colors.Blue, PixelAt(alone, second.Left + (second.Width / 2), second.Top + (second.Height / 2)));

        // No tile under them: clear between the places, and where the third would be.
        var third = GroupArt.Place(2);
        Assert.Equal(0, AlphaAt(alone, 128, 128));
        Assert.Equal(0, AlphaAt(alone, third.Left + (third.Width / 2), third.Top + (third.Height / 2)));
        Assert.Equal(0, AlphaAt(GroupArt.IconsOnly([]), 128, 128));
    });

    [Fact]
    public void ThePicture_IsDrawnOnceForTheSameIcons() => OnStaThread(() =>
    {
        ImageSource[] icons = [Solid(Colors.Red), Solid(Colors.Blue)];

        Assert.Same(GroupArt.For(icons), GroupArt.For([.. icons]));
        Assert.Same(GroupArt.Empty, GroupArt.For([]));
    });

    [Fact]
    public void TheFourPlaces_SitInsideTheTile_WithoutOverlapping()
    {
        var places = Enumerable.Range(0, GroupArt.Shown).Select(GroupArt.Place).ToList();

        foreach (var place in places)
        {
            Assert.True(place.Left >= 6 && place.Top >= 6 && place.Right <= 250 && place.Bottom <= 250);
        }

        for (var i = 0; i < places.Count; i++)
        {
            for (var j = i + 1; j < places.Count; j++)
            {
                var overlap = Rect.Intersect(places[i], places[j]);
                Assert.True(overlap.IsEmpty || overlap.Width == 0 || overlap.Height == 0);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AFittedPicture_IsCentredOnTheTile(int total)
    {
        var places = Enumerable.Range(0, total).Select(i => GroupArt.PlaceOf(i, total, fits: true)).ToList();
        var bounds = places.Aggregate(Rect.Union);

        Assert.Equal(128, bounds.Left + (bounds.Width / 2), 6);
        Assert.Equal(128, bounds.Top + (bounds.Height / 2), 6);
        Assert.All(places, place => Assert.True(place.Left >= 6 && place.Top >= 6 && place.Right <= 250 && place.Bottom <= 250));

        // One alone is larger than one of four.
        if (total == 1)
        {
            Assert.True(places[0].Width > GroupArt.Place(0).Width);
        }
    }

    [Fact]
    public void AFittedPicture_OfFourOrMore_IsTwoByTwo()
    {
        for (var i = 0; i < GroupArt.Shown; i++)
        {
            Assert.Equal(GroupArt.Place(i), GroupArt.PlaceOf(i, 4, fits: true));
            Assert.Equal(GroupArt.Place(i), GroupArt.PlaceOf(i, 9, fits: true));
        }
    }

    [Fact]
    public void Unfitted_TheFourAreTwoByTwo_HoweverFewThereAre()
    {
        for (var i = 0; i < GroupArt.Shown; i++)
        {
            Assert.Equal(GroupArt.Place(i), GroupArt.PlaceOf(i, 1, fits: false));
            Assert.Equal(GroupArt.Place(i), GroupArt.PlaceOf(i, 3, fits: false));
        }
    }

    // ---- the open group -----------------------------------------------------------------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(9, 3)]
    [InlineData(10, 4)]
    [InlineData(30, 4)]
    public void TheOpenGroup_IsNearSquare_AndNeverWiderThanFour(int count, int columns) =>
        Assert.Equal(columns, GroupFlyout.ColumnsFor(count));

    // ---- dragging onto a group ----------------------------------------------------------

    [Fact]
    public void AGroupAnIconWouldGoInto_OpensWithASpring_AndClosesAgain() => OnStaThread(() =>
    {
        const double growth = ArtDock.Controls.DockItemVisual.DropTargetGrowth;
        var visual = new ArtDock.Controls.DockItemVisual(
            new ArtDock.Dock.DockItem { Id = "g", Label = "g", IsGroup = true }, 64);

        Assert.False(visual.AdvanceDrop(16, instant: false));
        Assert.Equal(1, visual.DropScale);
        Assert.Equal(0, visual.Openness);

        visual.IsDropTarget = true;
        Assert.True(visual.AdvanceDrop(16, instant: false));
        Assert.InRange(visual.DropScale, 1.0001, growth - 0.0001);
        Assert.InRange(visual.Openness, 0.0001, 0.9999);

        // Out a little past its size, and back to it.
        var largest = visual.DropScale;
        for (var frame = 0; frame < 120 && visual.AdvanceDrop(16, instant: false); frame++)
        {
            largest = Math.Max(largest, visual.DropScale);
        }

        Assert.True(largest > growth + 0.01);
        Assert.Equal(growth, visual.DropScale);
        Assert.Equal(1, visual.Openness);

        // And closing: back to its size without going under it.
        visual.IsDropTarget = false;
        var smallest = visual.DropScale;
        for (var frame = 0; frame < 120 && visual.AdvanceDrop(16, instant: false); frame++)
        {
            smallest = Math.Min(smallest, visual.DropScale);
        }

        Assert.True(smallest > 0.999);
        Assert.Equal(1, visual.DropScale);
        Assert.Equal(0, visual.Openness);

        visual.IsDropTarget = true;
        Assert.True(visual.AdvanceDrop(16, instant: true));
        Assert.Equal(growth, visual.DropScale);

        // Reduced motion: there and back in a step, still said.
        visual.IsDropTarget = false;
        Assert.True(visual.AdvanceDrop(16, instant: true));
        Assert.Equal(1, visual.DropScale);
        Assert.Equal(0, visual.Openness);
        Assert.False(visual.AdvanceDrop(16, instant: true));
    });

    // ---- the open group coming out of its icon ---------------------------------------------

    [Fact]
    public void TheCard_IsPutOnTheTile_ByItsScaleAndOffset()
    {
        var card = new Rect(40, 20, 300, 250);
        var tile = new Rect(150, 330, 50, 50);

        var (scale, x, y, window) = GroupFlyout.MorphFrom(card, tile);

        // Scaled evenly about its top-left corner and moved, the window of its middle that shows
        // lands on the tile, corner to corner.
        Assert.Equal(tile.Left, card.Left + x + (scale * window.Left), 6);
        Assert.Equal(tile.Top, card.Top + y + (scale * window.Top), 6);
        Assert.Equal(tile.Width, scale * window.Width, 6);
        Assert.Equal(tile.Height, scale * window.Height, 6);

        // The tile's shape, inside the card, in its middle.
        Assert.True(window.Left >= 0 && window.Top >= 0 && window.Right <= card.Width + 1e-9 && window.Bottom <= card.Height + 1e-9);
        Assert.Equal(card.Width - window.Right, window.Left, 6);
    }

    [Fact]
    public void TheTile_IsWhereTheGroupsPictureDrawsIt_InsideTheIcon()
    {
        var icon = new Rect(0, 0, 100, 100);
        var tile = GroupArt.TileIn(icon);

        // The dock's 2% inset of a group, then the picture's 6 of 256.
        Assert.Equal(4.25, tile.Left, 3);
        Assert.Equal(100 - (2 * 4.25), tile.Width, 3);
        Assert.Equal(21.0, GroupArt.TileRadiusIn(icon), 4);
        Assert.Equal(6.0 / 244.0, GroupArt.ArtRoom, 6);
    }

    // ---- helpers -------------------------------------------------------------------------

    private static BitmapSource Solid(Color colour)
    {
        const int size = 32;
        var pixels = new byte[size * size * 4];
        for (var at = 0; at < pixels.Length; at += 4)
        {
            pixels[at] = colour.B;
            pixels[at + 1] = colour.G;
            pixels[at + 2] = colour.R;
            pixels[at + 3] = 255;
        }

        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static Color PixelAt(BitmapSource picture, double x, double y)
    {
        var pixel = new byte[4];
        picture.CopyPixels(new Int32Rect((int)x, (int)y, 1, 1), pixel, 4, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
    }

    private static byte AlphaAt(BitmapSource picture, double x, double y)
    {
        var pixel = new byte[4];
        picture.CopyPixels(new Int32Rect((int)x, (int)y, 1, 1), pixel, 4, 0);
        return pixel[3];
    }

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
}
