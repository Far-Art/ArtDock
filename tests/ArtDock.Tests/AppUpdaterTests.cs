using System.IO;
using ArtDock.Services;
using Velopack;

namespace ArtDock.Tests;

/// <summary>
/// Covers what the project file tells the updater, where a slip costs the user more than a
/// failed update.
/// </summary>
/// <remarks>
/// <para>
/// Velopack's setup installs into <c>%LOCALAPPDATA%\&lt;install id&gt;</c> and treats that folder
/// as its own: its uninstaller removes it. The settings and the packs live in
/// <c>%LOCALAPPDATA%\ArtDock</c>. An install id of <c>ArtDock</c> — the obvious one, and what
/// Velopack's own examples would suggest — would put the two in one folder, and the first
/// uninstall would take the user's settings and packs with it.
/// </para>
/// <para>
/// The rest: updates are fetched from a public repository over HTTPS, since an installed dock
/// can carry no token; and a copy nobody installed — this test run, or a build run from the
/// source tree — is not taken for an installed one, and starts itself at sign-in from where it
/// is.
/// </para>
/// </remarks>
public class AppUpdaterTests
{
    [Fact]
    public void TheInstallFolder_IsNotWhereTheSettingsAre()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var installFolder = Path.Combine(localData, AppUpdater.InstallId) + Path.DirectorySeparatorChar;
        var settingsFolder = Path.GetDirectoryName(SettingsStore.Location) + Path.DirectorySeparatorChar;

        Assert.False(string.IsNullOrWhiteSpace(AppUpdater.InstallId));
        Assert.False(settingsFolder.StartsWith(installFolder, StringComparison.OrdinalIgnoreCase));
        Assert.False(installFolder.StartsWith(settingsFolder, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Updates_ComeFromAGitHubRepository_OverHttps()
    {
        var repository = new Uri(AppUpdater.ReleasesRepository);

        Assert.Equal(Uri.UriSchemeHttps, repository.Scheme);
        Assert.Equal("github.com", repository.Host);
        Assert.Equal(2, repository.AbsolutePath.Trim('/').Split('/').Length);
    }

    [Fact]
    public void ACopyNobodyInstalled_IsNotTakenForAnInstalledOne()
    {
        Assert.False(new AppUpdater().IsInstalled);
        Assert.Equal(Environment.ProcessPath, AppUpdater.LaunchPath);
    }

    /// <summary>
    /// The same after Velopack has looked for an installation and not found one, which is the
    /// state a build run from the source tree is in — and the About page asks it every time
    /// the settings dialog opens, so anything this threw would take the dialog down.
    /// </summary>
    [Fact]
    public void ACopyNobodyInstalled_AfterVelopackLooked_IsStillNotTakenForOne()
    {
        VelopackApp.Build().SetArgs([]).Run();

        Assert.False(new AppUpdater().IsInstalled);
        Assert.Equal(Environment.ProcessPath, AppUpdater.LaunchPath);
    }
}
