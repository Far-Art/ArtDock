using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers export and import, and the format stamp they carry.
/// </summary>
/// <remarks>
/// The import path deliberately throws where <see cref="SettingsStore.Load"/> falls back to
/// defaults: startup must survive a bad file, but an import the user just asked for must not
/// quietly do nothing. Both halves of that are worth pinning down, because "imported
/// successfully" over a set of defaults is indistinguishable from a reset.
/// </remarks>
public class SettingsPortabilityTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ArtDockTests", Guid.NewGuid().ToString("n"));

    public SettingsPortabilityTests() => Directory.CreateDirectory(_dir);

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

    private string Write(string name, string json)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void AFreshSettingsObject_CarriesTheCurrentFormat()
    {
        Assert.Equal(DockSettings.CurrentVersion, new DockSettings().Version);
    }

    [Fact]
    public void ExportThenImport_BringsBackEverything()
    {
        var original = new DockSettings
        {
            BaseSize = 64,
            MaxScale = 1.9,
            BarColor = "#123456",
            BarOpacity = 0.5,
            AutoHide = true,
            HideDelayMs = 900,
            BottomMargin = 21,
            OffsetAlongEdge = -0.35,
            LabelFontFamily = "Georgia",
            LabelFontSize = 18,
            LabelFontStyle = "Italic",
            CustomColors = ["#AABBCC", "#DDEEFF"],
            NoRevealApps = [@"C:\Games\Strategy\strategy.exe"],
            Hotkeys = { ["ShowHide"] = "", ["Place2"] = "Win+Ctrl+Alt+2" },
            PinnedApps =
            [
                new PinnedAppSetting { Id = "a", Label = "Editor", TargetPath = @"C:\App.exe" },
                new PinnedAppSetting { Id = "b", IsSeparator = true }
            ]
        };

        var path = Path.Combine(_dir, "export.json");
        SettingsStore.Export(original, path);
        var back = SettingsStore.Import(path);

        Assert.Equal(original.BaseSize, back.BaseSize);
        Assert.Equal(original.MaxScale, back.MaxScale);
        Assert.Equal(original.BarColor, back.BarColor);
        Assert.Equal(original.BarOpacity, back.BarOpacity);
        Assert.Equal(original.AutoHide, back.AutoHide);
        Assert.Equal(original.HideDelayMs, back.HideDelayMs);

        // The one setting with no control on any page. It rides along in the file, so an
        // import has to bring it back or a hand-tuned margin is lost by round-tripping.
        Assert.Equal(original.BottomMargin, back.BottomMargin);
        Assert.Equal(original.OffsetAlongEdge, back.OffsetAlongEdge);

        Assert.Equal(original.LabelFontFamily, back.LabelFontFamily);
        Assert.Equal(original.LabelFontSize, back.LabelFontSize);
        Assert.Equal(original.LabelFontStyle, back.LabelFontStyle);

        Assert.Equal(original.CustomColors, back.CustomColors);
        Assert.Equal(original.NoRevealApps, back.NoRevealApps);
        Assert.Equal(original.Hotkeys, back.Hotkeys);
        Assert.Equal(2, back.PinnedApps.Count);
        Assert.Equal("Editor", back.PinnedApps[0].Label);
        Assert.Equal(@"C:\App.exe", back.PinnedApps[0].TargetPath);
        Assert.True(back.PinnedApps[1].IsSeparator);
    }

    [Fact]
    public void AnExportedFile_LeadsWithTheFormatVersion()
    {
        var path = Path.Combine(_dir, "stamped.json");
        SettingsStore.Export(new DockSettings(), path);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var first = document.RootElement.EnumerateObject().First();

        Assert.Equal(nameof(DockSettings.Version), first.Name);
        Assert.Equal(DockSettings.CurrentVersion, first.Value.GetInt32());
    }

    [Fact]
    public void AFileFromBeforeTheVersionField_IsTakenForTheFirstFormat_AndBroughtUpToDate()
    {
        // A file with no version predates the field, and its contents are exactly the format
        // the field was introduced alongside: format 1. Left to the property's initialiser it
        // would read as the current format, and no migration would ever touch it — so the
        // stock bar of its day, which format 2 moves, is what shows that it was taken for 1.
        var path = Write("legacy.json", """
            { "BaseSize": 44, "AutoHide": true, "BarColor": "#EEF1FF", "BarOpacity": 0.76 }
            """);

        var loaded = SettingsStore.Import(path);

        Assert.Equal(DockSettings.CurrentVersion, loaded.Version);
        Assert.Equal(44, loaded.BaseSize);
        Assert.True(loaded.AutoHide);
        Assert.Equal(DockSettings.DefaultBarColor, loaded.BarColor);
    }

    [Fact]
    public void AFileFromANewerFormat_IsRefusedRatherThanGuessedAt()
    {
        var path = Write("future.json", $$"""
            { "Version": {{DockSettings.CurrentVersion + 1}}, "BaseSize": 44 }
            """);

        var error = Assert.Throws<InvalidDataException>(() => SettingsStore.Import(path));
        Assert.Contains("newer", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JsonThatIsNotSettings_IsRefusedRatherThanImportedAsDefaults()
    {
        // This is the case that matters: it parses, and every property falls back to its
        // initialiser, so without a check it would import as a silent reset to defaults.
        var path = Write("other.json", """{ "name": "something else", "count": 3 }""");

        Assert.Throws<InvalidDataException>(() => SettingsStore.Import(path));
    }

    [Fact]
    public void JsonThatIsNotAnObject_IsRefused()
    {
        Assert.Throws<InvalidDataException>(
            () => SettingsStore.Import(Write("array.json", "[1, 2, 3]")));
    }

    [Fact]
    public void SomethingThatIsNotJsonAtAll_IsRefused()
    {
        // ThrowsAny, because the reader raises the JsonReaderException subclass. What the
        // dialog catches is JsonException, and an `is` pattern matches the derived type.
        Assert.ThrowsAny<JsonException>(
            () => SettingsStore.Import(Write("notes.txt", "just some text")));
    }

    [Fact]
    public void CloningTheSettings_KeepsBothItemLocks()
    {
        // Not an idle check on a copy constructor. DockWindow.ContentsToSave clones the
        // stored settings and saves the result, so a lock that Clone dropped would be
        // switched off by the next reorder or drop — silently, and by the very gestures the
        // locks exist to govern.
        var locked = new DockSettings { LockItemOrder = true, LockItemContents = true };

        var copy = locked.Clone();

        Assert.True(copy.LockItemOrder);
        Assert.True(copy.LockItemContents);
    }

    /// <summary>
    /// The general case of the one above, by reflection over the file format itself.
    /// </summary>
    /// <remarks>
    /// Every stored property is given a value other than its default, so one that
    /// <see cref="DockSettings.Clone"/> forgets comes back different. The stakes are the same
    /// for all of them: <c>DockWindow.ContentsToSave</c> saves a clone after a reorder or a
    /// drop, and a cancelled settings dialog saves one to put the dock back — so a property
    /// missing from it is reset on disk by an unrelated gesture, or by pressing Cancel. Done
    /// by reflection so that a setting added later is covered without anyone having to
    /// remember this test exists.
    /// </remarks>
    [Fact]
    public void CloningTheSettings_KeepsEverySetting()
    {
        var original = new DockSettings();
        FillStoredProperties(original);

        Assert.Equivalent(original, original.Clone(), strict: true);
    }

    /// <summary>Gives every stored property of an object a value other than the one it has.</summary>
    private static void FillStoredProperties(object target)
    {
        foreach (var property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.IsDefined(typeof(JsonIgnoreAttribute)))
            {
                continue;
            }

            property.SetValue(target, SampleUnlike(property.PropertyType, property.GetValue(target)));
        }
    }

    private static object SampleUnlike(Type type, object? current)
    {
        if (type == typeof(bool))
        {
            return !(bool)current!;
        }

        if (type == typeof(int))
        {
            return (int)current! + 3;
        }

        if (type == typeof(double) || type == typeof(double?))
        {
            return (current as double? ?? 0) + 0.375;
        }

        if (type == typeof(string))
        {
            return (current as string ?? string.Empty) + "~";
        }

        if (type == typeof(List<string>))
        {
            return new List<string> { "#010203" };
        }

        if (type == typeof(List<int>))
        {
            return new List<int> { 4, 7 };
        }

        if (type == typeof(Dictionary<string, string>))
        {
            return new Dictionary<string, string> { ["Keyboard"] = "Win+Ctrl+Q", ["ShowHide"] = "" };
        }

        if (type == typeof(List<PinnedAppSetting>))
        {
            var pin = new PinnedAppSetting();
            FillStoredProperties(pin);
            return new List<PinnedAppSetting> { pin };
        }

        throw new InvalidOperationException(
            $"No sample for a setting of type {type.Name}: teach {nameof(SampleUnlike)} one.");
    }

    [Fact]
    public void PinsWithNoIdOrTheSameOne_EachComeInWithTheirOwn()
    {
        // A file written by hand. The menu's Remove and Edit find their pin by id, so two
        // pins that share one — the two with none share "" — would be removed together and
        // edited as the first.
        var path = Write("hand.json", """
            { "PinnedApps": [
                { "Label": "No id" },
                { "Id": "", "Label": "Blank" },
                { "Id": "same", "Label": "First" },
                { "Id": "same", "Label": "Second" },
                { "Label": "Also no id" }
            ] }
            """);

        var pins = SettingsStore.Import(path).PinnedApps;

        Assert.All(pins, pin => Assert.False(string.IsNullOrWhiteSpace(pin.Id)));
        Assert.Equal(pins.Count, pins.Select(pin => pin.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("same", pins[2].Id);
    }

    [Fact]
    public void PinsWithIdsOfTheirOwn_KeepThem()
    {
        var settings = new DockSettings
        {
            PinnedApps =
            [
                new PinnedAppSetting { Id = "a", Label = "Editor" },
                new PinnedAppSetting { Id = "b", IsSeparator = true }
            ]
        };

        Assert.False(settings.RepairPinIds());
        Assert.Equal(["a", "b"], settings.PinnedApps.Select(pin => pin.Id));
    }

    [Fact]
    public void AFileFromWhenEveryPinCarriedTheLettering_GivesItToTheDock()
    {
        // Until 2026-10-01 the item editor wrote the labels' lettering to every pin, and a pin
        // added from the dock after that came in with none. That one is first here, so taking
        // the first pin's would have put the labels back to the default.
        var path = Write("lettering.json", """
            { "PinnedApps": [
                { "Id": "new", "Label": "Added later" },
                { "Id": "a", "Label": "Editor", "FontFamily": "Georgia", "FontSize": 18, "FontStyle": "Italic" },
                { "Id": "b", "Label": "Browser", "FontFamily": "Georgia", "FontSize": 18, "FontStyle": "Italic" }
            ] }
            """);

        var loaded = SettingsStore.Import(path);

        Assert.Equal("Georgia", loaded.LabelFontFamily);
        Assert.Equal(18, loaded.LabelFontSize);
        Assert.Equal("Italic", loaded.LabelFontStyle);
        Assert.All(loaded.PinnedApps, pin =>
        {
            Assert.Null(pin.FontFamily);
            Assert.Null(pin.FontSize);
            Assert.Null(pin.FontStyle);
        });
    }

    [Fact]
    public void TheDocksOwnLettering_OutranksWhatAPinStillCarries()
    {
        // Only a file written by hand has both. What it says at the dock's level is the newer
        // word, all of it: the pin's emphasis does not fill in the one the dock left alone.
        var path = Write("both.json", """
            { "LabelFontFamily": "Bahnschrift", "PinnedApps": [
                { "Id": "a", "Label": "Editor", "FontFamily": "Georgia", "FontStyle": "Italic" }
            ] }
            """);

        var loaded = SettingsStore.Import(path);

        Assert.Equal("Bahnschrift", loaded.LabelFontFamily);
        Assert.Null(loaded.LabelFontStyle);
        Assert.Null(loaded.PinnedApps[0].FontFamily);
        Assert.Null(loaded.PinnedApps[0].FontStyle);
    }

    [Fact]
    public void AWrittenFile_KeepsTheLetteringOnTheDock_AndNoneOnThePins()
    {
        // What makes the move stick: a file written now leaves nothing on its pins for the next
        // read to take up again over a lettering chosen since.
        var settings = new DockSettings
        {
            LabelFontFamily = "Georgia",
            LabelFontSize = 18,
            LabelFontStyle = "Regular",
            PinnedApps = [new PinnedAppSetting { Id = "a", Label = "Editor" }]
        };

        var path = Path.Combine(_dir, "written.json");
        SettingsStore.Export(settings, path);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var pin = document.RootElement.GetProperty("PinnedApps")[0];

        Assert.False(pin.TryGetProperty(nameof(PinnedAppSetting.FontFamily), out _));
        Assert.False(pin.TryGetProperty(nameof(PinnedAppSetting.FontSize), out _));
        Assert.False(pin.TryGetProperty(nameof(PinnedAppSetting.FontStyle), out _));
        Assert.Equal("Georgia", document.RootElement.GetProperty(nameof(DockSettings.LabelFontFamily)).GetString());
    }

    [Fact]
    public void AnUnrecognisedProperty_IsIgnoredRatherThanRefused()
    {
        // Adding a setting must not need a version bump, which means a file written by a
        // later build has to stay readable as long as it does not claim a newer format.
        var path = Write("extra.json", """{ "BaseSize": 44, "SomethingAddedLater": 7 }""");

        Assert.Equal(44, SettingsStore.Import(path).BaseSize);
    }
}
