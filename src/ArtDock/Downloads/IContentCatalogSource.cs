namespace ArtDock.Downloads;

/// <summary>
/// Fetches the catalog of what can be downloaded.
/// </summary>
/// <remarks>
/// <para>
/// Not implemented yet. The intended implementation reads <c>catalog.json</c> over HTTPS from
/// a public repository's GitHub Pages site, which costs nothing — see <c>docs/downloads.md</c>.
/// The address belongs in one place, because every build that has shipped will keep asking
/// it; a domain of the project's own in front of the host is what lets the host change later
/// without stranding those builds.
/// </para>
/// <para>What an implementation owes its callers:</para>
/// <list type="bullet">
///   <item>
///     The catalog's signature is checked before its contents are believed. The catalog is
///     what says which hashes are genuine, so a catalog that could be swapped would make
///     every other check in <see cref="IContentVerifier"/> worthless. It is published beside
///     the catalog as a detached signature, made with the project's key.
///   </item>
///   <item>
///     Only when asked. Nothing is fetched in the background at start-up or on a timer until
///     there is a setting that says the user wants it; the settings dialog asks when its page
///     for downloads is opened.
///   </item>
///   <item>
///     Conditional requests — <c>If-None-Match</c> with the last <c>ETag</c> — so that asking
///     again costs the host almost nothing when nothing has been published.
///   </item>
///   <item>
///     A catalog in a newer <see cref="ContentCatalog.Schema"/> than this build reads is
///     reported as such rather than parsed on a best guess.
///   </item>
/// </list>
/// </remarks>
public interface IContentCatalogSource
{
    /// <summary>Fetches and checks the catalog.</summary>
    /// <exception cref="ContentUnavailableException">It could not be fetched, or did not check out.</exception>
    Task<ContentCatalog> FetchAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Content could not be fetched, or what arrived was not what was published.
/// </summary>
/// <remarks>
/// One exception for the whole of downloading, carrying a message fit to show: a user deciding
/// whether to try again needs to know whether the network or the file was at fault, not which
/// layer noticed.
/// </remarks>
public sealed class ContentUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>
    /// True when the file arrived and failed its checks — a wrong hash or a bad signature —
    /// rather than failing to arrive. Worth telling apart: retrying helps with the one and not
    /// with the other.
    /// </summary>
    public bool IsIntegrityFailure { get; init; }
}
