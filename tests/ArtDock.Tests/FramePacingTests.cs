using System.IO;
using ArtDock.Dock;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers the Performance setting's frame cap: an even whole number of the display's frames
/// each time (the first try came out at 55 a second, two frames and three by turns), and the
/// setting's stops and how they are kept in the file.
/// </summary>
public class FramePacingTests
{
    private const double At120 = 1000.0 / 120;
    private const double At144 = 1000.0 / 144;
    private const double At60 = 1000.0 / 60;

    /// <summary>Which of <paramref name="frames"/> display frames are worked on, as frame numbers.</summary>
    private static List<int> Taken(double refreshMs, int maxFrameRate, int frames, double jitterMs = 0)
    {
        var interval = FramePacing.IntervalMs(refreshMs, maxFrameRate);
        var taken = new List<int> { 0 };
        var last = 0.0;
        for (var i = 1; i < frames; i++)
        {
            // WPF stamps a frame a little early or late; alternate the error to be unkind.
            var time = (i * refreshMs) + (i % 2 == 0 ? jitterMs : -jitterMs);
            if (!FramePacing.Skips(time - last, interval, refreshMs))
            {
                taken.Add(i);
                last = time;
            }
        }

        return taken;
    }

    [Theory]
    [InlineData(At120, 60, 2)]
    [InlineData(At120, 30, 4)]
    [InlineData(At60, 30, 2)]
    [InlineData(At144, 60, 2)]
    [InlineData(At144, 30, 5)]
    public void It_takes_every_nth_frame_evenly(double refreshMs, int cap, int every)
    {
        foreach (var jitter in new[] { 0, 0.5, 1.5 })
        {
            var gaps = Taken(refreshMs, cap, 241, jitter).Pairwise().ToList();
            Assert.All(gaps, gap => Assert.Equal(every, gap));
        }
    }

    [Theory]
    [InlineData(At120, 0)]
    [InlineData(At60, 60)]
    [InlineData(At120, 120)]
    [InlineData(At120, 1000)]
    public void A_cap_at_or_above_the_display_takes_every_frame(double refreshMs, int cap)
    {
        Assert.Equal(0, FramePacing.IntervalMs(refreshMs, cap));
        Assert.Equal(Enumerable.Range(0, 60), Taken(refreshMs, cap, 60));
    }

    [Fact]
    public void A_display_rate_that_cannot_be_read_takes_every_frame()
    {
        Assert.Equal(0, FramePacing.IntervalMs(0, 30));
        Assert.Equal(0, FramePacing.IntervalMs(double.NaN, 30));
    }

    [Theory]
    [InlineData(DockSettings.PerformanceFull, 0)]
    [InlineData(DockSettings.PerformanceBalanced, 60)]
    [InlineData(DockSettings.PerformanceLight, 30)]
    [InlineData(0, 30)]
    [InlineData(-5, 30)]
    [InlineData(9, 0)]
    public void Each_stop_has_its_frame_rate_and_a_hand_edited_value_the_nearest(int performance, int expected) =>
        Assert.Equal(expected, new DockSettings { Performance = performance }.MaxFrameRate);

    [Fact]
    public void A_new_dock_and_a_file_from_before_the_slider_run_at_the_full_rate()
    {
        Assert.Equal(DockSettings.PerformanceFull, new DockSettings().Performance);

        var path = Path.Combine(Path.GetTempPath(), $"artdock-before-slider-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "Version": 2, "ReduceMotion": true }""");
            var old = SettingsStore.Import(path);

            // The slider's lowest stop, Minimal, which takes No GPU with it now; and the level
            // it comes back to above that is the full rate.
            Assert.True(old.Minimal);
            Assert.Equal(DockSettings.PerformanceFull, old.Performance);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("""{ "Version": 2, "NoGpu": true }""")]
    [InlineData("""{ "Version": 2, "ReduceMotion": true, "NoGpu": true }""")]
    public void Either_half_of_minimal_in_a_file_is_read_as_minimal(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"artdock-minimal-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            var loaded = SettingsStore.Import(path);

            Assert.True(loaded.ReduceMotion);
            Assert.True(loaded.NoGpu);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Minimal_is_both_halves_and_neither_is_left_alone()
    {
        Assert.False(new DockSettings().Minimal);
        Assert.False(new DockSettings().JoinMinimal());

        var settings = new DockSettings { NoGpu = true };
        Assert.True(settings.JoinMinimal());
        Assert.True(settings.Minimal);
    }

    [Fact]
    public void A_first_run_with_nothing_to_draw_with_starts_at_minimal()
    {
        var settings = new DockSettings();
        Assert.True(settings.AdoptNoGpu(firstRun: true, hardwareMissing: true));
        Assert.True(settings.Minimal);
    }

    [Theory]
    [InlineData(DockSettings.PerformanceFull, true)]
    [InlineData(DockSettings.PerformanceBalanced, true)]
    [InlineData(DockSettings.PerformanceLight, false)]
    public void Light_goes_without_the_icons_shadows_and_keeps_the_setting(int performance, bool casts)
    {
        var settings = new DockSettings { IconShadows = true, Performance = performance };
        Assert.Equal(casts, settings.CastsIconShadows);
        Assert.True(settings.IconShadows);
    }

    [Fact]
    public void The_level_is_written_and_copied_but_the_frame_rate_is_not_written()
    {
        var settings = new DockSettings { Performance = DockSettings.PerformanceLight };
        var path = Path.Combine(Path.GetTempPath(), $"artdock-performance-{Guid.NewGuid():N}.json");
        try
        {
            SettingsStore.Export(settings, path);
            var json = File.ReadAllText(path);

            Assert.Contains("\"Performance\": 1", json);
            Assert.DoesNotContain("MaxFrameRate", json);
            Assert.Equal(DockSettings.PerformanceLight, SettingsStore.Import(path).Performance);
            Assert.Equal(DockSettings.PerformanceLight, settings.Clone().Performance);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

internal static class PairwiseExtensions
{
    public static IEnumerable<int> Pairwise(this List<int> values) =>
        values.Zip(values.Skip(1), (a, b) => b - a);
}
