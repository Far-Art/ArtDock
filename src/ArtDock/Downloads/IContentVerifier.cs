namespace ArtDock.Downloads;

/// <summary>
/// Decides whether a downloaded file is the one that was published.
/// </summary>
/// <remarks>
/// <para>Not implemented yet. What an implementation must check, in this order:</para>
/// <list type="number">
///   <item>The length is exactly <see cref="ContentFile.Size"/>.</item>
///   <item>The SHA-256 of the bytes is <see cref="ContentFile.Sha256"/>.</item>
///   <item>
///     <see cref="ContentFile.Signature"/>, when there is one, is a valid signature over that
///     hash, under the public key compiled into this build. A missing signature is allowed,
///     because a pack is pictures and strings, read by loaders that already refuse anything
///     outside their own folder; a signature that is present and wrong is refused.
///   </item>
/// </list>
/// <para>
/// The hashes are only as good as the catalog that states them, which is why the catalog
/// itself is signed — see <see cref="IContentCatalogSource"/>. The key is the project's own,
/// made once and kept offline, and the scheme is ECDSA P-256 — in .NET's box as
/// <c>System.Security.Cryptography.ECDsa</c>, so it needs no package and costs nothing.
/// </para>
/// <para>
/// New versions of the dock do not come through here: Velopack fetches those and checks them
/// against its own release feed's hashes — see <c>Services/AppUpdater.cs</c>. Signing that
/// feed with the same key is planned, and is where the key would first be
/// used; decide where it lives before then. Losing the private key means nothing can be
/// trusted again without a build that carries a new public key, delivered some other way.
/// Losing control of it is worse.
/// </para>
/// </remarks>
public interface IContentVerifier
{
    /// <summary>Checks a downloaded file against what the catalog said.</summary>
    /// <param name="file">The file as fetched.</param>
    /// <exception cref="ContentUnavailableException">
    /// It did not check out, with <see cref="ContentUnavailableException.IsIntegrityFailure"/> set.
    /// </exception>
    Task VerifyAsync(DownloadedFile file, CancellationToken cancellationToken);
}
