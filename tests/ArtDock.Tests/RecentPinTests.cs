using System.IO;
using System.Windows;
using ArtDock.Services;
using ArtDock.Views;

namespace ArtDock.Tests;

/// <summary>
/// Covers the Items page's <em>New</em> mark: a pin counts as recently added for a day after it
/// was put on the dock, and the stamp it is told by survives everything a pin goes through.
/// </summary>
public class RecentPinTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 18, 0, 0, TimeSpan.Zero);

    private static PinnedAppSetting Added(TimeSpan ago) =>
        new() { Id = "p", Label = "P", TargetPath = @"C:\p.exe", AddedAt = Now - ago };

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"artdock-recent-{Guid.NewGuid():N}.json");

    [Fact]
    public void APinWithNoStamp_IsNotRecent()
    {
        // Every pin from before the stamp was kept, and the set a new dock starts with.
        Assert.False(new PinnedAppSetting { Id = "p", Label = "P" }.IsRecent(Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(23 * 60)]
    [InlineData((24 * 60) - 1)]
    public void WithinADay_IsRecent(int minutesAgo)
    {
        Assert.True(Added(TimeSpan.FromMinutes(minutesAgo)).IsRecent(Now));
    }

    [Theory]
    [InlineData(24 * 60)]
    [InlineData(25 * 60)]
    [InlineData(30 * 24 * 60)]
    public void ADayOrMore_IsNot(int minutesAgo)
    {
        Assert.False(Added(TimeSpan.FromMinutes(minutesAgo)).IsRecent(Now));
    }

    [Fact]
    public void AStampAheadOfTheClock_IsNotRecent()
    {
        // A clock put back would otherwise leave the pin new until it caught up.
        Assert.False(Added(TimeSpan.FromHours(-1)).IsRecent(Now));
        Assert.True(Added(TimeSpan.FromSeconds(-30)).IsRecent(Now));
    }

    [Fact]
    public void ASeparator_IsNeverMarked()
    {
        var separator = new PinnedAppSetting { Id = "s", Label = "", IsSeparator = true, AddedAt = Now };
        Assert.False(separator.IsRecent(Now));
    }

    [Fact]
    public void TheStamp_SurvivesACloneAndTheFile()
    {
        var settings = new DockSettings { PinnedApps = [Added(TimeSpan.FromHours(2))] };

        Assert.Equal(Now - TimeSpan.FromHours(2), settings.Clone().PinnedApps[0].AddedAt);

        var path = TempFile();
        SettingsStore.Export(settings, path);
        Assert.Equal(Now - TimeSpan.FromHours(2), SettingsStore.Import(path).PinnedApps[0].AddedAt);
    }

    [Fact]
    public void AFileFromBeforeTheStamp_ReadsAsNotAdded()
    {
        const string json = """{ "PinnedApps": [ { "Id": "a", "Label": "A", "TargetPath": "C:\\a.exe" } ] }""";
        var path = TempFile();
        File.WriteAllText(path, json);

        Assert.Null(SettingsStore.Import(path).PinnedApps[0].AddedAt);
    }

    [Fact]
    public void TheStarterSet_CarriesNoStamp()
    {
        Assert.All(DockPresets.CreateDefaults(), pin => Assert.Null(pin.AddedAt));
    }

    [Fact]
    public void TheRowMark_ShowsForARecentPinOnly()
    {
        var mark = new RecentRowMarkConverter();
        var fresh = new PinnedAppSetting { Id = "f", Label = "F", AddedAt = DateTimeOffset.UtcNow };
        var old = new PinnedAppSetting { Id = "o", Label = "O", AddedAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(2) };

        Assert.Equal(Visibility.Visible, mark.Convert(fresh, typeof(Visibility), null, null!));
        Assert.Equal(Visibility.Collapsed, mark.Convert(old, typeof(Visibility), null, null!));
        Assert.Equal(Visibility.Collapsed, mark.Convert(null, typeof(Visibility), null, null!));

        var status = new RecentRowMarkConverter { AsStatus = true };
        Assert.NotEqual(string.Empty, status.Convert(fresh, typeof(string), null, null!));
        Assert.Equal(string.Empty, status.Convert(old, typeof(string), null, null!));
    }
}
