using ArtDock.Packs;

namespace ArtDock.Downloads;

/// <summary>A pack on this machine, as the download side sees it.</summary>
/// <param name="Kind">What sort of pack.</param>
/// <param name="Id">Its id, which matches it with a <see cref="CatalogPack"/>.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Version">Its version, when its <c>pack.json</c> gives one.</param>
/// <param name="Folder">Where it is — always under <see cref="PackLocations.Root"/>.</param>
public sealed record InstalledPack(PackKind Kind, string Id, string Name, Version? Version, string Folder);

/// <summary>
/// Puts a downloaded pack in place, and takes one away.
/// </summary>
/// <remarks>
/// <para>
/// Not implemented yet. A pack arrives as a zip of its folder, and ends up exactly where one
/// copied in by hand would be — <see cref="PackLocations.FolderFor(PackKind)"/>, then its id —
/// so the loaders that already read hand-installed packs read downloaded ones with no change.
/// </para>
/// <para>What an implementation owes its callers:</para>
/// <list type="bullet">
///   <item>
///     Only a file that <see cref="IContentVerifier"/> has passed. The installer does not
///     download and does not verify; it is handed the result of both.
///   </item>
///   <item>
///     Every entry in the zip stays inside the pack's folder. An entry named with <c>..</c>,
///     a drive, or a rooted path is refused and the whole pack with it — the classic
///     "zip slip", in which an archive writes wherever it likes.
///   </item>
///   <item>
///     Data only: <c>pack.json</c>, a licence or readme as text, and for an icon set its
///     <c>.png</c> files. Anything else — an executable, a script, a DLL, XAML — refuses the
///     pack. XAML is on that list on purpose: WPF's XAML reader will construct arbitrary
///     types, so a XAML file is code, whatever its extension suggests.
///   </item>
///   <item>
///     Limits on the number of entries and on their unpacked size, checked as it unpacks
///     rather than from the zip's own claims, which are the attacker's to write.
///   </item>
///   <item>
///     Unpacked beside the destination, read back through the same loader the dock will use
///     (<see cref="Localization.LanguageLibrary"/> or <see cref="IconSets.IconSet.Load"/>), and
///     only then swapped in — so a pack that turns out to be unreadable never replaces a
///     working one, and an update of a pack in use is never half-applied.
///   </item>
///   <item>
///     The id inside the <c>pack.json</c> is the id the catalog offered it under. A pack
///     cannot install itself over some other pack.
///   </item>
/// </list>
/// </remarks>
public interface IPackInstaller
{
    /// <summary>Installs, or updates, a pack from a verified download.</summary>
    /// <exception cref="ContentUnavailableException">The archive broke a rule above, or would not read.</exception>
    Task<InstalledPack> InstallAsync(CatalogPack pack, DownloadedFile archive, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a pack. A pack in use stops being used first: a language falls back the way a
    /// missing one always does, and an icon set's pins go back to their own icons.
    /// </summary>
    Task UninstallAsync(PackKind kind, string id, CancellationToken cancellationToken);
}
