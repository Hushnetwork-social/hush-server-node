using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public class AttachmentRepository : RepositoryBase<FeedsDbContext>, IAttachmentRepository
{
    public async Task CreateAttachmentAsync(AttachmentEntity attachment) =>
        await this.Context.Attachments.AddAsync(attachment);

    public async Task<AttachmentEntity?> GetByIdAsync(string attachmentId) =>
        await this.Context.Attachments
            .FirstOrDefaultAsync(x => x.Id == attachmentId);

    public async Task<IEnumerable<AttachmentEntity>> GetByMessageIdAsync(FeedMessageId feedMessageId) =>
        await this.Context.Attachments
            .Where(x => x.FeedMessageId == feedMessageId)
            .ToListAsync();
}
