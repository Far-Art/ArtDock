using System.IO;
using System.Text.Json;
using ArtDock.Dock;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Holds <see cref="DockSettings.NoGpu"/> to what it promises: it overrides the blur, the
/// bar's opacity and the handle's inversion, and rewrites none of them.
/// </summary>
/// <remarks>
/// The second half is the one worth a test. The settings it overrides are kept underneath, as
/// the bar's colour is under <em>Match the taskbar</em>, so that unticking the box puts back
/// the dock that was there — and a dock that came back opaque and unblurred after a spell on a
/// virtual machine would look like the setting had broken it.
/// </remarks>
public class NoGpuTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ArtDockTests", Guid.NewGuid().ToString("n"));

    public NoGpuTests() => Directory.CreateDirectory(_dir);

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

    [Fact]
    public void ANewDock_HasAGpu()
    {
        var settings = new DockSettings();

        Assert.False(settings.NoGpu);
        Assert.True(settings.Blurs);
        Assert.Equal(settings.BarOpacity, settings.BarAlpha);
        Assert.True(settings.HandleInverts);
        Assert.True(settings.CastsIconShadows);
        Assert.True(settings.CastsBarShadow);
    }

    [Fact]
    public void AFileFromBeforeTheSetting_ReadsAsOff()
    {
        // Every settings.json in the wild. Its dock must come up exactly as it was.
        var path = Path.Combine(_dir, "before.json");
        File.WriteAllText(path, """{ "BaseSize": 44, "BarOpacity": 0.5, "BlurBackground": true }""");

        var loaded = SettingsStore.Import(path);

        Assert.False(loaded.NoGpu);
        Assert.True(loaded.Blurs);
        Assert.Equal(0.5, loaded.BarAlpha);
    }

    /// <summary>
    /// The one time the dock ticks the box itself: a first run with nothing to draw with.
    /// </summary>
    [Fact]
    public void AFirstRun_WithNoHardwareToDrawWith_StartsWithNoGpuOn_AndSaysItDid()
    {
        var settings = new DockSettings();

        Assert.True(settings.AdoptNoGpu(firstRun: true, hardwareMissing: true));
        Assert.True(settings.NoGpu);
    }

    [Fact]
    public void AFirstRun_WithAGraphicsCard_IsLeftAsItIs()
    {
        var settings = new DockSettings();

        Assert.False(settings.AdoptNoGpu(firstRun: true, hardwareMissing: false));
        Assert.False(settings.NoGpu);
    }

    /// <summary>
    /// A dock with settings has an owner who has had the checkbox. A remote session can read as
    /// no hardware for as long as it lasts, and their dock must not change under them for it.
    /// </summary>
    [Fact]
    public void ADockThatHasSettings_IsNeverSwitchedByItself()
    {
        var settings = new DockSettings();

        Assert.False(settings.AdoptNoGpu(firstRun: false, hardwareMissing: true));
        Assert.False(settings.NoGpu);
    }

    [Fact]
    public void ADockAlreadyWithoutAGpu_HasNothingToBeToldAbout()
    {
        var settings = new DockSettings { NoGpu = true };

        Assert.False(settings.AdoptNoGpu(firstRun: true, hardwareMissing: true));
        Assert.True(settings.NoGpu);
    }

    [Fact]
    public void WithoutAGpu_TheBarIsSolid_Unblurred_AndTheHandleDoesNotInvert()
    {
        var settings = new DockSettings { NoGpu = true, BlurBackground = true, BarOpacity = 0.4 };

        Assert.False(settings.Blurs);
        Assert.Equal(1, settings.BarAlpha);
        Assert.False(settings.HandleInverts);
    }

    [Fact]
    public void WithoutAGpu_NothingCastsAShadow()
    {
        var settings = new DockSettings { NoGpu = true, IconShadows = true };

        // The bar's is the one that matters: in software it was measured at most of what the
        // wave costs, where the icons' are a picture each.
        Assert.False(settings.CastsBarShadow);
        Assert.False(settings.CastsIconShadows);
    }

    [Fact]
    public void WithAGpu_TheBlurTheOpacityAndTheShadowsAreTheUsersOwn()
    {
        Assert.False(new DockSettings { BlurBackground = false }.Blurs);
        Assert.True(new DockSettings { BlurBackground = true }.Blurs);
        Assert.Equal(0.4, new DockSettings { BarOpacity = 0.4 }.BarAlpha);
        Assert.False(new DockSettings { IconShadows = false }.CastsIconShadows);
        Assert.True(new DockSettings { IconShadows = true }.CastsIconShadows);
    }

    [Fact]
    public void WhatItOverrides_IsKeptUnderneath_ThroughTheFile()
    {
        var original = new DockSettings
        {
            NoGpu = true, BlurBackground = true, BarOpacity = 0.4, IconShadows = true
        };

        var path = Path.Combine(_dir, "nogpu.json");
        SettingsStore.Export(original, path);
        var back = SettingsStore.Import(path);

        Assert.True(back.NoGpu);
        Assert.True(back.BlurBackground);
        Assert.Equal(0.4, back.BarOpacity);
        Assert.True(back.IconShadows);

        // And so turning it off puts back the dock that was there.
        back.NoGpu = false;

        Assert.True(back.Blurs);
        Assert.Equal(0.4, back.BarAlpha);
        Assert.True(back.CastsIconShadows);
    }

    /// <summary>
    /// A solid bar is painted the colour it had been seen in, not the colour stored: that one
    /// at its opacity, over the grey that stands in for the desktop.
    /// </summary>
    /// <remarks>
    /// The stock bar is the case it was reported on — a pale blue at a half, which painted
    /// solid as stored was lighter than it had ever looked, and hid the white separator.
    /// </remarks>
    [Fact]
    public void WithoutAGpu_TheBarIsTheColourItLookedOverADesktop_NotTheOneStored()
    {
        var stock = new DockSettings { NoGpu = true };

        Assert.Equal("#CCD2FF", stock.BarColor);
        Assert.Equal(0.5, stock.BarOpacity);
        Assert.Equal("#9A9EB5", stock.BarPaint(stock.BarColor));
    }

    [Fact]
    public void WithoutAGpu_TheOpacityStillDoesSomething_FromTheGreyToTheColourItself()
    {
        var underlay = BarPalette.ToHex(BarPalette.Underlay);

        Assert.Equal(underlay, new DockSettings { NoGpu = true, BarOpacity = 0 }.BarPaint("#EEF1FF"));
        Assert.Equal("#EEF1FF", new DockSettings { NoGpu = true, BarOpacity = 1 }.BarPaint("#EEF1FF"));

        // Whatever colour is in force, the taskbar's included, and whatever it was written as.
        Assert.Equal("#000000", new DockSettings { NoGpu = true, BarOpacity = 1 }.BarPaint("Black"));

        // An opacity that is no number paints the colour itself rather than nothing.
        Assert.Equal("#EEF1FF", new DockSettings { NoGpu = true, BarOpacity = double.NaN }.BarPaint("#EEF1FF"));
    }

    [Fact]
    public void WithAGpu_TheBarIsPaintedTheColourInForce_Untouched()
    {
        Assert.Equal("#EEF1FF", new DockSettings { BarOpacity = 0.4 }.BarPaint("#EEF1FF"));
    }

    /// <summary>
    /// Windows' <i>Transparency effects</i>, off, make the bar solid as No GPU does — and only
    /// the bar: the shadows and the handle are not transparency.
    /// </summary>
    [Fact]
    public void WithWindowsTransparencyOff_TheBarIsSolidAndUnblurred_AndTheRestIsAsChosen()
    {
        var settings = new DockSettings
        {
            WindowsTransparency = false, BlurBackground = true, BarOpacity = 0.4, IconShadows = true
        };

        Assert.True(settings.SolidBar);
        Assert.False(settings.Blurs);
        Assert.Equal(1, settings.BarAlpha);
        Assert.Equal(
            new DockSettings { NoGpu = true, BarOpacity = 0.4 }.BarPaint("#EEF1FF"),
            settings.BarPaint("#EEF1FF"));

        Assert.True(settings.HandleInverts);
        Assert.True(settings.CastsIconShadows);
        Assert.True(settings.CastsBarShadow);
    }

    [Fact]
    public void WindowsTransparency_IsOnUntilTheDockSaysOtherwise_AndIsNeverStored()
    {
        Assert.True(new DockSettings().WindowsTransparency);

        var path = Path.Combine(_dir, "transparency.json");
        SettingsStore.Export(new DockSettings { WindowsTransparency = false, BlurBackground = true }, path);

        using (var document = JsonDocument.Parse(File.ReadAllText(path)))
        {
            var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToList();
            Assert.DoesNotContain(nameof(DockSettings.WindowsTransparency), names);
            Assert.DoesNotContain(nameof(DockSettings.SolidBar), names);
        }

        // The system's, not the user's: a file read back has the blur it was saved with.
        var back = SettingsStore.Import(path);
        Assert.True(back.WindowsTransparency);
        Assert.True(back.Blurs);
    }

    [Fact]
    public void TheFile_CarriesTheSetting_AndNotWhatFollowsFromIt()
    {
        // The three are worked out from the setting each time. Written to the file, they would
        // be a second copy of it to fall out of step with the first.
        var path = Path.Combine(_dir, "stored.json");
        SettingsStore.Export(new DockSettings { NoGpu = true }, path);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToList();

        Assert.Contains(nameof(DockSettings.NoGpu), names);
        Assert.DoesNotContain(nameof(DockSettings.Blurs), names);
        Assert.DoesNotContain(nameof(DockSettings.BarAlpha), names);
        Assert.DoesNotContain(nameof(DockSettings.HandleInverts), names);
        Assert.DoesNotContain(nameof(DockSettings.CastsIconShadows), names);
        Assert.DoesNotContain(nameof(DockSettings.CastsBarShadow), names);
    }

    /// <summary>
    /// The dock applies the blur and the opacity as they are in force, never as they are stored.
    /// </summary>
    /// <remarks>
    /// Read from the source, because that is where it would go wrong: one new line in
    /// <c>DockWindow</c> reading <c>settings.BlurBackground</c> compiles, works on every machine
    /// with a graphics card, and brings the acrylic sheet back on the ones this setting is for.
    /// </remarks>
    [Fact]
    public void TheDockWindow_ReadsTheBlurAndTheOpacityInForce_NotTheStoredOnes()
    {
        var source = File.ReadAllText(Path.Combine(FindSourceRoot(), "Views", "DockWindow.xaml.cs"));

        Assert.DoesNotContain("." + nameof(DockSettings.BlurBackground), source);
        Assert.DoesNotContain("." + nameof(DockSettings.BarOpacity), source);

        // The dock's own property has the stored one's name, so it is the settings' that is
        // looked for: by either of the two names the window has them under.
        Assert.DoesNotContain("settings." + nameof(DockSettings.IconShadows), source);
        Assert.DoesNotContain("_applied." + nameof(DockSettings.IconShadows), source);

        Assert.Contains("." + nameof(DockSettings.Blurs), source);
        Assert.Contains("." + nameof(DockSettings.BarAlpha), source);
        Assert.Contains("." + nameof(DockSettings.HandleInverts), source);
        Assert.Contains("." + nameof(DockSettings.CastsIconShadows), source);
        Assert.Contains("." + nameof(DockSettings.CastsBarShadow), source);

        // And it tells the settings what Windows' transparency effects are before reading them.
        Assert.Contains("." + nameof(DockSettings.WindowsTransparency) + " = TransparencyEffects.Enabled", source);
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
