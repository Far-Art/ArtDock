using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using ArtDock.Controls;
using ArtDock.Dock;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers how strong the bar's shadow is (<see cref="DockSettings.ShadowStrength"/>, the
/// Appearance page's <em>Shadow</em>): what a dock starts with, what reaches the bar, and that
/// No GPU sets it aside without rewriting it.
/// </summary>
public class ShadowStrengthTests
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

    [Fact]
    public void ANewDock_StartsAtTheDefault()
    {
        Assert.Equal(BarShadow.DefaultStrength, new DockSettings().ShadowStrength);
        Assert.Equal(BarShadow.DefaultStrength, new DockSettings().BarShadowStrength);
    }

    /// <summary>
    /// A file written before the setting existed has nothing to say about it, and reads as the
    /// default: a dock already set up gains the new shadow with the update that brings it.
    /// </summary>
    [Fact]
    public void AFileFromBeforeTheSetting_ReadsAsTheDefault()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ArtDockTests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "before.json");
            File.WriteAllText(path, """{ "BaseSize": 44, "AutoHide": true }""");
            Assert.Equal(BarShadow.DefaultStrength, SettingsStore.Import(path).ShadowStrength);

            // And what is set is what is written, and read back.
            var saved = Path.Combine(dir, "saved.json");
            SettingsStore.Export(new DockSettings { ShadowStrength = 0.8 }, saved);
            Assert.Equal(0.8, SettingsStore.Import(saved).ShadowStrength);

            using var document = JsonDocument.Parse(File.ReadAllText(saved));
            var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToList();
            Assert.Contains(nameof(DockSettings.ShadowStrength), names);
            Assert.DoesNotContain(nameof(DockSettings.BarShadowStrength), names);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Full strength is the strongest chosen on the dock, not the whole of the layout; the
    /// default looks as it did before the cap, at about half of the layout.
    /// </summary>
    [Fact]
    public void FullStrength_IsTheCap_AndTheDefaultIsAboutHalfTheLayout()
    {
        Assert.Equal(BarShadow.Strongest, BarShadow.Opacity(1), 9);
        Assert.Equal(0, BarShadow.Opacity(0), 9);
        Assert.Equal(BarShadow.Strongest, BarShadow.Opacity(2), 9);
        Assert.InRange(BarShadow.Opacity(BarShadow.DefaultStrength), 0.48, 0.52);
    }

    [Fact]
    public void ACopy_KeepsTheStrength() =>
        Assert.Equal(0.3, new DockSettings { ShadowStrength = 0.3 }.Clone().ShadowStrength);

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.25, 0.25)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.7, 1.0)]
    [InlineData(-0.4, 0.0)]
    [InlineData(double.NaN, BarShadow.DefaultStrength)]
    public void WhatReachesTheBar_IsKeptToNoneThroughFull(double stored, double drawn) =>
        Assert.Equal(drawn, new DockSettings { ShadowStrength = stored }.BarShadowStrength);

    /// <summary>
    /// No GPU draws no shadow at all, whatever the strength — and keeps the strength underneath,
    /// for when it is turned off.
    /// </summary>
    [Fact]
    public void UnderNoGpu_NoShadowIsDrawn_AndTheStrengthIsKept()
    {
        var settings = new DockSettings { NoGpu = true, ShadowStrength = 0.9 };
        Assert.Equal(0, settings.BarShadowStrength);
        Assert.Equal(0.9, settings.ShadowStrength);

        settings.NoGpu = false;
        Assert.Equal(0.9, settings.BarShadowStrength);
    }

    [Fact]
    public void TheBar_KeepsItsStrengthToNoneThroughFull() => OnStaThread(() =>
    {
        var bar = new DockBar(new DockMetrics { BaseSize = 50, MaxSize = 70, Gap = 8 });
        Assert.Equal(BarShadow.DefaultStrength, bar.ShadowStrength);

        bar.ShadowStrength = 3;
        Assert.Equal(1, bar.ShadowStrength);

        bar.ShadowStrength = -1;
        Assert.Equal(0, bar.ShadowStrength);

        bar.ShadowStrength = double.NaN;
        Assert.Equal(BarShadow.DefaultStrength, bar.ShadowStrength);
    });
}
