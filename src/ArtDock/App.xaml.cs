using System.Windows;
using ArtDock.Localization;
using ArtDock.Services;
using ArtDock.Views;

namespace ArtDock;

/// <summary>
/// Application entry point. Owns the settings store, the dock window and the tray icon, and
/// keeps the app alive when no window is open — closing the settings dialog must not exit.
/// </summary>
public partial class App : Application
{
    private SettingsStore? _settings;
    private DockWindow? _dockWindow;
    private SettingsWindow? _settingsWindow;
    private AlreadyRunningWindow? _alreadyRunning;
    private TrayIcon? _tray;
    private MenuHost? _menus;
    private MemoryTrim? _memoryTrim;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The dock has no main window in the usual sense; it lives in the tray.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Before anything is created. A second dock would overlap the first on the same
        // screen edge, add a second identical tray icon, and race it for the settings file.
        if (!SingleInstance.TryClaim())
        {
            // Reported rather than acted on: whether this launch wants an answer depends on
            // whether the dock can be seen, and only the copy already running knows that.
            SingleInstance.NotifyExisting(showSettings: WantsSettings(e.Args));
            Shutdown();
            return;
        }

        Interop.Autostart.RemoveLegacyEntry();

        _settings = new SettingsStore();
        var settings = _settings.Load();
        var firstRun = !_settings.LoadedFromDisk;

        // A dock starting for the first time where WPF finds nothing to draw with starts with
        // No GPU on, and says so once it is up — see ShowNoGpuNotice. Only then: a dock that
        // has settings has an owner who has seen the checkbox, and is left as they have it.
        var noGpuAdopted = settings.AdoptNoGpu(firstRun, SoftwareRendering.HardwareMissing);

        // Before any window exists, so that none is ever given a Direct3D device to give back.
        SoftwareRendering.Apply(settings.NoGpu);

        ApplyTheme(settings);

        // Gives the theme's drop-down lists and tooltips room for their shadows, which it cuts off.
        PopupShadows.Register();

        // And has every dialog opened under No GPU come up on the theme's plain background.
        WindowMaterial.Register();

        // Before any window or menu exists, so that everything is built in the right language
        // rather than built in English and then changed.
        Localizer.Apply(settings.Language);

        // A fresh install has no pin list; seed one — Start, This PC, the user folder,
        // Downloads, Settings and, past a separator, the Recycle Bin — and write it out, so
        // what the dock shows is immediately editable in the settings dialog. After the
        // language, so the labels are in it.
        //
        // Only when there was no settings file at all. Seeding whenever the list happened to
        // be empty was fine while nothing could empty it deliberately, but the Items page's
        // Clear all can — and the stock apps reappearing on the next launch made clearing
        // look like it had not worked.
        //
        // Written out as well when No GPU was turned on just now, which is as much a part of
        // what this dock starts with as its pins are.
        if (firstRun && (new PinnedAppsService().SeedIfEmpty(settings) || noGpuAdopted))
        {
            _settings.Save(settings);
        }

        // Starting at sign-in is the default, and the checkbox reads the Run key rather than
        // the file, so the first run is what makes it true. Only when there is no entry at
        // all: one that is there already belongs to another copy of the dock, and is left to
        // it — as is one turned off in Task Manager, which is why this is not IsEnabled.
        if (firstRun && settings.RunAtLogin && !Interop.Autostart.IsRegistered())
        {
            Interop.Autostart.Register();
        }

        // One owner for every context menu in the app; see MenuHost for why they need one.
        _menus = new MenuHost();

        _dockWindow = new DockWindow(_settings, _menus);
        _dockWindow.SettingsRequested += (_, _) => ShowSettings();

        // The dialog has no watch on the bin of its own; the dock's tells its list as well.
        _dockWindow.RecycleBinIconChanged += (_, _) =>
        {
            if (_settingsWindow is { IsLoaded: true })
            {
                _settingsWindow.RefreshRecycleBinRows();
            }
        };
        _dockWindow.Show();

        // Now that there is a dock to ask about, a later launch has somewhere to be sent — and
        // the uninstaller somewhere to ask for the dock to go, which it does the way Exit does.
        SingleInstance.Listen(OnRelaunched, ShowSettings, Quit);

        _tray = new TrayIcon(_menus);
        _tray.SettingsRequested += (_, _) => ShowSettings();
        _tray.ToggleRequested += (_, _) => ToggleDock();
        _tray.ExitRequested += (_, _) => Quit();

        // Gives back what a dialog, a menu or a run of the wave leaves behind, once the dock
        // has gone quiet again. Without it the dock holds its busiest moment indefinitely.
        _memoryTrim = new MemoryTrim();

        // Lets a shortcut open the dialog straight away, rather than making the tray icon
        // the only route to it.
        if (WantsSettings(e.Args))
        {
            ShowSettings();
        }

        if (noGpuAdopted)
        {
            // Once the dock is up and drawn, so that what the notice describes is there to be
            // seen behind it.
            Dispatcher.BeginInvoke(ShowNoGpuNotice, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>
    /// Says that the dock has turned No GPU on by itself, and where to turn it off.
    /// </summary>
    /// <remarks>
    /// The one time the dock decides it: a first run, with no hardware to draw with. A dock that
    /// came up solid and unblurred with nothing said would look broken to anyone who had seen it
    /// elsewhere, and the checkbox that explains it is three clicks away on a page nobody opens
    /// first. Windows' own message box rather than a window of the dock's: it is the one thing
    /// here that costs a machine with no graphics card nothing to draw.
    /// </remarks>
    private static void ShowNoGpuNotice() =>
        MessageBox.Show(
            Localizer.Get("FirstRun.NoGpu"),
            "ArtDock",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    private static bool WantsSettings(string[] args) =>
        args.Any(arg => arg.Equals("--settings", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// ArtDock was launched again while this copy is running.
    /// </summary>
    /// <remarks>
    /// Always answered, so running the exe again never looks like a launch that failed: a
    /// notice says ArtDock is already running, and where the dock is — or, when it cannot be
    /// seen, why not and how to get it back. The dock is lifted above whatever covers it as
    /// well, since a dock buried under a maximised window is out of sight in a way the notice
    /// cannot detect. It used to open the settings dialog instead, which answered a question
    /// nobody had asked.
    /// </remarks>
    private void OnRelaunched()
    {
        if (_dockWindow is not { } dock)
        {
            return;
        }

        var presence = dock.Presence;

        // One notice however many times the exe is run, brought up to date: the dock may
        // have come or gone since it opened.
        if (_alreadyRunning is { } open)
        {
            open.SetPresence(presence);
            open.Activate();
            dock.Raise();
            return;
        }

        var notice = new AlreadyRunningWindow(presence);
        notice.ShowDockRequested += (_, _) =>
        {
            if (!dock.IsDockShown)
            {
                ToggleDock();
            }
        };
        notice.ExitRequested += (_, _) => Quit();
        notice.Closed += (_, _) => _alreadyRunning = null;

        // After the notice's own Loaded, which takes the foreground: until this process holds
        // it, Windows will not lift the dock over the window that does.
        notice.Loaded += (_, _) => dock.Raise();

        _alreadyRunning = notice;
        notice.Show();
    }

    /// <summary>
    /// Puts the dialogs on the chosen appearance.
    /// </summary>
    /// <remarks>
    /// <see cref="ThemeMode"/> is WPF's own switch into the Fluent theme's light or dark
    /// resources, and <c>ThemeMode.System</c> follows Windows without this app having to
    /// watch for the change. Set on the application rather than per window, so every dialog
    /// agrees.
    /// </remarks>
    private void ApplyTheme(DockSettings settings)
    {
        // Only on a real change. Assigning ThemeMode re-merges the Fluent dictionaries, and
        // this runs on every tick of every slider in the settings dialog — doing that work
        // sixty times a drag made the whole dialog, and the dock preview with it, crawl.
        var mode = AppTheme.Resolve(settings.Theme);
        if (ThemeMode != mode)
        {
            ThemeMode = mode;

            // A change of theme puts every window back on the backdrop, as the theme does for a
            // new one, so under No GPU they are taken off it again.
            if (SoftwareRendering.IsOn)
            {
                WindowMaterial.ApplyToOpen(solid: true);
            }
        }
    }

    /// <summary>
    /// Draws the dock with or without a graphics card, as <see cref="DockSettings.NoGpu"/> says:
    /// the render mode, and with it what the open dialogs sit on.
    /// </summary>
    /// <remarks>
    /// Only on a change, like the theme: this runs on every tick of every slider. After the
    /// theme has been applied, since the dialogs' title bars are matched to it here.
    /// </remarks>
    private static void ApplyRendering(DockSettings settings)
    {
        if (SoftwareRendering.Apply(settings.NoGpu))
        {
            WindowMaterial.ApplyToOpen(solid: settings.NoGpu);
        }
    }

    private void ShowSettings()
    {
        if (_settings is null)
        {
            return;
        }

        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        // Hold the dock on screen for as long as the dialog is open: tuning the dock's
        // appearance while it keeps sliding away is not workable.
        // Hold the dock out and hold the wave up: the appearance sliders all describe the
        // magnified state, so a dock sitting flat at rest shows none of their effect.
        _settingsWindow = new SettingsWindow(_settings);

        // Changes go straight to the dock so they can be seen, but nothing is written until
        // Save. ApplySettings skips the expensive contents rebuild when only geometry moved,
        // so this stays cheap enough to run on every tick of a slider.
        _settingsWindow.Previewed += (_, updated) =>
        {
            // The theme, the language and how the dock is drawn are previewed like everything
            // else, so picking one shows it before Save rather than after. All are cheap when
            // unchanged, which they are on every tick of every slider.
            ApplyTheme(updated);
            Localizer.Apply(updated.Language);
            ApplyRendering(updated);
            _dockWindow?.ApplySettings(updated);
        };

        // Editing an item from the Items page holds that item's label open on the dock, the
        // way the dock's own menu does. The dialog cannot reach the dock itself, so the
        // routing lives here. Letting go puts back the demonstration the dialog was showing
        // before the editor opened, which the dock does by itself while the dialog is open.
        _settingsWindow.ItemBeingEdited += (_, item) => _dockWindow?.HoldItemLabel(item);

        // And selecting a row there holds that item up on the dock, as if pointed at, so the
        // row and the icon it describes are seen to be the same thing.
        _settingsWindow.ItemSelected += (_, id) => _dockWindow?.PreviewItem(id);

        _settingsWindow.ItemLabelPreviewed += (_, label) => _dockWindow?.PreviewItemLabel(label);

        _settingsWindow.ExitRequested += (_, _) => Quit();

        _dockWindow?.HoldRevealed(true);
        _dockWindow?.PreviewMagnification(true);

        var dialog = _settingsWindow;
        _settingsWindow.Closed += (_, _) =>
        {
            // Cancelled, or closed with the X: put the dock back the way it was found.
            // Everything shown since it opened was a preview over unsaved values — except
            // which page was open, which is where the user was rather than something they
            // were editing, and is kept either way.
            if (!dialog.Saved && _settings is { } store)
            {
                var restored = store.Current.Clone();
                restored.SettingsPage = dialog.SelectedPage;

                ApplyTheme(restored);
                Localizer.Apply(restored.Language);
                ApplyRendering(restored);

                // Saving is what puts the dock back: the store raises Changed, and
                // ApplySettings rebuilds from the values as they were before the preview.
                store.Save(restored);
            }

            _settingsWindow = null;
            _dockWindow?.PreviewMagnification(false);
            _dockWindow?.HoldRevealed(false);
        };

        _settingsWindow.Show();

        // Explicitly, because this is not always a foreground process asking: a second
        // launch with --settings is another process, and the tray icon is a background
        // window too.
        _settingsWindow.Activate();
    }

    /// <summary>
    /// Exits the application, from the tray menu or from the settings dialog's About page.
    /// </summary>
    /// <remarks>
    /// The dialog is closed first, while the dock is still there. Closing it unsaved is a
    /// cancel, and a cancel talks to the dock: it puts the preview back and releases the dock's
    /// holds. <see cref="Application.Shutdown()"/> would close it too, but it closes windows in
    /// the order they were created — the dock before the dialog — so the cancel would land on a
    /// dock that had already gone. With the blur on, that is an unhandled exception on the way
    /// out.
    /// </remarks>
    private void Quit()
    {
        _settingsWindow?.Close();
        Shutdown();
    }

    /// <summary>
    /// Windows signing out or shutting down, which WPF answers with a
    /// <see cref="Application.Shutdown()"/> of its own — so the dialog goes first here too,
    /// for the reason in <see cref="Quit"/>.
    /// </summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);

        if (!e.Cancel)
        {
            _settingsWindow?.Close();
        }
    }

    private void ToggleDock()
    {
        if (_dockWindow is null)
        {
            return;
        }

        _dockWindow.ToggleVisibility();
        _tray?.SetDockShown(_dockWindow.IsDockShown);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _memoryTrim?.Dispose();
        _tray?.Dispose();
        _menus?.Close();
        SingleInstance.Release();
        base.OnExit(e);
    }
}
