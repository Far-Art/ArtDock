using System.IO;
using System.Windows.Media;
using System.Xml.Linq;
using ArtDock.Controls;
using ArtDock.Dock;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Holds <i>Match the taskbar</i> (<see cref="DockSettings.UseTaskbarColor"/>) to what it
/// promises: the bar is the taskbar's material, so the bar's own opacity, colour and blur are
/// set aside — and none of them is rewritten.
/// </summary>
/// <remarks>
/// What the material looks like on the screen is not something a test here can see: it was
/// measured against the taskbar's own pixels, and <see cref="TaskbarMaterial"/> has the account.
/// The sheet's side — that it takes the material and gives it back — is in
/// <see cref="SheetFollowTests"/>.
/// </remarks>
public class TaskbarMatchTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ArtDockTests", Guid.NewGuid().ToString("n"));

    public TaskbarMatchTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }

        GC.SuppressFinalize(this);
    }

    private static readonly TaskbarMaterial Light = TaskbarMaterial.Neutral(light: true);

    [Fact]
    public void ANewDock_DoesNotMatchTheTaskbar()
    {
        Assert.False(new DockSettings().UseTaskbarColor);
    }

    /// <summary>
    /// The taskbar is always blurred, so a bar that matches it is too, whatever the bar's own
    /// blur was left at.
    /// </summary>
    [Fact]
    public void MatchingTheTaskbar_TheSheetIsUp_WhateverTheBlurWasLeftAt()
    {
        Assert.True(new DockSettings { UseTaskbarColor = true, BlurBackground = false }.Blurs);
        Assert.True(new DockSettings { UseTaskbarColor = true, BlurBackground = true }.Blurs);

        // And not matching, the blur is the user's own, as it was.
        Assert.False(new DockSettings { BlurBackground = false }.Blurs);
    }

    /// <summary>
    /// Where the taskbar itself is solid, so is the bar: no sheet, and the taskbar's own solid
    /// colour, which the opacity has no say in.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void WhereTheBarIsSolid_ItIsTheTaskbarsSolidColour_NotMixedWithGrey(bool noGpu, bool transparency)
    {
        var settings = new DockSettings
        {
            UseTaskbarColor = true, NoGpu = noGpu, WindowsTransparency = transparency, BarOpacity = 0.3
        };

        Assert.True(settings.SolidBar);
        Assert.False(settings.Blurs);
        Assert.Equal(("#EEEEEE", 1), settings.TaskbarPaint(Light));
        Assert.Equal(("#1C1C1C", 1), settings.TaskbarPaint(TaskbarMaterial.Neutral(light: false)));
    }

    /// <summary>
    /// What the dock paints where the sheet is up and cannot draw the material: the tint, as
    /// strong as the material lays it on — and never at the bar's own opacity.
    /// </summary>
    [Fact]
    public void WhereTheSheetCannotDrawTheMaterial_TheBarIsItsTint_AtItsStrength()
    {
        var settings = new DockSettings { UseTaskbarColor = true, BarOpacity = 0.3 };

        Assert.Equal(("#F3F3F3", 0.9), settings.TaskbarPaint(Light));
        Assert.Equal(("#202020", 0.96), settings.TaskbarPaint(TaskbarMaterial.Neutral(light: false)));
    }

    /// <summary>A contrast theme's taskbar is one colour, and so is the bar, transparency or no.</summary>
    [Fact]
    public void ATaskbarOfOnePlainColour_IsPaintedSolid()
    {
        var black = TaskbarMaterial.Flat(Colors.Black);

        Assert.True(black.Opaque);
        Assert.Equal(("#000000", 1), new DockSettings { UseTaskbarColor = true }.TaskbarPaint(black));
    }

    /// <summary>
    /// The recipe that was measured against the taskbar, so a change to it is a change someone
    /// meant: the light theme's, the one that was on the screen.
    /// </summary>
    [Fact]
    public void TheLightTaskbarsMaterial_IsTheOneMeasured()
    {
        Assert.Equal(Color.FromRgb(0xF3, 0xF3, 0xF3), Light.Tint);
        Assert.Equal(0, Light.TintOpacity);
        Assert.Equal(0.9, Light.LuminosityOpacity);
        Assert.Equal(Color.FromRgb(0xEE, 0xEE, 0xEE), Light.Solid);
        Assert.False(Light.Opaque);

        Assert.Equal(30, TaskbarMaterial.BlurRadius);
        Assert.Equal(0.02, TaskbarMaterial.GrainOpacity);
        Assert.Equal(Color.FromArgb(0x66, 0x75, 0x75, 0x75), TaskbarMaterial.Stroke);
    }

    [Fact]
    public void TheAccentsMaterial_IsTheShadeItIsGiven_AndThatShadeSolid()
    {
        var shade = Color.FromRgb(0x00, 0x1A, 0x6A);
        var accent = TaskbarMaterial.Accent(shade);

        Assert.Equal(shade, accent.Tint);
        Assert.Equal(shade, accent.Solid);
        Assert.False(accent.Opaque);
    }

    [Fact]
    public void WhatItSetsAside_IsKeptUnderneath_ThroughTheFile()
    {
        var original = new DockSettings
        {
            UseTaskbarColor = true, BlurBackground = false, BarOpacity = 0.4, BarColor = "#102030"
        };

        var path = Path.Combine(_dir, "match.json");
        SettingsStore.Export(original, path);
        var back = SettingsStore.Import(path);

        Assert.True(back.UseTaskbarColor);
        Assert.False(back.BlurBackground);
        Assert.Equal(0.4, back.BarOpacity);
        Assert.Equal("#102030", back.BarColor);

        // And so turning it off puts back the bar that was there.
        back.UseTaskbarColor = false;

        Assert.False(back.Blurs);
        Assert.Equal(0.4, back.BarAlpha);
        Assert.Equal("#102030", back.BarPaint(back.BarColor));
    }

    /// <summary>The bar's rim is the taskbar's edge line while it matches, and the dock's own again after.</summary>
    [Fact]
    public void TheBarsRim_CanBeTheTaskbarsEdge_AndGoesBack() => OnStaThread(() =>
    {
        var bar = new DockBar(new DockMetrics());
        Assert.Equal(DockBar.BarBorder, bar.BarRim);

        bar.SetBarRim(TaskbarMaterial.Stroke);
        Assert.Equal(TaskbarMaterial.Stroke, bar.BarRim);

        bar.SetBarRim(DockBar.BarBorder);
        Assert.Equal(DockBar.BarBorder, bar.BarRim);
    });

    /// <summary>
    /// The dock paints the bar with the taskbar's material when it matches, and tells the sheet.
    /// </summary>
    /// <remarks>
    /// Read from the source, as <see cref="NoGpuTests"/> reads it: a line that painted the
    /// taskbar's colour at the bar's own opacity — which is what this option did until
    /// 2026-11-21 — compiles and looks nearly right.
    /// </remarks>
    [Fact]
    public void TheDockWindow_PaintsTheTaskbarsMaterial_AndHandsItToTheSheet()
    {
        var source = File.ReadAllText(Path.Combine(FindSourceRoot(), "Views", "DockWindow.xaml.cs"));

        Assert.Contains("." + nameof(DockSettings.TaskbarPaint) + "(", source);
        Assert.Contains("TaskbarMaterial.Stroke", source);
        Assert.Contains("_backdrop.Material = ", source);
    }

    /// <summary>
    /// The checkbox greys the opacity, the colour and the blur, so it must sit in none of
    /// their cards: a checkbox that greys its own card cannot be unticked.
    /// </summary>
    [Fact]
    public void TheCheckbox_IsOutsideEverythingItGreys()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var page = XDocument.Load(Path.Combine(FindSourceRoot(), "Views", "SettingsWindow.xaml"));

        XElement Named(string name) =>
            page.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);

        var checkbox = Named("TaskbarColorCheck");
        foreach (var greyed in new[] { "OpacityCard", "ColorCard", "BlurCard" })
        {
            Assert.DoesNotContain(Named(greyed), checkbox.Ancestors());
        }

        // And before all of them on the page, since it decides them.
        Assert.True(checkbox.IsBefore(Named("OpacityCard")));

        var code = File.ReadAllText(Path.Combine(FindSourceRoot(), "Views", "SettingsWindow.xaml.cs"));
        // Each card whole, so its label dims with its controls: the colour's used to be greyed
        // a control at a time, which left its label and swatch at full strength.
        Assert.Contains("OpacityCard.IsEnabled = manual", code);
        Assert.Contains("ColorCard.IsEnabled = manual", code);
        Assert.Contains(Named("BarColorBox"), Named("ColorCard").Descendants());
        Assert.Contains(Named("ManualColorPanel"), Named("ColorCard").Descendants());
        Assert.Contains("TaskbarColorCheck.IsChecked != true;", code);
    }

    private static void OnStaThread(Action body)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException(failure.Message, failure);
        }
    }

    private static string FindSourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ArtDock.sln")))
            {
                return Path.Combine(dir.FullName, "src", "ArtDock");
            }
        }

        throw new DirectoryNotFoundException("The repository root, with ArtDock.sln in it, was not found above the tests.");
    }
}
