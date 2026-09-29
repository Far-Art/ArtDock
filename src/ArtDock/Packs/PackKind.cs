namespace ArtDock.Packs;

/// <summary>
/// The kinds of add-on content the dock can take.
/// </summary>
/// <remarks>
/// A pack is the unit of add-on content, whether it was copied into place by hand or — once
/// that is built — downloaded. Each kind has a folder of its own under
/// <see cref="PackLocations.Root"/>, and every pack in it is a folder holding a
/// <c>pack.json</c>. See <c>docs/packs.md</c> for the file formats.
/// </remarks>
public enum PackKind
{
    /// <summary>A translation of the dock's own text: menus, dialogs and messages.</summary>
    Language,

    /// <summary>Replacement icons for the things pinned to the dock.</summary>
    IconSet
}
