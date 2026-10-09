using System.IO;
using System.Reflection;
using ArtDock.Interop;
using ArtDock.Views;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace ArtDock.Services;

/// <summary>
/// The dock's own updates: installed by Velopack's setup, and updated by it from the releases
/// published to a public GitHub repository.
/// </summary>
/// <remarks>
/// <para>
/// Velopack rather than the updater <c>docs/downloads.md</c> first designed, because it does
/// the part with the risk in it. A running executable cannot replace itself — and the dock is
/// self-contained, so neither can the runtime beside it — so Velopack's <c>Update.exe</c> waits
/// for the dock to exit, swaps the application folder, and starts the new one. It brings the
/// installer that needs, too, and installs per user, so no update ever asks for administrator
/// rights.
/// </para>
/// <para>
/// Only when asked. Nothing here reaches the network until the About page's button is
/// pressed, and nothing is applied until the user presses it again for the version it found:
/// the dock sits on screen all day, and restarting it unannounced would be noticed.
/// </para>
/// </remarks>
public sealed class AppUpdater
{
    private UpdateManager? _manager;

    /// <summary>
    /// The public repository whose releases are the dock's updates, as the project file's
    /// <c>ArtDockReleasesRepository</c> sets it.
    /// </summary>
    public static string ReleasesRepository => Metadata("ReleasesRepository") ?? "";

    /// <summary>
    /// The id Velopack installs the dock under, as the project file's <c>ArtDockInstallId</c>
    /// sets it — and so the name of the folder in local application data it installs into.
    /// </summary>
    public static string InstallId => Metadata("InstallId") ?? "";

    /// <summary>
    /// True when this copy was installed by the setup and can update itself. A build run from
    /// the source tree was not, and has nothing to update.
    /// </summary>
    /// <remarks>
    /// Asked of Velopack's locator rather than of an update manager, so that opening the
    /// settings dialog never makes one: only a check does.
    /// </remarks>
    public bool IsInstalled => InstallRoot() is not null;

    /// <summary>Asks the releases whether there is a newer version than this one.</summary>
    /// <returns>What to update to, or null when this is the newest.</returns>
    public Task<UpdateInfo?> CheckAsync() =>
        Manager?.CheckForUpdatesAsync() ?? Task.FromResult<UpdateInfo?>(null);

    /// <summary>
    /// The release notes of every version <paramref name="update"/> brings, newest first: the
    /// one it lands on and each it passes over (<see cref="ReleaseNotes.Between"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked of the releases again, after the check: Velopack reads every release's feed to
    /// decide what to update to, but hands back only the version it chose. The feed of the
    /// ten newest releases, which is as far back as Velopack looks — and so as far back as an
    /// update goes in steps rather than whole.
    /// </para>
    /// <para>
    /// The notes are a courtesy, and never what stands between the user and the update: should
    /// the second ask fail, the version on offer still has its own, which the check brought.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<ReleaseNote>> NotesAsync(UpdateInfo update)
    {
        var target = update.TargetFullRelease;
        IReadOnlyList<ReleaseNote> own = string.IsNullOrWhiteSpace(target.NotesMarkdown)
            ? []
            : [new ReleaseNote(target.Version, target.NotesMarkdown)];

        if (Source is not { } source || Manager?.CurrentVersion is not { } current)
        {
            return own;
        }

        try
        {
            var feed = await source.GetReleaseFeed(new NullVelopackLogger(), InstallId, Channel());
            var notes = ReleaseNotes.Between(feed.Assets, current, target.Version);
            return notes.Count > 0 ? notes : own;
        }
        catch (Exception)
        {
            // Whatever it was, as for the check: Velopack does not say what it throws.
            return own;
        }
    }

    /// <summary>
    /// Fetches a version found by <see cref="CheckAsync"/>, verified against the release feed's
    /// hashes, while the dock carries on as it was.
    /// </summary>
    /// <param name="progress">Called with 0 to 100, from whichever thread is downloading.</param>
    public Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken cancellationToken) =>
        Manager?.DownloadUpdatesAsync(update, progress, cancellationToken) ?? Task.CompletedTask;

    /// <summary>
    /// Starts Velopack's updater, which waits for this process to exit, puts the downloaded
    /// version in place, and starts it with <paramref name="restartArguments"/>. The caller
    /// exits straight after — the updater gives up if that takes more than a minute.
    /// </summary>
    public void ApplyOnExit(UpdateInfo update, params string[] restartArguments) =>
        Manager?.WaitExitThenApplyUpdates(
            update.TargetFullRelease, silent: false, restart: true, restartArguments);

    /// <summary>
    /// Where Windows should start the dock from at sign-in: the executable itself, or, in a copy
    /// the setup installed, Velopack's launcher beside the application folder.
    /// </summary>
    /// <remarks>
    /// The launcher because it is the path Velopack keeps: it stays where it is across updates
    /// while the folder the dock runs from is replaced, and it is what the Start menu's
    /// shortcut points at.
    /// </remarks>
    public static string? LaunchPath
    {
        get
        {
            var process = Environment.ProcessPath;
            if (process is not null && InstallRoot() is { } root)
            {
                var launcher = Path.Combine(root, Path.GetFileName(process));
                if (File.Exists(launcher))
                {
                    return launcher;
                }
            }

            return process;
        }
    }

    /// <summary>
    /// Velopack's setup has just installed this copy, which starts at sign-in from now on —
    /// one of the two things its setup, updater and uninstaller need of the dock, which
    /// <see cref="Program"/> hands them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Here rather than left to the dock's first run, which would miss it twice over: the
    /// settings folder outlives an uninstall, so a reinstall is not a first run, and a silent
    /// install never starts the dock at all.
    /// </para>
    /// <para>
    /// Whatever the Run key held before. There is only one entry for every copy, and one that
    /// pointed at a copy run from a folder is taken over, since installing is choosing this
    /// one. The setup puts its launcher in the install folder before it calls here, so
    /// <see cref="LaunchPath"/> already finds it. An update does not come here, so autostart
    /// turned off stays off. A setup run over an installed copy does, and so the entry is only
    /// written, never Task Manager's flag cleared: a dock turned off there stays off through it.
    /// </para>
    /// <para>
    /// Not on Windows 10, which the setup lets through (<see cref="SupportedWindows"/>): a dock
    /// that will not run there would only say so again at every sign-in.
    /// </para>
    /// </remarks>
    public static void OnInstalled(SemanticVersion version)
    {
        if (SupportedWindows.IsCurrent)
        {
            Autostart.Register();
        }
    }

    /// <summary>
    /// Velopack's uninstaller is about to remove this installation — the other thing its
    /// setup, updater and uninstaller need of the dock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The autostart entry goes with it, or Windows goes on trying to start a program that is
    /// no longer there. Only an entry that points into this installation, so uninstalling one
    /// copy cannot turn off autostart for another. First, and before anything that could be
    /// slow, so that it happens whatever becomes of the question.
    /// </para>
    /// <para>
    /// Then any dock still running, closed the way its own Exit closes it. Not the copy being
    /// uninstalled — Velopack has already stopped that one, before it runs this — but a copy
    /// run from anywhere else, such as a build from the source tree, which would otherwise
    /// stay on screen after ArtDock was gone, and share the settings the next step may delete.
    /// </para>
    /// <para>
    /// Then the settings, which live outside the installation and so outlive it unless the user
    /// says otherwise — see <see cref="UninstallWindow"/>. Velopack's documentation gives this
    /// hook 30 seconds before it is killed, counted from before it got here, so everything
    /// here is given <see cref="QuestionTime"/> of that and the rest is margin. The 1.2
    /// uninstaller was seen to wait 60 in its log; the documented figure is the one relied on.
    /// </para>
    /// <para>
    /// Last, what Windows wrote down about the installation by itself — the tray icon it lists
    /// in Settings, the launch and switch counts, the jump list, Start's entries — with a second
    /// pass after Velopack has finished, for what is written after this (<see cref="WindowsTraces"/>).
    /// Not settings: none of it is the user's, so it goes whatever the answer to the question.
    /// After the question rather than before, because the question is itself a window Windows
    /// counts.
    /// </para>
    /// </remarks>
    public static void OnUninstalling(SemanticVersion version)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        if (InstallRoot() is { } root)
        {
            Autostart.RemoveIfUnder(root);
        }

        SingleInstance.CloseRunning(TimeSpan.FromSeconds(5));

        if (UninstallWindow.AskToDeleteSettings(QuestionTime - clock.Elapsed))
        {
            SettingsStore.DeleteFolder();
        }

        if (InstallRoot() is { } folder && InstallId.Length > 0)
        {
            new WindowsTraces(folder, InstallId).RemoveAll();
        }
    }

    /// <summary>
    /// How long the uninstall hook may take, closing the dock and the question together, of the
    /// 30 seconds Velopack allows.
    /// </summary>
    private static readonly TimeSpan QuestionTime = TimeSpan.FromSeconds(25);

    /// <summary>The folder a copy the setup installed lives in, or null for any other copy.</summary>
    private static string? InstallRoot()
    {
        try
        {
            return VelopackLocator.IsCurrentSet
                && VelopackLocator.Current is { CurrentlyInstalledVersion: not null, RootAppDir: { Length: > 0 } root }
                    ? root
                    : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Made when first asked for, by a check, rather than with the dialog; null in a copy that
    /// was not installed, or a build that was not told where its releases are. Whatever
    /// Velopack throws making one reaches the caller, which is already catching the check's.
    /// </summary>
    private UpdateManager? Manager =>
        _manager ??= IsInstalled && Source is { } source
            ? new UpdateManager(source)
            : null;

    /// <summary>The releases, for the check and for their notes alike; null where there are none to ask.</summary>
    private GithubSource? Source =>
        _source ??= ReleasesRepository.Length > 0
            ? new GithubSource(ReleasesRepository, accessToken: null, prerelease: false)
            : null;

    private GithubSource? _source;

    /// <summary>
    /// The channel this copy was packed for, which names the feed each release carries — the
    /// one Velopack checks. Windows' own, <c>win</c>, unless the package says otherwise.
    /// </summary>
    private static string Channel()
    {
        try
        {
            return VelopackLocator.IsCurrentSet && VelopackLocator.Current.Channel is { Length: > 0 } channel
                ? channel
                : "win";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return "win";
        }
    }

    private static string? Metadata(string key) =>
        typeof(AppUpdater).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)?.Value;
}
