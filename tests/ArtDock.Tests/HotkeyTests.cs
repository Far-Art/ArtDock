using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ArtDock.Interop;
using ArtDock.Localization;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers the hotkeys: how one is written and read, which combinations cannot be one, how what a
/// settings file holds becomes the hotkeys in force — absent and empty kept apart — and that
/// Windows is asked for them, and told to let them go, when it should be.
/// </summary>
/// <remarks>
/// The registrations are real, made on the test's own thread with no window, as the probe that
/// chose the defaults made them: a combination registered and let go of at once, with nothing
/// typed and nothing shown. Every modifier and F23 or F24, which no keyboard here has, so nothing
/// else has registered it and nobody can press it meanwhile.
/// </remarks>
public class HotkeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ArtDockTests", Guid.NewGuid().ToString("n"));

    public HotkeyTests() => Directory.CreateDirectory(_dir);

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

    private const HotkeyModifiers WinCtrl = HotkeyModifiers.Win | HotkeyModifiers.Ctrl;

    private const HotkeyModifiers Every =
        HotkeyModifiers.Win | HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift;

    /// <summary>Every modifier and F24: nothing has it, and nobody can press it.</summary>
    private static readonly Hotkey Unused = new(Every, 0x87);

    /// <summary>The same with F23.</summary>
    private static readonly Hotkey AlsoUnused = new(Every, 0x86);

    private static Hotkey Parse(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey), text);
        return hotkey;
    }

    // ---- one hotkey --------------------------------------------------------------------

    [Theory]
    [InlineData("Win+Ctrl+A")]
    [InlineData("Ctrl+Alt+Shift+F5")]
    [InlineData("Win+Ctrl+Alt+1")]
    [InlineData("Win+NumPad7")]
    [InlineData("Alt+Shift+PageUp")]
    [InlineData("Win+Oem1")]
    [InlineData("Ctrl+0xB3")]
    public void AHotkey_ReadsBackAsItWasWritten(string text)
    {
        Assert.Equal(text, Parse(text).ToString());
    }

    [Fact]
    public void Reading_IgnoresCaseAndSpaces_AndWritesTheModifiersInWindowsOrder()
    {
        Assert.Equal("Win+Ctrl+Alt+Shift+A", Parse(" shift + alt+ctrl + WIN + a ").ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Win+")]
    [InlineData("Win+Ctrl")]
    [InlineData("Super+A")]
    [InlineData("Win+Ctrl+AB")]
    [InlineData("Win+F25")]
    [InlineData("Win+0x100")]
    public void Nonsense_IsNotAHotkey(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    /// <summary>
    /// By virtual key: the A of every layout here, whatever it types — and so the same physical
    /// key under the US, Hebrew and Russian layouts, which give it the same code.
    /// </summary>
    [Fact]
    public void AKey_IsStoredByItsCode()
    {
        Assert.Equal(new Hotkey(WinCtrl, 0x41), Parse("Win+Ctrl+A"));
        Assert.Equal(new Hotkey(WinCtrl, 0xBA), Parse("Win+Ctrl+Oem1"));
    }

    [Theory]
    [InlineData("A", HotkeyProblem.NeedsModifier)]
    [InlineData("Shift+A", HotkeyProblem.NeedsModifier)]
    [InlineData("F12", HotkeyProblem.Reserved)]
    [InlineData("Win+Ctrl+F12", HotkeyProblem.Reserved)]
    [InlineData("Alt+F4", HotkeyProblem.Windows)]
    [InlineData("Alt+Space", HotkeyProblem.Windows)]
    [InlineData("Alt+Shift+Tab", HotkeyProblem.Windows)]
    [InlineData("Alt+Esc", HotkeyProblem.Windows)]
    [InlineData("Ctrl+Esc", HotkeyProblem.Windows)]
    [InlineData("Win+Ctrl+A", HotkeyProblem.None)]
    [InlineData("Ctrl+Alt+F4", HotkeyProblem.None)]
    [InlineData("Win+Esc", HotkeyProblem.None)]
    [InlineData("Alt+Shift+D", HotkeyProblem.None)]
    public void WhatCannotBeAHotkey_SaysWhy(string text, HotkeyProblem problem)
    {
        Assert.Equal(problem, Parse(text).Problem);
    }

    [Theory]
    [InlineData(0x11)] // Ctrl
    [InlineData(0x5B)] // the left Windows key
    [InlineData(0xA4)] // the left Alt
    [InlineData(0x14)] // Caps Lock
    [InlineData(0x90)] // Num Lock
    [InlineData(0xE5)] // a key the IME has
    public void AModifierOrALockKey_IsNotAKeyToTake(int key)
    {
        Assert.Equal(HotkeyProblem.NotAKey, new Hotkey(WinCtrl, key).Problem);
    }

    [Fact]
    public void CtrlAndAlt_WithoutTheWindowsKey_IsAltGr()
    {
        Assert.True(Parse("Ctrl+Alt+X").IsAltGr);
        Assert.True(Parse("Ctrl+Alt+Shift+X").IsAltGr);
        Assert.False(Parse("Win+Ctrl+Alt+X").IsAltGr);
        Assert.False(Parse("Ctrl+Shift+X").IsAltGr);
    }

    // ---- what a person reads ------------------------------------------------------------

    [Fact]
    public void AHotkey_IsWrittenOutWithTheKeysNames()
    {
        Assert.Equal("Win + Ctrl + A", KeyNames.Describe(Parse("Win+Ctrl+A")));
        Assert.Equal("Ctrl + Alt + Page Up", KeyNames.Describe(Parse("Ctrl+Alt+PageUp")));
        Assert.Equal("Win + F5", KeyNames.Describe(Parse("Win+F5")));
        Assert.Equal("Win + Num 7", KeyNames.Describe(Parse("Win+NumPad7")));
        Assert.Equal("Win + Shift + 3", KeyNames.Describe(Parse("Win+Shift+3")));
    }

    [Fact]
    public void TheModifiersHeldSoFar_AreShownAsTheStartOfACombination()
    {
        Assert.Equal("Win + Ctrl + …", KeyNames.Held(WinCtrl));
    }

    // ---- what the settings hold -----------------------------------------------------------

    [Fact]
    public void ANewDock_HasTheDefaults_AndNoSecondKeys()
    {
        var settings = new DockSettings();

        Assert.Equal("Win+Ctrl+A", settings.HotkeyFor(HotkeyAction.Keyboard)?.ToString());
        Assert.Equal("Win+Ctrl+H", settings.HotkeyFor(HotkeyAction.ShowHide)?.ToString());
        Assert.Equal("Win+Ctrl+I", settings.HotkeyFor(HotkeyAction.Settings)?.ToString());
        Assert.Equal("Win+Ctrl+NumPad1", settings.HotkeyFor(HotkeyAction.Place1)?.ToString());
        Assert.Equal("Win+Ctrl+NumPad9", settings.HotkeyFor(HotkeyAction.Place9)?.ToString());
        Assert.All(
            HotkeyActions.All.Where(HotkeyActions.IsSecondary),
            action => Assert.Null(settings.HotkeyFor(action)));
    }

    /// <summary>
    /// One family for every default, so the keys are consistent — the Windows key and Ctrl, two
    /// modifiers side by side — and the places on the keypad, since Windows has the number row's
    /// digits with the Windows key and every comfortable set of modifiers.
    /// </summary>
    [Fact]
    public void EveryDefault_IsTheWindowsKeyAndCtrl_AndThePlacesAreOnTheKeypad()
    {
        Assert.All(
            HotkeyActions.All.Where(action => HotkeyActions.Default(action) is not null),
            action => Assert.Equal(WinCtrl, HotkeyActions.Default(action)?.Modifiers));
        Assert.All(
            HotkeyActions.All.Where(action => HotkeyActions.Place(action) > 0 && !HotkeyActions.IsSecondary(action)),
            action => Assert.Equal(0x60 + HotkeyActions.Place(action), HotkeyActions.Default(action)?.Key));
    }

    [Fact]
    public void APlacesSecondKey_OpensTheSamePlace()
    {
        Assert.Equal(3, HotkeyActions.Place(HotkeyAction.Place3));
        Assert.Equal(3, HotkeyActions.Place(HotkeyAction.Place3Secondary));
        Assert.Equal(0, HotkeyActions.Place(HotkeyAction.Settings));
        Assert.True(HotkeyActions.IsSecondary(HotkeyAction.Place3Secondary));
        Assert.False(HotkeyActions.IsSecondary(HotkeyAction.Place3));
    }

    /// <summary>
    /// The case the two have to be kept apart for: read as absent, a cleared hotkey would come
    /// back at every start.
    /// </summary>
    [Fact]
    public void AnEmptyHotkey_IsNone_AndNotTheDefault()
    {
        var settings = new DockSettings { Hotkeys = { ["Keyboard"] = "" } };

        Assert.Null(settings.HotkeyFor(HotkeyAction.Keyboard));
        Assert.Equal("Win+Ctrl+H", settings.HotkeyFor(HotkeyAction.ShowHide)?.ToString());
    }

    [Theory]
    [InlineData("A")]
    [InlineData("Alt+F4")]
    [InlineData("not a hotkey")]
    public void AHotkeyThatCannotBeOne_IsNone(string stored)
    {
        var settings = new DockSettings { Hotkeys = { ["ShowHide"] = stored } };

        Assert.Null(settings.HotkeyFor(HotkeyAction.ShowHide));
    }

    [Fact]
    public void ThroughTheFile_AClearedHotkeyStaysCleared_AndAnAbsentOneFollowsTheDefault()
    {
        var original = new DockSettings
        {
            Hotkeys = { ["Keyboard"] = "", ["Place3"] = "Win+Shift+F3" }
        };

        var path = Path.Combine(_dir, "hotkeys.json");
        SettingsStore.Export(original, path);
        var back = SettingsStore.Import(path);

        Assert.Null(back.HotkeyFor(HotkeyAction.Keyboard));
        Assert.Equal("Win+Ctrl+H", back.HotkeyFor(HotkeyAction.ShowHide)?.ToString());
        Assert.Equal("Win+Shift+F3", back.HotkeyFor(HotkeyAction.Place3)?.ToString());

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var hotkeys = document.RootElement.GetProperty(nameof(DockSettings.Hotkeys));
        Assert.Equal("", hotkeys.GetProperty("Keyboard").GetString());
        Assert.False(hotkeys.TryGetProperty("ShowHide", out _));
    }

    [Fact]
    public void AFileFromBeforeHotkeys_HasTheDefaults()
    {
        var path = Path.Combine(_dir, "before.json");
        File.WriteAllText(path, """{ "BaseSize": 44 }""");

        var loaded = SettingsStore.Import(path);

        Assert.Equal("Win+Ctrl+A", loaded.HotkeyFor(HotkeyAction.Keyboard)?.ToString());
        Assert.Equal("Win+Ctrl+H", loaded.HotkeyFor(HotkeyAction.ShowHide)?.ToString());
        Assert.Equal("Win+Ctrl+I", loaded.HotkeyFor(HotkeyAction.Settings)?.ToString());
        Assert.Equal("Win+Ctrl+NumPad5", loaded.HotkeyFor(HotkeyAction.Place5)?.ToString());
        Assert.Null(loaded.HotkeyFor(HotkeyAction.Place5Secondary));
    }

    /// <summary>
    /// The settings' hotkey came a day after the others, and the places' defaults with it, so a
    /// file saved in between has hotkeys and nothing under those names — which is absent, and has
    /// the default, as for any action added or given a default later.
    /// </summary>
    [Fact]
    public void AFileFromBeforeAnActionCame_HasItsDefault()
    {
        var path = Path.Combine(_dir, "between.json");
        File.WriteAllText(path, """{ "BaseSize": 44, "Hotkeys": { "Keyboard": "", "Place3": "Win+Shift+F3" } }""");

        var loaded = SettingsStore.Import(path);

        Assert.Equal("Win+Ctrl+I", loaded.HotkeyFor(HotkeyAction.Settings)?.ToString());
        Assert.Equal("Win+Ctrl+NumPad4", loaded.HotkeyFor(HotkeyAction.Place4)?.ToString());
        Assert.Null(loaded.HotkeyFor(HotkeyAction.Keyboard));
        Assert.Equal("Win+Shift+F3", loaded.HotkeyFor(HotkeyAction.Place3)?.ToString());
    }

    [Fact]
    public void AFileWithNoHotkeysAtAll_IsReadAsEmpty_NotAsNothing()
    {
        var path = Path.Combine(_dir, "null.json");
        File.WriteAllText(path, """{ "BaseSize": 44, "Hotkeys": null }""");

        var loaded = SettingsStore.Import(path);

        Assert.NotNull(loaded.Hotkeys);
        Assert.Equal("Win+Ctrl+A", loaded.HotkeyFor(HotkeyAction.Keyboard)?.ToString());
    }

    private static Dictionary<HotkeyAction, Hotkey?> Defaults() =>
        HotkeyActions.All.ToDictionary(action => action, HotkeyActions.Default);

    /// <summary>
    /// A default given keys another default has would do nothing on every new dock, and its row
    /// would say so.
    /// </summary>
    [Fact]
    public void TheDefaults_AreAllInForce()
    {
        var defaults = Defaults();

        Assert.Equal(defaults.Values.Count(value => value is not null), HotkeyActions.InForce(defaults).Count);
        Assert.All(HotkeyActions.All, action => Assert.Null(HotkeyActions.SharedWith(defaults, action)));
    }

    [Fact]
    public void Storing_LeavesOutWhatIsTheDefault_AndWritesNoneAsEmpty()
    {
        var choices = Defaults();
        choices[HotkeyAction.ShowHide] = null;
        choices[HotkeyAction.Place2] = Parse("Win+Shift+F2");

        var stored = HotkeyActions.Store(choices);

        Assert.Equal(2, stored.Count);
        Assert.Equal("", stored["ShowHide"]);
        Assert.Equal("Win+Shift+F2", stored["Place2"]);
    }

    [Fact]
    public void Storing_CarriesALaterVersionsActionsThrough_AndNotWhatWasStoredForThisOnes()
    {
        var stored = HotkeyActions.Store(
            Defaults(),
            new Dictionary<string, string> { ["SomethingLater"] = "Win+Ctrl+S", ["Keyboard"] = "Win+Ctrl+Q" });

        Assert.Equal("Win+Ctrl+S", stored["SomethingLater"]);
        Assert.False(stored.ContainsKey("Keyboard"));
    }

    /// <summary>
    /// Keys set on the page outrank a default — even one higher on the page — or the keys just
    /// set would do nothing.
    /// </summary>
    [Fact]
    public void KeysSetOnThePage_OutrankADefault()
    {
        var choices = Defaults();
        choices[HotkeyAction.Place5] = Parse("Win+Ctrl+NumPad1");

        var inForce = HotkeyActions.InForce(choices);

        Assert.Equal(Parse("Win+Ctrl+NumPad1"), inForce[HotkeyAction.Place5]);
        Assert.False(inForce.ContainsKey(HotkeyAction.Place1));
        Assert.Equal(HotkeyAction.Place5, HotkeyActions.SharedWith(choices, HotkeyAction.Place1));
        Assert.Null(HotkeyActions.SharedWith(choices, HotkeyAction.Place5));
    }

    /// <summary>Between two set on the page, the one higher on it has the keys — a first key before any second.</summary>
    [Fact]
    public void TwoGivenTheSameKeysOnThePage_TheEarlierHasThem()
    {
        var choices = Defaults();
        choices[HotkeyAction.Place1Secondary] = Parse("Win+Shift+F7");
        choices[HotkeyAction.Place3] = Parse("Win+Shift+F7");

        var inForce = HotkeyActions.InForce(choices);

        Assert.Equal(Parse("Win+Shift+F7"), inForce[HotkeyAction.Place3]);
        Assert.False(inForce.ContainsKey(HotkeyAction.Place1Secondary));
        Assert.Equal(HotkeyAction.Place3, HotkeyActions.SharedWith(choices, HotkeyAction.Place1Secondary));
        Assert.Null(HotkeyActions.SharedWith(choices, HotkeyAction.Place3));
    }

    // ---- quick launch -------------------------------------------------------------------------

    [Fact]
    public void QuickLaunch_IsOnForANewDock_AndForAFileFromBeforeIt()
    {
        Assert.True(new DockSettings().QuickLaunch);

        var path = Path.Combine(_dir, "before-quick-launch.json");
        File.WriteAllText(path, """{ "BaseSize": 44, "Hotkeys": { "Place2": "Win+Shift+F2" } }""");

        Assert.True(SettingsStore.Import(path).QuickLaunch);
    }

    /// <summary>Holding the Windows key and Ctrl brings the dock up for a new dock, and for a file from before it could.</summary>
    [Fact]
    public void RevealOnWinCtrl_IsOnForANewDock_AndForAFileFromBeforeIt()
    {
        Assert.True(new DockSettings().RevealOnWinCtrl);

        var path = Path.Combine(_dir, "before-reveal.json");
        File.WriteAllText(path, """{ "QuickLaunch": true, "QuickLaunchOff": [] }""");

        Assert.True(SettingsStore.Import(path).RevealOnWinCtrl);
    }

    /// <summary>The items' numbers come up on Win+Ctrl for a new dock, and for a file from before they could be turned off.</summary>
    [Fact]
    public void NumbersOnWinCtrl_AreOnForANewDock_AndForAFileFromBeforeThem()
    {
        Assert.True(new DockSettings().NumbersOnWinCtrl);

        var path = Path.Combine(_dir, "before-numbers.json");
        File.WriteAllText(path, """{ "QuickLaunch": true, "RevealOnWinCtrl": true }""");

        Assert.True(SettingsStore.Import(path).NumbersOnWinCtrl);
    }

    /// <summary>
    /// Off, the items' keys — first and second — are let go of, for other programs to have, and
    /// kept as they were set, for when it is on again.
    /// </summary>
    [Fact]
    public void QuickLaunchOff_LetsTheItemsKeysGo_AndKeepsThem()
    {
        var settings = new DockSettings
        {
            QuickLaunch = false,
            Hotkeys = { ["Place2Secondary"] = "Win+Ctrl+Alt+2" }
        };

        Assert.Equal(
            new[] { HotkeyAction.Keyboard, HotkeyAction.ShowHide, HotkeyAction.Settings },
            settings.HotkeysInForce().Keys.Order());
        Assert.Equal("Win+Ctrl+NumPad1", settings.HotkeyFor(HotkeyAction.Place1)?.ToString());
        Assert.Equal("Win+Ctrl+Alt+2", settings.HotkeyFor(HotkeyAction.Place2Secondary)?.ToString());

        settings.QuickLaunch = true;

        var inForce = settings.HotkeysInForce();
        Assert.Equal(Parse("Win+Ctrl+NumPad1"), inForce[HotkeyAction.Place1]);
        Assert.Equal(Parse("Win+Ctrl+Alt+2"), inForce[HotkeyAction.Place2Secondary]);
    }

    /// <summary>
    /// Off, an item's keys are nobody's: given the keyboard's own, the item would have them, being
    /// set on the page — but with quick launch off the keyboard keeps them, and is not told
    /// another row has them.
    /// </summary>
    [Fact]
    public void QuickLaunchOff_TheItemsKeysAreNobodysToShare()
    {
        var choices = Defaults();
        choices[HotkeyAction.Place3] = Parse("Win+Ctrl+A");

        Assert.Equal(HotkeyAction.Place3, HotkeyActions.SharedWith(HotkeyActions.InUse(choices, quickLaunch: true), HotkeyAction.Keyboard));

        var used = HotkeyActions.InUse(choices, quickLaunch: false);
        var inForce = HotkeyActions.InForce(used);

        Assert.Null(HotkeyActions.SharedWith(used, HotkeyAction.Keyboard));
        Assert.Equal(Parse("Win+Ctrl+A"), inForce[HotkeyAction.Keyboard]);
        Assert.False(inForce.ContainsKey(HotkeyAction.Place3));
    }

    /// <summary>
    /// Every item is on for a new dock, for a file from before an item could be turned off by
    /// itself, and for one that says <c>null</c>, which only a hand can write.
    /// </summary>
    [Fact]
    public void EveryItem_IsOnForANewDock_AndForAFileFromBeforeItsCheckbox()
    {
        Assert.Empty(new DockSettings().QuickLaunchOff);

        var path = Path.Combine(_dir, "before-item-checkboxes.json");
        File.WriteAllText(path, """{ "QuickLaunch": true, "Hotkeys": { "Place2": "Win+Shift+F2" } }""");
        Assert.Empty(SettingsStore.Import(path).QuickLaunchOff);

        File.WriteAllText(path, """{ "QuickLaunchOff": null }""");
        Assert.Empty(SettingsStore.Import(path).QuickLaunchOff);
    }

    /// <summary>
    /// An item turned off by itself has its keys — first and second — let go of, and kept as they
    /// were set; every other item keeps its own.
    /// </summary>
    [Fact]
    public void AnItemTurnedOff_LetsItsKeysGo_AndKeepsThem()
    {
        var settings = new DockSettings
        {
            QuickLaunchOff = [2],
            Hotkeys = { ["Place2Secondary"] = "Win+Ctrl+Alt+2", ["Place3Secondary"] = "Win+Ctrl+Alt+3" }
        };

        var inForce = settings.HotkeysInForce();
        Assert.False(inForce.ContainsKey(HotkeyAction.Place2));
        Assert.False(inForce.ContainsKey(HotkeyAction.Place2Secondary));
        Assert.Equal(Parse("Win+Ctrl+NumPad1"), inForce[HotkeyAction.Place1]);
        Assert.Equal(Parse("Win+Ctrl+Alt+3"), inForce[HotkeyAction.Place3Secondary]);
        Assert.Equal("Win+Ctrl+NumPad2", settings.HotkeyFor(HotkeyAction.Place2)?.ToString());
        Assert.Equal("Win+Ctrl+Alt+2", settings.HotkeyFor(HotkeyAction.Place2Secondary)?.ToString());

        settings.QuickLaunchOff = [];

        inForce = settings.HotkeysInForce();
        Assert.Equal(Parse("Win+Ctrl+NumPad2"), inForce[HotkeyAction.Place2]);
        Assert.Equal(Parse("Win+Ctrl+Alt+2"), inForce[HotkeyAction.Place2Secondary]);
    }

    /// <summary>
    /// An item turned off has no keys to share either: given the keyboard's own, it would have
    /// them, being set on the page — turned off, the keyboard keeps them, and the items around it
    /// keep theirs.
    /// </summary>
    [Fact]
    public void AnItemTurnedOff_ItsKeysAreNobodysToShare()
    {
        var choices = Defaults();
        choices[HotkeyAction.Place3] = Parse("Win+Ctrl+A");

        var used = HotkeyActions.InUse(choices, quickLaunch: true, placesOff: [3]);
        var inForce = HotkeyActions.InForce(used);

        Assert.Null(HotkeyActions.SharedWith(used, HotkeyAction.Keyboard));
        Assert.Equal(Parse("Win+Ctrl+A"), inForce[HotkeyAction.Keyboard]);
        Assert.False(inForce.ContainsKey(HotkeyAction.Place3));
        Assert.Equal(Parse("Win+Ctrl+NumPad4"), inForce[HotkeyAction.Place4]);
    }

    // ---- Windows ----------------------------------------------------------------------------

    private static Dictionary<HotkeyAction, Hotkey> Wanting(Hotkey hotkey, HotkeyAction action = HotkeyAction.Place9) =>
        new() { [action] = hotkey };

    [Fact]
    public void Windows_GivesAHotkeyToOneRegistration_AndTheOtherIsToldItIsTaken()
    {
        using var first = new HotkeyRegistry(0);
        using var second = new HotkeyRegistry(0);

        first.Apply(Wanting(Unused));
        Assert.Equal(Unused, first.Registered[HotkeyAction.Place9]);
        Assert.Empty(first.Taken);

        second.Apply(Wanting(Unused));
        Assert.Empty(second.Registered);
        Assert.Equal(Unused, second.Taken[HotkeyAction.Place9]);
    }

    /// <summary>
    /// Let go of for real while the settings dialog records, or pressing the hotkey being changed
    /// would fire it rather than reach the box: another registration can have it meanwhile.
    /// </summary>
    [Fact]
    public void WhileTheDialogRecords_TheHotkeysAreLetGo_AndTakenBackAfter()
    {
        using var dock = new HotkeyRegistry(0);
        dock.Apply(Wanting(Unused));

        dock.SetRecording(true);
        Assert.Empty(dock.Registered);

        using (var other = new HotkeyRegistry(0))
        {
            other.Apply(Wanting(Unused));
            Assert.Equal(Unused, other.Registered[HotkeyAction.Place9]);
        }

        dock.SetRecording(false);
        Assert.Equal(Unused, dock.Registered[HotkeyAction.Place9]);
    }

    /// <summary>
    /// Let go of while a program on the Exclusions page is in front: a hotkey only ignored would
    /// still be a key the game never saw.
    /// </summary>
    [Fact]
    public void StandingDownForAGame_LetsTheHotkeysGo_AndTakesThemBackAfter()
    {
        using var dock = new HotkeyRegistry(0);
        dock.Apply(Wanting(Unused));

        dock.SetStandingDown(true);
        Assert.Empty(dock.Registered);

        // Still let go of while either reason holds.
        dock.SetRecording(true);
        dock.SetStandingDown(false);
        Assert.Empty(dock.Registered);

        dock.SetRecording(false);
        Assert.Equal(Unused, dock.Registered[HotkeyAction.Place9]);
    }

    [Fact]
    public void AHotkeyThatWasTaken_IsAskedForAgain_AndSaidToBeFreeOnceItIs()
    {
        var other = new HotkeyRegistry(0);
        other.Apply(Wanting(Unused));

        using var dock = new HotkeyRegistry(0);
        var told = 0;
        dock.TakenChanged += (_, _) => told++;

        dock.Apply(Wanting(Unused));
        Assert.Equal(Unused, dock.Taken[HotkeyAction.Place9]);
        Assert.Equal(1, told);

        other.Dispose();
        dock.Retry();

        Assert.Empty(dock.Taken);
        Assert.Equal(Unused, dock.Registered[HotkeyAction.Place9]);
        Assert.Equal(2, told);
    }

    [Fact]
    public void TwoActionsTradingKeys_EachGetsTheOthers()
    {
        using var dock = new HotkeyRegistry(0);
        dock.Apply(new Dictionary<HotkeyAction, Hotkey>
        {
            [HotkeyAction.Place8] = Unused,
            [HotkeyAction.Place9] = AlsoUnused
        });

        dock.Apply(new Dictionary<HotkeyAction, Hotkey>
        {
            [HotkeyAction.Place8] = AlsoUnused,
            [HotkeyAction.Place9] = Unused
        });

        Assert.Equal(AlsoUnused, dock.Registered[HotkeyAction.Place8]);
        Assert.Equal(Unused, dock.Registered[HotkeyAction.Place9]);
        Assert.Empty(dock.Taken);
    }

    [Fact]
    public void AnActionGivenNone_LetsItsHotkeyGo()
    {
        using var dock = new HotkeyRegistry(0);
        dock.Apply(Wanting(Unused));
        dock.Apply(new Dictionary<HotkeyAction, Hotkey>());

        using var other = new HotkeyRegistry(0);
        other.Apply(Wanting(Unused));
        Assert.Equal(Unused, other.Registered[HotkeyAction.Place9]);
    }

    [Fact]
    public void OnlyARegisteredHotkey_IsTheDocks()
    {
        using var dock = new HotkeyRegistry(0);
        dock.Apply(Wanting(Unused));

        // The id each action registers under is its number and one; a WM_HOTKEY for any other
        // — the system's own are negative — is not the dock's.
        Assert.Equal(HotkeyAction.Place9, dock.ActionFor((int)HotkeyAction.Place9 + 1));
        Assert.Null(dock.ActionFor((int)HotkeyAction.Keyboard + 1));
        Assert.Null(dock.ActionFor(0));
        Assert.Null(dock.ActionFor(-1));
    }

    // ---- the page -----------------------------------------------------------------------------

    /// <summary>
    /// The page's rows are found by each action's name when the dialog is built — and each item's
    /// icon and checkbox by its place — so one missing would stop the dialog opening at all; this
    /// finds it first.
    /// </summary>
    [Fact]
    public void TheHotkeysPage_HasARowForEveryAction()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var names = XDocument.Load(Path.Combine(FindSourceRoot(), "Views", "SettingsWindow.xaml"))
            .Descendants()
            .Select(element => (string?)element.Attribute(xaml + "Name"))
            .OfType<string>()
            .ToHashSet();

        var missing = HotkeyActions.All
            .SelectMany(action => new[] { $"{action}Hotkey", $"{action}HotkeyClear", $"{action}HotkeyStatus" })
            .Concat(Enumerable.Range(1, 9).SelectMany(place => new[] { $"Place{place}Icon", $"Place{place}Check" }))
            .Where(name => !names.Contains(name))
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// The hotkeys stand down for a program on the Exclusions page whenever it is in front — in a
    /// window, or on the other display — and not only while it fills the dock's display, which is
    /// the edge's question. Read from the source, since what is in front cannot be arranged here,
    /// and the hotkeys given the edge's answer would pass everything else.
    /// </summary>
    [Fact]
    public void TheHotkeys_StandDownForAnExcludedProgramInFront_FillingTheDisplayOrNot()
    {
        var source = File.ReadAllText(Path.Combine(FindSourceRoot(), "Views", "DockWindow.xaml.cs"));

        var calls = Regex.Matches(source, @"_hotkeys\?\.SetStandingDown\((?<argument>[^;]*)\);");
        Assert.Single(calls);
        Assert.Contains("IsExcludedAppInFront()", calls[0].Groups["argument"].Value);
        Assert.Matches(@"private bool IsExcludedAppInFront\(\) =>[^;]*_foreground\.InFront\(\)", source);
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
