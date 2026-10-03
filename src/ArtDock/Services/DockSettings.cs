using System.Text.Json.Serialization;
using ArtDock.Dock;

namespace ArtDock.Services;

/// <summary>One pinned application, as stored on disk.</summary>
public sealed class PinnedAppSetting
{
    /// <summary>What tells this pin from every other, for as long as it is on the dock.</summary>
    /// <remarks>
    /// Neither of the obvious stand-ins will do. The position changes with every reorder, which
    /// is exactly when the dock's icons have to keep their elements — and their slide — across
    /// a rebuild, and the settings dialog has to put its rows in the dock's new order without
    /// losing an unsaved rename. The target is not there for a separator or a Store app, and
    /// the edit dialog can set one pin's to another's. So the menu's <em>Remove</em> and
    /// <em>Edit</em> find their pin by this, and it must be unique: see
    /// <see cref="DockSettings.RepairPinIds"/>.
    /// </remarks>
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

    /// <summary>
    /// For a folder: its colour, as <c>#RRGGBB</c>, which has the dock draw it in place of the
    /// shell's folder icon. Null keeps the shell's.
    /// </summary>
    /// <remarks>
    /// The folder is drawn from this, <see cref="FolderSymbol"/> or <see cref="FolderText"/>, and
    /// <see cref="FolderSymbolTone"/> every time the pins are read — see <c>FolderArt</c> — and
    /// never saved as a picture, so these values are the whole of it: a reinstall or an import
    /// loses nothing. An image chosen in <see cref="IconPath"/> still wins over it.
    /// </remarks>
    public string? FolderColor { get; set; }

    /// <summary>
    /// The symbol pressed into such a folder: a glyph of the system's symbol font, as its code
    /// point in hex — <c>E896</c> is Downloads. Null for none; means nothing while
    /// <see cref="FolderColor"/> is null.
    /// </summary>
    public string? FolderSymbol { get; set; }

    /// <summary>
    /// Up to six characters on such a folder in place of a symbol. Its own property rather than
    /// a form of <see cref="FolderSymbol"/>, so that text reading <c>E896</c> can never be taken
    /// for the glyph. Null for none.
    /// </summary>
    public string? FolderText { get; set; }

    /// <summary>
    /// What the symbol or text is painted in: <c>White</c>, <c>Black</c>, or null for a deeper
    /// shade of the folder's own colour.
    /// </summary>
    public string? FolderSymbolTone { get; set; }

    /// <summary>
    /// Start the program as administrator when the pin is clicked, as Windows' own
    /// <em>Run this program as an administrator</em> does — Windows asks every time.
    /// </summary>
    /// <remarks>
    /// Off by default, and means nothing for a pin that cannot be started that way: see
    /// <c>DockItem.CanRunAsAdministrator</c>. Only a launch is elevated — a click on a pin whose
    /// program is already open brings its window forward, as for any other.
    /// </remarks>
    public bool RunAsAdministrator { get; set; }

    /// <summary>
    /// The label's typeface, as files written before the lettering was the dock's carry it.
    /// </summary>
    /// <remarks>
    /// Read and never written. Every pin held the same lettering by then — the item editor
    /// wrote its choice to all of them — so <see cref="DockSettings.AdoptPinLettering"/> takes
    /// it from the first pin that has one into <see cref="DockSettings.LabelFontFamily"/> and
    /// its two companions as the file is read, and empties these. Nothing else reads them.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FontFamily { get; set; }

    /// <summary>The label's size, read from older files as <see cref="FontFamily"/> is.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? FontSize { get; set; }

    /// <summary>The label's emphasis, read from older files as <see cref="FontFamily"/> is.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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
    /// value", which is not recoverable after the fact. <see cref="Migrate"/> is where a file
    /// from an older format is brought up to this one.
    /// <para>
    /// 2, since 2026-10-01: the bar's stock colour and opacity. A file of format 1 that holds
    /// <c>#EEF1FF</c> at 0.76 holds the stock values of its day, which the dock never painted.
    /// </para>
    /// <para>
    /// A build older than a format still reads its files — nothing was renamed or removed —
    /// so going back a version by hand costs nothing; see docs/downloads.md.
    /// </para>
    /// </remarks>
    public const int CurrentVersion = 2;

    /// <summary>The format this file was written against.</summary>
    /// <remarks>
    /// Declared first so it is the first line of the file, where it can be read without
    /// parsing the rest. A file written before this field existed has no version, and would
    /// read as <see cref="CurrentVersion"/> by this initialiser; its contents are format 1,
    /// the one the field was introduced alongside, so <see cref="SettingsStore"/> stamps it 1
    /// as it is read, before <see cref="Migrate"/> sees it.
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
    public double BarOpacity { get; set; } = BarPalette.DefaultOpacity;

    /// <summary>
    /// The bar's fill colour as <c>#RRGGBB</c>. Alpha comes from <see cref="BarOpacity"/>,
    /// so the two settings stay independent — picking a colour must not silently reset how
    /// see-through the bar is.
    /// </summary>
    public string BarColor { get; set; } = DefaultBarColor;

    /// <summary>The stock bar colour, and the fallback for anything unparseable.</summary>
    /// <remarks>The same colour as <see cref="BarPalette.Default"/>, which a test holds it to.</remarks>
    public const string DefaultBarColor = "#CCD2FF";

    /// <summary>The stock bar colour of format 1, which no dock on it ever painted.</summary>
    private const string FormerBarColor = "#EEF1FF";

    /// <summary>The stock opacity of format 1, as <see cref="FormerBarColor"/>.</summary>
    private const double FormerBarOpacity = 0.76;

    /// <summary>
    /// Brings settings read from a file of an older format up to
    /// <see cref="CurrentVersion"/>. Returns true when the file was older.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1 to 2, the bar's stock look.</b> Format 1's stock bar was <c>#EEF1FF</c> at 0.76,
    /// and a dock started on it painted <c>#CCD2FF</c> at a half: its first fill was written
    /// out beside the stock values rather than made from them, and setting the bar to what it
    /// was already recorded as holding changed nothing. So the stock look everyone had, at
    /// every start, was the one the settings did not describe — until the colour or the
    /// opacity was changed in the settings dialog, when the bar turned to what they said, a
    /// good deal lighter, and stayed there until the dock was next started.
    /// </para>
    /// <para>
    /// The fill is now made from the settings, and the stock values are the ones that were on
    /// the screen. A file still holding the old pair is moved to the new one, or its dock would
    /// turn lighter with the update for no reason its owner could find. A file from this format
    /// holding the old pair is somebody's choice, made with the dock showing it truly, and is
    /// left — which is what the version is for. Not while the bar takes the taskbar's colour:
    /// that never matched the stock one, so it was always painted as asked, at the opacity
    /// stored.
    /// </para>
    /// </remarks>
    public bool Migrate()
    {
        if (Version >= CurrentVersion)
        {
            return false;
        }

        if (Version < 2
            && !UseTaskbarColor
            && BarPalette.Parse(BarColor) == BarPalette.Parse(FormerBarColor)
            && Math.Abs(BarOpacity - FormerBarOpacity) < 0.0005)
        {
            BarColor = DefaultBarColor;
            BarOpacity = BarPalette.DefaultOpacity;
        }

        Version = CurrentVersion;
        return true;
    }

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
    public double BarRoundness { get; set; } = 0.40;

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
    /// Whether each icon casts a faint shadow onto the bar, falling a little below it.
    /// </summary>
    /// <remarks>
    /// On unless turned off, chosen once it had been seen on the dock. A settings file from
    /// before the shadow existed has no such property and so reads as on: a dock already set up
    /// gains the shadow with the update that brings it.
    /// </remarks>
    public bool IconShadows { get; set; } = true;

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
    /// The typeface of the label over a hovered icon, or null for the dock's own,
    /// <c>Segoe UI</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dock's rather than each item's, with <see cref="LabelFontSize"/> and
    /// <see cref="LabelFontStyle"/>: one dock, one lettering. Until 2026-10-01 these were
    /// stored on every pin and chosen in the item editor, which wrote its choice to all of them
    /// — so a dock with no pins had nowhere to keep it, and a pin added from the dock itself
    /// came in with the default lettering beside the others. A file from then is read across
    /// by <see cref="AdoptPinLettering"/>.
    /// </para>
    /// <para>
    /// Null rather than the name written in, so a typeface nobody chose keeps following the
    /// dock's own if that ever changes. A typeface that is not installed falls back the way
    /// WPF falls back for any missing font.
    /// </para>
    /// </remarks>
    public string? LabelFontFamily { get; set; }

    /// <summary>The labels' size in points, or null for the dock's own, 14.</summary>
    public double? LabelFontSize { get; set; }

    /// <summary>
    /// The labels' emphasis — <c>Regular</c>, <c>Bold</c>, <c>Italic</c> or <c>BoldItalic</c>
    /// — or null for the dock's own, which is bold.
    /// </summary>
    public string? LabelFontStyle { get; set; }

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
    /// <para>
    /// On by default, which is what a dock is for. Turned off it behaves like an ordinary
    /// window and can be covered — worth having for anyone who wants it out of the way of
    /// every window, not only a maximized or fullscreen one, without giving up the dock
    /// altogether.
    /// </para>
    /// <para>
    /// Not over a window that fills the dock's display, either way: while a window in front is
    /// maximized there or fullscreen — a game, a video, a browser maximized with the taskbar
    /// showing — the dock hides as <see cref="AutoHide"/> would, and comes back up over it
    /// only when the pointer reaches the bottom edge; and not at all over one of
    /// <see cref="NoRevealApps"/>.
    /// </para>
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
    /// Whether resting the pointer on a running app's icon shows its windows, live, above it —
    /// the taskbar's previews, on the dock.
    /// </summary>
    /// <remarks>
    /// On by default, for a dock already set up as much as for a new one: a settings file from
    /// before the previews has no such property and reads as on, decided on 2026-10-02. They
    /// open after <see cref="PreviewDelayMs"/> and close after <see cref="HideDelayMs"/> with the
    /// pointer elsewhere.
    /// </remarks>
    public bool WindowPreviews { get; set; } = true;

    /// <summary>
    /// How long the pointer rests on a running app's icon before its window previews open.
    /// </summary>
    /// <remarks>
    /// Its own setting since 2026-10-03, asked for once the previews had been seen: they first
    /// waited as long as the taskbar's (<c>ExtendedUIHoverTime</c>, 400 ms unless set), and felt
    /// slower than the taskbar's all the same — the icons grow and shift under a pointer arriving
    /// on the dock, and each icon it lands on starts the wait again. Shorter by default for that.
    /// </remarks>
    public int PreviewDelayMs { get; set; } = 250;

    /// <summary>
    /// Whether a slim handle marks where the dock is while it cannot be seen — slid away by
    /// auto-hide, or under the windows in front.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The phone's home indicator, on a desktop: a short bar just above the taskbar, under
    /// where the dock will come up. A dock out of sight otherwise leaves no trace that it is
    /// there at all. A mark and nothing more: resting the pointer on it brought the dock up
    /// until 2026-09-30, and was taken away on request, so the edge is the one way up. It
    /// floats over everything but the programs of <see cref="NoRevealApps"/>, which the dock
    /// does not come up over. One hidden from the tray was put away on purpose, and is left
    /// without one.
    /// </para>
    /// <para>
    /// On by default. Until 2026-09-30 it appeared only with auto-hide on, and the name is from
    /// then. A settings file written before this existed has no such property, so it comes up
    /// on as well.
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
    /// dock's display, the dock is under it, holding the pointer against the edge neither
    /// brings a hidden dock back nor lifts a covered one, and no handle is shown over it. While
    /// one is in front at all, whatever its size and whichever display it is on, the dock lets
    /// go of its hotkeys, so their keys reach the program.
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

    // ---- keyboard ------------------------------------------------------------

    /// <summary>
    /// The hotkeys, by what they do — <see cref="HotkeyAction"/>'s names — as a settings file
    /// writes them: <c>"Keyboard": "Win+Ctrl+A"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Absent and empty are different things, as they are for <see cref="InfluenceIcons"/>: an
    /// action that is not here has its default (<see cref="HotkeyActions.Default"/>), and one
    /// stored as <c>""</c> has been given none. Reading empty as absent would put a hotkey the
    /// user cleared back at every start. So the settings dialog writes nothing for an action left
    /// at its default, which goes on following the default if that ever changes — as the places'
    /// did on 2026-10-02, from none to the numeric keypad — and a dock set up before hotkeys
    /// existed, or before an action did, has its default from the update that brings it.
    /// </para>
    /// <para>
    /// Each key is stored by its virtual key, not by what it types — see <see cref="Hotkey"/> — so
    /// it is the same physical key on every keyboard layout. A name this build does not know is
    /// a later version's action, and is carried through as it was.
    /// </para>
    /// </remarks>
    public Dictionary<string, string> Hotkeys
    {
        get;
        set => field = value ?? [];
    } = [];

    /// <summary>The hotkey an action has, as these settings have it — see <see cref="Hotkeys"/>.</summary>
    public Hotkey? HotkeyFor(HotkeyAction action) => HotkeyActions.Resolve(Hotkeys, action);

    /// <summary>The hotkey each action has, as these settings have it.</summary>
    public Dictionary<HotkeyAction, Hotkey?> HotkeyChoices() =>
        HotkeyActions.All.ToDictionary(action => action, HotkeyFor);

    /// <summary>
    /// Whether the items' hotkeys — the places', first keys and second — are registered: the
    /// Hotkeys page's <em>Enable quick launch</em>. On by default. Off, the dock lets go of them,
    /// and they are free for other programs; they are kept as they were set, for when it is on
    /// again. The keyboard's own 1 to 9, once the dock has the keys, are not hotkeys, and work
    /// either way.
    /// </summary>
    public bool QuickLaunch { get; set; } = true;

    /// <summary>
    /// The items whose hotkeys are off by themselves, by place, 1 to 9: the checkbox on each
    /// item's row of the Hotkeys page, all of them ticked by default. An item that is here has
    /// its keys, first and second, let go of as <see cref="QuickLaunch"/> off lets go of every
    /// item's, and kept as they were set.
    /// </summary>
    /// <remarks>
    /// The places that are off rather than the ones that are on, so an empty list — and a file
    /// from before 2026-10-02, which has none — is every item on. By place, as the keys are: a
    /// place's checkbox goes with its keys, not with whichever item is in the place.
    /// </remarks>
    public List<int> QuickLaunchOff
    {
        get;
        set => field = value ?? [];
    } = [];

    /// <summary>
    /// Whether holding the Windows key and Ctrl puts each item's number on its icon — the place
    /// its keys open, while quick launch has keys: the Hotkeys page's <em>Show item numbers on
    /// Win+Ctrl</em>, on by default. Asked for on 2026-10-02, as a checkbox of its own, the same
    /// evening as the numbers; <see cref="RevealOnWinCtrl"/> is the dock coming up for them.
    /// </summary>
    public bool NumbersOnWinCtrl { get; set; } = true;

    /// <summary>
    /// Whether holding the Windows key and Ctrl brings the dock up until they are let go — out of
    /// auto-hide, from behind a window that fills the display, or put away from the tray: the
    /// Hotkeys page's <em>Reveal the dock on Win+Ctrl</em>, on by default. The items' numbers come
    /// up with it (<see cref="NumbersOnWinCtrl"/>); off, they still come up on a dock already in
    /// sight.
    /// </summary>
    /// <remarks>
    /// Asked for on 2026-10-02, with the numbers. Never while a program on the Exclusions page is
    /// in front, whose keys are its own (<see cref="NoRevealApps"/>).
    /// </remarks>
    public bool RevealOnWinCtrl { get; set; } = true;

    /// <summary>The hotkeys to register, as these settings have them.</summary>
    public Dictionary<HotkeyAction, Hotkey> HotkeysInForce() =>
        HotkeyActions.InForce(HotkeyActions.InUse(HotkeyChoices(), QuickLaunch, QuickLaunchOff));

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

    /// <summary>
    /// Whether the dock is drawn without a graphics card: for a virtual machine, a remote
    /// session, a server — anywhere Windows has only the processor to draw with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The user's to turn on, and off unless they do. The dock decides it once only, on a first
    /// run where WPF reports nothing to draw with (<see cref="AdoptNoGpu"/>), and says so; after
    /// that the settings dialog only says when that is so and the box is not ticked. On, the dock is
    /// drawn in software, with no Direct3D device at all (<see cref="SoftwareRendering"/>), and
    /// gives up what leans on the compositor: the blur behind the bar (<see cref="Blurs"/>),
    /// the bar's translucency (<see cref="BarAlpha"/>), the handle's inversion
    /// (<see cref="HandleInverts"/>) and the dialogs' Mica. And it draws less: neither the
    /// bar nor the icons cast a shadow (<see cref="CastsBarShadow"/>,
    /// <see cref="CastsIconShadows"/>). The wave itself stays, at the display's own pace —
    /// <see cref="ReduceMotion"/> is beside it for a machine that cannot keep up even so.
    /// </para>
    /// <para>
    /// What it overrides is kept underneath, the way <see cref="BarColor"/> is under
    /// <see cref="UseTaskbarColor"/>: nothing here rewrites <see cref="BlurBackground"/>,
    /// <see cref="BarOpacity"/> or <see cref="IconShadows"/>, so turning it off puts back the
    /// dock that was there. Per application, like the theme, since the render mode is the
    /// process's.
    /// </para>
    /// </remarks>
    public bool NoGpu { get; set; }

    /// <summary>
    /// Turns <see cref="NoGpu"/> on for a dock starting for the first time on a machine with no
    /// hardware to draw with. Returns true when it did, which is the caller's cue to say so.
    /// </summary>
    /// <remarks>
    /// A first run only. A dock with a settings file has an owner, who has had the checkbox and
    /// the line under it that says when this machine looks like one it is for; turning it on
    /// under them — after a remote session, say, where the tier reads 0 for a while — would
    /// change their dock for a reason they could not see.
    /// </remarks>
    public bool AdoptNoGpu(bool firstRun, bool hardwareMissing)
    {
        if (!firstRun || !hardwareMissing || NoGpu)
        {
            return false;
        }

        NoGpu = true;
        return true;
    }

    /// <summary>Whether the acrylic sheet is up: asked for, and not ruled out by <see cref="NoGpu"/>.</summary>
    [JsonIgnore]
    public bool Blurs => BlurBackground && !NoGpu;

    /// <summary>
    /// The opacity the bar is painted at: <see cref="BarOpacity"/>, or solid under
    /// <see cref="NoGpu"/> — where the opacity goes into the colour instead, see
    /// <see cref="BarPaint"/>.
    /// </summary>
    [JsonIgnore]
    public double BarAlpha => NoGpu ? 1 : BarOpacity;

    /// <summary>
    /// The colour the bar is painted, given the colour in force — <see cref="BarColor"/>, or
    /// the taskbar's: that colour, or under <see cref="NoGpu"/> what it looks like at
    /// <see cref="BarOpacity"/> over grey (<see cref="BarPalette.Underlay"/>), as
    /// <c>#RRGGBB</c>.
    /// </summary>
    /// <remarks>
    /// So a solid bar keeps the look it was chosen for, and the opacity slider still does
    /// something: it sets how much of the grey comes through, where it used to set how much of
    /// the desktop did. Painted in the stored colour alone, a bar set up translucent was
    /// lighter solid than it had ever been seen.
    /// </remarks>
    public string BarPaint(string color) => NoGpu
        ? BarPalette.ToHex(BarPalette.Over(BarPalette.Parse(color), BarOpacity, BarPalette.Underlay))
        : color;

    /// <summary>
    /// Whether the handle shows what is behind it inverted, which takes reading the screen
    /// under it, or takes the dock's colour — as it does under <see cref="NoGpu"/>.
    /// </summary>
    [JsonIgnore]
    public bool HandleInverts => !NoGpu;

    /// <summary>
    /// Whether the icons cast their shadows: asked for in <see cref="IconShadows"/>, and not
    /// ruled out by <see cref="NoGpu"/>, under which each would be one more picture to scale
    /// for every icon on every frame of the wave.
    /// </summary>
    [JsonIgnore]
    public bool CastsIconShadows => IconShadows && !NoGpu;

    /// <summary>
    /// Whether the bar casts its shadow: always, but under <see cref="NoGpu"/>.
    /// </summary>
    /// <remarks>
    /// The shadow is a stack of see-through rounded rectangles under a clip, drawn again every
    /// frame the bar changes width, which is every frame of the wave. On a graphics card that
    /// is about a tenth of what the wave costs. In software it is most of it: measured on
    /// 2026-10-01 with the dock as No GPU has it, the sweep cost about 85% of one core with the
    /// shadow and 14% without, at a display's 120 frames a second — all of the difference on
    /// WPF's render thread. Which is also why the wave is not slowed under No GPU: drawn at
    /// thirty frames a second, as it was for an evening, it came to 11%, three points for two
    /// frames in three.
    /// </remarks>
    [JsonIgnore]
    public bool CastsBarShadow => !NoGpu;

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

    /// <summary>
    /// Gives a new id to every pin whose id is blank or repeats an earlier pin's. Returns true
    /// when it changed any.
    /// </summary>
    /// <remarks>
    /// Everything the dock creates gets an id of its own, so this is for a file written
    /// somewhere else: by hand, or by an older build. A pin with no <c>Id</c> in the file reads
    /// as an empty one, and pins that share an id are one pin to everything that finds them by
    /// it — <em>Remove from dock</em> would take every one of them off, and <em>Edit</em>
    /// change the first. The first of a repeated id keeps it.
    /// </remarks>
    public bool RepairPinIds()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var changed = false;
        foreach (var pin in PinnedApps)
        {
            if (string.IsNullOrWhiteSpace(pin.Id) || !seen.Add(pin.Id))
            {
                pin.Id = Guid.NewGuid().ToString("N");
                seen.Add(pin.Id);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Takes the labels' lettering from the pins of a file written before it was the dock's,
    /// and empties it from every pin. Returns true when it changed anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// From the first pin that carries any of the three, and all three from that one, since
    /// the item editor wrote them together. Not simply the first pin: one added from the dock
    /// after the lettering was chosen came in without any, and taking it would have put the
    /// labels back to the default.
    /// </para>
    /// <para>
    /// Only into a file that has no lettering of its own. One that has both was written by
    /// hand, and what it says at the dock's level is the newer word.
    /// </para>
    /// </remarks>
    public bool AdoptPinLettering()
    {
        var source = PinnedApps.Find(pin =>
            pin.FontFamily is not null || pin.FontSize is not null || pin.FontStyle is not null);

        if (source is null)
        {
            return false;
        }

        if (LabelFontFamily is null && LabelFontSize is null && LabelFontStyle is null)
        {
            LabelFontFamily = string.IsNullOrWhiteSpace(source.FontFamily) ? null : source.FontFamily;
            LabelFontSize = source.FontSize;
            LabelFontStyle = string.IsNullOrWhiteSpace(source.FontStyle) ? null : source.FontStyle;
        }

        foreach (var pin in PinnedApps)
        {
            pin.FontFamily = null;
            pin.FontSize = null;
            pin.FontStyle = null;
        }

        return true;
    }

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
        IconShadows = IconShadows,
        IconSet = IconSet,
        LabelFontFamily = LabelFontFamily,
        LabelFontSize = LabelFontSize,
        LabelFontStyle = LabelFontStyle,
        PreviewSweep = PreviewSweep,
        Edge = Edge,
        OffsetAlongEdge = OffsetAlongEdge,
        ScreenDeviceName = ScreenDeviceName,
        ScreenDevicePath = ScreenDevicePath,
        AlwaysOnTop = AlwaysOnTop,
        AutoHide = AutoHide,
        HideDelayMs = HideDelayMs,
        RevealDelayMs = RevealDelayMs,
        WindowPreviews = WindowPreviews,
        PreviewDelayMs = PreviewDelayMs,
        ShowHandle = ShowHandle,
        HandleMatchesDock = HandleMatchesDock,
        HandleWidth = HandleWidth,
        NoRevealApps = [.. NoRevealApps],
        BottomMargin = BottomMargin,
        Hotkeys = new Dictionary<string, string>(Hotkeys, StringComparer.Ordinal),
        QuickLaunch = QuickLaunch,
        QuickLaunchOff = [.. QuickLaunchOff],
        NumbersOnWinCtrl = NumbersOnWinCtrl,
        RevealOnWinCtrl = RevealOnWinCtrl,
        RunAtLogin = RunAtLogin,
        Theme = Theme,
        Language = Language,
        SettingsPage = SettingsPage,
        ReduceMotion = ReduceMotion,
        NoGpu = NoGpu,
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
            FolderColor = app.FolderColor,
            FolderSymbol = app.FolderSymbol,
            FolderText = app.FolderText,
            FolderSymbolTone = app.FolderSymbolTone,
            RunAsAdministrator = app.RunAsAdministrator,

            // Not the pin's lettering, which is only ever read from an older file and is moved
            // to the dock's as it is read — see AdoptPinLettering.
            IsSeparator = app.IsSeparator
        })]
    };
}

/// <summary>Source-generated serialisation, so settings survive trimming.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DockSettings))]
internal partial class DockSettingsContext : JsonSerializerContext;
