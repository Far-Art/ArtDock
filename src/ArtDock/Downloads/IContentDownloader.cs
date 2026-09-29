namespace ArtDock.Downloads;

/// <summary>
/// Fetches one file the catalog offers.
/// </summary>
/// <remarks>
/// <para>Not implemented yet. What an implementation owes its callers:</para>
/// <list type="bullet">
///   <item>HTTPS only. A URL with any other scheme is refused without being fetched.</item>
///   <item>
///     Never more than <see cref="ContentFile.Size"/> bytes. A body that runs past it is cut
///     off and the download failed — a wrong file should cost a moment, not a disk.
///   </item>
///   <item>
///     Written to a temporary file under the application's own local data, not the system's
///     temp folder, and not beside anything it could be mistaken for.
///   </item>
///   <item>
///     Not checked here — that is <see cref="IContentVerifier"/>'s, and it is kept apart so
///     nothing can be installed through a path that skipped it. The caller verifies before
///     doing anything else with the file.
///   </item>
///   <item>Cancellable at any point, leaving nothing behind.</item>
/// </list>
/// </remarks>
public interface IContentDownloader
{
    /// <summary>Downloads a file to a temporary location.</summary>
    /// <exception cref="ContentUnavailableException">It could not be fetched.</exception>
    Task<DownloadedFile> DownloadAsync(
        ContentFile file,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken);
}
