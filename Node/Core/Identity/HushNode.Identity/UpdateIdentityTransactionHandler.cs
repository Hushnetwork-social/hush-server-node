using Microsoft.Extensions.Logging;
using Olimpo;
using Olimpo.EntityFramework.Persistency;
using HushNode.Caching;
using HushNode.Events;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public class UpdateIdentityTransactionHandler(
    IUnitOfWorkProvider<IdentityDbContext> unitOfWorkProvider,
    IFeedsStorageService feedsStorageService,
    IGroupMembersCacheService groupMembersCacheService,
    IFeedMetadataCacheService feedMetadataCacheService,
    IIdentityDisplayNameCacheService identityDisplayNameCacheService,
    IBlockchainCache blockchainCache,
    IEventAggregator eventAggregator,
    ILogger<UpdateIdentityTransactionHandler> logger)
    : IUpdateIdentityTransactionHandler
{
    private readonly IUnitOfWorkProvider<IdentityDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IGroupMembersCacheService _groupMembersCacheService = groupMembersCacheService;
    private readonly IFeedMetadataCacheService _feedMetadataCacheService = feedMetadataCacheService;
    private readonly IIdentityDisplayNameCacheService _identityDisplayNameCacheService = identityDisplayNameCacheService;
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

        // FEAT-065 E2: Update identity display name cache
        _ = this._identityDisplayNameCacheService.SetDisplayNameAsync(publicSigningAddress, newAlias);

        // FEAT-065 E1: Cascade title changes to feed metadata cache
        // Chat feeds show the other participant's name, so update counterparts' caches
        // Personal feed shows own name + " (YOU)", so update own cache
        await CascadeFeedTitleChangesAsync(publicSigningAddress, newAlias);

        // Publish event to invalidate identity cache
        await this._eventAggregator.PublishAsync(new IdentityUpdatedEvent(publicSigningAddress));
    }

    /// <summary>
    /// FEAT-065: Cascade identity name change to affected feed metadata caches.
    /// Updates Chat feed titles in counterparts' caches and Personal feed title in own cache.
    /// Group feed titles are NOT affected (they have their own title field).
    /// </summary>
    private async Task CascadeFeedTitleChangesAsync(string publicSigningAddress, string newAlias)
    {
        try
        {
            // Get all feeds where this user is a participant
            var feeds = await this._feedsStorageService
                .RetrieveFeedsForAddress(publicSigningAddress, new BlockIndex(0));

            foreach (var feed in feeds)
            {
                switch (feed.FeedType)
                {
                    case FeedType.Personal:
                        // Update own Personal feed title to "NewAlias (YOU)"
                        _ = this._feedMetadataCacheService.UpdateFeedTitleAsync(
                            publicSigningAddress, feed.FeedId, $"{newAlias} (YOU)");
                        break;

                    case FeedType.Chat:
                        // Update the OTHER participant's cache entry for this feed
                        // (they see OUR name as the feed title)
                        var otherParticipant = feed.Participants
                            .FirstOrDefault(p => p.ParticipantPublicAddress != publicSigningAddress);
                        if (otherParticipant != null)
                        {
                            _ = this._feedMetadataCacheService.UpdateFeedTitleAsync(
                                otherParticipant.ParticipantPublicAddress, feed.FeedId, newAlias);
                        }
                        break;

                    // Group and Broadcast feeds have independent titles — not affected
                }
            }
        }
        catch (Exception ex)
        {
            // Fire-and-forget: cache cascade failure should not block identity update
            this._logger.LogWarning(ex,
                "Failed to cascade feed title changes for identity update: {Address}", publicSigningAddress);
        }
    }
}
