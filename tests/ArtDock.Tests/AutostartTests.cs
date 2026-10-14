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
/// real Run key is never read or written here. Nor is their sign-in task: the task kept beside
/// the entry is a record of what was asked of it (<see cref="RecordingTask"/>).
/// </para>
/// <para>
/// "This copy" is the test host, which is what <see cref="ArtDock.Services.AppUpdater.LaunchPath"/>
/// names in a process Velopack did not install.
/// </para>
/// </remarks>
public sealed class AutostartTests : IDisposable
{
    private static string ThisCopy => ArtDock.Services.AppUpdater.LaunchPath!;

    private static readonly SignInCommand StartsThisCopy = new(ThisCopy, Autostart.SignInSwitch);

    private readonly RecordingTask _task = new();

    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string NotifiedPath = @"Software\Microsoft\Windows\CurrentVersion\RunNotification";

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

        Assert.True(Autostart.SetEnabled(_root, _task,true));

        Assert.True(Autostart.IsEnabled(_root));
        Assert.Equal(OnFlag, ReadFlag());
    }

    [Fact]
    public void TickingTheBox_WhereThereWasNothing_RegistersItOn()
    {
        Assert.True(Autostart.SetEnabled(_root, _task,true));

        Assert.True(Autostart.IsEnabled(_root));
        Assert.Equal(OnFlag, ReadFlag());
    }

    [Fact]
    public void UntickingTheBox_KeepsTheEntry_AndSwitchesItOff()
    {
        // As Windows' own switch does: the dock stays on the list of startup apps, shown off,
        // rather than vanishing from it.
        WriteEntry(@"C:\Apps\ArtDock\ArtDock.exe");

        Assert.True(Autostart.SetEnabled(_root, _task,false));

        Assert.True(Autostart.IsRegistered(_root));
        Assert.False(Autostart.IsEnabled(_root));
        Assert.Equal(0x03, ReadFlag()![0]);
    }

    [Fact]
    public void UntickingTheBox_WithNoEntry_WritesNothing()
    {
        Assert.True(Autostart.SetEnabled(_root, _task,false));

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
        Assert.True(Autostart.Register(_root, _task));

        Assert.True(Autostart.IsEnabled(_root));
    }

    [Fact]
    public void Registering_LeavesATaskManagerOffStanding()
    {
        // A setup run over an installed copy calls the install hook: it must not turn back on
        // what the user turned off.
        WriteEntry(@"C:\Apps\ArtDock\ArtDock.exe");
        WriteFlag(OffFlag);

        Assert.True(Autostart.Register(_root, _task));

        Assert.True(Autostart.IsRegistered(_root));
        Assert.False(Autostart.IsEnabled(_root));
    }

    // ---- uninstalling ----------------------------------------------------------------

    [Fact]
    public void RemovingAnEntryIntoTheInstallation_TakesTheFlagWithIt()
    {
        WriteEntry(@"C:\Users\someone\AppData\Local\ArtDock.App\ArtDock.exe");
        WriteFlag(OffFlag);

        Autostart.RemoveIfUnder(_root, _task,@"C:\Users\someone\AppData\Local\ArtDock.App");

        Assert.False(Autostart.IsRegistered(_root));
        Assert.Null(ReadFlag());
    }

    [Fact]
    public void RemovingAnEntryIntoTheInstallation_TakesWindowsNoteOfItToo()
    {
        WriteEntry(@"C:\Users\someone\AppData\Local\ArtDock.App\ArtDock.exe");
        using (var notified = _root.CreateSubKey(NotifiedPath))
        {
            notified.SetValue("StartupTNotiArtDock", 1);
            notified.SetValue("StartupTNotiOther", 1);
        }

        Autostart.RemoveIfUnder(_root, _task,@"C:\Users\someone\AppData\Local\ArtDock.App");

        using var after = _root.OpenSubKey(NotifiedPath);
        Assert.Equal(["StartupTNotiOther"], after!.GetValueNames());
    }

    [Fact]
    public void AnEntryForAnotherCopy_KeepsItsFlag()
    {
        WriteEntry(@"E:\Projects\ArtDock\bin\x64\Release\ArtDock.exe");
        WriteFlag(OffFlag);

        Autostart.RemoveIfUnder(_root, _task,@"C:\Users\someone\AppData\Local\ArtDock.App");

        Assert.True(Autostart.IsRegistered(_root));
        Assert.Equal(OffFlag, ReadFlag());
    }

    [Fact]
    public void RemovingAnEntryWithTheSwitch_ReadsThePathBeforeIt()
    {
        WriteValue(@"""C:\Users\some one\AppData\Local\ArtDock.App\ArtDock.exe"" --sign-in");

        Autostart.RemoveIfUnder(_root, _task, @"C:\Users\some one\AppData\Local\ArtDock.App");

        Assert.False(Autostart.IsRegistered(_root));
    }

    [Fact]
    public void Uninstalling_TakesATaskIntoTheInstallation()
    {
        _task.Command = new SignInCommand(@"C:\Users\someone\AppData\Local\ArtDock.App\ArtDock.exe", "--sign-in");

        Autostart.RemoveIfUnder(_root, _task, @"C:\Users\someone\AppData\Local\ArtDock.App");

        Assert.Null(_task.Command);
    }

    [Fact]
    public void Uninstalling_LeavesATaskForAnotherCopy()
    {
        var other = new SignInCommand(@"E:\Projects\ArtDock\bin\x64\Release\ArtDock.exe", "--sign-in");
        _task.Command = other;

        Autostart.RemoveIfUnder(_root, _task, @"C:\Users\someone\AppData\Local\ArtDock.App");

        Assert.Equal(other, _task.Command);
        Assert.Equal(0, _task.Removals);
    }

    // ---- the entry's switch ----------------------------------------------------------

    [Fact]
    public void TheEntry_StartsThisCopyWithTheSwitch()
    {
        Assert.True(Autostart.Register(_root, _task));

        Assert.Equal($"\"{ThisCopy}\" --sign-in", ReadValue());
    }

    [Theory]
    [InlineData(@"""C:\Apps\ArtDock\ArtDock.exe"" --sign-in", @"C:\Apps\ArtDock\ArtDock.exe")]
    [InlineData(@"""C:\Program Files\ArtDock\ArtDock.exe""", @"C:\Program Files\ArtDock\ArtDock.exe")]
    [InlineData(@"  ""C:\Apps\ArtDock.exe""  ", @"C:\Apps\ArtDock.exe")]
    [InlineData(@"C:\Apps\ArtDock.exe", @"C:\Apps\ArtDock.exe")]
    public void TheProgramAnEntryStarts_IsItsQuotedPath(string value, string program) =>
        Assert.Equal(program, Autostart.CommandPath(value));

    [Theory]
    [InlineData(@"""C:\Apps\ArtDock.exe"" --sign-in", true)]
    [InlineData(@"""C:\Apps\ArtDock.exe"" --SIGN-IN", true)]
    [InlineData(@"""C:\Apps\ArtDock.exe""", false)]
    [InlineData(@"""C:\Apps\--sign-in\ArtDock.exe""", false)]
    [InlineData(@"""C:\Apps\ArtDock.exe"" --sign-inside", false)]
    public void TheSwitch_IsReadFromTheArgumentsAlone(string value, bool has) =>
        Assert.Equal(has, Autostart.HasSignInSwitch(value));

    // ---- the sign-in task: following the entry -----------------------------------------

    [Fact]
    public void Registering_MakesTheTask_StartingThisCopyWithTheSwitch()
    {
        Assert.True(Autostart.Register(_root, _task));

        Assert.Equal(StartsThisCopy, _task.Command);
    }

    [Fact]
    public void Registering_OverATaskManagerOff_MakesNoTask()
    {
        WriteEntry(ThisCopy);
        WriteFlag(OffFlag);
        _task.Command = StartsThisCopy;

        Assert.True(Autostart.Register(_root, _task));

        Assert.Null(_task.Command);
    }

    [Fact]
    public void TickingTheBox_MakesTheTask()
    {
        Assert.True(Autostart.SetEnabled(_root, _task, true));

        Assert.Equal(StartsThisCopy, _task.Command);
    }

    [Fact]
    public void UntickingTheBox_TakesTheTaskAway()
    {
        Assert.True(Autostart.SetEnabled(_root, _task, true));

        Assert.True(Autostart.SetEnabled(_root, _task, false));

        Assert.Null(_task.Command);
        Assert.True(Autostart.IsRegistered(_root));
    }

    [Fact]
    public void Refreshing_AnEntryFromBeforeTheTask_GivesItTheSwitch_AndMakesTheTask()
    {
        // As every copy wrote it until there was a task: a copy updated from one has the entry,
        // and would otherwise meet the dock the task started and say it was already running.
        WriteEntry(ThisCopy);

        Autostart.Refresh(_root, _task);

        Assert.True(Autostart.HasSignInSwitch(ReadValue()!));
        Assert.Equal(StartsThisCopy, _task.Command);
    }

    [Fact]
    public void Refreshing_AnEntryFromBeforeTheTask_KeepsTaskManagersFlag()
    {
        WriteEntry(ThisCopy);
        WriteFlag(OnFlag);

        Autostart.Refresh(_root, _task);

        Assert.Equal(OnFlag, ReadFlag());
    }

    [Fact]
    public void Refreshing_AnEntrySwitchedOffInWindows_TakesTheTaskAway()
    {
        WriteValue($"\"{ThisCopy}\" --sign-in");
        WriteFlag(SettingsOffFlag);
        _task.Command = StartsThisCopy;

        Autostart.Refresh(_root, _task);

        Assert.Null(_task.Command);
    }

    [Fact]
    public void Refreshing_WithNoEntry_TakesTheTaskAway()
    {
        _task.Command = StartsThisCopy;

        Autostart.Refresh(_root, _task);

        Assert.Null(_task.Command);
    }

    [Fact]
    public void Refreshing_AnotherCopysEntry_LeavesTheTaskToIt()
    {
        var other = new SignInCommand(@"E:\Projects\ArtDock\bin\x64\Release\ArtDock.exe", "--sign-in");
        WriteEntry(other.Path);
        _task.Command = other;

        Autostart.Refresh(_root, _task);

        Assert.Equal(other, _task.Command);
        Assert.Equal(0, _task.Writes);
        Assert.Equal(0, _task.Removals);
        Assert.Equal($"\"{other.Path}\"", ReadValue());
    }

    [Fact]
    public void Refreshing_ATaskAlreadyStartingThisCopy_WritesNothing()
    {
        // So that one disabled in Task Scheduler stays disabled: writing it again would enable it.
        WriteValue($"\"{ThisCopy}\" --sign-in");
        _task.Command = new SignInCommand(ThisCopy.ToUpperInvariant(), Autostart.SignInSwitch);

        Autostart.Refresh(_root, _task);

        Assert.Equal(0, _task.Writes);
    }

    [Fact]
    public void Refreshing_ATaskStartingAnotherCopy_PointsItHere()
    {
        WriteValue($"\"{ThisCopy}\" --sign-in");
        _task.Command = new SignInCommand(@"C:\Users\someone\AppData\Local\ArtDock.App\ArtDock.exe", "--sign-in");

        Autostart.Refresh(_root, _task);

        Assert.Equal(StartsThisCopy, _task.Command);
    }

    // ---- a launch at sign-in ---------------------------------------------------------

    [Fact]
    public void ALaunchAtSignIn_GoesOn_WhenTheEntryIsThisCopysAndOn()
    {
        WriteValue($"\"{ThisCopy}\" --sign-in");

        Assert.True(Autostart.StartsThisCopy(_root));
    }

    [Fact]
    public void ALaunchAtSignIn_Goes_WhenTheEntryIsSwitchedOff()
    {
        WriteValue($"\"{ThisCopy}\" --sign-in");
        WriteFlag(OffFlag);

        Assert.False(Autostart.StartsThisCopy(_root));
    }

    [Fact]
    public void ALaunchAtSignIn_Goes_WhenTheEntryIsAnotherCopys()
    {
        WriteEntry(@"C:\Users\someone\AppData\Local\ArtDock.App\ArtDock.exe");

        Assert.False(Autostart.StartsThisCopy(_root));
    }

    [Fact]
    public void ALaunchAtSignIn_Goes_WhenThereIsNoEntry() => Assert.False(Autostart.StartsThisCopy(_root));

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

    /// <summary>The entry as every copy wrote it before there was a sign-in task: the quoted path alone.</summary>
    private void WriteEntry(string path) => WriteValue($"\"{path}\"");

    private void WriteValue(string value)
    {
        using var run = _root.CreateSubKey(RunPath);
        run.SetValue("ArtDock", value);
    }

    private string? ReadValue()
    {
        using var run = _root.OpenSubKey(RunPath);
        return run?.GetValue("ArtDock") as string;
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

    /// <summary>A sign-in task that is only a record of what it was asked.</summary>
    private sealed class RecordingTask : ISignInTask
    {
        public SignInCommand? Command { get; set; }

        public int Writes { get; private set; }

        public int Removals { get; private set; }

        public SignInCommand? Read() => Command;

        public void Write(SignInCommand command)
        {
            Command = command;
            Writes++;
        }

        public void Remove()
        {
            Command = null;
            Removals++;
        }
    }
}
