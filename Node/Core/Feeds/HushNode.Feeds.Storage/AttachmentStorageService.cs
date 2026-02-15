using Olimpo.EntityFramework.Persistency;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

/// <summary>
/// PostgreSQL-backed storage service for attachment entities.
/// Follows the same UnitOfWork pattern as FeedMessageStorageService.
/// </summary>
public class AttachmentStorageService(
    IUnitOfWorkProvider<FeedsDbContext> unitOfWorkProvider)
    : IAttachmentStorageService
{
    private readonly IUnitOfWorkProvider<FeedsDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public async Task CreateAttachmentAsync(AttachmentEntity attachment)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();
        await writableUnitOfWork
            .GetRepository<IAttachmentRepository>()
            .CreateAttachmentAsync(attachment);
        await writableUnitOfWork.CommitAsync();
    }

    public async Task<AttachmentEntity?> GetByIdAsync(string attachmentId)
    {
        using var readOnlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();
        return await readOnlyUnitOfWork
            .GetRepository<IAttachmentRepository>()
            .GetByIdAsync(attachmentId);
    }

    public async Task<IEnumerable<AttachmentEntity>> GetByMessageIdAsync(FeedMessageId feedMessageId)
    {
        using var readOnlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();
        return await readOnlyUnitOfWork
            .GetRepository<IAttachmentRepository>()
            .GetByMessageIdAsync(feedMessageId);
    }
}
