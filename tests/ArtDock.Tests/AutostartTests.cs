using ArtDock.Interop;
using Microsoft.Win32;

namespace ArtDock.Tests;

/// <summary>
/// Covers starting at sign-in as the Run value and Task Manager's flag decide it together.
/// </summary>
/// <remarks>
/// <para>
/// Task Manager's *Startup apps* page turns an entry off by writing a flag beside it, not by
/// deleting it. Reading the Run value alone showed a dock turned off there as on, and ticking
/// the box again left it off. The tests hold the three answers apart: the System page's choice
/// clears the flag, the setup and the first run leave it standing, and an uninstall takes it
/// with the entry.
/// </para>
/// <para>
/// Every test works under a scratch key of its own beneath <c>HKCU\Software</c>, with the same
/// paths under it that the dock uses under <c>HKCU</c>, and deletes it afterwards. The user's
/// real Run key is never read or written here.
/// </para>
/// </remarks>
public sealed class AutostartTests : IDisposable
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>As Task Manager writes them, read off the development machine.</summary>
    private static readonly byte[] OnFlag = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] OffFlag = [0x03, 0, 0, 0, 0x88, 0xC3, 0x33, 0x40, 0x37, 0x48, 0xDD, 0x01];

    /// <summary>
    /// As the Settings app's *Apps → Startup* page writes it for off, which is not Task
    /// Manager's byte — read off the development machine on 2026-10-01.
    /// </summary>
    private static readonly byte[] SettingsOffFlag = [0x01, 0, 0, 0, 0xF6, 0x92, 0x7F, 0xD7, 0xAD, 0x51, 0xDD, 0x01];

    private const string ScratchParent = @"Software\ArtDock.Tests";

    private readonly string _scratchPath = $@"{ScratchParent}\Autostart-{Guid.NewGuid():N}";
    private readonly RegistryKey _root;

    public AutostartTests() => _root = Registry.CurrentUser.CreateSubKey(_scratchPath);

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_scratchPath, throwOnMissingSubKey: false);

        // The tests of one class run one at a time, so an empty parent is no other test's.
        using (var parent = Registry.CurrentUser.OpenSubKey(ScratchParent))
        {
            if (parent is null || parent.SubKeyCount > 0 || parent.ValueCount > 0)
            {
                return;
            }
        }

        Registry.CurrentUser.DeleteSubKey(ScratchParent, throwOnMissingSubKey: false);
    }

    // ---- the flag --------------------------------------------------------------------

    [Fact]
    public void TaskManagersOnFlag_ReadsAsOn() => Assert.False(Autostart.IsOffFlag(OnFlag));

    [Fact]
    public void TaskManagersOffFlag_ReadsAsOff() => Assert.True(Autostart.IsOffFlag(OffFlag));

    [Fact]
    public void TheSettingsAppsOffFlag_ReadsAsOff() => Assert.True(Autostart.IsOffFlag(SettingsOffFlag));

    [Fact]
    public void AnEntryTurnedOffInTheSettingsApp_IsNotEnabled()
    {
        WriteEntry(@"C:\Apps\ArtDock\ArtDock.exe");
        WriteFlag(SettingsOffFlag);

        Assert.False(Autostart.IsEnabled(_root));
    }

    [Fact]
    public void NoFlag_ReadsAsOn()
    {
        Assert.False(Autostart.IsOffFlag(null));
        Assert.False(Autostart.IsOffFlag([]));
    }

    // ---- reading ---------------------------------------------------------------------

    [Fact]
    public void NoEntry_IsNeitherRegisteredNorEnabled()
    {
        Assert.False(Autostart.IsRegistered(_root));
        Assert.False(Autostart.IsEnabled(_root));
    }

    [Fact]
    public void AnEntryNobodyTouchedInTaskManager_IsEnabled()
    {
        WriteEntry(@"C:\Apps\ArtDock\ArtDock.exe");

        Assert.True(Autostart.IsEnabled(_root));
    }

    [Fact]
    public void AnEntryTurnedOffInTaskManager_IsRegisteredButNotEnabled()
    {
        WriteEntry(@"C:\Apps\ArtDock\ArtDock.exe");
        WriteFlag(OffFlag);

        Assert.True(Autostart.IsRegistered(_root));
        Assert.False(Autostart.IsEnabled(_root));
    }

    [Fact]
    public void AnEntryTurnedBackOnInTaskManager_IsEnabled()
    {
        WriteEntry(@"C:\Apps\ArtDock\ArtDock.exe");
        WriteFlag(OnFlag);

        Assert.True(Autostart.IsEnabled(_root));
    }

    // ---- the user's choice -----------------------------------------------------------

    [Fact]
    public void TickingTheBox_OverATaskManagerOff_TurnsItOn()
    {
        WriteEntry(@"C:\Apps\ArtDock\ArtDock.exe");
        WriteFlag(OffFlag);

        Assert.True(Autostart.SetEnabled(_root, true));

        Assert.True(Autostart.IsEnabled(_root));
        Assert.Equal(OnFlag, ReadFlag());
    }

    [Fact]
    public void TickingTheBox_WhereThereWasNothing_RegistersItOn()
    {
        Assert.True(Autostart.SetEnabled(_root, true));

        Assert.True(Autostart.IsEnabled(_root));
        Assert.Equal(OnFlag, ReadFlag());
    }

    [Fact]
    public void UntickingTheBox_KeepsTheEntry_AndSwitchesItOff()
    {
        // As Windows' own switch does: the dock stays on the list of startup apps, shown off,
        // rather than vanishing from it.
        WriteEntry(@"C:\Apps\ArtDock\ArtDock.exe");

        Assert.True(Autostart.SetEnabled(_root, false));

        Assert.True(Autostart.IsRegistered(_root));
        Assert.False(Autostart.IsEnabled(_root));
        Assert.Equal(0x03, ReadFlag()![0]);
    }

    [Fact]
    public void UntickingTheBox_WithNoEntry_WritesNothing()
    {
        Assert.True(Autostart.SetEnabled(_root, false));

        Assert.False(Autostart.IsRegistered(_root));
        Assert.Null(ReadFlag());
    }

    [Fact]
    public void TheOffFlag_IsTaskManagers_WithTheTimeItWasTurnedOff()
    {
        var at = new DateTime(2026, 10, 1, 14, 4, 57, DateTimeKind.Utc);

        var flag = Autostart.OffFlag(at);

        Assert.Equal(12, flag.Length);
        Assert.Equal(new byte[] { 0x03, 0, 0, 0 }, flag[..4]);
        Assert.Equal(at, DateTime.FromFileTimeUtc(BitConverter.ToInt64(flag, 4)));
        Assert.True(Autostart.IsOffFlag(flag));
        Assert.False(Autostart.IsOffFlag(Autostart.OnFlag()));
    }

    // ---- the setup and the first run -------------------------------------------------

    [Fact]
    public void Registering_WhereThereWasNothing_IsEnabled()
    {
        Assert.True(Autostart.Register(_root));

        Assert.True(Autostart.IsEnabled(_root));
    }

    [Fact]
    public void Registering_LeavesATaskManagerOffStanding()
    {
        // A setup run over an installed copy calls the install hook: it must not turn back on
        // what the user turned off.
        WriteEntry(@"C:\Apps\ArtDock\ArtDock.exe");
        WriteFlag(OffFlag);

        Assert.True(Autostart.Register(_root));

        Assert.True(Autostart.IsRegistered(_root));
        Assert.False(Autostart.IsEnabled(_root));
    }

    // ---- uninstalling ----------------------------------------------------------------

    [Fact]
    public void RemovingAnEntryIntoTheInstallation_TakesTheFlagWithIt()
    {
        WriteEntry(@"C:\Users\someone\AppData\Local\ArtDock.App\ArtDock.exe");
        WriteFlag(OffFlag);

        Autostart.RemoveIfUnder(_root, @"C:\Users\someone\AppData\Local\ArtDock.App");

        Assert.False(Autostart.IsRegistered(_root));
        Assert.Null(ReadFlag());
    }

    [Fact]
    public void AnEntryForAnotherCopy_KeepsItsFlag()
    {
        WriteEntry(@"E:\Projects\ArtDock\bin\x64\Release\ArtDock.exe");
        WriteFlag(OffFlag);

        Autostart.RemoveIfUnder(_root, @"C:\Users\someone\AppData\Local\ArtDock.App");

        Assert.True(Autostart.IsRegistered(_root));
        Assert.Equal(OffFlag, ReadFlag());
    }

    // ---- the watch: the settings dialog following a switch made in Windows --------------

    [Fact]
    public void TheWatch_HearsAnEntryWritten()
    {
        using var watch = new AutostartWatch(_root);
        using var heard = new SemaphoreSlim(0);
        watch.Changed += (_, _) => heard.Release();

        WriteEntry(@"C:\Apps\ArtDock\ArtDock.exe");

        Assert.True(heard.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void TheWatch_HearsWindowsSwitchItOff_AndOnAgain()
    {
        // Twice, because a registration is good for one change: the second is heard only if
        // the watch made it again after the first.
        WriteEntry(@"C:\Apps\ArtDock\ArtDock.exe");
        using var watch = new AutostartWatch(_root);
        using var heard = new SemaphoreSlim(0);
        watch.Changed += (_, _) => heard.Release();

        WriteFlag(SettingsOffFlag);
        Assert.True(heard.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(Autostart.IsEnabled(_root));

        WriteFlag(OnFlag);
        Assert.True(heard.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(Autostart.IsEnabled(_root));
    }

    [Fact]
    public void TheWatch_HearsNothingOnceDisposed()
    {
        var watch = new AutostartWatch(_root);
        var heard = 0;
        watch.Changed += (_, _) => Interlocked.Increment(ref heard);
        watch.Dispose();

        WriteEntry(@"C:\Apps\ArtDock\ArtDock.exe");
        WriteFlag(OffFlag);
        Thread.Sleep(250);

        Assert.Equal(0, Volatile.Read(ref heard));
    }

    // ---- helpers ---------------------------------------------------------------------

    private void WriteEntry(string path)
    {
        using var run = _root.CreateSubKey(RunPath);
        run.SetValue("ArtDock", $"\"{path}\"");
    }

    private void WriteFlag(byte[] flag)
    {
        using var approved = _root.CreateSubKey(ApprovedPath);
        approved.SetValue("ArtDock", flag, RegistryValueKind.Binary);
    }

    private byte[]? ReadFlag()
    {
        using var approved = _root.OpenSubKey(ApprovedPath);
        return approved?.GetValue("ArtDock") as byte[];
    }
}
