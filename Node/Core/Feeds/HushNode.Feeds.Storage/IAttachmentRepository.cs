using Olimpo.EntityFramework.Persistency;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public interface IAttachmentRepository : IRepository
{
    Task CreateAttachmentAsync(AttachmentEntity attachment);

    Task<AttachmentEntity?> GetByIdAsync(string attachmentId);

    Task<IEnumerable<AttachmentEntity>> GetByMessageIdAsync(FeedMessageId feedMessageId);
}
