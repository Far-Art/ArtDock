using System.Text.Json.Serialization;
using ArtDock.Dock;

namespace ArtDock.Services;

/// <summary>One pinned application, as stored on disk.</summary>
public sealed class PinnedAppSetting
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>Filesystem target for a classic app. Null for Store apps.</summary>
    public string? TargetPath { get; set; }

    /// <summary>Application User Model ID for a Store app.</summary>
    public string? Aumid { get; set; }

    /// <summary>
    /// A custom icon chosen by the user, overriding whatever the shell would supply.
    /// Null means "use the target's own icon".
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// For a pin that opens a picture: draw it with the file's icon instead of its thumbnail.
    /// </summary>
    /// <remarks>
    /// Off by default, so a picture is shown as itself — and so a file written before this
    /// existed, which has no such property, draws its pictures the new way. An image chosen
    /// in <see cref="IconPath"/> wins over both.
    /// </remarks>
    public bool UseIconNotThumbnail { get; set; }

    /// <summary>Typeface for this item's label. Null uses the dock's default.</summary>
    public string? FontFamily { get; set; }

    /// <summary>Label size in points. Null uses the dock's default.</summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Label emphasis: <c>Regular</c>, <c>Bold</c>, <c>Italic</c> or <c>BoldItalic</c>.
    /// Null uses the dock's default.
    /// </summary>
    public string? FontStyle { get; set; }

    /// <summary>A divider rather than an application; launches nothing.</summary>
    public bool IsSeparator { get; set; }
}

/// <summary>
/// Everything the user can configure. Serialised as-is, so property names are the file
/// format — rename with care.
/// </summary>
public sealed class DockSettings
{
    // ---- format --------------------------------------------------------------

    /// <summary>The settings format this build writes and understands.</summary>
    /// <remarks>
    /// Do not bump this for adding a setting: an unknown property is ignored on read and a
    /// missing one falls back to its initialiser, so growing the file is already safe. It is
    /// for a change that would make an existing file mean the wrong thing — a property whose
    /// units, scale or meaning changed, or one that was split or removed. Bumping it then is
    /// what lets a migration tell "this file predates the change" from "the user chose that
    /// value", which is not recoverable after the fact. Nothing migrates yet, because nothing
    /// has needed to.
    /// </remarks>
    public const int CurrentVersion = 1;

    /// <summary>The format this file was written against.</summary>
    /// <remarks>
    /// Declared first so it is the first line of the file, where it can be read without
    /// parsing the rest. Files written before this field existed have no version and so
    /// deserialise to <see cref="CurrentVersion"/>, which is correct: their contents are the
    /// format this field was introduced alongside, and stamping them 1 on the next save loses
    /// nothing.
    /// </remarks>
    public int Version { get; set; } = CurrentVersion;

    // ---- appearance ----------------------------------------------------------

    // ---- limits --------------------------------------------------------------
    //
    // Named because three places need to agree on them: the clamps below, the sliders in the
    // settings dialog, and the window size the dock holds while those sliders are being
    // dragged.

    public const double IconSizeMin = 20;
    public const double IconSizeMax = 96;
    public const double ScaleMin = 1;
    public const double ScaleMax = 2.5;
    public const double InfluenceMax = 400;
    public const double GapMax = 40;

    public const double GapRatioMax = 0.6;

    /// <summary>Sixteen per cent, which is the eight pixels the dock has always sat at.</summary>
    public const double DefaultGapRatio = 0.16;

    public const double InfluenceIconsMin = 1;
    public const double InfluenceIconsMax = 6;

    public const double DefaultInfluenceIcons = 3;

    /// <summary>Resting icon size in DIPs.</summary>
    public double BaseSize { get; set; } = 50;

    /// <summary>Peak magnification, as a multiple of <see cref="BaseSize"/>.</summary>
    public double MaxScale { get; set; } = 1.4;

    /// <summary>
    /// How many icons either side of the pointer lift with it.
    /// </summary>
    /// <remarks>
    /// Counted in icons rather than measured in pixels, so that changing the icon size does
    /// not quietly change the wave. A range in pixels covers a different number of
    /// neighbours at every icon size — and since the falloff has to span whole icon pitches
    /// for the dock to spread without changing width, it was rounded to a count anyway. This
    /// is that count, said out loud.
    /// </remarks>
    public double? InfluenceIcons { get; set; }

    /// <summary>
    /// The old radius in pixels, kept only so a settings file written before the change is
    /// not thrown away. <see cref="Neighbours"/> converts it once.
    /// </summary>
    /// <remarks>
    /// Nullable, and with no default, precisely so that absent and zero are different
    /// things. A file written before the change always carries a number here; a settings
    /// object that has never seen one carries nothing, and takes the current default
    /// instead of being migrated from a value it never had.
    /// </remarks>
    public double? InfluenceRange { get; set; }

    /// <summary>
    /// The influence range in icons, converting a pre-existing pixel value if that is all
    /// there is.
    /// </summary>
    [JsonIgnore]
    public double Neighbours
    {
        get
        {
            if (InfluenceIcons is { } icons)
            {
                return Math.Clamp(icons, InfluenceIconsMin, InfluenceIconsMax);
            }

            if (InfluenceRange is not { } legacy)
            {
                return DefaultInfluenceIcons;
            }

            var size = Math.Clamp(BaseSize, IconSizeMin, IconSizeMax);
            var pitch = size + (size * GapFraction);
            if (pitch <= 0)
            {
                return DefaultInfluenceIcons;
            }

            // The same rounding the dock used to do to the pixel value, so a dock that has
            // been tuned comes across looking exactly as it did.
            return Math.Clamp(
                Math.Ceiling(Math.Clamp(legacy, 0, InfluenceMax) / pitch),
                InfluenceIconsMin,
                InfluenceIconsMax);
        }
    }

    /// <summary>
    /// Space between icons, as a fraction of the icon size.
    /// </summary>
    /// <remarks>
    /// Relative for the same reason the influence range is: a gap fixed in pixels is a
    /// different gap at every icon size, so the dock reads as more crowded the larger the
    /// icons get. Everything about the dock's geometry is now a multiple of one number.
    /// </remarks>
    public double? GapRatio { get; set; }

    /// <summary>
    /// The old gap in pixels, kept only so a settings file written before the change is not
    /// thrown away. Absent and zero are different things — see <see cref="InfluenceRange"/>.
    /// </summary>
    public double? Gap { get; set; }

    /// <summary>The gap as a fraction of the icon size, converting a pixel value if that is all there is.</summary>
    [JsonIgnore]
    public double GapFraction
    {
        get
        {
            if (GapRatio is { } ratio)
            {
                return Math.Clamp(ratio, 0, GapRatioMax);
            }

            if (Gap is not { } legacy)
            {
                return DefaultGapRatio;
            }

            var size = Math.Clamp(BaseSize, IconSizeMin, IconSizeMax);
            return size <= 0 ? DefaultGapRatio : Math.Clamp(legacy / size, 0, GapRatioMax);
        }
    }

    /// <summary>Opacity of the bar's fill, 0 to 1.</summary>
    public double BarOpacity { get; set; } = 0.5;

    /// <summary>
    /// The bar's fill colour as <c>#RRGGBB</c>. Alpha comes from <see cref="BarOpacity"/>,
    /// so the two settings stay independent — picking a colour must not silently reset how
    /// see-through the bar is.
    /// </summary>
    public string BarColor { get; set; } = DefaultBarColor;

    /// <summary>The stock bar colour, and the fallback for anything unparseable.</summary>
    public const string DefaultBarColor = "#CCD2FF";

    /// <summary>
    /// Whether the bar takes its colour from the taskbar instead of from
    /// <see cref="BarColor"/>.
    /// </summary>
    /// <remarks>
    /// A standing choice rather than a one-off "start from this", so the dock keeps matching
    /// when the accent changes or Windows switches between light and dark. The chosen
    /// <see cref="BarColor"/> is kept underneath it, so turning this off puts back the colour
    /// that was there rather than leaving whatever the taskbar happened to be.
    /// </remarks>
    public bool UseTaskbarColor { get; set; }

    /// <summary>
    /// Colours the user has kept, in the order they were saved.
    /// </summary>
    /// <remarks>
    /// Their own list rather than a setting with a right answer, so — like the pinned items —
    /// no reset throws it away. Stored as <c>#RRGGBB</c>, the same as everything else here.
    /// </remarks>
    public List<string> CustomColors { get; set; } = [];

    /// <summary>
    /// The most colours the dialog will keep.
    /// </summary>
    /// <remarks>
    /// Eight, matching the stock row above it, so the two read as one palette of the same
    /// shape rather than as a short row and a long one.
    /// </remarks>
    public const int MaxCustomColors = 8;

    /// <summary>
    /// How rounded the bar's ends are: 1 is a full stadium, 0 the subtle rounding a flat
    /// bar wants. Deliberately never square — see <see cref="DockMetrics.MinBarRadius"/>.
    /// </summary>
    public double BarRoundness { get; set; } = 0.76;

    /// <summary>
    /// Whether a sheet of system acrylic sits behind the bar, blurring what is under it.
    /// </summary>
    /// <remarks>
    /// It costs a second window, kept in step with the first, so it is worth being able to
    /// turn off — and on a machine whose DWM will not give us the material, the dock is
    /// better off without an empty window behind it.
    /// </remarks>
    public bool BlurBackground { get; set; } = true;

    /// <summary>
    /// The icon set the dock's items are drawn from, by its id, or null for the apps' own
    /// icons.
    /// </summary>
    /// <remarks>
    /// Per dock rather than per application, with the rest of the dock's appearance — two
    /// docks in two styles is a reasonable thing to want once there can be two. A set that is
    /// no longer installed is carried through as it was, and the dock draws the apps' own
    /// icons until it comes back, rather than the choice being forgotten on the next save.
    /// An icon chosen for one item (<see cref="PinnedAppSetting.IconPath"/>) wins over the set.
    /// </remarks>
    public string? IconSet { get; set; }

    /// <summary>
    /// Whether the dock sweeps its own wave while the settings dialog is open.
    /// </summary>
    /// <remarks>
    /// On by default because two of the tuning values — magnification and influence range —
    /// do nothing at all until the wave is up, and a dock sitting flat gives no sign that
    /// moving those sliders is doing anything.
    /// </remarks>
    public bool PreviewSweep { get; set; } = true;

    // ---- position ------------------------------------------------------------

    /// <summary>
    /// Which edge of the display the dock sits on: <c>Bottom</c>, <c>Left</c> or
    /// <c>Right</c>.
    /// </summary>
    /// <remarks>
    /// A string rather than the enum's number, so the settings file stays readable — the
    /// same reason the theme and the bar colour are stored as they are.
    /// </remarks>
    public string Edge { get; set; } = nameof(DockEdge.Bottom);

    /// <summary>The edge as the dock understands it, defaulting to the bottom.</summary>
    [JsonIgnore]
    public DockEdge Position =>
        Enum.TryParse<DockEdge>(Edge, ignoreCase: true, out var edge) ? edge : DockEdge.Bottom;

    /// <summary>
    /// Where along its edge the dock sits: 0 is the middle, -1 as far left as it goes and 1
    /// as far right.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A share of the room there is rather than a distance, so it means the same thing on
    /// every display and at every size of dock. A dock pushed to one end stays at that end as
    /// icons come and go — it grows away from it — where a distance in pixels would leave it
    /// wherever that many pixels happened to land, and off the edge of a smaller screen.
    /// </para>
    /// <para>
    /// As far as it goes is where its widest wave is <see cref="BottomMargin"/> from the side
    /// of the screen, the same gap the bar keeps from the bottom. At rest the bar stops short
    /// of that by the room the wave grows into — see <c>DockLayout.RestingLeft</c> for why the
    /// wave is given room rather than held back. Along the edge rather than left and right,
    /// so the same number will mean up and down once there is a side edge to put the dock on.
    /// </para>
    /// </remarks>
    public double OffsetAlongEdge { get; set; }

    /// <summary>
    /// <see cref="OffsetAlongEdge"/> as the dock places it: 0 against the left end, ½ centred,
    /// 1 against the right.
    /// </summary>
    [JsonIgnore]
    public double EdgeAlignment =>
        double.IsFinite(OffsetAlongEdge) ? (Math.Clamp(OffsetAlongEdge, -1, 1) + 1) / 2 : 0.5;

    /// <summary>
    /// Windows' name for the display the dock lives on, or null for the main one.
    /// </summary>
    /// <remarks>
    /// The name rather than an index: indices shuffle when a monitor is unplugged, and a
    /// dock that quietly moved to another screen because of that would be worse than one
    /// that fell back to the main display and stayed put. It turned out not to be enough on
    /// its own: Windows reassigns the names too, so <see cref="ScreenDevicePath"/> is matched
    /// first and this is what a file without one goes by.
    /// </remarks>
    public string? ScreenDeviceName { get; set; }

    /// <summary>
    /// The identity of the monitor the dock lives on — its device interface path — or null
    /// for the main display, or for a file written before it was stored.
    /// </summary>
    /// <remarks>
    /// Stored beside <see cref="ScreenDeviceName"/> rather than instead of it, so an older
    /// file keeps working by the route it always took. The name is only the name of the
    /// moment: on this machine the two displays swapped names across a wake from sleep, and
    /// a dock asking for the name it was given moved to the other monitor with nothing to
    /// say anything had gone wrong. See <c>Screens.Match</c>.
    /// </remarks>
    public string? ScreenDevicePath { get; set; }

    // ---- behaviour -----------------------------------------------------------

    /// <summary>
    /// Whether the dock floats above other windows.
    /// </summary>
    /// <remarks>
    /// On by default, which is what a dock is for. Turned off it behaves like an ordinary
    /// window and can be covered — worth having for anyone who wants it out of the way of a
    /// full-screen window without giving up the dock altogether.
    /// </remarks>
    public bool AlwaysOnTop { get; set; } = true;

    public bool AutoHide { get; set; }

    public int HideDelayMs { get; set; } = 700;

    /// <summary>
    /// How long the cursor must be held at the screen edge before the dock returns.
    /// </summary>
    /// <remarks>
    /// Long enough to be a deliberate hold. It was 120ms when the reveal zone was, in
    /// effect, the whole taskbar — you dwelled in it without meaning to, so the delay
    /// never registered as a hold at all. Against a three-pixel band it does.
    /// </remarks>
    public int RevealDelayMs { get; set; } = 300;

    /// <summary>
    /// Whether a slim handle marks where the dock is while auto-hide has it tucked away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The phone's home indicator, on a desktop: a short bar just above the taskbar, under
    /// where the dock will come up. A hidden dock otherwise leaves no trace that it is there at
    /// all, and the handle is a target as well as a mark — resting the pointer on it for the
    /// reveal delay brings the dock back, as holding it against the edge does. Only auto-hide
    /// leaves a dock for it to mark; one hidden from the tray was put away on purpose, and is
    /// left without one.
    /// </para>
    /// <para>
    /// On by default. It only ever appears once auto-hide is on, which is itself a choice, and
    /// a dock that hides without a trace is the harder one to get used to. A settings file
    /// written before this existed has no such property, so it comes up on as well.
    /// </para>
    /// </remarks>
    public bool ShowHandle { get; set; } = true;

    /// <summary>
    /// Whether the handle is as wide as the dock's bar, rather than <see cref="HandleWidth"/>.
    /// </summary>
    /// <remarks>
    /// The width of its own is kept underneath, the way <see cref="BarColor"/> is under
    /// <see cref="UseTaskbarColor"/>, so going back to it puts back the one that was chosen.
    /// </remarks>
    public bool HandleMatchesDock { get; set; } = true;

    /// <summary>The handle's width in DIPs, when it is not the dock's.</summary>
    public double HandleWidth { get; set; } = DockHandle.DefaultWidth;

    /// <summary>
    /// The programs the dock stays down for: while one of them is in front and fills the
    /// dock's display, holding the pointer against the edge neither brings a hidden dock
    /// back nor lifts a covered one.
    /// </summary>
    /// <remarks>
    /// Full paths to their executables, so the settings dialog can say which is which, but
    /// matched by file name alone — see <see cref="FullscreenApps.Contains"/>. The user's own
    /// list rather than a setting with a right answer, so, like the pinned items, no reset
    /// throws it away. Empty by default: which fullscreen programs want the edge to
    /// themselves is not something to guess at.
    /// </remarks>
    public List<string> NoRevealApps { get; set; } = [];

    /// <summary>Gap between the dock and the bottom of the work area.</summary>
    /// <remarks>
    /// Also the closest the dock comes to the side of the screen when it is moved to one end
    /// of its edge — see <see cref="OffsetAlongEdge"/> — so a dock in the corner sits the same
    /// distance from both of its walls.
    /// </remarks>
    public double BottomMargin { get; set; } = 12;

    // ---- system --------------------------------------------------------------

    /// <summary>Whether the dock starts when the user signs in. On by default.</summary>
    /// <remarks>
    /// Not what decides it: the Run key does, and the settings dialog reads its checkbox from
    /// there — see <see cref="Interop.Autostart"/>. This records what was saved, and is what
    /// the System page's reset goes back to. A default nobody wrote to the registry would be a
    /// checkbox that says one thing while Windows does another, so a first run
    /// (<c>App.OnStartup</c>) and Velopack's setup (<see cref="AppUpdater.OnInstalled"/>)
    /// put the entry there.
    /// </remarks>
    public bool RunAtLogin { get; set; } = true;

    /// <summary>
    /// The settings page that was open last, so reopening the dialog goes back to it.
    /// </summary>
    /// <remarks>
    /// Where the user was rather than something they chose, so it is kept whether the dialog
    /// was saved or cancelled — see <c>App.ShowSettings</c>. Cancelling an edit should not
    /// also throw away which page the edit was on.
    /// </remarks>
    public int SettingsPage { get; set; }

    /// <summary>
    /// Which appearance the dialogs use: <c>System</c>, <c>Light</c> or <c>Dark</c>.
    /// </summary>
    /// <remarks>
    /// Defaults to following Windows, which is what an app that has not been told otherwise
    /// should do. The dock itself is unaffected — its colours are the user's own.
    /// </remarks>
    public string Theme { get; set; } = AppTheme.System;

    /// <summary>
    /// The language the dock's own text is shown in, as a language tag (<c>ru</c>,
    /// <c>pt-BR</c>), or null to follow Windows' display language.
    /// </summary>
    /// <remarks>
    /// Per application, like the theme: it is the language of the dialogs and menus, which
    /// every dock shares. A language whose pack has gone falls back the way following Windows
    /// does, and the choice is kept for when the pack returns — see
    /// <c>Localization.LanguageLibrary.Resolve</c>.
    /// </remarks>
    public string? Language { get; set; }

    /// <summary>
    /// Forces magnification off regardless of the system animation setting. The system
    /// setting is honoured on its own; this is the explicit override.
    /// </summary>
    public bool ReduceMotion { get; set; }

    // ---- contents ------------------------------------------------------------

    /// <summary>
    /// Whether the order of the pinned items is fixed, so that nothing but an explicit edit
    /// can rearrange them.
    /// </summary>
    /// <remarks>
    /// Honoured by both of the places that can reorder — the drag along the dock itself, and
    /// the settings list's drag along with its Move up and Move down buttons — because a lock
    /// that held in one of them is a lock the other quietly undoes. Adding and removing are
    /// untouched: this is about an order that has settled down, not about a list that is
    /// closed.
    /// </remarks>
    public bool LockItemOrder { get; set; }

    /// <summary>
    /// Whether the dock is closed to changes, so that nothing can be added to it, taken off
    /// it or edited.
    /// </summary>
    /// <remarks>
    /// The companion to <see cref="LockItemOrder"/>, and deliberately a separate switch: one
    /// fixes the arrangement of a list that is still open, the other closes the list while
    /// leaving the arrangement free. Honoured by every way in which the dock's contents can
    /// change — the dock's own <em>Edit item…</em>, <em>Remove from dock</em> and
    /// <em>Add</em>, which give way to a single greyed <em>Locked</em> rather than being
    /// greyed one by one, dropping a file onto the bar, and the settings list's <em>Item
    /// settings…</em>, <em>Add</em>, <em>Remove</em> and <em>Clear all</em>, double-click
    /// included — because a lock that held in one of them is a lock the others quietly undo.
    /// <em>Dock settings…</em> is never taken away, so the lock is always one right-click
    /// from being undone.
    /// </remarks>
    public bool LockItemContents { get; set; }

    public List<PinnedAppSetting> PinnedApps { get; set; } = [];

    /// <summary>Projects the tuning values into the geometry the dock actually uses.</summary>
    [JsonIgnore]
    public DockMetrics Metrics
    {
        get
        {
            var size = Math.Clamp(BaseSize, IconSizeMin, IconSizeMax);
            var gap = size * GapFraction;

            return new DockMetrics
            {
                BaseSize = size,
                MaxSize = size * Math.Clamp(MaxScale, ScaleMin, ScaleMax),

                // Multiplied out here, from a count of icons. Everything about the wave is
                // therefore a multiple of the icon size — its height through MaxScale, its
                // width through this — so changing the icon size scales the whole effect
                // rather than reshaping it.
                InfluenceRange = Neighbours * (size + gap),
                Gap = gap,
                Roundness = Math.Clamp(BarRoundness, 0, 1)
            };
        }
    }


    public DockSettings Clone() => new()
    {
        Version = Version,
        BaseSize = BaseSize,
        MaxScale = MaxScale,
        InfluenceRange = InfluenceRange,
        InfluenceIcons = InfluenceIcons,
        Gap = Gap,
        GapRatio = GapRatio,
        BarOpacity = BarOpacity,
        BarColor = BarColor,
        UseTaskbarColor = UseTaskbarColor,
        CustomColors = [.. CustomColors],
        BarRoundness = BarRoundness,
        BlurBackground = BlurBackground,
        IconSet = IconSet,
        PreviewSweep = PreviewSweep,
        Edge = Edge,
        OffsetAlongEdge = OffsetAlongEdge,
        ScreenDeviceName = ScreenDeviceName,
        ScreenDevicePath = ScreenDevicePath,
        AlwaysOnTop = AlwaysOnTop,
        AutoHide = AutoHide,
        HideDelayMs = HideDelayMs,
        RevealDelayMs = RevealDelayMs,
        ShowHandle = ShowHandle,
        HandleMatchesDock = HandleMatchesDock,
        HandleWidth = HandleWidth,
        NoRevealApps = [.. NoRevealApps],
        BottomMargin = BottomMargin,
        RunAtLogin = RunAtLogin,
        Theme = Theme,
        Language = Language,
        SettingsPage = SettingsPage,
        ReduceMotion = ReduceMotion,
        LockItemOrder = LockItemOrder,
        LockItemContents = LockItemContents,
        PinnedApps = [.. PinnedApps.Select(app => new PinnedAppSetting
        {
            Id = app.Id,
            Label = app.Label,
            TargetPath = app.TargetPath,
            Aumid = app.Aumid,
            IconPath = app.IconPath,
            UseIconNotThumbnail = app.UseIconNotThumbnail,
            FontFamily = app.FontFamily,
            FontSize = app.FontSize,
            FontStyle = app.FontStyle,
            IsSeparator = app.IsSeparator
        })]
    };
}

/// <summary>Source-generated serialisation, so settings survive trimming.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DockSettings))]
internal partial class DockSettingsContext : JsonSerializerContext;
