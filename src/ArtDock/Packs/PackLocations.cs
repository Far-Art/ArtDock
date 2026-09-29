using System.IO;

namespace ArtDock.Packs;

/// <summary>
/// Where packs live on disk.
/// </summary>
/// <remarks>
/// <para>
/// Beside the settings file, in the user's own local application data, so installing a pack
/// needs no rights over the program's folder — which may well be under <c>Program Files</c> —
/// and removing one is deleting a folder. One folder per kind, one folder per pack:
/// <c>Packs\Languages\ru\pack.json</c>, <c>Packs\IconSets\flat\pack.json</c>.
/// </para>
/// <para>
/// This is also where a downloaded pack will be unpacked to, so the pack a user copied in by
/// hand and the one the dock fetched are the same thing to everything that reads them.
/// </para>
/// </remarks>
public static class PackLocations
{
    /// <summary>The name of the file that describes a pack, at the top of its folder.</summary>
    public const string ManifestFileName = "pack.json";

    /// <summary>The folder every kind of pack lives under.</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArtDock",
        "Packs");

    /// <summary>The folder one kind of pack is installed into.</summary>
    public static string FolderFor(PackKind kind) => FolderFor(Root, kind);

    /// <summary>The folder one kind of pack lives in, under a root of the caller's choosing.</summary>
    /// <remarks>For tests, which must never read or write the user's real packs.</remarks>
    public static string FolderFor(string root, PackKind kind) => Path.Combine(root, kind switch
    {
        PackKind.Language => "Languages",
        PackKind.IconSet => "IconSets",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    });

    /// <summary>
    /// Makes sure a kind's folder exists and returns it, for the settings dialog's
    /// <em>Open folder</em>.
    /// </summary>
    /// <returns>The folder, or null when it could not be created.</returns>
    public static string? EnsureFolder(PackKind kind)
    {
        var folder = FolderFor(kind);
        try
        {
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
