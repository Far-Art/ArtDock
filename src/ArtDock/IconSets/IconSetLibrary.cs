using System.Globalization;
using System.IO;
using ArtDock.Packs;

namespace ArtDock.IconSets;

/// <summary>
/// The icon sets that are installed.
/// </summary>
/// <remarks>
/// A snapshot, like <see cref="Localization.LanguageLibrary"/>: the settings dialog takes a
/// new one when its picker opens, so a set copied into place appears without a restart. The
/// dock keeps using the sets it was given until the next change of contents asks again.
/// </remarks>
public sealed class IconSetLibrary
{
    private readonly List<IconSet> _sets = [];
    private readonly List<PackProblem> _problems = [];

    /// <summary>Reads every set in a folder. A folder that does not exist is simply no sets.</summary>
    public IconSetLibrary(string folder)
    {
        string[] packs;
        try
        {
            packs = Directory.Exists(folder) ? Directory.GetDirectories(folder) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            packs = [];
        }

        foreach (var pack in packs.Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(pack);
            var (set, problem, skipped) = IconSet.Load(pack);

            if (set is null)
            {
                _problems.Add(new PackProblem(name, problem ?? string.Empty));
                continue;
            }

            if (Find(set.Id) is not null)
            {
                _problems.Add(new PackProblem(
                    name, Localization.Localizer.Format("Packs.Problem.Duplicate", set.Id)));
                continue;
            }

            // Usable, with some of its images dropped: said, but the set is kept.
            foreach (var reason in skipped)
            {
                _problems.Add(new PackProblem(name, reason) { IsPartial = true });
            }

            _sets.Add(set);
        }

        _sets.Sort((a, b) => string.Compare(a.Name, b.Name, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase));
    }

    /// <summary>The sets that can be used, by name.</summary>
    public IReadOnlyList<IconSet> Sets => _sets;

    /// <summary>Sets that could not be used, and images dropped from ones that could.</summary>
    public IReadOnlyList<PackProblem> Problems => _problems;

    /// <summary>A set by its id, or null.</summary>
    public IconSet? Find(string? id) =>
        id is { Length: > 0 }
            ? _sets.FirstOrDefault(set => string.Equals(set.Id, id, StringComparison.OrdinalIgnoreCase))
            : null;

    private static IconSetLibrary? _installed;

    /// <summary>The sets installed for this user, as last read.</summary>
    public static IconSetLibrary Installed => _installed ??= new IconSetLibrary(PackLocations.FolderFor(PackKind.IconSet));

    /// <summary>Reads the installed sets again.</summary>
    public static IconSetLibrary Rescan() => _installed = new IconSetLibrary(PackLocations.FolderFor(PackKind.IconSet));
}
