using ArtDock.Packs;

namespace ArtDock.Downloads;

// Downloadable content: the contracts, and nothing that implements them yet.
//
// Everything in this folder describes how packs will be fetched from the internet, so that the
// day the fetching is written it has a shape to fill rather than a design to invent under time
// pressure. Nothing in the running dock calls any of it. What *is* built is the other end:
// packs installed by hand are read from PackLocations, and a downloaded pack will be unpacked
// into exactly the same place.
//
// New versions of the dock itself are not in here. They were, as an IAppUpdater and a release
// list in this catalog, until Velopack took the job — see Services/AppUpdater.cs.
//
// The design, the hosting choices and the reasons are in docs/downloads.md.

/// <summary>
/// Every pack that can be downloaded, as one published file says it.
/// </summary>
/// <remarks>
/// One catalog for every kind of pack — language packs and icon sets — so there is one file to
/// fetch, one signature to check and one place to publish to. It is small and changes rarely,
/// which is what lets it live on free static hosting.
/// </remarks>
/// <param name="Schema">
/// The catalog's own layout. A build ignores a catalog in a schema newer than it reads rather
/// than misreading it, and says the dock needs updating — which the About page can fetch.
/// </param>
/// <param name="Published">When the catalog was last published.</param>
/// <param name="Packs">Every pack on offer, of every kind.</param>
public sealed record ContentCatalog(
    int Schema,
    DateTimeOffset Published,
    IReadOnlyList<CatalogPack> Packs);

/// <summary>
/// A file the catalog offers: where it is, and how to know that what arrived is what was
/// published.
/// </summary>
/// <param name="Url">Where to fetch it. HTTPS only; anything else is refused before a byte is read.</param>
/// <param name="Size">Its exact length in bytes. A download is cut off past it.</param>
/// <param name="Sha256">The SHA-256 of its bytes, as hex.</param>
/// <param name="Signature">
/// A detached signature over <see cref="Sha256"/>, made with the project's key, as base64.
/// Optional, because a pack is data the dock reads rather than code it runs — see
/// <see cref="IContentVerifier"/>.
/// </param>
public sealed record ContentFile(Uri Url, long Size, string Sha256, string? Signature = null);

/// <summary>One pack on offer.</summary>
/// <param name="Kind">What sort of pack it is, and so which folder it installs into.</param>
/// <param name="Id">
/// Its id, the same one its <c>pack.json</c> carries — which is how an installed pack is
/// matched with what the catalog offers.
/// </param>
/// <param name="Name">What to call it where it is offered.</param>
/// <param name="Version">Its version, compared with the installed one's to offer an update.</param>
/// <param name="File">The pack itself: a zip of its folder.</param>
/// <param name="Format">The <c>pack.json</c> format, so a build that cannot read it does not fetch it.</param>
/// <param name="MinAppVersion">The oldest dock it works with.</param>
/// <param name="Author">Who made it.</param>
/// <param name="License">What it may be used and shared under — shown before installing.</param>
/// <param name="Description">A sentence about it.</param>
/// <param name="Preview">An image of it, for an icon set.</param>
public sealed record CatalogPack(
    PackKind Kind,
    string Id,
    string Name,
    Version Version,
    ContentFile File,
    int Format = 1,
    Version? MinAppVersion = null,
    string? Author = null,
    string? License = null,
    string? Description = null,
    Uri? Preview = null);

/// <summary>How far along a download is, for a progress bar.</summary>
/// <param name="Received">Bytes so far.</param>
/// <param name="Total">Bytes expected, when known.</param>
public readonly record struct TransferProgress(long Received, long? Total);

/// <summary>A file that has been fetched and not yet checked.</summary>
/// <param name="Source">What the catalog said it would be.</param>
/// <param name="Path">
/// Where it was written — a temporary file the caller owns, and deletes once it has been
/// installed or refused.
/// </param>
public sealed record DownloadedFile(ContentFile Source, string Path);
