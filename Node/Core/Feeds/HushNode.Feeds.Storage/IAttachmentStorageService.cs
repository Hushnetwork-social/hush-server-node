using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

/// <summary>
/// Service for persisting and retrieving attachment entities in PostgreSQL.
/// </summary>
public interface IAttachmentStorageService
{
    Task CreateAttachmentAsync(AttachmentEntity attachment);

    Task<AttachmentEntity?> GetByIdAsync(string attachmentId);

    Task<IEnumerable<AttachmentEntity>> GetByMessageIdAsync(FeedMessageId feedMessageId);
}
