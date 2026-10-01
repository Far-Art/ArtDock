using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Media;
using ArtDock.Controls;
using ArtDock.Dock;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Holds the dock to painting what its settings say from its first frame, and holds the move
/// of the stock bar that making it do so needed.
/// </summary>
/// <remarks>
/// <para>
/// Until 2026-10-01 a dock's first fill was written out by itself — <c>#CCD2FF</c> at a half —
/// beside fields that recorded the stock <c>#EEF1FF</c> at 0.76, and setting the bar to what it
/// was already recorded as holding changed nothing. So every dock on the stock settings painted
/// the first at every start, and turned to the second, a good deal lighter, the first time its
/// colour or opacity was touched in the settings dialog — even in a preview that was then
/// cancelled, since putting the stock values back changed nothing either.
/// </para>
/// <para>
/// The look everyone had was kept. The stock values became the ones that were on the screen,
/// and a file of the older format still holding the old ones is moved with them.
/// </para>
/// </remarks>
public class BarStockLookTests : IDisposable
{
    private const string FormerColor = "#EEF1FF";
    private const double FormerOpacity = 0.76;

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ArtDockTests", Guid.NewGuid().ToString("n"));

    public BarStockLookTests() => Directory.CreateDirectory(_dir);

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

    private DockSettings Read(string json)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("n") + ".json");
        File.WriteAllText(path, json);
        return SettingsStore.Import(path);
    }

    private static Color Painted(Color color, double opacity) =>
        Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B);

    [Fact]
    public void ADockJustMade_PaintsTheStockBar() => OnStaThread(() =>
    {
        var bar = new DockBar(new DockMetrics());

        Assert.Equal(Painted(BarPalette.Default, BarPalette.DefaultOpacity), bar.BarFill);
    });

    /// <summary>The one that was wrong: told the stock values, a new dock painted something else.</summary>
    [Fact]
    public void ADockToldTheStockSettings_PaintsThem() => OnStaThread(() =>
    {
        var stock = new DockSettings();
        var bar = new DockBar(new DockMetrics());

        bar.SetBarAppearance(stock.BarColor, stock.BarOpacity);

        Assert.Equal(Painted(BarPalette.Parse(stock.BarColor), stock.BarOpacity), bar.BarFill);
    });

    [Fact]
    public void ADockPaintsWhatItIsTold_AndGoesBackToTheStockBar() => OnStaThread(() =>
    {
        var stock = new DockSettings();
        var bar = new DockBar(new DockMetrics());

        bar.SetBarAppearance("#102030", 0.9);
        Assert.Equal(Painted(Color.FromRgb(0x10, 0x20, 0x30), 0.9), bar.BarFill);

        bar.SetBarAppearance(stock.BarColor, stock.BarOpacity);
        Assert.Equal(Painted(BarPalette.Default, BarPalette.DefaultOpacity), bar.BarFill);
    });

    [Fact]
    public void TheStockColour_IsOneColour_WhereverItIsWritten()
    {
        Assert.Equal(DockSettings.DefaultBarColor, BarPalette.ToHex(BarPalette.Default));
        Assert.Equal(BarPalette.DefaultOpacity, new DockSettings().BarOpacity);
    }

    [Fact]
    public void AFileOfTheOldFormat_OnTheOldStockBar_IsMovedToTheLookItHad()
    {
        var loaded = Read($$"""{ "Version": 1, "BarColor": "{{FormerColor}}", "BarOpacity": {{FormerOpacity}} }""");

        Assert.Equal(DockSettings.CurrentVersion, loaded.Version);
        Assert.Equal(DockSettings.DefaultBarColor, loaded.BarColor);
        Assert.Equal(BarPalette.DefaultOpacity, loaded.BarOpacity);
    }

    /// <summary>
    /// What the version is for: the same two values in a file of this format were chosen with
    /// the dock showing them truly, and are somebody's bar.
    /// </summary>
    [Fact]
    public void AFileOfThisFormat_HoldingTheOldStockBar_IsLeftAsChosen()
    {
        var loaded = Read($$"""
            { "Version": {{DockSettings.CurrentVersion}}, "BarColor": "{{FormerColor}}", "BarOpacity": {{FormerOpacity}} }
            """);

        Assert.Equal(FormerColor, loaded.BarColor);
        Assert.Equal(FormerOpacity, loaded.BarOpacity);
    }

    /// <summary>
    /// Only the pair that was never painted is moved. A colour or an opacity of the user's own
    /// differed from the dock's record of itself, and so was painted as asked all along.
    /// </summary>
    [Theory]
    [InlineData("#EEF1FF", 0.8)]
    [InlineData("#102030", 0.76)]
    [InlineData("#102030", 0.4)]
    public void AFileOfTheOldFormat_WithABarOfItsOwn_IsLeft(string color, double opacity)
    {
        var loaded = Read($$"""{ "Version": 1, "BarColor": "{{color}}", "BarOpacity": {{opacity}} }""");

        Assert.Equal(DockSettings.CurrentVersion, loaded.Version);
        Assert.Equal(color, loaded.BarColor);
        Assert.Equal(opacity, loaded.BarOpacity);
    }

    /// <summary>
    /// A bar that takes the taskbar's colour was painted in it, at the opacity stored: the stock
    /// values underneath are left for the day the box is unticked.
    /// </summary>
    [Fact]
    public void AFileOfTheOldFormat_MatchingTheTaskbar_KeepsItsOpacity()
    {
        var loaded = Read($$"""
            { "Version": 1, "UseTaskbarColor": true, "BarColor": "{{FormerColor}}", "BarOpacity": {{FormerOpacity}} }
            """);

        Assert.Equal(FormerColor, loaded.BarColor);
        Assert.Equal(FormerOpacity, loaded.BarOpacity);
    }

    [Fact]
    public void MigratingTwice_ChangesNothingTheSecondTime()
    {
        var settings = new DockSettings { Version = 1, BarColor = FormerColor, BarOpacity = FormerOpacity };

        Assert.True(settings.Migrate());
        Assert.False(settings.Migrate());

        // And setting the old pair by hand afterwards is a choice, which a second look leaves.
        settings.BarColor = FormerColor;
        settings.BarOpacity = FormerOpacity;

        Assert.False(settings.Migrate());
        Assert.Equal(FormerColor, settings.BarColor);
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
