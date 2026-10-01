using System.IO;
using System.Text.Json;
using ArtDock.Localization;

namespace ArtDock.Services;

/// <summary>
/// Loads and saves <see cref="DockSettings"/>, and tells the dock when they change.
/// </summary>
/// <remarks>
/// Writes go to a temporary file that then replaces the real one, so a crash or a power cut
/// mid-write leaves the previous settings intact rather than a truncated file the app would
/// refuse to start with.
/// </remarks>
public sealed class SettingsStore
{
    private static readonly string Directory =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ArtDock");

    private static readonly string FilePath = Path.Combine(Directory, "settings.json");

    /// <summary>
    /// Where settings lived before the app was renamed from ImsDock.
    /// </summary>
    /// <remarks>
    /// Kept so an existing install keeps its pinned apps and tuning across the rename instead
    /// of silently resetting to defaults. This can be deleted once no installs predate the
    /// rename.
    /// </remarks>
    private static readonly string LegacyDirectory =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImsDock");

    private DockSettings _current = new();

    /// <summary>Raised after settings are saved, so the live dock can apply them.</summary>
    public event EventHandler<DockSettings>? Changed;

    public DockSettings Current => _current;

    /// <summary>Where the settings file lives, for the "open folder" affordance.</summary>
    public static string Location => FilePath;

    /// <summary>
    /// Whether <see cref="Load"/> read settings from disk, rather than starting from defaults.
    /// </summary>
    /// <remarks>
    /// The first-run seeding of the pin list keys off this. It used to key off the list simply
    /// being empty, which stopped being safe the moment the Items page could empty it on
    /// purpose: the stock apps came back on the next launch, so clearing looked like it had
    /// not worked. A file that exists but cannot be read counts as no file — the user is
    /// getting defaults either way, and an empty bar is a poor thing to hand them.
    /// </remarks>
    public bool LoadedFromDisk { get; private set; }

    /// <summary>Reads settings from disk, falling back to defaults for anything unreadable.</summary>
    public DockSettings Load()
    {
        AdoptLegacySettings();

        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize(json, DockSettingsContext.Default.DockSettings);
                if (loaded is not null)
                {
                    // Not written back: the ids only have to be unique while the dock runs,
                    // and the next save stores them — as it stores the lettering where it now
                    // lives, which until then is read from the pins again at each start, and
                    // the format, which until then is brought up to date again at each start.
                    BringUpToDate(loaded, json);
                    loaded.RepairPinIds();
                    loaded.AdoptPinLettering();
                    _current = loaded;
                    LoadedFromDisk = true;
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file must not stop the dock from starting; the user
            // gets defaults and the next save repairs the file.
            _current = new DockSettings();
            LoadedFromDisk = false;
        }

        return _current;
    }

    /// <summary>
    /// Moves settings written under the old name into place, once, if nothing is there yet.
    /// </summary>
    private static void AdoptLegacySettings()
    {
        var legacyFile = Path.Combine(LegacyDirectory, "settings.json");
        if (File.Exists(FilePath) || !File.Exists(legacyFile))
        {
            return;
        }

        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            // Copied rather than moved: if anything goes wrong the user still has the original.
            File.Copy(legacyFile, FilePath, overwrite: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not worth failing startup over; the user gets defaults instead.
        }
    }

    /// <summary>
    /// Deletes everything ArtDock keeps: the settings folder, with the packs inside it, and the
    /// folder the settings lived in under the app's former name.
    /// </summary>
    /// <remarks>
    /// Only for the uninstaller, when the user has chosen it. The former name's folder goes too
    /// because it would otherwise come back: with no settings file here, the next install's
    /// first run adopts the one it finds there — see <see cref="AdoptLegacySettings"/>.
    /// Whatever cannot be deleted is left; an uninstall is no place to fail.
    /// </remarks>
    public static void DeleteFolder()
    {
        foreach (var folder in new[] { Directory, LegacyDirectory })
        {
            try
            {
                if (System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.Delete(folder, recursive: true);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left as it is.
            }
        }
    }

    /// <summary>Writes a copy of the settings to a file of the user's choosing.</summary>
    /// <remarks>
    /// The same serialiser as <see cref="Save"/>, so an export is a settings file rather than
    /// a format of its own and can be dropped straight into place by hand. Not written through
    /// a temporary file the way <see cref="Save"/> is: this one is not the file the app needs
    /// to start, and a half-written export the user can see and retry beats one that silently
    /// replaced something they picked.
    /// </remarks>
    public static void Export(DockSettings settings, string path)
    {
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(settings, DockSettingsContext.Default.DockSettings));
    }

    /// <summary>Reads a settings file from anywhere on disk.</summary>
    /// <remarks>
    /// Throws rather than falling back to defaults, which is the opposite of <see cref="Load"/>
    /// and deliberate: startup must not be stopped by a bad file, but an import the user just
    /// asked for must not quietly do nothing. The caller reports the message.
    /// </remarks>
    /// <exception cref="InvalidDataException">
    /// The file is not an ArtDock settings file, or is from a newer format than this build
    /// knows how to read.
    /// </exception>
    public static DockSettings Import(string path)
    {
        var json = File.ReadAllText(path);

        // Any JSON object deserialises to a full set of defaults without complaining, so a
        // file that is not this one would import as "everything reset" and look like it had
        // worked. Require it to carry at least one property this build recognises.
        using (var document = JsonDocument.Parse(json))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(Localizer.Get("Settings.Import.NotSettings"));
            }

            var known = DockSettingsContext.Default.DockSettings.Properties
                .Select(property => property.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!document.RootElement.EnumerateObject().Any(property => known.Contains(property.Name)))
            {
                throw new InvalidDataException(Localizer.Get("Settings.Import.NotSettings"));
            }
        }

        var loaded = JsonSerializer.Deserialize(json, DockSettingsContext.Default.DockSettings)
            ?? throw new InvalidDataException(Localizer.Get("Settings.Import.NotSettings"));

        if (loaded.Version > DockSettings.CurrentVersion)
        {
            throw new InvalidDataException(Localizer.Format(
                "Settings.Import.Newer", loaded.Version, DockSettings.CurrentVersion));
        }

        BringUpToDate(loaded, json);
        loaded.RepairPinIds();
        loaded.AdoptPinLettering();
        return loaded;
    }

    /// <summary>
    /// Brings settings just read from <paramref name="json"/> up to the format this build
    /// writes — see <see cref="DockSettings.Migrate"/>.
    /// </summary>
    /// <remarks>
    /// A file from before the version field has no version to read, and the property's
    /// initialiser would pass it off as the current format. Its contents are format 1, so it
    /// is stamped that first, or nothing in it would ever be migrated.
    /// </remarks>
    private static void BringUpToDate(DockSettings loaded, string json)
    {
        using (var document = JsonDocument.Parse(json))
        {
            if (!document.RootElement.TryGetProperty(nameof(DockSettings.Version), out _))
            {
                loaded.Version = 1;
            }
        }

        loaded.Migrate();
    }

    /// <summary>Persists settings and notifies listeners.</summary>
    public void Save(DockSettings settings)
    {
        _current = settings;

        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            var json = JsonSerializer.Serialize(settings, DockSettingsContext.Default.DockSettings);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);

            if (File.Exists(FilePath))
            {
                File.Replace(temp, FilePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temp, FilePath);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Settings that cannot be written are still applied for this session.
        }

        Changed?.Invoke(this, settings);
    }
}
