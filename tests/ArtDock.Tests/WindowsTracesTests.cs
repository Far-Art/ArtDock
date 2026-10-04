using System.IO;
using ArtDock.Interop;
using Microsoft.Win32;

namespace ArtDock.Tests;

/// <summary>
/// Covers finding what Windows keeps about an installation, and only that installation.
/// </summary>
/// <remarks>
/// Every test works under a scratch key of its own beneath <c>HKCU\Software</c>, with the same
/// paths under it that Windows uses under <c>HKCU</c>, and a scratch folder for <c>Recent</c>.
/// The user's real stores are never read or written here. The names are as they were read off
/// the development machine on 2026-10-04.
/// </remarks>
public sealed class WindowsTracesTests : IDisposable
{
    private const string Install = @"C:\Users\someone\AppData\Local\ArtDock.App";
    private const string SourceBuild = @"E:\Projects\ArtDock\bin\x64\Release\ArtDock.exe";

    /// <summary>FOLDERID_LocalAppData, as the tray writes a path inside it.</summary>
    private static readonly Guid LocalAppData = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");

    private const string ScratchParent = @"Software\ArtDock.Tests";

    private readonly string _scratchPath = $@"{ScratchParent}\Traces-{Guid.NewGuid():N}";
    private readonly RegistryKey _root;
    private readonly string _recent = Path.Combine(Path.GetTempPath(), "ArtDockTests", Guid.NewGuid().ToString("n"));

    private readonly WindowsTraces _traces = new(
        Install, "ArtDock.App",
        id => id == LocalAppData ? @"C:\Users\someone\AppData\Local" : null);

    public WindowsTracesTests() => _root = Registry.CurrentUser.CreateSubKey(_scratchPath);

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_scratchPath, throwOnMissingSubKey: false);

        using (var parent = Registry.CurrentUser.OpenSubKey(ScratchParent))
        {
            if (parent is not null && parent.SubKeyCount == 0 && parent.ValueCount == 0)
            {
                Registry.CurrentUser.DeleteSubKey(ScratchParent, throwOnMissingSubKey: false);
            }
        }

        if (Directory.Exists(_recent))
        {
            Directory.Delete(_recent, recursive: true);
        }
    }

    // ---- what is this installation's ---------------------------------------------------

    [Fact]
    public void ThePathsInsideTheInstall_AreOurs()
    {
        Assert.True(_traces.IsOurs($@"{Install}\Update.exe"));
        Assert.True(_traces.IsOurs($@"{Install}\current\ArtDock.exe"));
    }

    [Fact]
    public void AFolderThatOnlyBeginsWithTheInstallsName_IsNot() =>
        Assert.False(_traces.IsOurs($@"{Install}2\ArtDock.exe"));

    [Fact]
    public void ACopyRunFromAnywhereElse_IsNot() => Assert.False(_traces.IsOurs(SourceBuild));

    [Fact]
    public void TheAppsId_IsOurs_InAnyCase() => Assert.True(_traces.IsOurs("VELOPACK.artdock.app"));

    [Fact]
    public void ThePathWrittenAsAKnownFolder_IsOurs() =>
        Assert.True(_traces.IsOurs($@"{{{LocalAppData}}}\ArtDock.App\current\ArtDock.exe"));

    [Fact]
    public void MuiCachesNames_AreReadWithoutTheirSuffix()
    {
        Assert.True(_traces.IsOurs($@"{Install}\Update.exe.FriendlyAppName"));
        Assert.True(_traces.IsOurs(@"C:\Users\someone\Downloads\ArtDock.App-win-Setup.exe.ApplicationCompany"));
    }

    [Fact]
    public void TheSetup_IsOurs_WhereverItWasRunFrom()
    {
        Assert.True(_traces.IsOurs(@"C:\Users\someone\Downloads\ArtDock.App-win-Setup.exe"));
        Assert.True(_traces.IsOurs(@"D:\ArtDock.App-win-Setup (2).exe"));
        Assert.False(_traces.IsOurs(@"D:\SomethingElse-win-Setup.exe"));
    }

    // ---- finding them -------------------------------------------------------------------

    [Fact]
    public void TheTrayIcon_IsFoundByItsExecutable_AndNoOtherIs()
    {
        Write($@"{WindowsTraces.NotifyIconsPath}\14316228260205497803", "ExecutablePath", $@"{Install}\current\ArtDock.exe");
        Write($@"{WindowsTraces.NotifyIconsPath}\3754792238035298125", "ExecutablePath", SourceBuild);

        var found = Find();

        Assert.Contains(new WindowsTraces.KeyTrace($@"{WindowsTraces.NotifyIconsPath}\14316228260205497803"), found);
        Assert.DoesNotContain(new WindowsTraces.KeyTrace($@"{WindowsTraces.NotifyIconsPath}\3754792238035298125"), found);
    }

    [Fact]
    public void UserAssistsNames_AreReadInRot13()
    {
        var count = $@"{WindowsTraces.UserAssistPath}\{{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}}\Count";
        var ours = WindowsTraces.Rot13("velopack.ArtDock.App");
        Write(count, ours, new byte[72]);
        Write(count, WindowsTraces.Rot13(SourceBuild), new byte[72]);

        var found = Find();

        Assert.Equal([new WindowsTraces.ValueTrace(count, ours)], found);
    }

    [Fact]
    public void Rot13_IsItsOwnInverse() =>
        Assert.Equal(@"C:\Hfref\ArtDock", WindowsTraces.Rot13(WindowsTraces.Rot13(@"C:\Hfref\ArtDock")));

    [Fact]
    public void TheValuesNamedByProgram_AreFoundInEveryStore()
    {
        Write(WindowsTraces.MuiCachePath, $@"{Install}\Update.exe.FriendlyAppName", "Velopack");
        Write(WindowsTraces.CompatibilityStorePath, $@"{Install}\current\ArtDock.exe", new byte[8]);
        Write(WindowsTraces.JumplistDataPath, "velopack.ArtDock.App", 1L);
        Write($@"{WindowsTraces.FeatureUsagePath}\AppSwitched", "velopack.ArtDock.App", 3);
        Write($@"{WindowsTraces.FeatureUsagePath}\AppSwitched", SourceBuild, 13);

        var found = Find();

        Assert.Equal(4, found.Count);
        Assert.DoesNotContain(new WindowsTraces.ValueTrace($@"{WindowsTraces.FeatureUsagePath}\AppSwitched", SourceBuild), found);
    }

    [Fact]
    public void StartsEntries_AreFound()
    {
        var tile = $@"{WindowsTraces.TilePropertiesPath}\W~velopack.ArtDock.App";
        var list = $@"{WindowsTraces.CloudStorePath}\Current\{{ce30d9cc-9836-47eb-86f2-239287b5acc8}}$windows.data.apps.appmetadata$appmetadatalist";
        Write(tile, "Category", 0);
        Write($@"{list}\windows.data.apps.appmetadata$artdock.app", "Data", new byte[4]);
        Write($@"{list}\windows.data.apps.appmetadata$292998ff", "Data", new byte[4]);

        var found = Find();

        Assert.Equal(
            [new WindowsTraces.KeyTrace(tile), new WindowsTraces.KeyTrace($@"{list}\windows.data.apps.appmetadata$artdock.app")],
            found);
    }

    [Fact]
    public void TheJumpList_IsFoundByTheHashOfTheAppsId()
    {
        var automatic = Path.Combine(_recent, "AutomaticDestinations");
        Directory.CreateDirectory(automatic);
        var ours = Path.Combine(automatic, "ddc8f37c9e9e9633.automaticDestinations-ms");
        File.WriteAllBytes(ours, [0]);
        File.WriteAllBytes(Path.Combine(automatic, "f01b4d95cf55d32a.automaticDestinations-ms"), [0]);

        Assert.Equal([new WindowsTraces.FileTrace(ours)], Find());
    }

    [Fact]
    public void TheJumpListsHash_IsTheShells()
    {
        // File Explorer's, the same on every machine.
        Assert.Equal("f01b4d95cf55d32a", WindowsTraces.JumpListId("Microsoft.Windows.Explorer"));
        Assert.Equal("ddc8f37c9e9e9633", WindowsTraces.JumpListId("velopack.ArtDock.App"));
    }

    [Fact]
    public void NothingThere_FindsNothing() => Assert.Empty(Find());

    // ---- removing them ------------------------------------------------------------------

    [Fact]
    public void Remove_TakesWhatWasFound_AndLeavesTheRest()
    {
        Write($@"{WindowsTraces.NotifyIconsPath}\1", "ExecutablePath", $@"{Install}\current\ArtDock.exe");
        Write($@"{WindowsTraces.NotifyIconsPath}\2", "ExecutablePath", SourceBuild);
        Write(WindowsTraces.MuiCachePath, $@"{Install}\Update.exe.FriendlyAppName", "Velopack");
        Write(WindowsTraces.MuiCachePath, $@"{SourceBuild}.FriendlyAppName", "ArtDock");

        WindowsTraces.Remove(_root, Find());

        Assert.Empty(Find());
        using (var icons = _root.OpenSubKey(WindowsTraces.NotifyIconsPath))
        {
            Assert.Equal(["2"], icons!.GetSubKeyNames());
        }

        using var cache = _root.OpenSubKey(WindowsTraces.MuiCachePath);
        Assert.Equal([$@"{SourceBuild}.FriendlyAppName"], cache!.GetValueNames());
    }

    // ---- Windows Backup's queue -----------------------------------------------------------

    [Fact]
    public void TheBackupQueuesEvents_AreFoundByAppAndByTile_AndNoOthers()
    {
        // As Windows writes it, backslashes unescaped: not JSON any reader would take.
        Write($@"{WindowsTraces.BackupQueuePath}\ListOfEventDrivenBackedUpApps_113500485", "ListOfEventDrivenBackedUpApps_113500485",
            """[{"appId":"ArtDock.App", "installSource":"External MSI", "appName":"ArtDock", "action":"1", "wingetId":"ARP\User\X64\ArtDock.App", "wingetSource":"NoReliableInfo"}]""");
        Write($@"{WindowsTraces.BackupQueuePath}\ListOfEventDrivenBackedUpTiles_113503435", "ListOfEventDrivenBackedUpTiles_113503435",
            """{"tileId":"W~velopack.ArtDock.App", "displayName":"", "action":"2"}""");
        Write($@"{WindowsTraces.BackupQueuePath}\ListOfEventDrivenBackedUpApps_5833960", "ListOfEventDrivenBackedUpApps_5833960",
            """[{"appId":"5319275A.WhatsAppDesktop_cv1g1gvanyjgm", "appName":"WhatsApp", "action":"1"}]""");

        Assert.Equal(
            [
                new WindowsTraces.KeyTrace($@"{WindowsTraces.BackupQueuePath}\ListOfEventDrivenBackedUpApps_113500485"),
                new WindowsTraces.KeyTrace($@"{WindowsTraces.BackupQueuePath}\ListOfEventDrivenBackedUpTiles_113503435")
            ],
            Find());
    }

    [Fact]
    public void ABackupEventThatOnlyMentionsTheId_IsNot()
    {
        Assert.False(_traces.NamesUs("""[{"appId":"Other", "appName":"ArtDock.App", "wingetId":"ARP\User\X64\ArtDock.App"}]"""));
        Assert.False(_traces.NamesUs("""[{"appId":"ArtDock.App.Other"}]"""));
        Assert.False(_traces.NamesUs("""[{"appId":"ArtDockXApp"}]"""));
        Assert.False(_traces.NamesUs("not json"));
    }

    // ---- the second pass ------------------------------------------------------------------

    [Fact]
    public void TheSecondPass_NamesAheadWhatExplorerWritesLate()
    {
        var count = $@"{WindowsTraces.UserAssistPath}\{{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}}\Count";
        Write(count, "anything", 0);

        var predicted = _traces.Predicted(_root, _recent);

        Assert.Contains(new WindowsTraces.ValueTrace($@"{WindowsTraces.FeatureUsagePath}\AppSwitched", "velopack.ArtDock.App"), predicted);
        Assert.Contains(new WindowsTraces.ValueTrace(WindowsTraces.JumplistDataPath, "velopack.ArtDock.App"), predicted);
        Assert.Contains(new WindowsTraces.KeyTrace($@"{WindowsTraces.TilePropertiesPath}\W~velopack.ArtDock.App"), predicted);
        Assert.Contains(new WindowsTraces.ValueTrace(count, WindowsTraces.Rot13("velopack.ArtDock.App")), predicted);
        Assert.Contains(new WindowsTraces.FileTrace(Path.Combine(_recent, "AutomaticDestinations", "ddc8f37c9e9e9633.automaticDestinations-ms")), predicted);
    }

    [Fact]
    public void TheSecondPass_WaitsForVelopack_ThenDeletes_AndTheLog()
    {
        var script = _traces.LaterScript(
        [
            new WindowsTraces.KeyTrace($@"{WindowsTraces.NotifyIconsPath}\1"),
            new WindowsTraces.ValueTrace(WindowsTraces.MuiCachePath, $@"{Install}\Update.exe.FriendlyAppName"),
            new WindowsTraces.FileTrace(@"C:\Recent\AutomaticDestinations\ddc8f37c9e9e9633.automaticDestinations-ms")
        ]);

        Assert.Contains($@"Where-Object {{ $_.Path -eq '{Install}\Update.exe' }} | Wait-Process", script);
        Assert.True(script.IndexOf("Wait-Process", StringComparison.Ordinal) < script.IndexOf("DeleteSubKeyTree", StringComparison.Ordinal));
        Assert.Contains($@"$u.DeleteSubKeyTree('{WindowsTraces.NotifyIconsPath}\1', $false)", script);
        Assert.Contains($@"$u.OpenSubKey('{WindowsTraces.MuiCachePath}', $true); if ($k) {{ $k.DeleteValue('{Install}\Update.exe.FriendlyAppName', $false)", script);
        Assert.Contains(@"[IO.File]::Delete('C:\Recent\AutomaticDestinations\ddc8f37c9e9e9633.automaticDestinations-ms')", script);
        Assert.Contains(@"$j -match '\x22(appId\x22\s*:\s*\x22ArtDock\.App|tileId\x22\s*:\s*\x22W~velopack\.ArtDock\.App)\x22'", script);
        Assert.Contains(@"'\velopack\velopack_ArtDock.App.log'", script);

        // Never recursive: other programs' logs share the folder.
        Assert.EndsWith("if (-not (Get-ChildItem -LiteralPath $v -Force)) { Remove-Item -LiteralPath $v }", script);
    }

    [Fact]
    public void TheSecondPass_HasNoDoubleQuotes()
    {
        // Nothing for the command line's own quoting to get wrong.
        var script = _traces.LaterScript(
            [new WindowsTraces.ValueTrace(WindowsTraces.MuiCachePath, $@"{Install}\Update.exe")]);

        Assert.DoesNotContain("\"", script);
    }

    [Fact]
    public void TheSecondPass_ReadsEveryNameLiterally()
    {
        var script = _traces.LaterScript(
        [
            new WindowsTraces.ValueTrace(WindowsTraces.MuiCachePath, @"C:\Users\O'Brien\$(calc)\x.exe"),
            new WindowsTraces.ValueTrace(WindowsTraces.MuiCachePath, "C:\\Users\\a\u2019; calc; \u2018\\x.exe")
        ]);

        Assert.Contains(@"'C:\Users\O''Brien\$(calc)\x.exe'", script);
        Assert.DoesNotContain("\u2019", script);
    }

    [Fact]
    public void TheSecondPass_StaysUnderTheCommandLinesLimit()
    {
        var many = Enumerable.Range(0, 2000)
            .Select(i => new WindowsTraces.ValueTrace(WindowsTraces.MuiCachePath, $@"{Install}\{i}\Update.exe"));

        var script = _traces.LaterScript(many);

        Assert.True(script.Length < 32_000);
        Assert.Contains("velopack_ArtDock.App.log", script);
    }

    [Fact]
    public void Quote_DoublesTheQuote_AndRefusesTheTypographicOnes()
    {
        Assert.Equal("'it''s'", WindowsTraces.Quote("it's"));
        Assert.Null(WindowsTraces.Quote("it\u2019s"));
        Assert.Null(WindowsTraces.Quote("a\nb"));
    }

    private IReadOnlyList<WindowsTraces.Trace> Find() => _traces.Find(_root, _recent);

    private void Write(string path, string name, object value)
    {
        using var key = _root.CreateSubKey(path);
        key.SetValue(name, value);
    }
}
