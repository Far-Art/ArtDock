using System.Xml.Linq;
using ArtDock.Interop;

namespace ArtDock.Tests;

/// <summary>
/// Covers the sign-in task's definition: what Task Scheduler is given, and what is read back
/// out of what it gives.
/// </summary>
/// <remarks>
/// Only the XML. Registering a task is the machine's, and a test that did it would leave the
/// user's sign-in starting whatever it registered should it fail half way; the registration was
/// tried by hand instead, under a throwaway name (TODO.md item 35).
/// </remarks>
public sealed class SignInTaskTests
{
    private const string Sid = "S-1-5-21-1000000000-2000000000-3000000000-1001";

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static readonly SignInCommand Command =
        new(@"C:\Users\some one\AppData\Local\ArtDock.App\ArtDock.exe", "--sign-in");

    private static XElement Task => XElement.Parse(SignInTask.Xml(Command, Sid));

    private static string Setting(string name) => Task.Element(Ns + "Settings")!.Element(Ns + name)!.Value;

    [Fact]
    public void TheTask_IsNamedForTheUser() =>
        Assert.Equal($"ArtDock {Sid}", SignInTask.NameFor(Sid));

    [Fact]
    public void ItStartsAsTheUserSignsIn()
    {
        var trigger = Assert.Single(Task.Element(Ns + "Triggers")!.Elements());

        Assert.Equal(Ns + "LogonTrigger", trigger.Name);
        Assert.Equal(Sid, trigger.Element(Ns + "UserId")!.Value);
    }

    [Fact]
    public void ItRunsAsTheUser_WithNoElevationAndNoPasswordKept()
    {
        var principal = Task.Element(Ns + "Principals")!.Element(Ns + "Principal")!;

        Assert.Equal(Sid, principal.Element(Ns + "UserId")!.Value);
        Assert.Equal("InteractiveToken", principal.Element(Ns + "LogonType")!.Value);
        Assert.Equal("LeastPrivilege", principal.Element(Ns + "RunLevel")!.Value);
    }

    [Fact]
    public void ItRunsAtNormalPriority()
    {
        // A task's default is 7, below normal, which the dock would hand on to every program it
        // starts: a process below normal gives its own class to the processes it creates.
        Assert.Equal("4", Setting("Priority"));
    }

    [Fact]
    public void ItIsNeverStoppedForRunningLong() => Assert.Equal("PT0S", Setting("ExecutionTimeLimit"));

    [Fact]
    public void ItStartsAndStaysOnBattery()
    {
        Assert.Equal("false", Setting("DisallowStartIfOnBatteries"));
        Assert.Equal("false", Setting("StopIfGoingOnBatteries"));
    }

    [Fact]
    public void ItStartsTheProgramQuoted_WithTheSwitch_FromItsOwnFolder()
    {
        var exec = Task.Descendants(Ns + "Exec").Single();

        Assert.Equal($"\"{Command.Path}\"", exec.Element(Ns + "Command")!.Value);
        Assert.Equal("--sign-in", exec.Element(Ns + "Arguments")!.Value);
        Assert.Equal(@"C:\Users\some one\AppData\Local\ArtDock.App", exec.Element(Ns + "WorkingDirectory")!.Value);
    }

    [Fact]
    public void WhatItStarts_ReadsBackAsItWasGiven() =>
        Assert.Equal(Command, SignInTask.CommandOf(SignInTask.Xml(Command, Sid)));

    [Fact]
    public void WhatItStarts_ReadsBack_FromTaskSchedulersOwnXml()
    {
        // Task Scheduler hands the definition back with a declaration, and with what it filled
        // in around what it was given.
        const string registered = """
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Author>ArtDock</Author><URI>\ArtDock S-1-5-21-1</URI></RegistrationInfo>
              <Settings><Priority>4</Priority><Enabled>false</Enabled></Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"C:\Apps\ArtDock.exe"</Command>
                  <Arguments>--sign-in</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;

        Assert.Equal(new SignInCommand(@"C:\Apps\ArtDock.exe", "--sign-in"), SignInTask.CommandOf(registered));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("""<Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task"><Actions /></Task>""")]
    public void ATaskStartingNothing_ReadsAsNone(string xml) => Assert.Null(SignInTask.CommandOf(xml));

    [Fact]
    public void APathIsTheSame_WhateverItsCase()
    {
        var upper = Command with { Path = Command.Path.ToUpperInvariant() };

        Assert.True(Command.Matches(upper));
        Assert.False(Command.Matches(Command with { Arguments = "" }));
    }
}
