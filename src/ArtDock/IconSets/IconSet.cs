using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.Localization;
using ArtDock.Packs;

namespace ArtDock.IconSets;

/// <summary>
/// Everything an icon set needs to know about a pin to pick its image.
/// </summary>
/// <param name="TargetPath">What the pin opens, as stored.</param>
/// <param name="LinkTarget">Where a pinned shortcut points, when it is one.</param>
/// <param name="Aumid">A Store app's id.</param>
/// <param name="IsFolder">Whether the pin is a folder. Only asked when the set matches folders.</param>
/// <param name="RecycleBinEmpty">
/// Whether the Recycle Bin is empty, for the bin's pin — and only when the set draws it
/// differently full and empty, because the answer costs a shell query. Null otherwise.
/// </param>
public sealed record IconSubject(
    string? TargetPath,
    string? LinkTarget,
    string? Aumid,
    bool IsFolder = false,
    bool? RecycleBinEmpty = null);

/// <summary>
/// One installed icon set, checked and ready to draw from.
/// </summary>
/// <remarks>
/// <para>
/// The images are read only when they are first drawn and then kept, so a set with icons for
/// two hundred applications costs what the few pinned ones cost.
/// </para>
/// <para>
/// Nothing in a set is trusted to stay inside it. The image paths come from a file anyone can
/// write, and a set is only ever pictures: each path is resolved and must land inside the
/// set's own folder, and must be a PNG. A rule that fails either is dropped, and the rest of
/// the set is used.
/// </para>
/// </remarks>
public sealed partial class IconSet
{
    /// <summary>The newest icon-set format this build reads.</summary>
    public const int HighestFormat = 1;

    /// <summary>Decoded no larger than this, which is enough for the dock's largest icon at 150%.</summary>
    private const int MaxPixelSize = 384;

    private readonly List<Rule> _rules;
    private readonly Dictionary<string, ImageSource?> _images = new(StringComparer.OrdinalIgnoreCase);

    private IconSet(IconSetFile manifest, string folder, List<Rule> rules, long revision)
    {
        Manifest = manifest;
        Folder = folder;
        _rules = rules;
        Key = $"{Id}@{revision}";
        MatchesFolders = rules.Any(rule => rule.Match.Folder == true);
        MatchesRecycleBinState = rules.Any(rule => rule.Match.State is { Length: > 0 });
    }

    /// <summary>What the settings file stores to choose this set.</summary>
    public string Id => Manifest.Id!;

    /// <summary>What the set is called where it is picked.</summary>
    public string Name => Manifest.Name!;

    /// <summary>The folder the set was read from.</summary>
    public string Folder { get; }

    /// <summary>The set's <c>pack.json</c> as read.</summary>
    public IconSetFile Manifest { get; }

    /// <summary>
    /// Identity for caching: the id, and when the set's files last changed. An image edited in
    /// place is then a different set to a cache, rather than a stale entry under the same name.
    /// </summary>
    public string Key { get; }

    /// <summary>Whether any image is for folders, which costs asking the filesystem about each pin.</summary>
    public bool MatchesFolders { get; }

    /// <summary>Whether the set draws anything differently full and empty.</summary>
    public bool MatchesRecycleBinState { get; }

    /// <summary>How many images the set has for things that can be pinned.</summary>
    public int Count => _rules.Count;

    /// <summary>The image file for a pin, or null when the set has none for it.</summary>
    public string? FileFor(IconSubject subject)
    {
        Rule? best = null;
        foreach (var rule in _rules)
        {
            if (rule.Rank > (best?.Rank ?? 0) && Matches(rule.Match, subject))
            {
                best = rule;
            }
        }

        return best?.File;
    }

    /// <summary>The image for a pin, or null when the set has none — or it will not decode.</summary>
    public ImageSource? IconFor(IconSubject subject)
    {
        if (FileFor(subject) is not { } file)
        {
            return null;
        }

        if (!_images.TryGetValue(file, out var image))
        {
            image = Decode(file);
            _images[file] = image;
        }

        return image;
    }

    private static bool Matches(IconMatch match, IconSubject subject)
    {
        // What a shortcut runs is what a set means by "this application".
        var runs = subject.LinkTarget ?? subject.TargetPath;

        if (match.Exe is { Length: > 0 } exe
            && !string.Equals(System.IO.Path.GetFileName(runs), exe, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (match.Path is { Length: > 0 } path
            && !SamePath(Environment.ExpandEnvironmentVariables(path), subject.TargetPath)
            && !SamePath(Environment.ExpandEnvironmentVariables(path), subject.LinkTarget))
        {
            return false;
        }

        if (match.Aumid is { Length: > 0 } aumid
            && !string.Equals(aumid, subject.Aumid, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (match.Target is { Length: > 0 } target
            && !string.Equals(target, subject.TargetPath?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (match.Extension is { Length: > 0 } extension
            && !string.Equals(
                extension.StartsWith('.') ? extension : $".{extension}",
                System.IO.Path.GetExtension(runs),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (match.Folder is { } folder && folder != subject.IsFolder)
        {
            return false;
        }

        if (match.State is { Length: > 0 } state)
        {
            var wantsEmpty = string.Equals(state, "empty", StringComparison.OrdinalIgnoreCase);
            if (subject.RecycleBinEmpty is not { } empty || empty != wantsEmpty)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SamePath(string a, string? b)
    {
        if (string.IsNullOrEmpty(b))
        {
            return false;
        }

        try
        {
            return string.Equals(
                System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(a)),
                System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// How specific a match is, so the most particular image wins; 0 for one that names
    /// nothing a pin can be identified by.
    /// </summary>
    private static double RankOf(IconMatch match)
    {
        var rank =
            match.Path is { Length: > 0 } ? 5
            : match.Aumid is { Length: > 0 } || match.Target is { Length: > 0 } ? 4
            : match.Exe is { Length: > 0 } ? 3
            : match.Extension is { Length: > 0 } ? 2
            : match.Folder is not null ? 1
            : 0;

        // The bin drawn full beats the bin drawn either way, when the bin is full.
        return rank > 0 && match.State is { Length: > 0 } ? rank + 0.5 : rank;
    }

    /// <summary>
    /// Reads, but does not keep, an image — the frame is decoded into memory and the file let
    /// go, so a set can be edited or removed while the dock is showing it.
    /// </summary>
    private static ImageSource? Decode(string file)
    {
        try
        {
            int width;
            using (var stream = File.OpenRead(file))
            {
                width = BitmapDecoder.Create(
                    stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0].PixelWidth;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.UriSource = new Uri(file);

            // Down, never up: asked for a width, the decoder scales to it either way.
            if (width > MaxPixelSize)
            {
                bitmap.DecodePixelWidth = MaxPixelSize;
            }

            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
                                      or ArgumentException or UriFormatException or FileFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a set from its folder.
    /// </summary>
    /// <returns>
    /// The set, or null with the reason it cannot be used; and, for a set that can be used,
    /// what was wrong with the rules that were dropped from it.
    /// </returns>
    public static (IconSet? Set, string? Problem, IReadOnlyList<string> Skipped) Load(string folder)
    {
        var (manifest, problem) = PackManifest.Read(folder, PackJsonContext.Default.IconSetFile);
        problem ??= manifest!.Check(PackKind.IconSet, HighestFormat, PackManifest.AppVersion);

        if (problem is null && !IdPattern().IsMatch(manifest!.Id!))
        {
            problem = Localizer.Format("Packs.Problem.BadId", manifest.Id);
        }

        if (problem is not null)
        {
            return (null, problem, []);
        }

        var root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder))
            + System.IO.Path.DirectorySeparatorChar;

        var rules = new List<Rule>();
        var skipped = new List<string>();
        var revision = File.GetLastWriteTimeUtc(System.IO.Path.Combine(folder, PackLocations.ManifestFileName)).Ticks;

        foreach (var rule in manifest!.Icons ?? [])
        {
            if (Refuse(rule, root) is { } reason)
            {
                skipped.Add(reason);
                continue;
            }

            var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, rule.File!));
            revision = Math.Max(revision, File.GetLastWriteTimeUtc(file).Ticks);
            rules.Add(new Rule(rule.Match!, file, RankOf(rule.Match!) - (rules.Count * 1e-6)));
        }

        if (rules.Count == 0)
        {
            return (null, Localizer.Format("Packs.Problem.NoIcons", skipped.FirstOrDefault() ?? "-"), []);
        }

        return (new IconSet(manifest, folder, rules, revision), null, skipped);
    }

    /// <summary>Why a rule cannot be used, or null when it can.</summary>
    private static string? Refuse(IconRule rule, string root)
    {
        if (rule.Match is not { } match || RankOf(match) <= 0)
        {
            return Localizer.Get("Packs.Problem.IconNoMatch");
        }

        if (rule.File is not { Length: > 0 } name)
        {
            return Localizer.Format("Packs.Problem.IconMissing", string.Empty);
        }

        string full;
        try
        {
            full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, name));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Localizer.Format("Packs.Problem.IconOutside", name);
        }

        // Resolved, then held to the folder: a rooted path, a drive, "..", and a UNC path all
        // end up somewhere else, and all are caught by the one question.
        if (System.IO.Path.IsPathRooted(name) || !full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return Localizer.Format("Packs.Problem.IconOutside", name);
        }

        if (!string.Equals(System.IO.Path.GetExtension(full), ".png", StringComparison.OrdinalIgnoreCase))
        {
            return Localizer.Format("Packs.Problem.IconNotPng", name);
        }

        return File.Exists(full) ? null : Localizer.Format("Packs.Problem.IconMissing", name);
    }

    /// <summary>
    /// What a set's id may be: short, and safe to write into a settings file, a folder name
    /// and a cache key without escaping anything.
    /// </summary>
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex IdPattern();

    /// <summary>A usable rule: what it matches, the checked full path, and how specific it is.</summary>
    /// <remarks>
    /// The rank carries a vanishing penalty for position, so that among equally specific
    /// rules the one listed first wins without a second comparison.
    /// </remarks>
    private sealed record Rule(IconMatch Match, string File, double Rank);
}
