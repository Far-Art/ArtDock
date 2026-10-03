using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArtDock.Controls;
using ArtDock.Dock;
using ArtDock.IconSets;
using ArtDock.Interop;
using ArtDock.Localization;
using ArtDock.Packs;
using ArtDock.Services;
using Microsoft.Win32;
using Velopack;

namespace ArtDock.Views;

/// <summary>
/// The configuration dialog.
/// </summary>
/// <remarks>
/// Changes reach the live dock as they are made — the appearance controls are only meaningful
/// if you can watch what they do — but nothing is written to disk until Save. Cancel therefore
/// has something real to undo: the dock is put back the way it was found.
/// </remarks>
public sealed partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly ObservableCollection<PinnedAppSetting> _pinned = [];

    /// <summary>The Exclusions page's list, as it stands in the dialog.</summary>
    private readonly ObservableCollection<NoRevealRow> _noReveal = [];

    private readonly ColorWheel _wheel = new();

    /// <summary>
    /// The colours the user has kept, as <c>#RRGGBB</c>.
    /// </summary>
    /// <remarks>
    /// Held here rather than read back off the buttons, so the row can be rebuilt from one
    /// list and Cancel puts back what was there without having to undo anything.
    /// </remarks>
    private readonly List<string> _savedColors = [];

    /// <summary>
    /// Suppresses the wheel being re-aimed by the very text change it just caused.
    /// </summary>
    /// <remarks>
    /// The hex box is the single source of truth, so the wheel writes to it and reads back
    /// from it. Without this the round trip would re-derive hue and saturation from the RGB
    /// on every drag frame, and near the centre of the wheel — where saturation rounds to
    /// nothing in eight bits — that walks the marker away from the pointer.
    /// </remarks>
    private bool _syncingColor;

    /// <summary>Suppresses save-on-change while the controls are being populated.</summary>
    private bool _loading = true;

    /// <summary>
    /// Keeps the autostart checkbox in step with Windows while the dialog is open — see
    /// <see cref="ShowAutostart"/>. Made when the dialog is shown, so one built and never shown
    /// watches nothing.
    /// </summary>
    private AutostartWatch? _autostartWatch;

    /// <summary>
    /// The dock's bottom margin, one of the two stored settings with no control on any page.
    /// </summary>
    /// <remarks>
    /// Held here rather than read from the store inside <see cref="Compose"/>, so that a value
    /// arriving with an imported file survives — the store still holds the old one until Save.
    /// Only an import moves it, which is why the page resets leave it alone exactly as they
    /// did when <see cref="Compose"/> read it from the store.
    /// </remarks>
    private double _bottomMargin;

    /// <summary>
    /// The edge the dock is stored against, the other setting with no control.
    /// </summary>
    /// <remarks>
    /// The Position page no longer offers a choice of edge, since the bottom is the only one
    /// built. Carried through as it was found, for the same reasons as
    /// <see cref="_bottomMargin"/>, rather than written back as <c>Bottom</c>: a file that
    /// says otherwise was written by hand or imported, and Save is not the place to decide it
    /// was wrong.
    /// </remarks>
    private string _edge;

    /// <summary>
    /// Whether the pinned list has been changed here since the dialog opened.
    /// </summary>
    /// <remarks>
    /// Decides who wins when the dock changes the list underneath — see
    /// <see cref="OnStoreChanged"/>. A dialog that has not been touched should follow the
    /// dock; one that has should not have the user's own work replaced by it.
    /// </remarks>
    private bool _pinsEdited;

    /// <summary>
    /// The displays, in the order the dropdown lists them.
    /// </summary>
    /// <remarks>
    /// Read once when the dialog opens. A monitor plugged in while it is open will not
    /// appear until it is reopened, which is a fair trade for not re-enumerating the desktop
    /// on every repaint of a combo box.
    /// </remarks>
    private readonly List<ScreenInfo> _screens = [.. Screens.All()];

    /// <summary>The dock's own updates, for the About page's card.</summary>
    private readonly AppUpdater _updater = new();

    /// <summary>Where the About page's update card stands.</summary>
    private UpdateStage _updateStage;

    /// <summary>The version the last check found, while it is on offer.</summary>
    private UpdateInfo? _update;

    /// <summary>How far a download has got, 0 to 100.</summary>
    private int _updatePercent;

    /// <summary>Stops a download in progress when the dialog closes.</summary>
    private CancellationTokenSource? _updateDownload;

    /// <summary>
    /// Raised on every change, so the dock can show it. Nothing is persisted here.
    /// </summary>
    public event EventHandler<DockSettings>? Previewed;

    /// <summary>
    /// Raised with the item whose label the dock should hold open while it is being edited,
    /// and with null when the editor closes.
    /// </summary>
    /// <remarks>
    /// This dialog has no reference to the dock — it talks to the store, and the dock
    /// follows the store. A held label is not a setting, though, so it goes out as an event
    /// for the application to route rather than through a settings object.
    /// </remarks>
    public event EventHandler<DockItem?>? ItemBeingEdited;

    /// <summary>Raised with the name as it is typed into the item editor.</summary>
    public event EventHandler<string?>? ItemLabelPreviewed;

    /// <summary>
    /// Raised with the id of the item the dock should hold up — the row selected on the Items
    /// page while that page is open, and an item with a label while the Icons page is — and
    /// with null when there is none.
    /// </summary>
    /// <remarks>
    /// Only while one of those pages is open. The other pages have a demonstration of their
    /// own, and the Size page's sweep would otherwise stand still on whatever row was selected
    /// last.
    /// </remarks>
    public event EventHandler<string?>? ItemSelected;

    /// <summary>The id <see cref="ItemSelected"/> was last raised with, so each change is raised once.</summary>
    private string? _selectedItemId;

    /// <summary>Raised by the About page's Exit.</summary>
    /// <remarks>
    /// Asked for rather than done here: exiting has to close this dialog while the dock it
    /// previews on is still there, and the ordering belongs to whoever owns both.
    /// </remarks>
    public event EventHandler? ExitRequested;

    /// <summary>True once the user has pressed Save.</summary>
    public bool Saved { get; private set; }

    /// <summary>
    /// The pages, in the order they were numbered — the number <see cref="DockSettings.SettingsPage"/>
    /// stores is a page's place in this list.
    /// </summary>
    /// <remarks>
    /// Not the order the dialog shows them in. A page is numbered when it is added, at the end
    /// of this list, so a page put anywhere else in the dialog moves no other page's number —
    /// where numbering by position would reopen the dialog a page off for everyone whose file
    /// names one. Icons came after About, and stands below Behaviour; Hotkeys came last, and
    /// stands below Items.
    /// </remarks>
    private readonly TabItem[] _pages;

    /// <summary>The page that is open, by its number, so it can be returned to next time.</summary>
    public int SelectedPage => Math.Max(0, Array.IndexOf(_pages, Tabs.SelectedItem));

    public SettingsWindow(SettingsStore store)
    {
        InitializeComponent();

        _pages =
        [
            SizePage, AppearancePage, BehaviourPage, ItemsPage, ExclusionsPage, PositionPage,
            SystemPage, AboutPage, IconsPage, HotkeysPage
        ];

        _store = store;
        _bottomMargin = store.Current.BottomMargin;
        _edge = store.Current.Edge;
        PinnedList.ItemsSource = _pinned;
        NoRevealList.ItemsSource = _noReveal;
        ShowAbout();

        FillThemeBox();
        BuildSwatches();
        BuildAddMenu();
        FillScreenBox();
        BuildHotkeyRows();

        WheelHost.Content = _wheel;
        _wheel.ColorPicked += (_, color) =>
        {
            _syncingColor = true;
            BarColorBox.Text = BarPalette.ToHex(color);
            _syncingColor = false;
        };

        LoadFrom(store.Current);
        WireEvents();

        // The dock writes changes of its own straight to the store — a reorder, an unpin, a
        // drop-to-pin — and this dialog holds a copy. Without following the store, that copy
        // goes stale the moment the dock is touched and then overwrites it on Save.
        _store.Changed += OnStoreChanged;
        UpdateItemButtons();
        _loading = false;

        // The words built in code — hints, list entries, menus — are said again in a new
        // language; the ones in the XAML follow by themselves.
        Localizer.LanguageChanged += OnLanguageChanged;

        // The No GPU hint follows what Windows gives the dock to draw with, which can change
        // while the dialog is open: a remote session taking over is the usual way.
        RenderCapability.TierChanged += OnRenderTierChanged;

        // Coming back to this window is when a pack copied in through Open folder has had a
        // chance to arrive, so that is when the pickers look again.
        Activated += (_, _) => RefreshPacks();

        Loaded += (_, _) =>
        {
            // The dock never takes the foreground, and neither does the tray icon, so this
            // dialog opens without it — behind whatever had it, and needing a click before
            // it will accept a keystroke. AppLauncher.Activate does the AttachThreadInput
            // dance Windows requires to hand focus to a process that is not in front. The
            // same call the item editor has always made, for the same reason.
            AppLauncher.Activate(new WindowInteropHelper(this).Handle);

            // A dialog that opens on the Icons page holds its item up from the start. Not from
            // the constructor, which runs before anything is listening.
            AnnounceSelectedItem();

            // Then looked at once more, for a switch in Windows between the constructor's read
            // and the watch starting.
            _autostartWatch = new AutostartWatch();
            _autostartWatch.Changed += (_, _) => Dispatcher.BeginInvoke(ShowAutostart);
            ShowAutostart();
        };
    }

    /// <summary>
    /// Brings the dialog back to the front as it was left, for the tray's entry, the dock's menu
    /// or the hotkey asking for it while it is open.
    /// </summary>
    /// <remarks>
    /// Restored if it was minimised, which activating alone would leave on the taskbar; and to
    /// whichever of its own dialogs is open over it — the item editor, a scan — as Alt+Tab goes,
    /// since Windows activates no window that is disabled, and one of those disables it. Through
    /// <see cref="AppLauncher.Activate"/>, for the reason the dialog opens through it.
    /// </remarks>
    public void BringToFront()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (WindowsApi.IsIconic(handle))
        {
            WindowsApi.ShowWindow(handle, WindowsApi.SW_RESTORE);
        }

        AppLauncher.Activate(WindowsApi.GetLastActivePopup(handle));
    }

    private void LoadFrom(DockSettings settings)
    {
        _loading = true;

        BaseSizeSlider.Value = settings.BaseSize;
        MaxScaleSlider.Value = settings.MaxScale;
        InfluenceSlider.Value = settings.Neighbours;
        GapSlider.Value = settings.GapFraction;
        OpacitySlider.Value = settings.BarOpacity;
        RoundnessSlider.Value = Math.Clamp(settings.BarRoundness, 0, 1);
        BarColorBox.Text = BarPalette.ToHex(BarPalette.Parse(settings.BarColor));
        TaskbarColorCheck.IsChecked = settings.UseTaskbarColor;
        BlurCheck.IsChecked = settings.BlurBackground;
        IconShadowsCheck.IsChecked = settings.IconShadows;
        BuildIconSetList(settings.IconSet);
        LoadLabelFont(settings.LabelFontFamily, settings.LabelFontSize, settings.LabelFontStyle);

        _savedColors.Clear();
        _savedColors.AddRange(settings.CustomColors.Select(hex => BarPalette.ToHex(BarPalette.Parse(hex))));
        BuildSavedSwatches();

        RefreshColorPreview();
        UpdateColorControls();

        AlwaysOnTopCheck.IsChecked = settings.AlwaysOnTop;
        AutoHideCheck.IsChecked = settings.AutoHide;
        HideDelaySlider.Value = settings.HideDelayMs;
        RevealDelaySlider.Value = settings.RevealDelayMs;
        WindowPreviewsCheck.IsChecked = settings.WindowPreviews;
        PreviewDelaySlider.Value = settings.PreviewDelayMs;
        UpdatePreviewControls();
        HandleCheck.IsChecked = settings.ShowHandle;
        HandleMatchDockCheck.IsChecked = settings.HandleMatchesDock;
        HandleWidthSlider.Value = settings.HandleWidth;
        UpdateHandleControls();
        FillNoRevealList(settings.NoRevealApps);

        SweepCheck.IsChecked = settings.PreviewSweep;

        EdgeOffsetSlider.Value = settings.OffsetAlongEdge;
        ShowEdgeOffset();
        SelectScreen(settings.ScreenDeviceName, settings.ScreenDevicePath);
        Tabs.SelectedItem = _pages[Math.Clamp(settings.SettingsPage, 0, _pages.Length - 1)];
        ThemeBox.SelectedIndex = AppTheme.IndexOf(settings.Theme);
        BuildLanguageList(settings.Language);

        // Read the registry rather than the stored flag: the user may have turned the dock off
        // in Task Manager's Startup tab, and the checkbox should reflect reality.
        RunAtLoginCheck.IsChecked = Autostart.IsEnabled();
        ReduceMotionCheck.IsChecked = settings.ReduceMotion;
        NoGpuCheck.IsChecked = settings.NoGpu;
        UpdateNoGpuControls();

        LockOrderCheck.IsChecked = settings.LockItemOrder;
        LockContentsCheck.IsChecked = settings.LockItemContents;

        LoadHotkeys(settings);

        _pinned.Clear();
        foreach (var app in settings.PinnedApps)
        {
            _pinned.Add(app);
        }

        _loading = false;
        UpdateItemLocks();
    }

    private void WireEvents()
    {
        foreach (var slider in new[]
                 {
                     BaseSizeSlider, MaxScaleSlider, InfluenceSlider, GapSlider,
                     OpacitySlider, RoundnessSlider, LabelSizeSlider, HideDelaySlider,
                     RevealDelaySlider, PreviewDelaySlider, HandleWidthSlider
                 })
        {
            slider.ValueChanged += (_, _) => Preview();
        }

        foreach (var check in new[] { HandleCheck, HandleMatchDockCheck })
        {
            check.Checked += (_, _) => UpdateHandleControls();
            check.Unchecked += (_, _) => UpdateHandleControls();
        }

        HandleCheck.Checked += (_, _) => Preview();
        HandleCheck.Unchecked += (_, _) => Preview();
        HandleMatchDockCheck.Checked += (_, _) => Preview();
        HandleMatchDockCheck.Unchecked += (_, _) => Preview();

        SweepCheck.Checked += (_, _) => Preview();
        SweepCheck.Unchecked += (_, _) => Preview();

        AlwaysOnTopCheck.Checked += (_, _) => Preview();
        AlwaysOnTopCheck.Unchecked += (_, _) => Preview();
        AutoHideCheck.Checked += (_, _) => Preview();
        AutoHideCheck.Unchecked += (_, _) => Preview();
        WindowPreviewsCheck.Checked += (_, _) =>
        {
            UpdatePreviewControls();
            Preview();
        };
        WindowPreviewsCheck.Unchecked += (_, _) =>
        {
            UpdatePreviewControls();
            Preview();
        };
        BlurCheck.Checked += (_, _) => Preview();
        BlurCheck.Unchecked += (_, _) => Preview();
        IconShadowsCheck.Checked += (_, _) => Preview();
        IconShadowsCheck.Unchecked += (_, _) => Preview();

        ThemeBox.SelectionChanged += (_, _) =>
        {
            // Relabelling empties and refills the list, and the selection passes through
            // nothing on the way — which is not a choice of theme to show Mica for.
            if (_relabelling)
            {
                return;
            }

            // The controls follow ThemeMode on their own; the window's Mica and title bar
            // are DWM's and have to be told separately.
            ApplyMica();
            Preview();
        };

        LanguageBox.SelectionChanged += (_, _) => Preview();
        // The item list draws its icons from the chosen set, as the dock does — followed while
        // the pages load as well, so the list starts with the set in force.
        IconSetBox.SelectionChanged += (_, _) =>
        {
            ItemRowIconConverter.SetIconSet(PinnedList, IconSetLibrary.Installed.Find(SelectedIconSet));
            SchedulePlaceIcons();
            Preview();
        };
        LanguageFolderButton.Click += (_, _) => OpenPackFolder(PackKind.Language);
        IconSetFolderButton.Click += (_, _) => OpenPackFolder(PackKind.IconSet);

        // Both previewed, as the size slider is, on the label the Icons page holds up. Their
        // refills in a new language choose nothing, and pass while the pages are loading.
        LabelFontBox.SelectionChanged += (_, _) => Preview();
        LabelStyleBox.SelectionChanged += (_, _) => Preview();
        ReduceMotionCheck.Checked += (_, _) => Preview();
        ReduceMotionCheck.Unchecked += (_, _) => Preview();
        NoGpuCheck.Checked += (_, _) => OnNoGpuToggled();
        NoGpuCheck.Unchecked += (_, _) => OnNoGpuToggled();

        // Autostart is a system change, so it waits for Save like everything else —
        // otherwise Cancel would leave it altered.
        RunAtLoginCheck.Checked += (_, _) => Preview();
        RunAtLoginCheck.Unchecked += (_, _) => Preview();

        BarColorBox.TextChanged += (_, _) =>
        {
            RefreshColorPreview();
            Preview();
        };

        TaskbarColorCheck.Checked += (_, _) => OnTaskbarColorToggled();
        TaskbarColorCheck.Unchecked += (_, _) => OnTaskbarColorToggled();
        SaveColorButton.Click += (_, _) => SaveCurrentColor();

        // The button is the dropdown: WPF has no split button, and a menu opened from the
        // button is closer to what the dock's own right-click Add offers than a second
        // combo box beside it would be.
        AddButton.Click += (_, _) =>
        {
            if (AddButton.ContextMenu is { } menu)
            {
                menu.PlacementTarget = AddButton;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            }
        };

        NoRevealAddButton.Click += (_, _) => OpenNoRevealMenu();
        NoRevealScanButton.Click += (_, _) => ScanForNoReveal(ScanKind.Games);
        NoRevealScanAppsButton.Click += (_, _) => ScanForNoReveal(ScanKind.Apps);
        NoRevealRemoveButton.Click += (_, _) => RemoveNoReveal();
        NoRevealList.SelectionChanged += (_, _) => UpdateNoRevealControls();

        PinnedList.SelectionChanged += (_, _) =>
        {
            UpdateItemButtons();
            AnnounceSelectedItem();
        };

        // A selection change inside any page bubbles up to here as well, which is harmless:
        // the announcement is made only when what it says has changed.
        Tabs.SelectionChanged += (_, _) => AnnounceSelectedItem();

        // A press on nothing in particular puts the row down, and the dock lets go of it.
        PreviewMouseDown += OnPressOutsideItems;

        PinnedList.MouseDoubleClick += (_, _) => EditSelected();
        ItemSettingsButton.Click += (_, _) => EditSelected();
        RemoveButton.Click += (_, _) => RemoveSelected();
        MoveUpButton.Click += (_, _) => MoveSelected(-1);
        MoveDownButton.Click += (_, _) => MoveSelected(1);

        LockOrderCheck.Checked += (_, _) => OnItemLockChanged();
        LockOrderCheck.Unchecked += (_, _) => OnItemLockChanged();
        LockContentsCheck.Checked += (_, _) => OnItemLockChanged();
        LockContentsCheck.Unchecked += (_, _) => OnItemLockChanged();

        // Dragging a row is the same reorder the Move buttons do, said the direct way. The
        // dock itself has been reorderable by drag since it had icons on it; the list that
        // describes the dock should not be the one place where it is not.
        PinnedList.PreviewMouseLeftButtonDown += OnPinnedPress;
        PinnedList.PreviewMouseLeftButtonUp += (_, _) => _pressedPin = -1;
        PinnedList.PreviewMouseMove += OnPinnedMove;
        PinnedList.DragOver += OnPinnedDragOver;
        PinnedList.Drop += OnPinnedDrop;

        // The read-out follows the slider even while the pages are being filled in, which is
        // when Preview stands down — a reset has to be able to say "Centre" as well.
        EdgeOffsetSlider.ValueChanged += (_, e) =>
        {
            // Caught at the middle. The catch sets the slider again, and it is that value which
            // comes back through here to be shown and previewed: the one it was caught from is
            // never shown, so the dock does not visit it on the way.
            if (CatchAtCentre(e.NewValue))
            {
                return;
            }

            ShowEdgeOffset();
            Preview();
        };

        // After a layout rather than at load: the fill is lined up against where the theme's
        // template has actually put its parts, and the page is not laid out until it is shown.
        EdgeOffsetSlider.SizeChanged += (_, _) => AlignEdgeOffsetFill();

        ScreenBox.SelectionChanged += (_, _) => Preview();

        ResetSizeButton.Click += (_, _) => ResetSizePage();
        ResetPositionButton.Click += (_, _) => ResetPositionPage();
        ResetAppearanceButton.Click += (_, _) => ResetAppearancePage();
        ResetIconsButton.Click += (_, _) => ResetIconsPage();
        ClearItemsButton.Click += (_, _) => ClearItems();
        ResetBehaviourButton.Click += (_, _) => ResetBehaviourPage();
        ResetHotkeysButton.Click += (_, _) => ResetHotkeysPage();
        QuickLaunchCheck.Checked += (_, _) => OnHotkeysEdited();
        QuickLaunchCheck.Unchecked += (_, _) => OnHotkeysEdited();
        foreach (var check in _placeChecks)
        {
            check.Checked += (_, _) => OnHotkeysEdited();
            check.Unchecked += (_, _) => OnHotkeysEdited();
        }

        NumbersOnWinCtrlCheck.Checked += (_, _) => Preview();
        NumbersOnWinCtrlCheck.Unchecked += (_, _) => Preview();
        RevealOnWinCtrlCheck.Checked += (_, _) => Preview();
        RevealOnWinCtrlCheck.Unchecked += (_, _) => Preview();
        ResetSystemButton.Click += (_, _) => ResetSystemPage();
        ResetButton.Click += (_, _) => ResetToDefaults();
        ImportButton.Click += (_, _) => ImportSettings();
        ExportButton.Click += (_, _) => ExportSettings();
        ExitButton.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        UpdateButton.Click += (_, _) => OnUpdateClicked();
        SaveButton.Click += (_, _) => Save();
        CancelButton.Click += (_, _) => Close();

        // Escape cancels, matching the button — but not out of a box recording a hotkey, where it
        // puts back what the box held. This is the window's, and so hears the key before the box.
        PreviewKeyDown += (_, key) =>
        {
            if (key.Key == System.Windows.Input.Key.Escape && !IsRecordingHotkey)
            {
                Close();
            }
        };
    }

    /// <summary>Pushes the current controls onto the dock without persisting them.</summary>
    private void Preview()
    {
        if (_loading)
        {
            return;
        }

        Previewed?.Invoke(this, Compose());
    }

    /// <summary>Commits everything to disk, including the startup registration.</summary>
    private void Save()
    {
        ApplyAutostart(RunAtLoginCheck.IsChecked == true);
        _store.Save(Compose());
        Saved = true;
        Close();
    }

    /// <summary>Builds a settings object from the current state of the controls.</summary>
    private DockSettings Compose() =>
        new()
        {
            BaseSize = BaseSizeSlider.Value,
            MaxScale = MaxScaleSlider.Value,
            InfluenceIcons = InfluenceSlider.Value,
            GapRatio = GapSlider.Value,
            BarOpacity = OpacitySlider.Value,

            // Normalised rather than stored as typed, so a half-finished hex string in the
            // box never reaches the settings file.
            BarColor = BarPalette.ToHex(BarPalette.Parse(BarColorBox.Text)),
            UseTaskbarColor = TaskbarColorCheck.IsChecked == true,
            CustomColors = [.. _savedColors],
            BarRoundness = RoundnessSlider.Value,
            BlurBackground = BlurCheck.IsChecked == true,
            IconShadows = IconShadowsCheck.IsChecked == true,
            IconSet = SelectedIconSet,
            LabelFontFamily = SelectedLabelFont,
            LabelFontSize = SelectedLabelSize,
            LabelFontStyle = SelectedLabelStyle,
            PreviewSweep = SweepCheck.IsChecked == true,
            SettingsPage = SelectedPage,
            Edge = _edge,

            // To the slider's own step. It snaps to hundredths by adding them up from -1, and
            // the sum carries binary residue that would otherwise land in the settings file
            // as -0.39999999999999997.
            OffsetAlongEdge = Math.Round(EdgeOffsetSlider.Value, 2),
            ScreenDeviceName = SelectedScreen?.DeviceName,
            ScreenDevicePath = SelectedScreen?.DevicePath,
            AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true,
            AutoHide = AutoHideCheck.IsChecked == true,
            HideDelayMs = (int)HideDelaySlider.Value,
            RevealDelayMs = (int)RevealDelaySlider.Value,
            WindowPreviews = WindowPreviewsCheck.IsChecked == true,
            PreviewDelayMs = (int)PreviewDelaySlider.Value,
            ShowHandle = HandleCheck.IsChecked == true,
            HandleMatchesDock = HandleMatchDockCheck.IsChecked == true,
            HandleWidth = Math.Round(HandleWidthSlider.Value),
            NoRevealApps = [.. NoRevealPaths],
            BottomMargin = _bottomMargin,
            Hotkeys = HotkeyActions.Store(HotkeyChoices(), _otherHotkeys),
            QuickLaunch = QuickLaunch,
            QuickLaunchOff = PlacesOff,
            NumbersOnWinCtrl = NumbersOnWinCtrlCheck.IsChecked == true,
            RevealOnWinCtrl = RevealOnWinCtrlCheck.IsChecked == true,
            RunAtLogin = RunAtLoginCheck.IsChecked == true,
            Theme = SelectedTheme,
            Language = SelectedLanguage,
            ReduceMotion = ReduceMotionCheck.IsChecked == true,
            NoGpu = NoGpuCheck.IsChecked == true,
            LockItemOrder = OrderLocked,
            LockItemContents = ContentsLocked,
            PinnedApps = [.. _pinned]
        };

    private void ApplyAutostart(bool enabled)
    {
        if (enabled == Autostart.IsEnabled())
        {
            return;
        }

        if (!Autostart.SetEnabled(enabled))
        {
            MessageBox.Show(
                this,
                Localizer.Get("Settings.Autostart.Failed"),
                "ArtDock",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _loading = true;
            RunAtLoginCheck.IsChecked = Autostart.IsEnabled();
            _loading = false;
        }
    }

    /// <summary>
    /// Shows autostart as Windows now has it, when the entry has changed while the dialog is
    /// open — switched in Task Manager or the Settings app beside it.
    /// </summary>
    /// <remarks>
    /// Over a tick or untick not yet saved, too: the switch in Windows is the newer choice, and
    /// it is already made, where the checkbox's waits for Save. Saving then finds the two agree
    /// and writes nothing.
    /// </remarks>
    private void ShowAutostart()
    {
        if (_autostartWatch is null)
        {
            return;
        }

        var enabled = Autostart.IsEnabled();
        if (RunAtLoginCheck.IsChecked == enabled)
        {
            return;
        }

        _loading = true;
        RunAtLoginCheck.IsChecked = enabled;
        _loading = false;
    }

    // ---- position ------------------------------------------------------------

    /// <summary>The chosen display, or null for whichever is the main one.</summary>
    private ScreenInfo? SelectedScreen =>
        ScreenBox.SelectedIndex > 0 && ScreenBox.SelectedIndex - 1 < _screens.Count
            ? _screens[ScreenBox.SelectedIndex - 1]
            : null;

    /// <summary>
    /// Says where the dock is along its edge: in words beside the slider, and on the slider by
    /// filling its track from the middle out to the thumb.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Left" and "right" because the bottom is the only edge there is. A side edge will want
    /// "up" and "down" here, since the setting itself is along the edge — see
    /// <see cref="DockSettings.OffsetAlongEdge"/>.
    /// </para>
    /// <para>
    /// Filled from the middle because the setting is measured from there. A track filled
    /// from its left end reads as an amount, and this is not one: the middle is the dock
    /// centred and each end is as far as it goes one way, so the fill says which way and how
    /// far. The theme's own selection range draws it — the same accent and the same shape as
    /// the fill it stands in for — kept here between the middle and the value.
    /// </para>
    /// </remarks>
    private void ShowEdgeOffset()
    {
        var value = EdgeOffsetSlider.Value;
        var offset = Math.Round(value, 2);

        EdgeOffsetText.Text = offset == 0
            ? Localizer.Get("Settings.Position.Centre")
            : Localizer.Format(offset < 0 ? "Settings.Position.Left" : "Settings.Position.Right", Math.Abs(offset));

        EdgeOffsetSlider.SelectionStart = Math.Min(0, value);
        EdgeOffsetSlider.SelectionEnd = Math.Max(0, value);
    }

    /// <summary>
    /// How near the middle, in pixels of the thumb's travel, the offset slider catches there.
    /// </summary>
    /// <remarks>
    /// Pixels rather than a share of the setting, because this is about the hand: the middle
    /// has to be easy to land on with a mouse whatever width the slider is drawn at. At the
    /// width it has, that catches every value up to five per cent either side — the slider
    /// rounds to hundredths before the catch sees the value.
    /// </remarks>
    private const double CentreDetent = 5;

    /// <summary>
    /// Catches the offset slider at the middle when it is dragged or clicked to near it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The middle is the one position with a meaning of its own — the dock centred, where it
    /// sat before it could be moved — and without a catch it was one hundredth of the slider,
    /// under a pixel wide, to be found by eye. With one, the thumb holds there until it is
    /// dragged a few pixels clear, the way a balance control does. The cost is that nothing
    /// between the middle and the edge of the catch can be chosen by dragging, and a dock that
    /// close to the middle is not a position anyone moving it aside is aiming for.
    /// </para>
    /// <para>
    /// The drag carries on correctly after a catch because the slider works each movement out
    /// from where the pointer is relative to the thumb, not from how far it has travelled: the
    /// thumb put back in the middle is simply where the next movement is measured from.
    /// Values the dialog sets itself — loading, importing, resetting — are taken as they are,
    /// so a settings file holding an offset inside the catch keeps it.
    /// </para>
    /// </remarks>
    /// <returns>True when the slider was caught, which sets it again.</returns>
    private bool CatchAtCentre(double value)
    {
        if (_loading
            || value == 0
            || EdgeOffsetSlider.Template?.FindName("PART_Track", EdgeOffsetSlider)
                is not System.Windows.Controls.Primitives.Track { ActualWidth: > 0 } track)
        {
            return false;
        }

        // The track has no scale to convert with until it has been laid out once — which is
        // also before anything but the dialog itself can have moved the slider.
        var reach = Math.Abs(track.ValueFromDistance(CentreDetent, 0));
        if (!double.IsFinite(reach) || Math.Abs(value) > reach)
        {
            return false;
        }

        EdgeOffsetSlider.Value = 0;
        return true;
    }

    /// <summary>
    /// Lines the theme's selection range up with the thumb, so the fill starts in the middle.
    /// </summary>
    /// <remarks>
    /// The theme draws its range parts inside a canvas pulled half a thumb to the left of the
    /// track. That suits the fill it normally shows, which runs from the left end of the track
    /// — further out than the thumb's centre ever goes. Any other range it puts half a thumb
    /// to the left of where its values say, so this one started half a thumb left of the
    /// middle whenever the thumb was to the right of it, and stopped half a thumb short of the
    /// middle whenever it was to the left. Measured from the template as laid out rather than
    /// assumed, so a theme that places the canvas differently is followed rather than fought.
    /// </remarks>
    private void AlignEdgeOffsetFill()
    {
        var template = EdgeOffsetSlider.Template;
        if (template?.FindName("PART_SelectionRange", EdgeOffsetSlider) is not FrameworkElement range
            || template.FindName("PART_Track", EdgeOffsetSlider)
                is not System.Windows.Controls.Primitives.Track track
            || VisualTreeHelper.GetParent(range) is not UIElement canvas)
        {
            return;
        }

        // The slider places the range at the thumb's centre plus the canvas's own offset from
        // the track, so taking that offset back off leaves it at the thumb's centre.
        var shift = -canvas.TranslatePoint(new Point(0, 0), track).X;
        if (range.RenderTransform is TranslateTransform { X: var current } && current == shift)
        {
            return;
        }

        range.RenderTransform = new TranslateTransform(shift, 0);
    }

    /// <summary>
    /// Fills the display dropdown, keeping whichever entry was selected.
    /// </summary>
    /// <remarks>
    /// The first entry follows Windows rather than naming a display, so a dock that was
    /// never moved deliberately stays on the main one when the monitors are rearranged.
    /// Filled again, from the same displays, when the language changes.
    /// </remarks>
    private void FillScreenBox()
    {
        var selected = ScreenBox.SelectedIndex;

        ScreenBox.Items.Clear();
        ScreenBox.Items.Add(Localizer.Get("Settings.Position.Display.Main"));
        for (var i = 0; i < _screens.Count; i++)
        {
            ScreenBox.Items.Add(Screens.Label(_screens[i], i + 1));
        }

        ScreenBox.SelectedIndex = Math.Clamp(selected, 0, ScreenBox.Items.Count - 1);

        // One display is not a choice, and a dropdown with a single meaningful entry reads
        // as something that is broken rather than as something that does not apply.
        if (_screens.Count < 2)
        {
            ScreenCard.IsEnabled = false;
            ScreenHint.Text = Localizer.Get("Settings.Position.Display.OnlyOne");
        }
    }

    /// <summary>
    /// Selects the stored display the way the dock finds it — by the monitor first, then by
    /// the name — so the dialog never shows one screen while the dock sits on another.
    /// </summary>
    private void SelectScreen(string? deviceName, string? devicePath) =>
        ScreenBox.SelectedIndex = Screens.Match(_screens, deviceName, devicePath) is { } screen
            ? _screens.IndexOf(screen) + 1
            : 0;

    private void ResetPositionPage()
    {
        var defaults = new DockSettings();

        _loading = true;
        EdgeOffsetSlider.Value = defaults.OffsetAlongEdge;
        SelectScreen(defaults.ScreenDeviceName, defaults.ScreenDevicePath);
        _loading = false;

        Preview();
    }

    /// <summary>Fills the colour swatches from the shared palette.</summary>
    private void BuildSwatches()
    {
        foreach (var hex in BarPalette.Swatches)
        {
            SwatchPanel.Children.Add(BuildSwatch(hex, saved: false));
        }
    }

    /// <summary>
    /// One swatch button. A saved one can also be removed.
    /// </summary>
    /// <remarks>
    /// Removal is on the right-click menu rather than on a delete button beside every
    /// swatch: the row is meant to read as a strip of colours, and doubling the controls in
    /// it to carry an affordance used once in a while would cost more than it gives. The
    /// tooltip says so, since a gesture nothing mentions is a gesture nobody finds.
    /// </remarks>
    private Button BuildSwatch(string hex, bool saved)
    {
        var fill = new SolidColorBrush(BarPalette.Parse(hex));
        fill.Freeze();

        var swatch = new Button
        {
            Width = 30,
            Height = 22,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(0),
            ToolTip = saved ? Localizer.Format("Settings.Appearance.SavedSwatch.Tip", hex) : hex,

            // The colour lives in a child rather than in the button's own Background,
            // which the default template repaints on hover. Sized explicitly because the
            // button centres its content, and an empty Border with no size measures to
            // nothing — which is exactly as visible as it sounds.
            Content = new Border
            {
                Width = 22,
                Height = 14,
                Background = fill,
                CornerRadius = new CornerRadius(3)
            }
        };

        swatch.Click += (_, _) => BarColorBox.Text = hex;

        // A swatch shows nothing but its colour, so a screen reader has only this to say: the
        // colour as the box beside it takes one, and for a saved colour how to remove it.
        System.Windows.Automation.AutomationProperties.SetName(swatch, hex);

        if (saved)
        {
            System.Windows.Automation.AutomationProperties.SetHelpText(
                swatch, Localizer.Format("Settings.Appearance.SavedSwatch.Tip", hex));

            var remove = new MenuItem
            {
                Header = Localizer.Get("Common.Remove"),
                Icon = MenuIcons.Glyph(MenuGlyph.Delete)
            };
            remove.Click += (_, _) => RemoveSavedColor(hex);
            swatch.ContextMenu = new ContextMenu { Items = { remove } };
        }

        return swatch;
    }

    /// <summary>Builds the Add dropdown from the same presets the dock's own menu offers.</summary>
    private void BuildAddMenu()
    {
        var menu = new ContextMenu();
        foreach (var group in DockPresets.Menu())
        {
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            foreach (var preset in group)
            {
                var key = preset.Key;
                var entry = new MenuItem { Header = preset.Label, Icon = MenuIcons.For(preset) };
                entry.Click += (_, _) => AddPreset(key);
                menu.Items.Add(entry);
            }
        }

        AddButton.ContextMenu = menu;
    }

    /// <summary>Adds a preset, or browses or searches for targets when that is what was chosen.</summary>
    private void AddPreset(string key)
    {
        IReadOnlyList<PinnedAppSetting> pins = key switch
        {
            DockPresets.BrowseKey => BrowseForPin() is { } chosen ? [chosen] : [],
            DockPresets.SearchAppsKey => SearchForPins(ScanKind.Apps),
            DockPresets.SearchGamesKey => SearchForPins(ScanKind.Games),
            _ => DockPresets.Create(key) is { } preset ? [preset] : []
        };

        if (pins.Count == 0)
        {
            return;
        }

        // After the selection when there is one, so a run of additions stays in order and
        // lands where the user was looking. The last of them is selected, so the next lands
        // after it.
        var index = PinnedList.SelectedIndex < 0 ? _pinned.Count : PinnedList.SelectedIndex + 1;
        for (var i = 0; i < pins.Count; i++)
        {
            _pinned.Insert(index + i, pins[i]);
        }

        PinnedList.SelectedIndex = index + pins.Count - 1;
        PinnedList.ScrollIntoView(PinnedList.SelectedItem);

        _pinsEdited = true;
        UpdateItemButtons();
        Preview();
    }

    private PinnedAppSetting? BrowseForPin()
    {
        var dialog = new OpenFileDialog
        {
            Title = Localizer.Get("Pin.Browse.Title"),
            Filter = FileFilters.Pinnable,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };

        if (dialog.ShowDialog(this) != true)
        {
            return null;
        }

        return PinnedAppsService.CreateChosenPin(dialog.FileName);
    }

    /// <summary>
    /// Looks through the apps — or the games — on this machine, and makes a pin of each one
    /// ticked; the Exclusions page's scan, put to the dock's use.
    /// </summary>
    private List<PinnedAppSetting> SearchForPins(ScanKind kind)
    {
        var scan = new ScanWindow(kind, PinnedAppsService.TargetPaths(_pinned), ScanPurpose.Pin) { Owner = this };
        return scan.ShowDialog() == true ? PinnedAppsService.CreateFoundPins(scan.ChosenPins) : [];
    }

    /// <summary>Opens the item editor for the selected pin.</summary>
    private void EditSelected()
    {
        // The lock is checked here rather than only on the button, because double-clicking a
        // row is the other way in and would otherwise walk straight past a greyed button.
        if (ContentsLocked
            || PinnedList.SelectedItem is not PinnedAppSetting selected
            || selected.IsSeparator)
        {
            return;
        }

        var index = _pinned.IndexOf(selected);
        var edited = PinnedAppsService.ToDockItem(selected);
        var editor = new EditPinWindow(edited, IconSetLibrary.Installed.Find(SelectedIconSet)) { Owner = this };

        // The dock shows the label being edited, exactly as it does when the same editor is
        // opened from the dock's own menu.
        editor.PreviewChanged += (_, _) => ItemLabelPreviewed?.Invoke(this, editor.EditedLabel);

        bool saved;
        ItemBeingEdited?.Invoke(this, edited);
        try
        {
            saved = editor.ShowDialog() == true;
        }
        finally
        {
            // Released whichever way the dialog went, including if it threw.
            ItemBeingEdited?.Invoke(this, null);
        }

        if (!saved)
        {
            return;
        }

        // Replaced rather than mutated. The pins in this list are the very objects the
        // store is holding, so editing one in place would survive Cancel — and a replace
        // is also what tells the list box to redraw the row. Only this item: the editor has
        // nothing that belongs to any other, now that the labels' lettering is the Icons
        // page's.
        _pinned[index] = Edited(selected, editor);
        PinnedList.SelectedIndex = index;

        _pinsEdited = true;
        Preview();
    }

    /// <summary>A copy of a pin carrying what the item editor has for it.</summary>
    private static PinnedAppSetting Edited(PinnedAppSetting source, EditPinWindow editor) =>
        new()
        {
            Id = source.Id,
            Label = editor.EditedLabel,
            TargetPath = editor.EditedTargetPath,
            Aumid = editor.EditedAumid,
            IconPath = editor.EditedIconPath,
            UseIconNotThumbnail = editor.EditedUseIconNotThumbnail,
            FolderColor = editor.EditedFolderColor,
            FolderSymbol = editor.EditedFolderSymbol,
            FolderText = editor.EditedFolderText,
            FolderSymbolTone = editor.EditedFolderSymbolTone,
            IsSeparator = source.IsSeparator
        };

    /// <summary>
    /// Enables only the buttons that have something to act on.
    /// </summary>
    /// <remarks>
    /// Every one of these needs a selected item, and two of them need it to have somewhere
    /// to go. Offering them regardless left four buttons that did nothing, which reads as a
    /// dialog that is not listening rather than as a dialog with nothing selected.
    /// </remarks>
    private void UpdateItemButtons()
    {
        var index = PinnedList.SelectedIndex;
        var selected = PinnedList.SelectedItem as PinnedAppSetting;

        // A separator has nothing the item editor could act on. Editing survives the order
        // lock — rearranging is not renaming — but not the contents lock, which the dock's
        // own menu also takes Edit item… away for: a lock that held on the dock and not in
        // the dialog would be one the dialog quietly undoes.
        ItemSettingsButton.IsEnabled = !ContentsLocked && selected is { IsSeparator: false };

        AddButton.IsEnabled = !ContentsLocked;
        RemoveButton.IsEnabled = !ContentsLocked && selected is not null;
        MoveUpButton.IsEnabled = !OrderLocked && index > 0;
        MoveDownButton.IsEnabled = !OrderLocked && index >= 0 && index < _pinned.Count - 1;

        // Nothing to clear reads better greyed than as a button that does nothing. Not tied
        // to the order lock: that fixes the arrangement, it does not protect the contents.
        // The contents lock is exactly what it does protect, so that one does reach it.
        ClearItemsButton.IsEnabled = !ContentsLocked && _pinned.Count > 0;
    }

    /// <summary>
    /// Tells the dock which item to hold up: the selected row while the Items page is open, an
    /// item with a label while the Icons page is, and none otherwise.
    /// </summary>
    /// <remarks>
    /// By id, and announced as soon as the row is selected — which can be before the dock has
    /// the item, since an addition is selected first and previewed after. The dock looks for it
    /// again whenever its contents change, so it does not have to be told twice.
    /// </remarks>
    private void AnnounceSelectedItem()
    {
        var id = Tabs.SelectedItem == ItemsPage && PinnedList.SelectedItem is PinnedAppSetting pin
            ? pin.Id
            : Tabs.SelectedItem == IconsPage
                ? LabelledPin()?.Id
                : null;

        if (id == _selectedItemId)
        {
            return;
        }

        _selectedItemId = id;
        ItemSelected?.Invoke(this, id);
    }

    /// <summary>
    /// The item the Icons page holds up on the dock: the row selected on the Items page, or the
    /// one nearest the middle — where the dock holds its wave with nothing selected — so long
    /// as it has a label to show.
    /// </summary>
    /// <remarks>
    /// Held as a selected row is, which labels it as a hovered icon is labelled: the page's
    /// lettering has nothing to be judged on otherwise, the pointer being on the dialog. Never
    /// a separator, which has no label, and so would show nothing.
    /// </remarks>
    private PinnedAppSetting? LabelledPin()
    {
        if (PinnedList.SelectedItem is PinnedAppSetting selected && HasLabel(selected))
        {
            return selected;
        }

        var middle = _pinned.Count / 2;
        for (var distance = 0; distance <= middle; distance++)
        {
            foreach (var index in (ReadOnlySpan<int>)[middle - distance, middle + distance])
            {
                if (index >= 0 && index < _pinned.Count && HasLabel(_pinned[index]))
                {
                    return _pinned[index];
                }
            }
        }

        return null;

        static bool HasLabel(PinnedAppSetting pin) => !pin.IsSeparator && !string.IsNullOrWhiteSpace(pin.Label);
    }

    /// <summary>
    /// Puts the Items page's selection down when a press lands on nothing in particular — the
    /// page around the list, or the list below its last row — as a click on empty space does in
    /// Explorer. The dock lets go of the item it was holding up along with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never on a control. The buttons beside the list act on the selection, and must find it
    /// still there; a checkbox, a scroll bar or a page's tab is pressed for itself.
    /// </para>
    /// <para>
    /// Inside the list, only below the last row. The rows stand a couple of pixels apart, and a
    /// press in the gap between two is a near miss for one of them, not a choice of neither.
    /// </para>
    /// </remarks>
    private void OnPressOutsideItems(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right)
            || Tabs.SelectedItem != ItemsPage
            || PinnedList.SelectedIndex < 0)
        {
            return;
        }

        for (var at = e.OriginalSource as DependencyObject; at is not null; at = PressParent(at))
        {
            if (at is ListBoxItem or ButtonBase or Thumb or ScrollBar or TextBoxBase or ComboBox
                or Slider or TabItem or MenuItem or Hyperlink)
            {
                return;
            }

            if (ReferenceEquals(at, PinnedList) && !IsBelowLastRow(e.GetPosition(PinnedList)))
            {
                return;
            }

            // Reached only by a press in this window. One in a popup — the Add menu's — ends
            // its climb at the popup, and is not a press on the page.
            if (ReferenceEquals(at, this))
            {
                PinnedList.SelectedIndex = -1;
                return;
            }
        }
    }

    /// <summary>Whether a point in the list lies below its last row, where there is nothing.</summary>
    private bool IsBelowLastRow(Point point) =>
        _pinned.Count > 0
        && RowContainer(_pinned.Count - 1) is { } last
        && point.Y > last.TranslatePoint(new Point(0, last.ActualHeight), PinnedList).Y;

    /// <summary>
    /// The next element up from where a press landed: the visual parent, or for text — a run
    /// inside a text block — the logical one, which is where its visual tree begins.
    /// </summary>
    private static DependencyObject? PressParent(DependencyObject child) =>
        child is Visual ? VisualTreeHelper.GetParent(child) : LogicalTreeHelper.GetParent(child);

    /// <summary>
    /// Reads the Recycle Bin's row icon again, when the dock says the bin's icon has changed by
    /// itself — emptied, or given something, wherever that was done.
    /// </summary>
    /// <remarks>
    /// A row asks for its icon once, when it is made, so the bin's stayed full or empty as it
    /// was when the dialog opened. Only that row's binding is run again: the list is not rebuilt,
    /// so the selection and the scroll stay where they were. The bin's icon is the one
    /// <see cref="PinnedAppsService.LoadIcon"/> never keeps, so asking again reads it afresh. A
    /// row scrolled out of view has no container, and will ask when it is made again.
    /// </remarks>
    public void RefreshRecycleBinRows()
    {
        // The Hotkeys page shows it too, when it is among the first nine.
        SchedulePlaceIcons();

        for (var index = 0; index < _pinned.Count; index++)
        {
            if (!DockPresets.IsRecycleBin(_pinned[index].TargetPath)
                || RowContainer(index) is not { } row
                || FindRowIcon(row) is not { } icon)
            {
                continue;
            }

            System.Windows.Data.BindingOperations
                .GetMultiBindingExpression(icon, Image.SourceProperty)?.UpdateTarget();
        }
    }

    /// <summary>The row template's icon, <c>ItemIcon</c>, somewhere under a row's container.</summary>
    private static Image? FindRowIcon(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Image { Name: "ItemIcon" } icon)
            {
                return icon;
            }

            if (FindRowIcon(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // ---- the two locks -------------------------------------------------------

    /// <summary>Whether the order is fixed, as the checkbox has it right now.</summary>
    /// <remarks>
    /// Read from the control rather than from the settings, because this dialog previews
    /// without saving: the lock has to bite here the moment it is ticked, not once Save has
    /// been pressed.
    /// </remarks>
    private bool OrderLocked => LockOrderCheck.IsChecked == true;

    /// <summary>
    /// Whether the dock is closed to changes — additions, removals and edits alike. Read the
    /// same way, and for the same reason.
    /// </summary>
    private bool ContentsLocked => LockContentsCheck.IsChecked == true;

    /// <summary>What the hint under the list says while neither lock is on.</summary>
    private static string ReorderHint => Localizer.Get("Settings.Items.Hint.Reorder");

    /// <summary>And while the order cannot be dragged.</summary>
    private static string OrderLockedHint => Localizer.Get("Settings.Items.Hint.OrderLocked");

    /// <summary>And while nothing may be added, taken away or edited.</summary>
    private static string ContentsLockedHint => Localizer.Get("Settings.Items.Hint.ContentsLocked");

    /// <summary>And while both are on, which leaves nothing to do here at all.</summary>
    private static string BothLockedHint => Localizer.Get("Settings.Items.Hint.BothLocked");

    private void OnItemLockChanged()
    {
        UpdateItemLocks();
        Preview();
    }

    /// <summary>Puts the Items page in step with both locks.</summary>
    /// <remarks>
    /// The buttons and the hint both, because a hint that still explains how to drag rows
    /// around beneath two greyed Move buttons reads as a dialog that has broken rather than
    /// as one doing what it was asked.
    /// </remarks>
    private void UpdateItemLocks()
    {
        ItemsHint.Text = (OrderLocked, ContentsLocked) switch
        {
            (true, true) => BothLockedHint,
            (true, false) => OrderLockedHint,
            (false, true) => ContentsLockedHint,
            _ => ReorderHint
        };

        UpdateItemButtons();
    }

    private void RefreshColorPreview()
    {
        var color = BarPalette.Parse(BarColorBox.Text);

        // The swatch shows what the bar will actually be, which is not the box's colour
        // while the taskbar is driving it.
        BarColorPreview.Color = TaskbarColorCheck.IsChecked == true
            ? TaskbarColour.Current()
            : color;

        if (!_syncingColor)
        {
            _wheel.Color = color;
        }
    }

    /// <summary>
    /// Greys out everything the taskbar option overrides.
    /// </summary>
    /// <remarks>
    /// Rather than leaving the wheel and the swatches live and quietly ignored: a control
    /// that responds but changes nothing is worse than one that says it is not in charge.
    /// </remarks>
    private void UpdateColorControls()
    {
        var manual = TaskbarColorCheck.IsChecked != true;

        ManualColorPanel.IsEnabled = manual;
        BarColorBox.IsEnabled = manual;
    }

    private void OnTaskbarColorToggled()
    {
        // Read afresh: the accent may have changed since the dialog opened.
        TaskbarColour.Invalidate();

        UpdateColorControls();
        RefreshColorPreview();
        Preview();
    }

    // ---- saved colours -------------------------------------------------------

    /// <summary>Keeps the colour that is currently chosen, so it can be picked again later.</summary>
    private void SaveCurrentColor()
    {
        var hex = BarPalette.ToHex(BarPalette.Parse(BarColorBox.Text));

        // Moved to the front rather than added twice, so saving a colour you already have
        // is a no-op you do not have to notice.
        _savedColors.Remove(hex);
        _savedColors.Insert(0, hex);

        if (_savedColors.Count > DockSettings.MaxCustomColors)
        {
            _savedColors.RemoveRange(
                DockSettings.MaxCustomColors,
                _savedColors.Count - DockSettings.MaxCustomColors);
        }

        BuildSavedSwatches();
        Preview();
    }

    private void RemoveSavedColor(string hex)
    {
        if (!_savedColors.Remove(hex))
        {
            return;
        }

        BuildSavedSwatches();
        Preview();
    }

    /// <summary>Rebuilds the saved row from the list.</summary>
    private void BuildSavedSwatches()
    {
        CustomSwatchPanel.Children.Clear();

        foreach (var hex in _savedColors)
        {
            CustomSwatchPanel.Children.Add(BuildSwatch(hex, saved: true));
        }

        // The row is empty on a fresh install, and an empty strip of nothing does not
        // explain itself.
        NoSavedColorsHint.Visibility = _savedColors.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RemoveSelected()
    {
        var index = PinnedList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        _pinned.RemoveAt(index);

        // Leave the selection where the removed item was, so clearing several in a row does
        // not need the pointer back on the list between each.
        PinnedList.SelectedIndex = Math.Min(index, _pinned.Count - 1);

        _pinsEdited = true;
        UpdateItemButtons();
        Preview();
    }

    // ---- dragging a row up and down the list ---------------------------------
    //
    // Move up and Move down still work, and are still the way to do this from the keyboard.
    // This is for the mouse: the row is lifted out under the pointer and the rest of the list
    // reorders around it as it moves, so the order you are about to get is the order you can
    // already see.

    /// <summary>
    /// The private clipboard format the rows are dragged as.
    /// </summary>
    /// <remarks>
    /// Named after this application on purpose. The list is a drop target, and it must accept
    /// its own rows and nothing else — a file dragged in from Explorer is not a reorder.
    /// </remarks>
    private const string PinDragFormat = "ArtDock.PinnedApp";

    /// <summary>Row the left button went down on, or -1. Cleared once a drag starts.</summary>
    private int _pressedPin = -1;

    /// <summary>Where it went down, for telling a click from the start of a drag.</summary>
    private Point _pinPressPoint;

    /// <summary>The row being dragged, or null.</summary>
    private PinnedAppSetting? _draggedPin;

    /// <summary>The picture of it that follows the pointer.</summary>
    private DragGhost? _ghost;

    /// <summary>
    /// The row's own container, dimmed for the length of the drag so the ghost reads as the
    /// thing being held and the row as where it would land.
    /// </summary>
    private ListBoxItem? _draggedRow;

    /// <summary>The order before the drag, to go back to if it is abandoned.</summary>
    private List<PinnedAppSetting> _orderBeforeDrag = [];

    /// <summary>
    /// The top of the first row and the height of one, measured once when the drag starts.
    /// </summary>
    /// <remarks>
    /// Measured once rather than per move, and from the layout rather than from a container's
    /// current position: the rows are sliding while the drag is live, and asking a moving row
    /// where it is would feed the animation back into the arithmetic that drives it.
    /// </remarks>
    private double _rowTop;

    private double _rowHeight;

    /// <summary>True while a row is being dragged, which nothing else should disturb.</summary>
    private bool IsDraggingRow => _draggedPin is not null;

    private void OnPinnedPress(object sender, MouseButtonEventArgs e)
    {
        _pinPressPoint = e.GetPosition(PinnedList);
        _pressedPin = RowIndexUnder(e.OriginalSource as DependencyObject);
    }

    /// <summary>
    /// Promotes a held press into a drag once the pointer has travelled far enough.
    /// </summary>
    /// <remarks>
    /// The threshold is the system's own, so this feels like every other list in Windows and
    /// so that a click with an unsteady hand still selects rather than reorders.
    /// </remarks>
    private void OnPinnedMove(object sender, MouseEventArgs e)
    {
        if (_pressedPin < 0 || IsDraggingRow || OrderLocked
            || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var moved = e.GetPosition(PinnedList) - _pinPressPoint;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var from = _pressedPin;
        _pressedPin = -1;

        if (from >= _pinned.Count || RowContainer(from) is not ListBoxItem row)
        {
            return;
        }

        // Selected before it moves, so the row being held is the one the buttons beside the
        // list are talking about — and the one still selected after the drop.
        PinnedList.SelectedIndex = from;

        BeginRowDrag(from, row, e.GetPosition(PinnedList));

        try
        {
            // Blocks until the drop. Escape ends it with no effect, which is the one case
            // that has to put the order back.
            var result = DragDrop.DoDragDrop(
                PinnedList, new DataObject(PinDragFormat, from), DragDropEffects.Move);

            if (result == DragDropEffects.None)
            {
                RestoreOrderBeforeDrag();
            }
        }
        finally
        {
            EndRowDrag();
        }
    }

    private void BeginRowDrag(int index, ListBoxItem row, Point pointer)
    {
        _draggedPin = _pinned[index];
        _draggedRow = row;
        _orderBeforeDrag = [.. _pinned];

        // The geometry the whole drag is measured against, taken while nothing is moving.
        _rowHeight = row.ActualHeight;
        _rowTop = row.TranslatePoint(new Point(0, 0), PinnedList).Y - (index * _rowHeight);

        if (AdornerLayer.GetAdornerLayer(PinnedList) is { } layer)
        {
            _ghost = new DragGhost(PinnedList, row, pointer);
            layer.Add(_ghost);
        }

        // The row underneath is where the drop would land; the ghost is what is being held.
        row.Opacity = 0.35;
    }

    private void EndRowDrag()
    {
        if (_ghost is { } ghost)
        {
            AdornerLayer.GetAdornerLayer(PinnedList)?.Remove(ghost);
            _ghost = null;
        }

        if (_draggedRow is { } row)
        {
            row.Opacity = 1;
            _draggedRow = null;
        }

        _draggedPin = null;
        _orderBeforeDrag = [];
    }

    /// <summary>Puts the list back the way it was, for a drag abandoned with Escape.</summary>
    private void RestoreOrderBeforeDrag()
    {
        if (_orderBeforeDrag.Count != _pinned.Count)
        {
            return;
        }

        ApplyOrder(_orderBeforeDrag);
        Preview();
    }

    /// <summary>
    /// Moves the dragged row to wherever the pointer now is.
    /// </summary>
    /// <remarks>
    /// The reorder happens here rather than on the drop, so the list shows the order it is
    /// about to have. Rows slide into place — see <see cref="AnimatedRowPanel"/> — and the
    /// dock is told at the same time, because a reorder previewed in one place and not the
    /// other is worse than one previewed nowhere.
    /// </remarks>
    private void OnPinnedDragOver(object sender, DragEventArgs e)
    {
        if (_draggedPin is not { } dragged || !e.Data.GetDataPresent(PinDragFormat))
        {
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        var point = e.GetPosition(PinnedList);
        _ghost?.MoveTo(point);

        var target = RowIndexAt(point);
        var current = _pinned.IndexOf(dragged);

        if (current < 0 || target == current)
        {
            return;
        }

        _pinned.Move(current, target);
        PinnedList.SelectedIndex = target;

        // The container is a new one after a move, and it is the new one that has to be dim.
        _draggedRow = RowContainer(target) as ListBoxItem;
        if (_draggedRow is { } row)
        {
            row.Opacity = 0.35;
        }

        _pinsEdited = true;
        UpdateItemButtons();
        Preview();
    }

    /// <summary>The drop itself has nothing left to do; the list is already in order.</summary>
    private void OnPinnedDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(PinDragFormat))
        {
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    /// <summary>The row an element belongs to, or -1 when it belongs to none.</summary>
    private int RowIndexUnder(DependencyObject? source) =>
        source is not null
        && ItemsControl.ContainerFromElement(PinnedList, source) is ListBoxItem row
            ? PinnedList.ItemContainerGenerator.IndexFromContainer(row)
            : -1;

    /// <summary>
    /// The row the pointer is over, clamped to the list.
    /// </summary>
    /// <remarks>
    /// Arithmetic from the geometry measured at the start of the drag, rather than a walk
    /// over the containers: the containers are sliding, and one measured mid-slide would
    /// answer with where it is being drawn instead of where it belongs.
    /// </remarks>
    private int RowIndexAt(Point point)
    {
        if (_rowHeight <= 0 || _pinned.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Floor((point.Y - _rowTop) / _rowHeight);
        return Math.Clamp(index, 0, _pinned.Count - 1);
    }

    private FrameworkElement? RowContainer(int index) =>
        PinnedList.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;

    /// <summary>Reorders the list to match, moving rows rather than replacing them.</summary>
    /// <remarks>
    /// Moved so the rows slide, and so each row keeps its own object — which is what lets an
    /// order arriving from the dock be applied over an unsaved rename without undoing it.
    /// </remarks>
    private void ApplyOrder(IReadOnlyList<PinnedAppSetting> order)
    {
        for (var target = 0; target < order.Count; target++)
        {
            var current = IndexOfPin(order[target].Id);
            if (current >= 0 && current != target)
            {
                _pinned.Move(current, target);
            }
        }
    }

    private int IndexOfPin(string id)
    {
        for (var i = 0; i < _pinned.Count; i++)
        {
            if (string.Equals(_pinned[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The dragged row, drawn under the pointer.
    /// </summary>
    /// <remarks>
    /// A snapshot rather than a live <see cref="VisualBrush"/> of the row. The row is being
    /// moved and animated for the length of the drag, and a brush pointing at it would show
    /// the ghost doing the same — the ghost has to be the one still thing on screen.
    /// </remarks>
    private sealed class DragGhost : Adorner
    {
        private readonly ImageSource _row;
        private readonly Size _size;
        private readonly Vector _grab;
        private Point _pointer;

        public DragGhost(FrameworkElement adorned, FrameworkElement row, Point pointer)
            : base(adorned)
        {
            IsHitTestVisible = false;

            _size = new Size(row.ActualWidth, row.ActualHeight);
            _grab = pointer - row.TranslatePoint(new Point(0, 0), adorned);
            _pointer = pointer;

            var dpi = VisualTreeHelper.GetDpi(row);
            var snapshot = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Round(_size.Width * dpi.DpiScaleX)),
                Math.Max(1, (int)Math.Round(_size.Height * dpi.DpiScaleY)),
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);

            snapshot.Render(row);
            snapshot.Freeze();
            _row = snapshot;
        }

        public void MoveTo(Point pointer)
        {
            if ((pointer - _pointer).Length < 0.5)
            {
                return;
            }

            _pointer = pointer;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            // Held at the grab point, so the row does not jump under the pointer the moment
            // it is picked up.
            var origin = _pointer - _grab;
            var bounds = new Rect(origin.X, origin.Y, _size.Width, _size.Height);

            drawingContext.PushOpacity(0.75);
            drawingContext.DrawImage(_row, bounds);
            drawingContext.Pop();
        }
    }

    private void MoveSelected(int delta)
    {
        var index = PinnedList.SelectedIndex;
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _pinned.Count)
        {
            return;
        }

        _pinned.Move(index, target);
        PinnedList.SelectedIndex = target;

        _pinsEdited = true;

        // The ends of the list are where these stop being available.
        UpdateItemButtons();
        Preview();
    }

    /// <summary>
    /// Puts every page back to the stock values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pinned items go back to what a new dock starts with
    /// (<see cref="DockPresets.CreateDefaults"/>). They used to be kept, on the grounds that
    /// the list is the user's own — but a full reset that left the dock looking exactly as it
    /// did was taken for one that had not worked, and the starting set is all presets now
    /// rather than stock apps nobody wanted back. Nothing is written until Save, so Cancel
    /// still puts the old list back.
    /// </para>
    /// <para>
    /// The saved colours and the Exclusions page's programs are still kept: nothing on the
    /// dock shows them, and they have no starting set to go back to. The page the user is on
    /// is not one of the things being reset either.
    /// </para>
    /// </remarks>
    private void ResetToDefaults()
    {
        var defaults = new DockSettings
        {
            PinnedApps = DockPresets.CreateDefaults(),
            CustomColors = [.. _savedColors],
            NoRevealApps = [.. NoRevealPaths],
            SettingsPage = SelectedPage
        };
        LoadFrom(defaults);

        // The new list is unsaved work now, so the dock must not replace it if something is
        // pinned or reordered out there before Save — the same as after an import.
        _pinsEdited = true;
        UpdateItemButtons();
        Preview();
    }

    /// <summary>
    /// Runs one page's controls back to the stock values.
    /// </summary>
    /// <remarks>
    /// Each of these takes a fresh <see cref="DockSettings"/> and reads the stock value off
    /// it, rather than repeating the numbers here — the defaults are declared once, on the
    /// settings object, and a page reset that quietly disagreed with a fresh install would
    /// be worse than no page reset at all.
    /// </remarks>
    private void ResetSizePage()
    {
        var defaults = new DockSettings();

        _loading = true;
        BaseSizeSlider.Value = defaults.BaseSize;
        MaxScaleSlider.Value = defaults.MaxScale;
        InfluenceSlider.Value = defaults.Neighbours;
        GapSlider.Value = defaults.GapFraction;
        SweepCheck.IsChecked = defaults.PreviewSweep;
        _loading = false;

        Preview();
    }

    /// <remarks>
    /// The saved colours are left alone, for the same reason the pinned items are: they are
    /// the user's own list rather than a setting with a right answer. Removing them one at a
    /// time is what the row's own menu is for.
    /// </remarks>
    private void ResetAppearancePage()
    {
        var defaults = new DockSettings();

        _loading = true;
        OpacitySlider.Value = defaults.BarOpacity;
        RoundnessSlider.Value = Math.Clamp(defaults.BarRoundness, 0, 1);
        BarColorBox.Text = BarPalette.ToHex(BarPalette.Parse(defaults.BarColor));
        TaskbarColorCheck.IsChecked = defaults.UseTaskbarColor;
        BlurCheck.IsChecked = defaults.BlurBackground;
        UpdateColorControls();
        RefreshColorPreview();
        _loading = false;

        Preview();
    }

    /// <remarks>
    /// The icon set goes back to the apps' own icons, which is what a fresh dock draws. A set
    /// the user installed stays installed, and is one choice away in the list.
    /// </remarks>
    private void ResetIconsPage()
    {
        var defaults = new DockSettings();

        _loading = true;
        BuildIconSetList(defaults.IconSet);
        IconShadowsCheck.IsChecked = defaults.IconShadows;
        LoadLabelFont(defaults.LabelFontFamily, defaults.LabelFontSize, defaults.LabelFontStyle);
        _loading = false;

        Preview();
    }

    /// <summary>
    /// Empties the pinned list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This was a page reset, and re-seeded the list with the machine's own stock apps — the
    /// same set a fresh install is given. That is a poor thing to call a default: the seeded
    /// apps exist so a dock nobody has arranged yet is not an empty bar, not because anyone
    /// wants Notepad and Paint back. What the page is actually reached for is starting again
    /// from nothing.
    /// </para>
    /// <para>
    /// Emptying only means anything because the first-run seeding no longer fires on an empty
    /// list — see <see cref="SettingsStore.LoadedFromDisk"/>. Until that changed, a cleared
    /// list came back stocked on the next launch.
    /// </para>
    /// <para>
    /// The order lock is left alone: it is this page's own setting rather than part of the
    /// list, it does not stop items being added back, and silently unlocking would undo a
    /// choice the user made about something else. Nothing is written until Save, so Cancel
    /// still puts the whole list back.
    /// </para>
    /// </remarks>
    private void ClearItems()
    {
        if (_pinned.Count == 0)
        {
            return;
        }

        _pinned.Clear();
        PinnedList.SelectedIndex = -1;

        _pinsEdited = true;
        UpdateItemButtons();
        Preview();
    }

    /// <summary>Greys the preview delay while the previews are off: a slider that changes nothing should say so.</summary>
    private void UpdatePreviewControls() =>
        PreviewDelayCard.IsEnabled = WindowPreviewsCheck.IsChecked == true;

    private void ResetBehaviourPage()
    {
        var defaults = new DockSettings();

        _loading = true;
        AlwaysOnTopCheck.IsChecked = defaults.AlwaysOnTop;
        AutoHideCheck.IsChecked = defaults.AutoHide;
        HideDelaySlider.Value = defaults.HideDelayMs;
        RevealDelaySlider.Value = defaults.RevealDelayMs;
        WindowPreviewsCheck.IsChecked = defaults.WindowPreviews;
        PreviewDelaySlider.Value = defaults.PreviewDelayMs;
        UpdatePreviewControls();
        HandleCheck.IsChecked = defaults.ShowHandle;
        HandleMatchDockCheck.IsChecked = defaults.HandleMatchesDock;
        HandleWidthSlider.Value = defaults.HandleWidth;
        UpdateHandleControls();
        _loading = false;

        Preview();
    }

    /// <summary>
    /// Greys the handle's width with the handle off, and the width slider while the width is the
    /// dock's — a control that responds but changes nothing is worse than one that says it is
    /// not in charge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handle itself is not greyed with auto-hide off any more, since 2026-09-30: it marks a
    /// dock that is under other windows as well as one that has slid away.
    /// </para>
    /// <para>
    /// Disabled is not enough on its own. A disabled card dims, because the <c>Card</c> style
    /// says so, but nothing dims a panel inside one: a <c>TextBlock</c> keeps its colour whether
    /// it is enabled or not, so the width row went on reading "Handle width … 140 px" at full
    /// strength while its slider could not be moved, and only the slider's thumb said otherwise.
    /// </para>
    /// <para>
    /// So the outermost part that is disabled is dimmed as the card is, and only that part. A
    /// style trigger on each would dim them all — IsEnabled passes down, so a row inside a
    /// greyed card would be greyed twice over, to a fifth of its strength.
    /// </para>
    /// </remarks>
    private void UpdateHandleControls()
    {
        var options = HandleCheck.IsChecked == true;
        var width = HandleMatchDockCheck.IsChecked != true;

        HandleOptionsPanel.IsEnabled = options;
        HandleWidthRow.IsEnabled = width;

        HandleOptionsPanel.Opacity = options ? 1 : DisabledOpacity;
        HandleWidthRow.Opacity = options && !width ? DisabledOpacity : 1;
    }

    /// <summary>How far a disabled part of a card is dimmed: as far as the <c>Card</c> style dims a disabled card.</summary>
    private const double DisabledOpacity = 0.45;

    // ---- exclusions ----------------------------------------------------------

    /// <summary>One program the dock stays down for, as the Exclusions page lists it.</summary>
    /// <param name="Path">Where the program is — what is stored.</param>
    /// <param name="Name">What it calls itself, read once when the row is made.</param>
    /// <param name="Icon">
    /// Its icon, also read once. Null for a program that has gone — uninstalled since it was
    /// listed — which keeps its row, since it may come back.
    /// </param>
    public sealed record NoRevealRow(string Path, string Name, ImageSource? Icon)
    {
        /// <summary>Twice the 24 DIPs the page draws it at, so it is crisp at 200%.</summary>
        private const int IconPixels = 48;

        public static NoRevealRow For(string path) =>
            new(path, FullscreenApps.DisplayName(path), ShellIcons.Load(path, IconPixels));
    }

    /// <summary>The paths the page holds, in its order.</summary>
    private IEnumerable<string> NoRevealPaths => _noReveal.Select(row => row.Path);

    private void FillNoRevealList(IEnumerable<string?> apps)
    {
        _noReveal.Clear();
        foreach (var path in apps)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                _noReveal.Add(NoRevealRow.For(path));
            }
        }

        UpdateNoRevealControls();
    }

    /// <summary>
    /// Offers every program with a window open, then Browse… for one without.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built afresh on every click, rather than once like the Items page's Add: what is open
    /// changes, and the likeliest way anyone arrives here is by leaving the very game they
    /// want to add running in the background.
    /// </para>
    /// <para>
    /// ArtDock itself is left out, and so is anything already listed — by the same file-name
    /// match the dock uses, so the menu never offers what would be a duplicate in effect.
    /// </para>
    /// </remarks>
    private void OpenNoRevealMenu()
    {
        var own = Environment.ProcessPath;
        var listed = NoRevealPaths.ToList();
        var open = RunningAppsService.WithWindows()
            .Where(path => !string.Equals(path, own, StringComparison.OrdinalIgnoreCase)
                && !FullscreenApps.Contains(listed, path))
            .Select(NoRevealRow.For)
            .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var menu = new ContextMenu
        {
            PlacementTarget = NoRevealAddButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        if (open.Count == 0)
        {
            menu.Items.Add(new MenuItem
            {
                Header = Localizer.Get("Settings.Exclusions.NoneOpen"),
                Icon = MenuIcons.Glyph(MenuGlyph.Info),
                IsEnabled = false
            });
        }

        foreach (var row in open)
        {
            var entry = new MenuItem
            {
                // Doubled, or an underscore in a program's name is taken for an access key and
                // disappears from the menu.
                Header = row.Name.Replace("_", "__"),

                // The column menus keep for shortcut keys: the file name, which is what the
                // entry is matched on, and what tells two programs of the same name apart.
                InputGestureText = FullscreenApps.KeyOf(row.Path),
                ToolTip = row.Path,

                // The program's own icon rather than a glyph: a program is recognised by its
                // icon before its name.
                Icon = MenuIcons.Picture(row.Icon)
            };

            entry.Click += (_, _) => AddNoReveal([row]);
            menu.Items.Add(entry);
        }

        menu.Items.Add(new Separator());

        var browse = new MenuItem
        {
            Header = Localizer.Get("Common.Browse"),
            Icon = MenuIcons.Glyph(MenuGlyph.Browse)
        };
        browse.Click += (_, _) => BrowseForNoReveal();
        menu.Items.Add(browse);

        menu.IsOpen = true;
    }

    private void BrowseForNoReveal()
    {
        var dialog = new OpenFileDialog
        {
            Title = Localizer.Get("Settings.Exclusions.Browse.Title"),
            Filter = FileFilters.Programs,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        // A shortcut arrives here as the program it points at — the dialog follows links — so
        // anything else really is not a program, and could never be in front to be matched.
        if (!string.Equals(Path.GetExtension(dialog.FileName), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            Complain(Localizer.Get("Settings.Exclusions.NotAProgram"));
            return;
        }

        AddNoReveal([NoRevealRow.For(dialog.FileName)]);
    }

    /// <summary>
    /// Looks through the games — or the apps — on this machine, and adds the ones ticked.
    /// </summary>
    /// <remarks>
    /// For a game that is started by a launcher, whose program nobody has had reason to know
    /// the name or the place of — see <see cref="ScanWindow"/>. It asks for no folder: it finds
    /// the launchers' games by itself, and offers a folder of the user's own from inside.
    /// </remarks>
    private void ScanForNoReveal(ScanKind kind)
    {
        var scan = new ScanWindow(kind, NoRevealPaths) { Owner = this };
        if (scan.ShowDialog() != true || scan.Chosen.Count == 0)
        {
            return;
        }

        AddNoReveal(scan.Chosen.Select(NoRevealRow.For));
    }

    /// <summary>
    /// Adds programs, passing over any already there, and selects the last of them.
    /// </summary>
    private void AddNoReveal(IEnumerable<NoRevealRow> rows)
    {
        NoRevealRow? last = null;
        foreach (var row in rows)
        {
            var existing = _noReveal.FirstOrDefault(
                other => FullscreenApps.Contains([other.Path], row.Path));

            if (existing is null)
            {
                _noReveal.Add(row);
                existing = row;
            }

            last = existing;
        }

        if (last is null)
        {
            return;
        }

        NoRevealList.SelectedItem = last;
        NoRevealList.ScrollIntoView(last);

        UpdateNoRevealControls();
        Preview();
    }

    private void RemoveNoReveal()
    {
        var index = NoRevealList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        _noReveal.RemoveAt(index);

        // Left where the removed one was, as the Items page does, so a run of removals does
        // not need the pointer back on the list between each.
        NoRevealList.SelectedIndex = Math.Min(index, _noReveal.Count - 1);

        UpdateNoRevealControls();
        Preview();
    }

    private void UpdateNoRevealControls()
    {
        NoRevealRemoveButton.IsEnabled = NoRevealList.SelectedIndex >= 0;
        NoRevealEmptyHint.Visibility = _noReveal.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- hotkeys -------------------------------------------------------------

    /// <summary>One row of the Hotkeys page: the box that records, the × in it, and the line under it.</summary>
    private sealed class HotkeyRow(HotkeyAction action, HotkeyRecorder recorder, Button clear, TextBlock status)
    {
        public HotkeyAction Action { get; } = action;

        public HotkeyRecorder Recorder { get; } = recorder;

        public Button Clear { get; } = clear;

        public TextBlock Status { get; } = status;

        /// <summary>
        /// What was wrong with the last combination pressed into the box, until another is
        /// recorded or the box is left.
        /// </summary>
        public HotkeyProblem? Refusal { get; set; }
    }

    /// <summary>The Hotkeys page's rows, by action, in the order the page lists them.</summary>
    private readonly Dictionary<HotkeyAction, HotkeyRow> _hotkeyRows = [];

    /// <summary>
    /// Hotkeys stored under names this build does not know — a later version's actions — carried
    /// through to Save as they were found, the way <see cref="_edge"/> is.
    /// </summary>
    private Dictionary<string, string> _otherHotkeys = [];

    /// <summary>The hotkeys another program has, as the dock last found them, by action.</summary>
    private IReadOnlyDictionary<HotkeyAction, Hotkey> _takenHotkeys = new Dictionary<HotkeyAction, Hotkey>();

    /// <summary>Whether the dock was last told a box is recording, so it is told only of a change.</summary>
    private bool _toldRecording;

    /// <summary>
    /// Raised with true as a box on the Hotkeys page starts recording, and with false once none
    /// is — for the dock to let its hotkeys go meanwhile, or pressing the one being changed would
    /// fire it rather than reach the box.
    /// </summary>
    public event EventHandler<bool>? HotkeyRecording;

    /// <summary>True while a box on the Hotkeys page has the keyboard, and records.</summary>
    private bool IsRecordingHotkey => _hotkeyRows.Values.Any(row => row.Recorder.IsRecording);

    /// <summary>Says, beside each hotkey another program has, that it does.</summary>
    public void ShowTakenHotkeys(IReadOnlyDictionary<HotkeyAction, Hotkey> taken)
    {
        _takenHotkeys = new Dictionary<HotkeyAction, Hotkey>(taken);
        ShowHotkeyStatus();
    }

    /// <summary>Finds each action's row by its name, and makes its box record.</summary>
    private void BuildHotkeyRows()
    {
        foreach (var action in HotkeyActions.All)
        {
            var name = action + "Hotkey";
            if (FindName(name) is not TextBox box
                || FindName(name + "Clear") is not Button clear
                || FindName(name + "Status") is not TextBlock status)
            {
                throw new InvalidOperationException($"The Hotkeys page has no row named {name}.");
            }

            var row = new HotkeyRow(action, new HotkeyRecorder(box), clear, status);

            row.Recorder.ValueChanged += (_, _) =>
            {
                row.Refusal = null;
                OnHotkeysEdited();
            };

            row.Recorder.Refused += (_, problem) =>
            {
                row.Refusal = problem;
                ShowHotkeyStatus();
            };

            row.Recorder.RecordingChanged += (_, _) =>
            {
                if (!row.Recorder.IsRecording && row.Refusal is not null)
                {
                    row.Refusal = null;
                    ShowHotkeyStatus();
                }

                // Once the keyboard has settled: from one box to the next is a moment with none
                // recording, and the dock would take its hotkeys back for it.
                Dispatcher.BeginInvoke(TellRecording, DispatcherPriority.Input);
            };

            clear.Click += (_, _) =>
            {
                row.Recorder.Value = null;
                row.Refusal = null;
                OnHotkeysEdited();
            };

            _hotkeyRows[action] = row;
        }

        for (var place = 1; place <= _placeIcons.Length; place++)
        {
            _placeIcons[place - 1] = FindName($"Place{place}Icon") as Image
                ?? throw new InvalidOperationException($"The Hotkeys page has no icon named Place{place}Icon.");
            _placeChecks[place - 1] = FindName($"Place{place}Check") as CheckBox
                ?? throw new InvalidOperationException($"The Hotkeys page has no checkbox named Place{place}Check.");
        }

        // The items move, come and go on the Items page — and the whole list is read again when
        // the dock changes it — so the icons follow the list.
        _pinned.CollectionChanged += (_, _) => SchedulePlaceIcons();
    }

    /// <summary>The icon beside each place's row, place 1 first.</summary>
    private readonly Image[] _placeIcons = new Image[9];

    /// <summary>The checkbox that turns each place's keys on and off by themselves, place 1 first.</summary>
    private readonly CheckBox[] _placeChecks = new CheckBox[9];

    /// <summary>Whether <see cref="ShowPlaceIcons"/> is already on its way.</summary>
    private bool _placeIconsPending;

    /// <summary>Shows the places' icons afresh, once whatever is changing the list has finished.</summary>
    /// <remarks>
    /// A list read again is cleared and filled a pin at a time, each a change of its own, and the
    /// Recycle Bin's icon is read afresh every time it is asked for; so the icons are put right
    /// once, after the last. Before the next frame is drawn, so a page opening shows them from
    /// the first.
    /// </remarks>
    private void SchedulePlaceIcons()
    {
        if (_placeIconsPending)
        {
            return;
        }

        _placeIconsPending = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                _placeIconsPending = false;
                ShowPlaceIcons();
            },
            DispatcherPriority.Normal);
    }

    /// <summary>
    /// Puts before each place's name the icon of the item in that place, with the item's name as
    /// its tooltip — from this dialog's list, unsaved changes and all, counted without the
    /// separators as the hotkeys count them, and drawn from the icon set chosen on the Icons
    /// page, as the dock draws it. Nothing past the last item.
    /// </summary>
    private void ShowPlaceIcons()
    {
        var items = _pinned.Where(pin => !pin.IsSeparator).ToList();
        var set = IconSetLibrary.Installed.Find(SelectedIconSet);

        for (var index = 0; index < _placeIcons.Length; index++)
        {
            var pin = index < items.Count ? items[index] : null;
            var icon = _placeIcons[index];

            icon.Source = pin is null ? null : PinnedAppsService.LoadIcon(PinnedAppsService.ToDockItem(pin), set);
            icon.ToolTip = pin is { Label.Length: > 0 } ? pin.Label : null;
            System.Windows.Automation.AutomationProperties.SetName(icon, pin?.Label ?? string.Empty);
        }
    }

    private void LoadHotkeys(DockSettings settings)
    {
        foreach (var row in _hotkeyRows.Values)
        {
            row.Recorder.Value = settings.HotkeyFor(row.Action);
            row.Refusal = null;
        }

        _otherHotkeys = settings.Hotkeys
            .Where(pair => !HotkeyActions.IsAction(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        QuickLaunchCheck.IsChecked = settings.QuickLaunch;
        ShowPlacesOff(settings.QuickLaunchOff);
        NumbersOnWinCtrlCheck.IsChecked = settings.NumbersOnWinCtrl;
        RevealOnWinCtrlCheck.IsChecked = settings.RevealOnWinCtrl;

        UpdateHotkeyControls();
    }

    /// <summary>Whether the items' hotkeys are to be registered, as the page has it.</summary>
    private bool QuickLaunch => QuickLaunchCheck.IsChecked == true;

    /// <summary>The places whose keys are off by themselves, as the page has them — their checkboxes not ticked.</summary>
    private List<int> PlacesOff =>
        [.. Enumerable.Range(1, _placeChecks.Length).Where(place => _placeChecks[place - 1].IsChecked != true)];

    /// <summary>Ticks each place's checkbox, but for those of the places that are off.</summary>
    private void ShowPlacesOff(IReadOnlyCollection<int> placesOff)
    {
        for (var place = 1; place <= _placeChecks.Length; place++)
        {
            _placeChecks[place - 1].IsChecked = !placesOff.Contains(place);
        }
    }

    /// <summary>The hotkey each row holds.</summary>
    private Dictionary<HotkeyAction, Hotkey?> HotkeyChoices() =>
        _hotkeyRows.ToDictionary(pair => pair.Key, pair => pair.Value.Recorder.Value);

    private void OnHotkeysEdited()
    {
        UpdateHotkeyControls();
        Preview();
    }

    /// <summary>
    /// Greys each box's × with nothing to clear — which hides it — and says what is wrong with
    /// each row.
    /// </summary>
    private void UpdateHotkeyControls()
    {
        foreach (var row in _hotkeyRows.Values)
        {
            row.Clear.IsEnabled = row.Recorder.Value is not null;
        }

        ShowHotkeyStatus();
    }

    /// <summary>
    /// Says, under each row, the worst that is wrong with it: keys just pressed that cannot be a
    /// hotkey; keys another row has, which that row keeps; keys another program has; and,
    /// for keys that work, what they may cost — Ctrl+Alt is AltGr on many keyboards, and
    /// without the Windows key a combination is one programs may want for themselves. Nothing
    /// for the items while quick launch is off, nor for an item turned off by itself: their keys
    /// are nobody's then, theirs included.
    /// </summary>
    private void ShowHotkeyStatus()
    {
        var choices = HotkeyActions.InUse(HotkeyChoices(), QuickLaunch, PlacesOff);

        foreach (var row in _hotkeyRows.Values)
        {
            var value = row.Recorder.Value;
            var message = !choices.ContainsKey(row.Action) ? null : row.Refusal switch
            {
                HotkeyProblem.Reserved => Localizer.Get("Settings.Hotkeys.Reserved"),
                HotkeyProblem.Windows => Localizer.Get("Settings.Hotkeys.Windows"),
                HotkeyProblem.NeedsModifier or HotkeyProblem.NotAKey => Localizer.Get("Settings.Hotkeys.NeedsModifier"),
                _ => HotkeyActions.SharedWith(choices, row.Action) is { } earlier
                        ? Localizer.Format("Settings.Hotkeys.Shared", HotkeyLabel(earlier))
                    : value is { } hotkey && _takenHotkeys.TryGetValue(row.Action, out var taken) && taken == hotkey
                        ? Localizer.Get("Settings.Hotkeys.Taken")
                    : value is { IsAltGr: true }
                        ? Localizer.Get("Settings.Hotkeys.AltGr")
                    : value is { HasWin: false }
                        ? Localizer.Get("Settings.Hotkeys.Programs")
                    : null
            };

            row.Status.Text = message ?? string.Empty;
            row.Status.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>What a row is called on the page, for another row to say it has its keys.</summary>
    private static string HotkeyLabel(HotkeyAction action) => action switch
    {
        HotkeyAction.Keyboard => Localizer.Get("Settings.Hotkeys.Take"),
        HotkeyAction.ShowHide => Localizer.Get("Settings.Hotkeys.ShowHide"),
        HotkeyAction.Settings => Localizer.Get("Settings.Hotkeys.Settings"),
        _ when HotkeyActions.IsSecondary(action) => Localizer.Format("Settings.Hotkeys.Place.Secondary", HotkeyActions.Place(action)),
        _ => Localizer.Format("Settings.Hotkeys.Place", HotkeyActions.Place(action))
    };

    /// <summary>Tells the dock whether a box is recording, if that has changed.</summary>
    private void TellRecording()
    {
        var recording = IsRecordingHotkey;
        if (recording == _toldRecording)
        {
            return;
        }

        _toldRecording = recording;
        HotkeyRecording?.Invoke(this, recording);
    }

    /// <remarks>
    /// Back to the hotkeys a new dock starts with, quick launch on, for every item, and the items'
    /// numbers and the dock both up on Win+Ctrl. Those under names this build does not know are left as they were,
    /// being no row's here.
    /// </remarks>
    private void ResetHotkeysPage()
    {
        var defaults = new DockSettings();

        _loading = true;
        QuickLaunchCheck.IsChecked = defaults.QuickLaunch;
        ShowPlacesOff(defaults.QuickLaunchOff);
        NumbersOnWinCtrlCheck.IsChecked = defaults.NumbersOnWinCtrl;
        RevealOnWinCtrlCheck.IsChecked = defaults.RevealOnWinCtrl;
        _loading = false;

        foreach (var row in _hotkeyRows.Values)
        {
            row.Recorder.Value = defaults.HotkeyFor(row.Action);
            row.Refusal = null;
        }

        OnHotkeysEdited();
    }

    private void ResetSystemPage()
    {
        var defaults = new DockSettings();

        _loading = true;
        ThemeBox.SelectedIndex = AppTheme.IndexOf(defaults.Theme);
        BuildLanguageList(defaults.Language);
        RunAtLoginCheck.IsChecked = defaults.RunAtLogin;
        ReduceMotionCheck.IsChecked = defaults.ReduceMotion;
        NoGpuCheck.IsChecked = defaults.NoGpu;
        _loading = false;

        // The theme picker's own handler is suppressed while loading, and Mica is DWM's
        // rather than the theme system's, so it has to be told separately.
        ApplyMica();
        Preview();
    }

    /// <summary>
    /// No GPU was ticked or unticked: the cards it overrides say so, the dialog's own material
    /// goes or comes back, and the dock is shown the change.
    /// </summary>
    private void OnNoGpuToggled()
    {
        UpdateNoGpuControls();
        ApplyMica();
        Preview();
    }

    /// <summary>
    /// Greys out what No GPU overrides, and says when this machine looks like one it is for.
    /// </summary>
    /// <remarks>
    /// The blur and the icons' shadows keep their own values while they are greyed — it is the
    /// dock that sets them aside, in <see cref="DockSettings.Blurs"/> and
    /// <see cref="DockSettings.CastsIconShadows"/> — so they are still what is saved, and what
    /// comes back when the box is unticked. The opacity is not greyed: the bar is solid, but the
    /// slider still sets how much grey is mixed into its colour
    /// (<see cref="DockSettings.BarPaint"/>). The hint is only for a box that is not ticked:
    /// ticked, there is nothing left to suggest.
    /// </remarks>
    private void UpdateNoGpuControls()
    {
        var noGpu = NoGpuCheck.IsChecked == true;
        var note = noGpu ? Visibility.Visible : Visibility.Collapsed;

        BlurCard.IsEnabled = !noGpu;
        NoGpuNote.Visibility = note;

        IconShadowsCard.IsEnabled = !noGpu;
        NoGpuIconsNote.Visibility = note;

        NoGpuDetected.Visibility = !noGpu && SoftwareRendering.HardwareMissing
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// What Windows gives this process to draw with has changed — a remote session beginning or
    /// ending, a display adapter coming or going — so the hint is asked again.
    /// </summary>
    private void OnRenderTierChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(UpdateNoGpuControls);

    /// <summary>
    /// Puts the dialog on Mica, so it sits on the same material as Windows' own Settings
    /// app rather than on a flat colour of this project's choosing.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Here rather than in the constructor because picking the icon frame needs to know
        // which display the dialog came up on, and that needs the window handle.
        ShowLogo();

        ApplyMica();
    }

    /// <summary>
    /// Re-picks the mark when the dialog is dragged to a display at a different scale.
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ShowLogo();
    }

    /// <summary>Writes what the pages are showing to a file of the user's choosing.</summary>
    /// <remarks>
    /// <see cref="Compose"/> rather than the store, so an export carries what is on screen —
    /// including edits made since the dialog opened and not yet saved. Exporting the file on
    /// disk instead would quietly ignore whatever the user had just changed, which is the
    /// opposite of what someone reaches for this button to capture.
    /// </remarks>
    private void ExportSettings()
    {
        var dialog = new SaveFileDialog
        {
            Title = Localizer.Get("Settings.Export.Title"),
            FileName = $"ArtDock-settings-{DateTime.Now:yyyy-MM-dd}.json",
            DefaultExt = ".json",
            Filter = FileFilters.Settings,
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            SettingsStore.Export(Compose(), dialog.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Complain(Localizer.Format("Settings.Export.Failed", e.Message));
        }
    }

    /// <summary>Fills every page from a settings file, without writing anything.</summary>
    /// <remarks>
    /// No confirmation prompt, deliberately: an import lands on the pages and on the live dock
    /// exactly the way dragging a slider does, and Cancel still puts it all back. Save is what
    /// commits, which is what the card says it does. Autostart is the one thing that does not
    /// come across — <see cref="LoadFrom"/> reads that checkbox from the Run key rather than
    /// from the file, so importing cannot silently register the dock to start on a machine the
    /// user was only copying their tuning to.
    /// </remarks>
    private void ImportSettings()
    {
        var dialog = new OpenFileDialog
        {
            Title = Localizer.Get("Settings.Import.Title"),
            DefaultExt = ".json",
            Filter = FileFilters.Settings,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        DockSettings imported;
        try
        {
            imported = SettingsStore.Import(dialog.FileName);
        }
        catch (Exception e) when (e is InvalidDataException)
        {
            Complain(e.Message);
            return;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Complain(Localizer.Format("Settings.Import.Failed", e.Message));
            return;
        }

        // The page the user is looking at is theirs rather than the file's — the same
        // reasoning as the whole-application reset. Everything else comes across, pinned
        // items included, which is most of the point of moving a dock to another machine.
        imported.SettingsPage = SelectedPage;
        _bottomMargin = imported.BottomMargin;
        _edge = imported.Edge;

        LoadFrom(imported);

        // The imported list is now the user's unsaved work, so the dock must not replace it
        // if something is pinned or reordered out there while this dialog is open.
        _pinsEdited = true;
        UpdateItemButtons();
        Preview();
    }

    private void Complain(string message) =>
        MessageBox.Show(this, message, "ArtDock", MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>Which build this is, so the About card cannot claim to be the other one.</summary>
    /// <remarks>
    /// Worth the conditional: the dock that runs on this machine is the Release binary, and a
    /// change that reached only the Debug output looks exactly like a fix that did not work.
    /// </remarks>
    private const string BuildConfiguration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    /// <summary>Fills in the About card's text.</summary>
    /// <remarks>
    /// The informational version rather than the assembly version, because that is the one the
    /// SDK fills in from <c>&lt;Version&gt;</c> in the project file. It picks up a
    /// <c>+commit</c> suffix on builds that know their commit, which is not wanted here. The
    /// copyright comes from <c>&lt;Copyright&gt;</c> the same way, and is shown as it stands:
    /// the project file keeps it to a sign, a year and a name, which need no translating.
    /// </remarks>
    private void ShowAbout()
    {
        var assembly = typeof(SettingsWindow).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

        AboutCopyrightText.Text = copyright ?? "";
        AboutCopyrightText.Visibility = string.IsNullOrWhiteSpace(copyright)
            ? Visibility.Collapsed
            : Visibility.Visible;

        AboutVersionText.Text = Localizer.Format(
            "Settings.About.Version",
            informational?.Split('+')[0] ?? Localizer.Get("Settings.About.VersionUnknown"),
            BuildConfiguration);
        AboutRuntimeText.Text = $".NET {Environment.Version} · " +
            $"{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()} · " +
            $"Windows {Environment.OSVersion.Version}";
        SettingsPathText.Text = Localizer.Format("Settings.About.SettingsPath", SettingsStore.Location);
        ShowUpdates();
    }

    /// <summary>Where the About page's update card stands.</summary>
    private enum UpdateStage
    {
        Idle,
        Checking,
        UpToDate,
        Available,
        Downloading,
        Failed
    }

    /// <summary>Shows where the update card stands, in the current language.</summary>
    private void ShowUpdates()
    {
        if (!_updater.IsInstalled)
        {
            UpdateStatusText.Text = Localizer.Get("Settings.About.Updates.NotInstalled");
            UpdateButton.Visibility = Visibility.Collapsed;
            return;
        }

        var offered = _update?.TargetFullRelease.Version.ToString() ?? "";

        UpdateStatusText.Text = _updateStage switch
        {
            UpdateStage.Checking => Localizer.Get("Settings.About.Updates.Checking"),
            UpdateStage.UpToDate => Localizer.Get("Settings.About.Updates.UpToDate"),
            UpdateStage.Available => Localizer.Format("Settings.About.Updates.Available", offered),
            UpdateStage.Downloading => Localizer.Format("Settings.About.Updates.Downloading", offered, _updatePercent),
            UpdateStage.Failed => Localizer.Get("Settings.About.Updates.Failed"),
            _ => Localizer.Get("Settings.About.Updates.Hint")
        };

        UpdateButton.Content = Localizer.Get(_updateStage is UpdateStage.Available or UpdateStage.Downloading
            ? "Settings.About.Updates.Install"
            : "Settings.About.Updates.Check");
        UpdateButton.IsEnabled = _updateStage is not (UpdateStage.Checking or UpdateStage.Downloading);
        UpdateButton.Visibility = Visibility.Visible;
    }

    private void SetUpdateStage(UpdateStage stage)
    {
        _updateStage = stage;
        ShowUpdates();
    }

    /// <summary>
    /// The update card's button: a check, or — once a check has found a version — fetching
    /// that version and handing over to Velopack to put it in place.
    /// </summary>
    /// <remarks>
    /// Every failure is caught, whatever it is: this is an async void handler, where anything
    /// that escapes takes the dock down with it, and Velopack does not say what it throws — for
    /// a network that is not there, a feed that is not one, a hash that does not match.
    /// </remarks>
    private async void OnUpdateClicked()
    {
        if (_updateStage == UpdateStage.Available && _update is { } update)
        {
            await InstallUpdateAsync(update);
        }
        else
        {
            await CheckForUpdateAsync();
        }
    }

    private async Task CheckForUpdateAsync()
    {
        SetUpdateStage(UpdateStage.Checking);

        try
        {
            _update = await _updater.CheckAsync();
            SetUpdateStage(_update is null ? UpdateStage.UpToDate : UpdateStage.Available);
        }
        catch (Exception)
        {
            _update = null;
            SetUpdateStage(UpdateStage.Failed);
        }
    }

    private async Task InstallUpdateAsync(UpdateInfo update)
    {
        _updateDownload = new CancellationTokenSource();
        _updatePercent = 0;
        SetUpdateStage(UpdateStage.Downloading);

        try
        {
            await _updater.DownloadAsync(
                update,
                percent => Dispatcher.BeginInvoke(() =>
                {
                    if (_updateStage == UpdateStage.Downloading)
                    {
                        _updatePercent = percent;
                        ShowUpdates();
                    }
                }),
                _updateDownload.Token);

            // Velopack's updater waits for this process to go, so the dock is closed the way
            // Exit closes it — this dialog first, then the tray icon — and comes back as the
            // new version with this dialog open again. On this page, since the page open when
            // the dialog closes is kept, which shows that the update took.
            _updater.ApplyOnExit(update, "--settings");
        }
        catch (OperationCanceledException)
        {
            // The dialog was closed during the download, which is the user saying not now.
            return;
        }
        catch (Exception)
        {
            SetUpdateStage(UpdateStage.Failed);
            return;
        }

        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts the application mark on the About card at this display's scale.</summary>
    /// <remarks>
    /// The .ico carries nine sizes precisely so that whatever shows it can pick one rather than
    /// rescale another — see <c>tools/make-icon.py</c>. Handing the .ico straight to an
    /// <see cref="Image"/> gives that choice to WPF, which makes it once, from the frame order,
    /// rather than from the size it is about to draw: at 150% a 48 DIP image covers 72 real
    /// pixels, and stretching the 48px frame over them is the softness a multi-size icon exists
    /// to avoid. So take an exact frame if there is one and otherwise the smallest frame above
    /// the target, because downscaling stays sharp where upscaling does not.
    /// </remarks>
    private void ShowLogo()
    {
        var scale = MonitorDpi.ScaleForWindow(new WindowInteropHelper(this).Handle);
        var wanted = (int)Math.Round(AboutLogo.Width * scale);

        try
        {
            var frames = BitmapDecoder.Create(
                new Uri("pack://application:,,,/Assets/ArtDock.ico"),
                BitmapCreateOptions.None,
                BitmapCacheOption.OnLoad).Frames;

            AboutLogo.Source = frames
                .OrderBy(frame => frame.PixelWidth < wanted)
                .ThenBy(frame => Math.Abs(frame.PixelWidth - wanted))
                .FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UriFormatException or NotSupportedException)
        {
            // The card reads perfectly well without the mark.
        }
    }

    /// <summary>The appearance the picker is on.</summary>
    private string SelectedTheme => AppTheme.Choices[Math.Max(0, ThemeBox.SelectedIndex)];

    /// <summary>Fills the theme picker in the current language, keeping its selection.</summary>
    private void FillThemeBox()
    {
        var selected = ThemeBox.SelectedIndex;

        ThemeBox.Items.Clear();
        foreach (var label in AppTheme.Labels)
        {
            ThemeBox.Items.Add(label);
        }

        ThemeBox.SelectedIndex = selected;
    }

    // ---- the labels' lettering -----------------------------------------------

    /// <summary>What each typeface entry stands for: a family's name, or null for the dock's own.</summary>
    private readonly List<string?> _labelFontIds = [];

    /// <summary>What each emphasis entry stands for, in the order the list shows them.</summary>
    private readonly List<string?> _labelStyleIds = [];

    /// <summary>The typefaces installed, by name, read once — there are hundreds of them.</summary>
    private List<string>? _installedFonts;

    private List<string> InstalledFonts => _installedFonts ??=
    [
        .. Fonts.SystemFontFamilies
            .Select(family => family.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
    ];

    /// <summary>The chosen typeface, or null for the dock's own.</summary>
    private string? SelectedLabelFont => SelectedId(LabelFontBox, _labelFontIds);

    /// <summary>The chosen size, or null when it is the dock's own.</summary>
    private double? SelectedLabelSize =>
        Math.Abs(LabelSizeSlider.Value - DockBar.DefaultTooltipSize) < 0.01 ? null : LabelSizeSlider.Value;

    /// <summary>
    /// The chosen emphasis. Always one of the four — the list has no entry for the dock's own.
    /// </summary>
    /// <remarks>
    /// The typeface list does have one, because a typeface the user never chose should keep
    /// following the dock if the dock's own ever changes. Emphasis is different: the dock's own
    /// is bold, so an entry meaning "no choice" would have to be labelled Bold to be truthful,
    /// and would then sit next to the Bold entry doing the same thing.
    /// </remarks>
    private string? SelectedLabelStyle => SelectedId(LabelStyleBox, _labelStyleIds);

    /// <summary>Puts the lettering's three controls to the values given; null for the dock's own.</summary>
    private void LoadLabelFont(string? family, double? size, string? style)
    {
        BuildLabelFontLists(family, style);
        LabelSizeSlider.Value = Math.Clamp(
            size ?? DockBar.DefaultTooltipSize, LabelSizeSlider.Minimum, LabelSizeSlider.Maximum);
    }

    /// <summary>
    /// Fills the typeface and emphasis lists in the current language, with the given choices
    /// selected.
    /// </summary>
    /// <remarks>
    /// The first typeface is the dock's own, and says which that is. A typeface that is not
    /// installed here — in a file brought from another machine — stays in the list, marked as
    /// missing, and stays selected, as an icon set does; the dock falls back from it as WPF
    /// falls back for any missing font. An emphasis nobody chose shows as the dock's own, which
    /// is what the labels in front of the dialog are using.
    /// </remarks>
    private void BuildLabelFontLists(string? family, string? style)
    {
        var fonts = new List<(string? Id, string Label)>
        {
            (null, Localizer.Format("Settings.Icons.LabelFont.Default", DockBar.DefaultTooltipFont))
        };

        fonts.AddRange(InstalledFonts.Select(name => ((string?)name, name)));

        if (family is { Length: > 0 } && !InstalledFonts.Contains(family, StringComparer.OrdinalIgnoreCase))
        {
            fonts.Add((family, Localizer.Format("Settings.Icons.LabelFont.Missing", family)));
        }

        FillPicker(LabelFontBox, _labelFontIds, fonts, family);

        FillPicker(
            LabelStyleBox,
            _labelStyleIds,
            [
                ("Regular", Localizer.Get("Settings.Icons.LabelStyle.Regular")),
                ("Bold", Localizer.Get("Settings.Icons.LabelStyle.Bold")),
                ("Italic", Localizer.Get("Settings.Icons.LabelStyle.Italic")),
                ("BoldItalic", Localizer.Get("Settings.Icons.LabelStyle.BoldItalic"))
            ],
            style ?? DockBar.DefaultTooltipStyle);
    }

    // ---- language and icon set -----------------------------------------------

    /// <summary>What each language entry stands for: a tag, or null for following Windows.</summary>
    private readonly List<string?> _languageIds = [];

    /// <summary>What each icon-set entry stands for: an id, or null for the apps' own icons.</summary>
    private readonly List<string?> _iconSetIds = [];

    /// <summary>Set while the lists are being refilled in a new language, which chooses nothing.</summary>
    private bool _relabelling;

    private string? SelectedLanguage => SelectedId(LanguageBox, _languageIds);

    private string? SelectedIconSet => SelectedId(IconSetBox, _iconSetIds);

    private static string? SelectedId(ComboBox box, List<string?> ids) =>
        box.SelectedIndex >= 0 && box.SelectedIndex < ids.Count ? ids[box.SelectedIndex] : null;

    /// <summary>Fills the language picker, with <paramref name="chosen"/> selected.</summary>
    /// <remarks>
    /// The first entry follows Windows and says which language that comes to, since "Windows
    /// display language" on a machine with no pack for it means English, and saying so is
    /// better than leaving the user to wonder why nothing changed.
    /// </remarks>
    private void BuildLanguageList(string? chosen)
    {
        var library = Localizer.Library;
        var entries = new List<(string? Id, string Label)>
        {
            (null, Localizer.Format(
                "Settings.System.Language.FollowWindows",
                Localizer.WindowsChoice?.DisplayName ?? LanguageLibrary.EnglishId))
        };

        entries.AddRange(library.Languages.Select(language => ((string?)language.Id, language.DisplayName)));

        if (chosen is { Length: > 0 } && library.Find(chosen) is null)
        {
            entries.Add((chosen, Localizer.Format("Settings.System.Language.Missing", chosen)));
        }

        FillPicker(LanguageBox, _languageIds, entries, chosen);
        ShowProblems(LanguageProblems, library.Problems);
    }

    /// <summary>Fills the icon-set picker, with <paramref name="chosen"/> selected.</summary>
    private void BuildIconSetList(string? chosen)
    {
        var library = IconSetLibrary.Installed;
        var entries = new List<(string? Id, string Label)>
        {
            (null, Localizer.Get("Settings.Icons.IconSet.None"))
        };

        entries.AddRange(library.Sets.Select(set => ((string?)set.Id, set.Name)));

        if (chosen is { Length: > 0 } && library.Find(chosen) is null)
        {
            entries.Add((chosen, Localizer.Format("Settings.Icons.IconSet.Missing", chosen)));
        }

        FillPicker(IconSetBox, _iconSetIds, entries, chosen);
        ShowProblems(IconSetProblems, library.Problems);
    }

    /// <summary>
    /// Refills a picker whose entries stand for ids, and selects one.
    /// </summary>
    /// <remarks>
    /// A choice that is not installed stays in the list, marked as missing, and stays
    /// selected — the way the Position page carries an edge it cannot show. Falling back to
    /// the first entry would have Save write that fallback over the user's choice, and the
    /// pack coming back later would find its choice gone.
    /// </remarks>
    private void FillPicker(ComboBox box, List<string?> ids, List<(string? Id, string Label)> entries, string? chosen)
    {
        var wasLoading = _loading;
        _loading = true;

        box.Items.Clear();
        ids.Clear();
        foreach (var (id, label) in entries)
        {
            box.Items.Add(label);
            ids.Add(id);
        }

        box.SelectedIndex = Math.Max(0, ids.FindIndex(id => string.Equals(id, chosen, StringComparison.OrdinalIgnoreCase)));

        _loading = wasLoading;
    }

    /// <summary>
    /// Says which packs were skipped and why, under their picker — or nothing, when all is well.
    /// </summary>
    /// <remarks>
    /// A pack copied into place that does not appear, with no word why, is the worst outcome
    /// for whoever made it. The first few are enough to go on; the rest are almost always the
    /// same mistake repeated.
    /// </remarks>
    private static void ShowProblems(TextBlock text, IReadOnlyList<PackProblem> problems)
    {
        text.Text = string.Join(Environment.NewLine, problems.Take(3).Select(problem => problem.Describe()));
        text.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Reads the installed packs again and refreshes both pickers, keeping their selections.</summary>
    /// <remarks>
    /// Cheap — a directory listing and a few small files — and it runs whenever the window
    /// comes back to the front. The dock takes the new list on the next change of contents,
    /// which choosing a set is.
    /// </remarks>
    private void RefreshPacks()
    {
        Localizer.Rescan();
        IconSetLibrary.Rescan();

        BuildLanguageList(SelectedLanguage);
        BuildIconSetList(SelectedIconSet);
    }

    /// <summary>Opens a kind of pack's folder in Explorer, making it if it is not there yet.</summary>
    private static void OpenPackFolder(PackKind kind)
    {
        if (PackLocations.EnsureFolder(kind) is not { } folder)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing to say that the absence of an Explorer window does not already say.
        }
    }

    /// <summary>
    /// Says everything built in code again, in the language just chosen.
    /// </summary>
    /// <remarks>
    /// Posted rather than run where it is raised, because it is raised from inside this
    /// dialog's own preview of the language picker's selection — and refilling a combo box
    /// from inside its own selection change is asking for trouble.
    /// </remarks>
    private void OnLanguageChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            var wasLoading = _loading;
            _loading = true;
            _relabelling = true;

            FillThemeBox();
            FillScreenBox();
            BuildLanguageList(SelectedLanguage);
            BuildIconSetList(SelectedIconSet);
            BuildLabelFontLists(SelectedLabelFont, SelectedLabelStyle);

            _relabelling = false;
            _loading = wasLoading;

            BuildAddMenu();
            BuildSavedSwatches();
            ShowAbout();
            ShowEdgeOffset();
            UpdateItemLocks();

            foreach (var row in _hotkeyRows.Values)
            {
                row.Recorder.Show();
            }

            ShowHotkeyStatus();
        });

    /// <summary>
    /// Puts the window on Mica in whichever appearance is currently chosen, and tells DWM
    /// to match the title bar to it.
    /// </summary>
    /// <remarks>
    /// Not under No GPU, where it is given the theme's plain background instead — see
    /// <see cref="WindowMaterial"/>, which does the same for every dialog. Followed as the box
    /// is ticked and unticked, since that is previewed like everything else.
    /// </remarks>
    private void ApplyMica() =>
        WindowMaterial.Apply(this, solid: NoGpuCheck.IsChecked == true, AppTheme.IsDark(SelectedTheme));

    /// <summary>
    /// Takes a change made outside this dialog while it is open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A reorder on the dock is the case this exists for. It arrives as the same items in a
    /// different order, and is applied to the rows already here rather than replacing them —
    /// so a name typed into the item editor and not yet saved survives a reorder made behind
    /// it, and the rows slide into their new places instead of appearing in them.
    /// </para>
    /// <para>
    /// A change to the <i>set</i> of items — something pinned or unpinned on the dock — is
    /// taken only when the list here has not been edited. If it has, the user has unsaved
    /// work in front of them and it is theirs that Save should write; adopting silently
    /// would discard it.
    /// </para>
    /// </remarks>
    private void OnStoreChanged(object? sender, DockSettings settings)
    {
        // Nothing may move under a drag in progress, and the load is not a change.
        if (_loading || IsDraggingRow)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            if (HasSamePins(settings.PinnedApps))
            {
                _loading = true;
                ApplyOrder(settings.PinnedApps);
                _loading = false;

                UpdateItemButtons();

                // The Icons page holds the item nearest the middle, which a reorder can change.
                AnnounceSelectedItem();
                return;
            }

            if (_pinsEdited)
            {
                return;
            }

            _loading = true;
            _pinned.Clear();
            foreach (var pin in settings.PinnedApps)
            {
                _pinned.Add(pin);
            }

            _loading = false;
            UpdateItemButtons();
            AnnounceSelectedItem();
        });
    }

    /// <summary>True when a list holds the same items as this one, in any order.</summary>
    private bool HasSamePins(IReadOnlyList<PinnedAppSetting> other)
    {
        if (other.Count != _pinned.Count)
        {
            return false;
        }

        foreach (var pin in other)
        {
            if (IndexOfPin(pin.Id) < 0)
            {
                return false;
            }
        }

        return true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _autostartWatch?.Dispose();
        _autostartWatch = null;
        _store.Changed -= OnStoreChanged;
        Localizer.LanguageChanged -= OnLanguageChanged;
        RenderCapability.TierChanged -= OnRenderTierChanged;
        _updateDownload?.Cancel();

        Preview();
        base.OnClosed(e);
    }
}
