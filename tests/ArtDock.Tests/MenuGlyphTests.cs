using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Media;
using ArtDock.Services;
using ArtDock.Views;

namespace ArtDock.Tests;

/// <summary>
/// Covers the icons in the menus: that every glyph is really in the font, and that every
/// entry of the Add menu has something to show.
/// </summary>
/// <remarks>
/// A code point the font does not have renders as an empty box, and nothing fails — the menu
/// simply shows it. So the glyphs are held against both fonts the theme's symbol font names:
/// Segoe Fluent Icons, and Segoe MDL2 Assets, which it falls back to where the first is absent.
/// </remarks>
public class MenuGlyphTests
{
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

    public static TheoryData<string> SymbolFonts => ["Segoe Fluent Icons", "Segoe MDL2 Assets"];

    [Theory]
    [MemberData(nameof(SymbolFonts))]
    public void EveryGlyph_IsInTheFont(string font)
    {
        var typeface = new Typeface(font);
        Assert.True(typeface.TryGetGlyphTypeface(out var glyphs), $"{font} is not installed");

        var missing = Enum.GetValues<MenuGlyph>()
            .Where(glyph => !glyphs.CharacterToGlyphMap.ContainsKey(MenuIcons.CodePoint(glyph)[0]))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryAddMenuEntry_HasAnIcon() => OnStaThread(() =>
    {
        // Less the commands: Start's mark ships as a resource of the application, found through
        // its pack URI, and a test host has no application for that to resolve against. It is
        // the image the dock draws Start with, through the same call, so it is seen there.
        foreach (var preset in DockPresets.Menu()
            .SelectMany(group => group)
            .Where(preset => !DockCommands.IsCommand(preset.Target)))
        {
            var icon = MenuIcons.For(preset);

            Assert.True(icon is not null, $"{preset.Key} has no icon");
            if (icon is Image image)
            {
                Assert.True(image.Source is not null, $"{preset.Key} has an empty image");
            }
        }
    });

    [Fact]
    public void AnEntryThatPinsATarget_SaysWhichTarget()
    {
        // The icon is read from the target, so an entry that pins something without saying
        // what would be the one blank in the menu.
        foreach (var preset in DockPresets.Menu().SelectMany(group => group))
        {
            var pinsNothingKnown = preset.Key is DockPresets.BrowseKey or DockPresets.SeparatorKey;
            Assert.Equal(pinsNothingKnown, preset.Target is null);
        }
    }
}
