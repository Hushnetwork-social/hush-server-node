using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public class AttachmentRepository : RepositoryBase<FeedsDbContext>, IAttachmentRepository
{
    public async Task CreateAttachmentAsync(AttachmentEntity attachment)
    {
        FeedAttachmentIdPolicy.EnsureCanStore(attachment);
        await this.Context.Attachments.AddAsync(attachment);
    }

    public async Task<AttachmentEntity?> GetByIdAsync(string attachmentId) =>
        FeedAttachmentIdPolicy.IsElectionAnomalyRestrictedPayloadReference(attachmentId)
            ? null
            : await this.Context.Attachments
                .FirstOrDefaultAsync(x => x.Id == attachmentId);

    public async Task<IEnumerable<AttachmentEntity>> GetByMessageIdAsync(FeedMessageId feedMessageId)
    {
        var attachments = await this.Context.Attachments
            .Where(x => x.FeedMessageId == feedMessageId)
            .ToListAsync();

        return attachments
            .Where(x => !FeedAttachmentIdPolicy.IsElectionAnomalyRestrictedPayloadReference(x.Id))
            .ToList();
    }
}
