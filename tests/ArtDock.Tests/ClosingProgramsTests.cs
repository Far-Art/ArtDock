using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers a program counting as closing — its last window gone, its process not — and a click
/// held until it has gone, as measured on Rider: window gone, process alive 5.2 s more.
/// </summary>
/// <remarks>Processes are numbers here; which have exited, and which were let go, is kept by the test.</remarks>
public class ClosingProgramsTests
{
    private const string Rider = @"C:\Program Files\JetBrains\Rider\bin\rider64.exe";

    private static readonly DateTime Start = new(2026, 10, 3, 16, 3, 58, DateTimeKind.Utc);

    private readonly HashSet<int> _exited = [];

    private readonly List<int> _released = [];

    private readonly ClosingPrograms<int> _closing;

    public ClosingProgramsTests() => _closing = new(_exited.Contains, _released.Add);

    private static HashSet<string> NoWindows() => new(StringComparer.OrdinalIgnoreCase);

    private bool Lose(string path, params int[] processes) =>
        _closing.Note([(path, processes)], NoWindows(), Start);

    [Fact]
    public void AProgramWhoseWindowsWentAndProcessDidNot_IsClosing()
    {
        Assert.True(Lose(Rider, 13792));

        Assert.True(_closing.Any);
        Assert.Equal(Rider, _closing.Find(Rider));
    }

    [Fact]
    public void AProgramWhoseProcessHadGoneToo_IsNot()
    {
        _exited.Add(13792);

        Assert.False(Lose(Rider, 13792));
        Assert.False(_closing.Any);
        Assert.Equal([13792], _released);
    }

    [Fact]
    public void ItEnds_WhenTheProcessExits()
    {
        Lose(Rider, 13792);
        Assert.False(_closing.Check(Start.AddSeconds(2)));

        _exited.Add(13792);
        Assert.True(_closing.Check(Start.AddSeconds(5.2)));
        Assert.Null(_closing.Find(Rider));
        Assert.Equal([13792], _released);
    }

    [Fact]
    public void ItEnds_WhenEveryProcessHasExited_NotTheFirst()
    {
        Lose(Rider, 1, 2);

        _exited.Add(1);
        Assert.False(_closing.Check(Start.AddSeconds(1)));

        _exited.Add(2);
        Assert.True(_closing.Check(Start.AddSeconds(2)));
    }

    [Fact]
    public void ItEnds_AtTheLimit_ForAProgramThatStaysInTheTray()
    {
        Lose(Rider, 13792);

        Assert.False(_closing.Check(Start + ClosingPrograms<int>.Limit - TimeSpan.FromMilliseconds(1)));
        Assert.True(_closing.Check(Start + ClosingPrograms<int>.Limit));
        Assert.Equal([13792], _released);
    }

    [Fact]
    public void ItEnds_WhenAWindowComesBack()
    {
        Lose(Rider, 13792);

        var withWindows = NoWindows();
        withWindows.Add(Rider);
        Assert.True(_closing.Note([], withWindows, Start.AddSeconds(1)));
        Assert.Null(_closing.Find(Rider));
    }

    [Fact]
    public void AClick_WaitsForIt_AndRunsOnceItHasGone()
    {
        Lose(Rider, 13792);
        var opened = 0;

        Assert.True(_closing.WhenClosed(Rider, () => opened++));
        _closing.Check(Start.AddSeconds(1));
        Assert.Equal(0, opened);

        _exited.Add(13792);
        _closing.Check(Start.AddSeconds(5.2));
        Assert.Equal(1, opened);

        // And not again.
        _closing.Check(Start.AddSeconds(6));
        Assert.Equal(1, opened);
    }

    [Fact]
    public void AClick_OnAProgramNotClosing_IsNotHeld()
    {
        var opened = 0;

        Assert.False(_closing.WhenClosed(Rider, () => opened++));
        Assert.Equal(0, opened);
    }

    [Fact]
    public void AClick_WaitingWhenAWindowComesBack_Runs()
    {
        // Taken again then, it raises the window rather than launching.
        Lose(Rider, 13792);
        var opened = 0;
        _closing.WhenClosed(Rider, () => opened++);

        var withWindows = NoWindows();
        withWindows.Add(Rider);
        _closing.Note([], withWindows, Start.AddSeconds(1));

        Assert.Equal(1, opened);
    }

    [Fact]
    public void APinOfALauncherStub_FindsItsProgramByFileName()
    {
        // As the census matches Windows' own stubs: the pin's path is not where it runs.
        Lose(@"C:\Program Files\WindowsApps\Notepad\Notepad.exe", 7);

        Assert.Equal(
            @"C:\Program Files\WindowsApps\Notepad\Notepad.exe",
            _closing.Find(@"C:\Windows\System32\notepad.exe"));
    }

    [Fact]
    public void Clearing_LetsGoOfEveryProcess_AndRunsNothing()
    {
        Lose(Rider, 1, 2);
        var opened = 0;
        _closing.WhenClosed(Rider, () => opened++);

        _closing.Clear();

        Assert.False(_closing.Any);
        Assert.Equal([1, 2], _released.Order());
        Assert.Equal(0, opened);
    }
}
