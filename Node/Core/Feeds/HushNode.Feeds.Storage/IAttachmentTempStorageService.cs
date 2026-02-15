namespace HushNode.Feeds.Storage;

/// <summary>
/// Manages temporary file storage for attachment blobs during the mempool phase.
/// Blobs are written to disk during gRPC validation and moved to PostgreSQL during block indexing.
/// </summary>
public interface IAttachmentTempStorageService
{
    /// <summary>
    /// Save encrypted attachment bytes to temp storage.
    /// </summary>
    Task SaveAsync(string attachmentId, byte[] encryptedOriginal, byte[]? encryptedThumbnail);

    /// <summary>
    /// Retrieve encrypted attachment bytes from temp storage.
    /// Returns null if the attachment doesn't exist.
    /// </summary>
    Task<(byte[]? EncryptedOriginal, byte[]? EncryptedThumbnail)?> RetrieveAsync(string attachmentId);

    /// <summary>
    /// Delete attachment files from temp storage.
    /// </summary>
    Task DeleteAsync(string attachmentId);

    /// <summary>
    /// Remove orphan temp files older than the specified age (crash recovery).
    /// </summary>
    Task CleanupOrphansAsync(TimeSpan maxAge);
}
