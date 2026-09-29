using System.ComponentModel;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ArtDock.Interop;
using ArtDock.Localization;
using ArtDock.Services;
using Microsoft.Win32;

namespace ArtDock.Views;

/// <summary>
/// Finds the programs of the games on this machine, and lets the ones to exclude be ticked.
/// </summary>
/// <remarks>
/// <para>
/// For the game nobody starts by hand. Battle.net starts StarCraft II, and nothing about that
/// says which of the programs in its folder is the one whose window fills the screen — see
/// <see cref="ProgramScan"/>. So the dialog does not ask where to look: it finds the games the
/// launchers on this machine have installed (<see cref="GameLibraries"/>) and looks through
/// all of them, and <em>Add a folder…</em> is there for whatever they do not know about.
/// </para>
/// <para>
/// The list is a heading per game with its programs under it, and a heading has a tick of its
/// own that ticks the lot: someone who knows the game and not its programs ticks the game. The
/// programs giving the same name are one row within it.
/// </para>
/// <para>
/// The work runs on a thread of its own and says how far it has got every tenth of a second,
/// from a timer rather than from the work itself — a library can hold thousands of programs,
/// and a dispatcher call for each would be the slowest part of looking. The thread is STA,
/// because reading icons is a shell call. Stop keeps what was found so far.
/// </para>
/// </remarks>
public sealed partial class ScanWindow : Window
{
    private readonly ScanKind _kind;

    /// <summary>What each installed app is called in the Start menu, by its program's path.</summary>
    private readonly Dictionary<string, string> _titles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The edge length the row icons are read at: 28 DIPs, crisp at up to 200%.</summary>
    private const int IconPixels = 64;

    /// <summary>The file names already on the list, which are shown ticked and greyed.</summary>
    private readonly IReadOnlyCollection<string> _listed;

    private readonly DispatcherTimer _progress = new() { Interval = TimeSpan.FromMilliseconds(100) };

    // ---- what has been found, across every look ---------------------------------------
    //
    // Touched by the worker while it runs and by this thread once it has finished; never by
    // both at once, because a look is only started when none is running.

    private readonly List<GameFolder> _roots = [];
    private readonly List<FoundFile> _files = [];
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProgramFacts> _facts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ScannedApp> _apps = [];

    private CancellationTokenSource _stop = new();
    private bool _closed;
    private bool _stoppedByUser;

    private List<ScanGame> _games = [];

    /// <summary>The row made for each app, so a change of order moves rows rather than making new ones.</summary>
    private readonly Dictionary<ScannedApp, ScanRow> _rowOf = new(ReferenceEqualityComparer.Instance);

    /// <summary>What is typed into the filter.</summary>
    private string _filter = string.Empty;

    // ---- progress, written by the worker, read by the timer ------------------------------

    private enum Phase { Libraries, Apps, Walking, Naming, Icons }

    private volatile Phase _phase;
    private int _folders;
    private int _found;
    private int _done;
    private int _total;

    public ScanWindow(ScanKind kind, IEnumerable<string> listed)
    {
        InitializeComponent();

        _kind = kind;
        _listed = [.. listed];
        HeadingText.Text = Localizer.Get("Scan.Title");
        HintText.Text = Localizer.Get(kind == ScanKind.Games ? "Scan.Hint" : "Scan.Hint.Apps");

        StopButton.Click += (_, _) =>
        {
            _stoppedByUser = true;
            StopButton.IsEnabled = false;
            _stop.Cancel();
        };

        AddButton.Click += (_, _) => DialogResult = true;

        // Most relevant first is what someone who does not know the program wants; by name
        // is for someone who does and wants to find it.
        SortBox.Items.Add(Localizer.Get("Scan.Sort.Relevance"));
        SortBox.Items.Add(Localizer.Get("Scan.Sort.Name"));
        SortBox.SelectedIndex = 0;
        SortBox.SelectionChanged += (_, _) => ShowInOrder();

        FilterBox.TextChanged += (_, _) =>
        {
            _filter = FilterBox.Text;
            FilterPrompt.Visibility = FilterBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            FilterRows();
        };

        // Escape empties the filter before it closes the dialog, and Enter in it is not Add:
        // both are keys a person typing a filter presses meaning the filter.
        FilterBox.PreviewKeyDown += (_, key) =>
        {
            if (key.Key == System.Windows.Input.Key.Escape && FilterBox.Text.Length > 0)
            {
                FilterBox.Clear();
                key.Handled = true;
            }
            else if (key.Key == System.Windows.Input.Key.Enter)
            {
                key.Handled = true;
            }
        };
        AddFolderButton.Click += async (_, _) => await AddFolderAsync();

        _progress.Tick += (_, _) => ShowProgress();

        Loaded += async (_, _) => await LookAsync(findSources: true, folder: null);

        // Closing mid-look is the other way to stop it, and nothing is kept.
        Closed += (_, _) =>
        {
            _closed = true;
            _progress.Stop();
            _stop.Cancel();
        };
    }

    /// <summary>
    /// The programs to add: one path for each ticked program not already on the list.
    /// </summary>
    public IReadOnlyList<string> Chosen =>
    [
        .. _games
            .SelectMany(game => game.Rows)
            .Where(row => row.IsChecked && row.CanAdd)
            .SelectMany(row => row.App.Programs)
            .Where(program => !IsListed(program))
            .DistinctBy(program => program.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(program => program.Path)
    ];

    private bool IsListed(ScannedProgram program) => FullscreenApps.Contains(_listed, program.Path);

    // ---- looking ------------------------------------------------------------------------

    private async Task AddFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = Localizer.Get("Scan.AddFolder.Title"),
            DefaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var path = dialog.FolderName;
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        await LookAsync(findSources: false, new GameFolder(string.IsNullOrEmpty(name) ? path : name, path, null));
    }

    /// <summary>
    /// Finds the games or the apps and looks through them, or looks through one more folder.
    /// </summary>
    private async Task LookAsync(bool findSources, GameFolder? folder)
    {
        _stop = new CancellationTokenSource();
        _phase = !findSources ? Phase.Walking : _kind == ScanKind.Games ? Phase.Libraries : Phase.Apps;
        Volatile.Write(ref _folders, 0);
        Volatile.Write(ref _found, 0);

        AddFolderButton.IsEnabled = false;
        AddButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        ProgressPanel.Visibility = Visibility.Visible;
        Results.Visibility = Visibility.Collapsed;
        MessageText.Visibility = Visibility.Collapsed;
        ShowProgress();
        _progress.Start();

        string? failure = null;
        var token = _stop.Token;
        try
        {
            failure = await OnStaThread(() => Look(findSources, folder, token));
        }
        finally
        {
            _progress.Stop();
        }

        if (_closed)
        {
            return;
        }

        AddFolderButton.IsEnabled = true;
        ShowResults();

        if (failure is not null)
        {
            MessageBox.Show(this, Localizer.Format("Scan.Failed", failure), "ArtDock", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// The whole of one look, off the UI thread. Returns why a chosen folder could not be read,
    /// or null.
    /// </summary>
    /// <remarks>
    /// A library that cannot be read is passed over quietly — the dock found it, not the user.
    /// A folder the user chose and the dock could not read is said out loud.
    /// </remarks>
    private string? Look(bool findSources, GameFolder? folder, CancellationToken stop)
    {
        var roots = new List<GameFolder>();
        if (findSources && _kind == ScanKind.Games)
        {
            roots.AddRange(GameLibraries.Find());
        }
        else if (findSources)
        {
            // Installed apps are not walked for: each shortcut already names its one program.
            // The games are asked for only to be left out, since they have a scan of their own.
            foreach (var app in InstalledApps.Find(GameLibraries.Find().Select(game => game.Path)))
            {
                if (_seen.Add(app.Path))
                {
                    _files.Add(new FoundFile(app.Path, string.Empty));
                    _titles[app.Path] = app.Name;
                    Interlocked.Increment(ref _found);
                }
            }
        }

        if (folder is not null)
        {
            roots.Add(folder);
        }

        _roots.AddRange(roots);
        _phase = Phase.Walking;

        string? failure = null;
        var counted = 0;
        foreach (var root in roots)
        {
            if (stop.IsCancellationRequested)
            {
                break;
            }

            var before = counted;
            try
            {
                foreach (var path in ProgramScan.Executables(
                             root.Path, stop, count => Volatile.Write(ref _folders, before + count)))
                {
                    if (stop.IsCancellationRequested)
                    {
                        break;
                    }

                    // First come, first kept: a folder inside another is the outer one's.
                    if (_seen.Add(path))
                    {
                        _files.Add(new FoundFile(path, root.Name));
                        Interlocked.Increment(ref _found);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
            {
                if (root == folder)
                {
                    failure = e.Message;
                }
            }

            counted = Volatile.Read(ref _folders);
        }

        // Names, for every program not named in an earlier look.
        _phase = Phase.Naming;
        Volatile.Write(ref _done, 0);
        Volatile.Write(ref _total, _files
            .Select(file => (file.Game.ToUpperInvariant(), FullscreenApps.KeyOf(file.Path).ToUpperInvariant()))
            .Distinct()
            .Count());

        // Installers, updaters, crash reporters and the like are left out altogether, not
        // hidden behind a switch: none of them is the window a game plays in, and a list
        // that offered them only gave the user more to read past.
        _apps = [.. ProgramScan.Arrange(_files, ModifiedOrNever, path =>
        {
            Interlocked.Increment(ref _done);
            if (!_facts.TryGetValue(path, out var facts))
            {
                facts = stop.IsCancellationRequested ? Unread(path) : Read(path);

                // An installed app goes by its Start menu name, which is the one the user
                // knows it by — "Word", not "Microsoft Word".
                if (_titles.TryGetValue(path, out var title))
                {
                    facts = facts with { Name = title, Described = true };
                }

                _facts[path] = facts;
            }

            return facts;
        }).Where(app => !app.IsHelper)];

        // An icon per row, from its first program — the programs in a row are one app.
        _phase = Phase.Icons;
        Volatile.Write(ref _done, 0);
        Volatile.Write(ref _total, _apps.Count);
        foreach (var app in _apps)
        {
            Interlocked.Increment(ref _done);
            var path = app.Programs[0].Path;
            if (!_icons.ContainsKey(path) && !stop.IsCancellationRequested)
            {
                _icons[path] = ShellIcons.Load(path, IconPixels);
            }
        }

        return failure;
    }

    /// <summary>What a program says about itself, and whether it carries an icon.</summary>
    private static ProgramFacts Read(string path)
    {
        var description = FullscreenApps.Description(path);
        return new ProgramFacts(
            description ?? Path.GetFileNameWithoutExtension(path),
            Described: description is not null,
            HasIcon: ShellIcons.HasOwnIcon(path),
            Size: SizeOrNothing(path),
            IsSystem: InstalledApps.IsInside(path, WindowsFolder));
    }

    private static readonly string WindowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <summary>
    /// A program that was not read, because the look was stopped: its file name still tells
    /// it apart from the others, and costs nothing.
    /// </summary>
    private static ProgramFacts Unread(string path) =>
        new(Path.GetFileNameWithoutExtension(path), Described: false, HasIcon: false, Size: 0);

    private static long SizeOrNothing(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static DateTime ModifiedOrNever(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>Runs work on a background thread of its own, in a single-threaded apartment.</summary>
    private static Task<T> OnStaThread<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        })
        {
            IsBackground = true,
            Name = "ArtDock program scan"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private void ShowProgress()
    {
        switch (_phase)
        {
            case Phase.Libraries:
                ProgressText.Text = Localizer.Get("Scan.Progress.Libraries");
                ProgressDetail.Text = string.Empty;
                break;

            case Phase.Apps:
                ProgressText.Text = Localizer.Get("Scan.Progress.Apps");
                ProgressDetail.Text = string.Empty;
                break;

            case Phase.Walking:
                ProgressText.Text = Localizer.Format("Scan.Progress.Folders", Volatile.Read(ref _folders));
                ProgressDetail.Text = Localizer.Format("Scan.Progress.Found", Volatile.Read(ref _found));
                break;

            default:
                ProgressText.Text = Localizer.Format(
                    _phase == Phase.Naming ? "Scan.Progress.Naming" : "Scan.Progress.Icons",
                    Volatile.Read(ref _done),
                    Math.Max(Volatile.Read(ref _done), Volatile.Read(ref _total)));
                break;
        }
    }

    // ---- showing ------------------------------------------------------------------------

    private void ShowResults()
    {
        ProgressPanel.Visibility = Visibility.Collapsed;
        StoppedText.Visibility = _stoppedByUser ? Visibility.Visible : Visibility.Collapsed;
        HeadingText.Text = _kind == ScanKind.Apps ? Localizer.Get("Scan.Heading.Apps")
            : _roots.Count == 0 ? Localizer.Get("Scan.Heading.Nothing")
            : Localizer.Format("Scan.Heading", string.Join(", ", Sources()));
        HeadingText.ToolTip = _roots.Count == 0 ? null : string.Join(Environment.NewLine, _roots.Select(root => root.Path));

        if (_kind == ScanKind.Games && _roots.Count == 0)
        {
            ShowMessage(Localizer.Get("Scan.NoLibraries"));
            return;
        }

        if (_apps.Count == 0)
        {
            ShowMessage(Localizer.Get("Scan.None"));
            return;
        }

        // Ticks carried over from before a folder was added, by what the row is rather than
        // by the object, since the rows are built again.
        var ticked = _games
            .SelectMany(game => game.Rows)
            .Where(row => row.IsChecked && row.CanAdd)
            .Select(row => row.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Asked now: a game started while the libraries were being looked through counts.
        var open = RunningAppsService.WithWindows()
            .Select(FullscreenApps.KeyOf)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sources = _roots
            .GroupBy(root => root.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => SourceOf(group.First()), StringComparer.OrdinalIgnoreCase);

        _rowOf.Clear();
        var games = new Dictionary<string, ScanGame>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in _apps)
        {
            if (!games.TryGetValue(app.Game, out var game))
            {
                game = new ScanGame(app.Game, sources.GetValueOrDefault(app.Game, string.Empty), IsRowVisible);
                games[app.Game] = game;
            }

            var row = new ScanRow(
                game,
                app,
                underHeading: _kind == ScanKind.Games,
                _icons.GetValueOrDefault(app.Programs[0].Path),
                isOpen: app.Programs.Any(program => open.Contains(program.FileName)),
                listed: app.Programs.Count(IsListed));

            if (ticked.Contains(row.Key))
            {
                row.IsChecked = true;
            }

            var owner = game;
            row.PropertyChanged += (_, _) =>
            {
                owner.Refresh();
                UpdateAddButton();
            };

            game.Rows.Add(row);
            _rowOf[app] = row;
        }

        _games = [.. games.Values];

        ListTools.IsEnabled = true;
        Results.Visibility = Visibility.Visible;
        ShowInOrder();
        UpdateAddButton();
    }

    /// <summary>
    /// Lays the rows out in the order chosen, each game's heading before its rows.
    /// </summary>
    /// <remarks>
    /// The same row objects every time, only reordered, so a tick survives a change of order
    /// without having to be carried across.
    /// </remarks>
    private void ShowInOrder()
    {
        if (_rowOf.Count == 0)
        {
            return;
        }

        // Apps are one list with no headings: a heading per app would only say its name twice,
        // and one over all of them would be a tick that excludes everything installed.
        var perGame = _kind == ScanKind.Games;
        var ordered = SortBox.SelectedIndex == 1
            ? ProgramScan.ByName(_apps, perGame)
            : ProgramScan.ByRelevance(_apps, app => _rowOf[app].IsOpen, perGame);

        var items = new List<object>();
        ScanGame? current = null;
        foreach (var app in ordered)
        {
            var row = _rowOf[app];
            if (perGame && row.Game != current)
            {
                current = row.Game;
                items.Add(current);
            }

            items.Add(row);
        }

        Results.ItemsSource = items;
        FilterRows();
    }

    /// <summary>Where the rows came from, for the heading: launchers by name, folders by their own.</summary>
    private IEnumerable<string> Sources() =>
        _roots
            .Select(SourceOf)
            .Distinct(StringComparer.CurrentCultureIgnoreCase);

    private static string SourceOf(GameFolder root) => root.Launcher switch
    {
        // Named key by key rather than built from the enum, so the string table's test can
        // see every one of them used.
        GameLauncher.Steam => Localizer.Get("Library.Steam"),
        GameLauncher.BattleNet => Localizer.Get("Library.BattleNet"),
        GameLauncher.EaApp => Localizer.Get("Library.EaApp"),
        GameLauncher.EpicGames => Localizer.Get("Library.EpicGames"),
        GameLauncher.Gog => Localizer.Get("Library.Gog"),
        GameLauncher.UbisoftConnect => Localizer.Get("Library.UbisoftConnect"),
        GameLauncher.Xbox => Localizer.Get("Library.Xbox"),
        GameLauncher.RiotGames => Localizer.Get("Library.RiotGames"),
        GameLauncher.RockstarGames => Localizer.Get("Library.RockstarGames"),
        _ => root.Name
    };

    private void ShowMessage(string message)
    {
        Results.Visibility = Visibility.Collapsed;
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Whether a row answers to the filter. A row the filter hides keeps its tick — the
    /// filter is for finding things, not for choosing them — but a game's heading ticks only
    /// the rows that can be seen.
    /// </summary>
    private bool IsRowVisible(ScanRow row) => ProgramScan.Matches(row.App, _filter);

    /// <summary>Shows the rows that answer to the filter, and the games they are in.</summary>
    private void FilterRows()
    {
        if (Results.ItemsSource is { } items)
        {
            CollectionViewSource.GetDefaultView(items).Filter = item => item switch
            {
                ScanRow row => IsRowVisible(row),
                ScanGame game => game.Rows.Any(IsRowVisible),
                _ => true
            };
        }

        foreach (var game in _games)
        {
            game.Refresh();
        }

        // An empty list after typing says why, rather than looking like a scan that found nothing.
        if (Results.Visibility == Visibility.Visible || MessageText.Tag is "filter")
        {
            var nothing = _filter.Trim().Length > 0 && !_games.Any(game => game.Rows.Any(IsRowVisible));
            MessageText.Text = nothing ? Localizer.Format("Scan.NoMatch", _filter.Trim()) : string.Empty;
            MessageText.Tag = nothing ? "filter" : null;
            MessageText.Visibility = nothing ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateAddButton() =>
        AddButton.IsEnabled = _games.Any(game => game.Rows.Any(row => row.IsChecked && row.CanAdd));
}

/// <summary>What a scan looks for.</summary>
public enum ScanKind
{
    /// <summary>The games the launchers installed, and every program in them.</summary>
    Games,

    /// <summary>The apps installed on this machine, from their Start menu shortcuts.</summary>
    Apps
}

/// <summary>A game's heading in the scan, whose tick is every one of its programs.</summary>
/// <param name="name">The game.</param>
/// <param name="source">Where it was found: its launcher, or the folder the user added.</param>
/// <param name="isShown">Whether a row is on show — a row the filter hides is not the heading's to tick.</param>
public sealed class ScanGame(string name, string source, Func<ScanRow, bool> isShown) : INotifyPropertyChanged
{
    private bool _setting;

    public string Name { get; } = name;

    public string Source { get; } = source;

    public List<ScanRow> Rows { get; } = [];

    private IEnumerable<ScanRow> Shown => Rows.Where(isShown);

    /// <summary>Ticked when every program on show is, unticked when none is, a dash between.</summary>
    public bool? IsChecked
    {
        get
        {
            var shown = Shown.ToList();
            return shown.All(row => row.IsChecked) ? true
                : shown.Any(row => row.IsChecked) ? null
                : false;
        }
        set
        {
            _setting = true;
            try
            {
                foreach (var row in Shown.Where(row => row.CanAdd))
                {
                    row.IsChecked = value == true;
                }
            }
            finally
            {
                _setting = false;
            }

            Refresh();
        }
    }

    /// <summary>False once everything on show is on the list already.</summary>
    public bool CanAdd => Shown.Any(row => row.CanAdd);

    public void Refresh()
    {
        if (_setting)
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanAdd)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One row of the scan: an app within a game, and whether it is ticked.</summary>
/// <param name="underHeading">Whether the row sits under its game's heading, and is indented to say so.</param>
/// <param name="listed">How many of its programs are on the list already.</param>
public sealed class ScanRow(ScanGame game, ScannedApp app, bool underHeading, ImageSource? icon, bool isOpen, int listed)
    : INotifyPropertyChanged
{
    public Thickness Margin { get; } = underHeading ? new Thickness(28, 3, 0, 3) : new Thickness(0, 3, 0, 3);

    public ScanGame Game { get; } = game;

    public ScannedApp App { get; } = app;

    /// <summary>What the row is, to carry its tick across a rebuild of the list.</summary>
    public string Key => $"{App.Game}|{App.Name}|{App.IsHelper}";

    public ImageSource? Icon { get; } = icon;

    public string Name => App.Name;

    /// <summary>The file names, which is what will be matched.</summary>
    public string Files => string.Join(", ", App.Programs.Select(program => program.FileName));

    /// <summary>Where each one is, for the tooltip.</summary>
    public string Paths => string.Join(Environment.NewLine, App.Programs.Select(program => program.Path));

    public bool IsOpen { get; } = isOpen;

    /// <summary>Every program in it is on the list already, so it is shown ticked and greyed.</summary>
    public bool IsListed => listed == App.Programs.Count;

    /// <summary>
    /// Some of it is on the list — typically the game, added while it was running, with the
    /// launcher that starts it not yet. Ticking the row adds the rest.
    /// </summary>
    public bool HasListed => listed > 0;

    public string ListedText => IsListed
        ? Localizer.Get("Scan.Listed")
        : Localizer.Format("Scan.PartlyListed", listed, App.Programs.Count);

    public bool CanAdd => !IsListed;

    public bool IsChecked
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    } = listed == app.Programs.Count;

    public event PropertyChangedEventHandler? PropertyChanged;
}
