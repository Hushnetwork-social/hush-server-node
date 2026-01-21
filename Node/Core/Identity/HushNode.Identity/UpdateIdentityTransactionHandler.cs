using Microsoft.Extensions.Logging;
using Olimpo;
using Olimpo.EntityFramework.Persistency;
using HushNode.Caching;
using HushNode.Events;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public class UpdateIdentityTransactionHandler(
    IUnitOfWorkProvider<IdentityDbContext> unitOfWorkProvider,
    IFeedsStorageService feedsStorageService,
    IGroupMembersCacheService groupMembersCacheService,
    IBlockchainCache blockchainCache,
    IEventAggregator eventAggregator,
    ILogger<UpdateIdentityTransactionHandler> logger)
    : IUpdateIdentityTransactionHandler
{
    private readonly IUnitOfWorkProvider<IdentityDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IGroupMembersCacheService _groupMembersCacheService = groupMembersCacheService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IEventAggregator _eventAggregator = eventAggregator;
    private readonly ILogger<UpdateIdentityTransactionHandler> _logger = logger;

    public async Task HandleUpdateIdentityTransaction(ValidatedTransaction<UpdateIdentityPayload> transaction)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(transaction.Payload.NewAlias))
        {
            this._logger.LogWarning("Rejecting UpdateIdentity transaction: NewAlias is null or empty. Signatory: {Signatory}",
                transaction.UserSignature.Signatory);
            return;
        }

        // The signatory is the identity owner - use their public signing address
        var publicSigningAddress = transaction.UserSignature.Signatory;

        // Verify identity exists
        using var readonlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();
        var identityExists = await readonlyUnitOfWork
            .GetRepository<IIdentityRepository>()
            .AnyAsync(publicSigningAddress);

        if (!identityExists)
        {
            this._logger.LogWarning("Rejecting UpdateIdentity transaction: Identity does not exist for address {Address}",
                publicSigningAddress);
            return;
        }

        await this.UpdateIdentityAlias(publicSigningAddress, transaction.Payload.NewAlias);
    }

    private async Task UpdateIdentityAlias(string publicSigningAddress, string newAlias)
    {
        var blockIndex = this._blockchainCache.LastBlockIndex;

        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IIdentityRepository>()
            .UpdateAliasAsync(publicSigningAddress, newAlias, blockIndex);

        await writableUnitOfWork.CommitAsync();

        // Update BlockIndex on all regular feeds (Chat/Personal) where this user is a participant
        await this._feedsStorageService.UpdateFeedsBlockIndexForParticipantAsync(publicSigningAddress, blockIndex);

        // Get all GroupFeed IDs where this user is a participant (for cache invalidation)
        var groupFeedIds = await this._feedsStorageService.GetGroupFeedIdsForUserAsync(publicSigningAddress);

        if (groupFeedIds.Count > 0)
        {
            // Update LastUpdatedAtBlock on all GroupFeeds where this user is a participant
            // This signals to UI clients that something changed in the feed
            await this._feedsStorageService.UpdateGroupFeedsLastUpdatedAtBlockForParticipantAsync(publicSigningAddress, blockIndex);

            // Invalidate group members cache for all affected feeds
            // Next GetGroupMembers call will fetch fresh data with the new display name
            await this._groupMembersCacheService.InvalidateGroupMembersForUserAsync(publicSigningAddress, groupFeedIds);

            this._logger.LogInformation(
                "Identity alias updated: {Address} -> {NewAlias} (updated {FeedCount} regular feeds, {GroupCount} group feeds, cache invalidated)",
                publicSigningAddress, newAlias, 0, groupFeedIds.Count);
        }
        else
        {
            this._logger.LogInformation(
                "Identity alias updated: {Address} -> {NewAlias} (no group feeds to update)",
                publicSigningAddress, newAlias);
        }

        // Publish event to invalidate identity cache
        await this._eventAggregator.PublishAsync(new IdentityUpdatedEvent(publicSigningAddress));
    }
}
