using Microsoft.Extensions.Logging;

namespace HushNode.Feeds.Storage;

/// <summary>
/// File-system based temp storage for attachment blobs during the mempool phase.
/// Files are named {uuid}.original and {uuid}.thumbnail in a configurable temp directory.
/// </summary>
public class AttachmentTempStorageService : IAttachmentTempStorageService
{
    private readonly string _tempDirectory;
    private readonly ILogger<AttachmentTempStorageService> _logger;

    public AttachmentTempStorageService(string tempDirectory, ILogger<AttachmentTempStorageService> logger)
    {
        _tempDirectory = tempDirectory;
        _logger = logger;
        Directory.CreateDirectory(_tempDirectory);
    }

    public async Task SaveAsync(string attachmentId, byte[] encryptedOriginal, byte[]? encryptedThumbnail)
    {
        var originalPath = GetOriginalPath(attachmentId);
        await File.WriteAllBytesAsync(originalPath, encryptedOriginal);

        if (encryptedThumbnail is { Length: > 0 })
        {
            var thumbnailPath = GetThumbnailPath(attachmentId);
            await File.WriteAllBytesAsync(thumbnailPath, encryptedThumbnail);
        }
    }

    public async Task<(byte[]? EncryptedOriginal, byte[]? EncryptedThumbnail)?> RetrieveAsync(string attachmentId)
    {
        var originalPath = GetOriginalPath(attachmentId);
        if (!File.Exists(originalPath))
            return null;

        var original = await File.ReadAllBytesAsync(originalPath);

        var thumbnailPath = GetThumbnailPath(attachmentId);
        byte[]? thumbnail = File.Exists(thumbnailPath) ? await File.ReadAllBytesAsync(thumbnailPath) : null;

        return (original, thumbnail);
    }

    public Task DeleteAsync(string attachmentId)
    {
        var originalPath = GetOriginalPath(attachmentId);
        if (File.Exists(originalPath))
            File.Delete(originalPath);

        var thumbnailPath = GetThumbnailPath(attachmentId);
        if (File.Exists(thumbnailPath))
            File.Delete(thumbnailPath);

        return Task.CompletedTask;
    }

    public Task CleanupOrphansAsync(TimeSpan maxAge)
    {
        if (!Directory.Exists(_tempDirectory))
            return Task.CompletedTask;

        var cutoff = DateTime.UtcNow - maxAge;
        var files = Directory.GetFiles(_tempDirectory);
        var deletedCount = 0;

        foreach (var file in files)
        {
            var lastWrite = File.GetLastWriteTimeUtc(file);
            if (lastWrite < cutoff)
            {
                File.Delete(file);
                deletedCount++;
            }
        }

        if (deletedCount > 0)
            _logger.LogInformation("Cleaned up {Count} orphan attachment temp files older than {MaxAge}", deletedCount, maxAge);

        return Task.CompletedTask;
    }

    private string GetOriginalPath(string attachmentId) => Path.Combine(_tempDirectory, $"{attachmentId}.original");
    private string GetThumbnailPath(string attachmentId) => Path.Combine(_tempDirectory, $"{attachmentId}.thumbnail");
}
