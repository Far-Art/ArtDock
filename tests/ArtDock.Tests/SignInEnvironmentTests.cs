using ArtDock.Interop;

namespace ArtDock.Tests;

/// <summary>
/// Covers the dock taking the user's own environment as it starts, so that what it launches is
/// not handed the variables of whatever shell started the dock.
/// </summary>
/// <remarks>
/// The environment is never adopted here: that would change the test host's own.
/// </remarks>
public class SignInEnvironmentTests
{
    private static Dictionary<string, string> Env(params (string Name, string Value)[] variables) =>
        variables.ToDictionary(pair => pair.Name, pair => pair.Value);

    [Fact]
    public void AShellsVariable_IsTakenOut()
    {
        var (remove, set) = SignInEnvironment.Changes(
            Env(("Path", @"C:\Windows"), ("GIT_EDITOR", "true"), ("CLAUDECODE", "1")),
            Env(("Path", @"C:\Windows")));

        Assert.Equal(["CLAUDECODE", "GIT_EDITOR"], remove.Order());
        Assert.Empty(set);
    }

    [Fact]
    public void AVariableTheShellChanged_IsPutBack_AndOneItLacked_IsAdded()
    {
        var (remove, set) = SignInEnvironment.Changes(
            Env(("Path", @"C:\Tools;C:\Windows")),
            Env(("Path", @"C:\Windows"), ("SESSIONNAME", "Console")));

        Assert.Empty(remove);
        Assert.Equal([("Path", @"C:\Windows"), ("SESSIONNAME", "Console")], set.OrderBy(pair => pair.Name));
    }

    [Fact]
    public void Names_CompareWithoutCase_AsWindowsCompares()
    {
        var (remove, set) = SignInEnvironment.Changes(Env(("PATH", @"C:\Windows")), Env(("Path", @"C:\Windows")));

        Assert.Empty(remove);
        Assert.Empty(set);
    }

    [Fact]
    public void ADrivesCurrentDirectory_IsLeftAlone()
    {
        // =C: is the process's own state, not a variable: neither taken out nor set.
        var (remove, set) = SignInEnvironment.Changes(Env(("=C:", @"C:\Work")), Env(("=D:", @"D:\")));

        Assert.Empty(remove);
        Assert.Empty(set);
    }

    [Fact]
    public void TheUsersEnvironment_IsTheirs()
    {
        var fresh = SignInEnvironment.Read();

        Assert.NotNull(fresh);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fresh["USERPROFILE"], ignoreCase: true);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.Windows), fresh["SystemRoot"], ignoreCase: true);
        Assert.True(fresh.ContainsKey("Path"));
    }
}
