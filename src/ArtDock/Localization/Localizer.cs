using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using ArtDock.Packs;

namespace ArtDock.Localization;

/// <summary>
/// The language the dock is being shown in, for the whole application.
/// </summary>
/// <remarks>
/// <para>
/// Two ways in, one table behind both. Code asks <see cref="Get"/> and <see cref="Format"/>.
/// XAML names a key as a <c>DynamicResource</c> — <c>Text="{DynamicResource Settings.Size.Title}"</c>
/// — and is answered from a <see cref="ResourceDictionary"/> this class keeps merged into the
/// application's resources. Being dynamic is what lets a language change reach windows that
/// are already open, the settings dialog in particular, whose language picker previews its
/// choice like every other control on it.
/// </para>
/// <para>
/// Strings built in code are not updated by the dictionary, so anything long-lived that
/// holds one — the tray menu, the settings dialog's hints — listens to
/// <see cref="LanguageChanged"/> and says it again.
/// </para>
/// <para>
/// Three resources carry the language itself rather than a string, under keys a window can
/// name: <see cref="FlowDirectionKey"/>, <see cref="LanguageTagKey"/> for font fallback and
/// hyphenation, and <see cref="CultureKey"/>, which <see cref="Localized"/> watches to know
/// when to reformat a number.
/// </para>
/// </remarks>
public static class Localizer
{
    /// <summary>Resource key of the current <see cref="CultureInfo"/>.</summary>
    public const string CultureKey = "Language.Culture";

    /// <summary>Resource key of the current <see cref="System.Windows.FlowDirection"/>.</summary>
    public const string FlowDirectionKey = "Language.FlowDirection";

    /// <summary>Resource key of the current <see cref="XmlLanguage"/>, for <c>FrameworkElement.Language</c>.</summary>
    public const string LanguageTagKey = "Language.Tag";

    /// <summary>
    /// Windows' display language as the process started with it.
    /// </summary>
    /// <remarks>
    /// Read once. Nothing here changes the thread's culture, but a later reader might, and
    /// "follow Windows" must not come to mean "follow whatever this process last set".
    /// </remarks>
    private static readonly CultureInfo WindowsLanguage = CultureInfo.CurrentUICulture;

    private static StringTable? _current;
    private static LanguageLibrary? _library;
    private static ResourceDictionary? _resources;

    /// <summary>The strings in force. English until <see cref="Apply"/> says otherwise.</summary>
    public static StringTable Current => _current ?? LanguageLibrary.English;

    /// <summary>The culture numbers are formatted in.</summary>
    public static CultureInfo Culture => Current.Culture;

    /// <summary>Which way the current language reads.</summary>
    public static FlowDirection FlowDirection =>
        Current.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Raised on the UI thread after the language has changed.</summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>A string in the current language.</summary>
    public static string Get(string key) => Current.Get(key);

    /// <summary>A string in the current language with arguments put into it.</summary>
    /// <remarks>See <see cref="StringTable.Format"/> for how plurals are chosen.</remarks>
    public static string Format(string key, params object?[] args) => Current.Format(key, args);

    /// <summary>
    /// The languages there are, as last read. <see cref="Rescan"/> reads them again.
    /// </summary>
    public static LanguageLibrary Library => _library ??= new LanguageLibrary(PackLocations.FolderFor(PackKind.Language));

    /// <summary>Reads the installed language packs again, for a picker about to be shown.</summary>
    public static LanguageLibrary Rescan() => _library = new LanguageLibrary(PackLocations.FolderFor(PackKind.Language));

    /// <summary>What "follow Windows" comes to, as a language there is.</summary>
    public static LanguageInfo? WindowsChoice => Library.Find(Library.Resolve(null, WindowsLanguage));

    /// <summary>
    /// Shows the dock in a language.
    /// </summary>
    /// <param name="chosen">The stored choice: a language tag, or null to follow Windows.</param>
    /// <remarks>
    /// Cheap when nothing changes, which matters: the settings dialog previews on every tick
    /// of every slider, and this is on that path. Swapping the dictionary invalidates every
    /// <c>DynamicResource</c> in every window, which is fine once per change of language and
    /// would not be sixty times a second.
    /// </remarks>
    public static void Apply(string? chosen)
    {
        var id = Library.Resolve(chosen, WindowsLanguage);
        if (_current is { } current && current.Id == id && _resources is not null)
        {
            EnsureMerged();
            return;
        }

        _current = Library.Load(id);
        Publish();
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Puts the current table where XAML can see it.</summary>
    /// <remarks>
    /// The dictionary is replaced as a whole rather than edited in place. Setting an entry
    /// in a dictionary the application is using raises a resource change per entry, and
    /// each of those walks every window; one replacement raises one.
    /// </remarks>
    private static void Publish()
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        var table = Current;
        var dictionary = new ResourceDictionary
        {
            [CultureKey] = table.Culture,
            [FlowDirectionKey] = FlowDirection,
            [LanguageTagKey] = XmlLanguage.GetLanguage(table.Culture.IetfLanguageTag)
        };

        foreach (var key in table.Keys)
        {
            dictionary[key] = table.Get(key);
        }

        var merged = application.Resources.MergedDictionaries;
        var index = _resources is null ? -1 : merged.IndexOf(_resources);
        if (index >= 0)
        {
            merged[index] = dictionary;
        }
        else
        {
            merged.Add(dictionary);
        }

        _resources = dictionary;
    }

    /// <summary>
    /// Puts the strings back if anything has taken them out of the application's resources.
    /// </summary>
    /// <remarks>
    /// WPF's own theme switching edits the same merged dictionaries. It is only known to
    /// replace its own entry, but a dialog full of keys where its words should be is too
    /// visible a failure to rest on that.
    /// </remarks>
    private static void EnsureMerged()
    {
        if (Application.Current is { } application
            && _resources is { } resources
            && !application.Resources.MergedDictionaries.Contains(resources))
        {
            application.Resources.MergedDictionaries.Add(resources);
        }
    }
}
