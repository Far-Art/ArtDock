using ArtDock.Packs;

namespace ArtDock.Downloads;

/// <summary>Where a pack on offer stands against what is installed.</summary>
public enum PackOfferState
{
    /// <summary>Not installed, and can be.</summary>
    Available,

    /// <summary>Installed, at the version on offer or a later one.</summary>
    Installed,

    /// <summary>Installed, and the catalog has a later version.</summary>
    UpdateAvailable,

    /// <summary>Needs a newer dock — which the About page's update check can fetch.</summary>
    NeedsNewerApp,

    /// <summary>In a pack format this build cannot read at all.</summary>
    Unsupported
}

/// <summary>A pack on offer, and where it stands.</summary>
/// <param name="Pack">What the catalog says.</param>
/// <param name="State">Where it stands against what is installed.</param>
/// <param name="Installed">The installed copy, when there is one.</param>
public sealed record PackOffer(CatalogPack Pack, PackOfferState State, InstalledPack? Installed);

/// <summary>A pack was installed, updated or removed.</summary>
public sealed class PacksChangedEventArgs(PackKind kind, string id) : EventArgs
{
    public PackKind Kind { get; } = kind;

    public string Id { get; } = id;
}

/// <summary>
/// The one place the settings dialog will go to for downloadable packs.
/// </summary>
/// <remarks>
/// <para>
/// Not implemented yet. It is the other interfaces in this folder put together: fetch the
/// catalog (<see cref="IContentCatalogSource"/>), set it against what is installed, and for an
/// install, download (<see cref="IContentDownloader"/>), verify
/// (<see cref="IContentVerifier"/>) and put in place (<see cref="IPackInstaller"/>) — in that
/// order, with the temporary file deleted whatever happens.
/// </para>
/// <para>
/// After <see cref="PacksChanged"/>, whoever shows packs reads them again —
/// <see cref="Localization.Localizer.Rescan"/> and <see cref="IconSets.IconSetLibrary.Rescan"/> —
/// and the dock re-applies the settings in force, so an updated language or icon set is seen
/// straight away. The settings in force, not the ones on disk: the settings dialog previews
/// without saving, and re-applying the stored ones would undo its preview.
/// </para>
/// </remarks>
public interface IPackManager
{
    /// <summary>What can be downloaded, and where each stands.</summary>
    /// <param name="kind">One kind, or null for all.</param>
    Task<IReadOnlyList<PackOffer>> GetOffersAsync(PackKind? kind, CancellationToken cancellationToken);

    /// <summary>Downloads, checks and installs a pack, or updates an installed one.</summary>
    Task<InstalledPack> InstallAsync(
        CatalogPack pack,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>Removes an installed pack.</summary>
    Task UninstallAsync(PackKind kind, string id, CancellationToken cancellationToken);

    /// <summary>Raised on the UI thread after a pack was installed, updated or removed.</summary>
    event EventHandler<PacksChangedEventArgs>? PacksChanged;
}
